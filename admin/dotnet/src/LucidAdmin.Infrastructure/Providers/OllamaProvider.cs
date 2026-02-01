using System.Net.Http;
using System.Text.Json;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Models;

namespace LucidAdmin.Infrastructure.Providers;

/// <summary>
/// Provider for Ollama local LLM instances
/// </summary>
public class OllamaProvider : BaseServiceAccountProvider
{
    public override string ProviderId => "llm-ollama";
    public override string DisplayName => "Ollama (Local LLM)";
    public override string Description => "Local LLM server via Ollama";
    public override bool IsImplemented => true;

    public override IEnumerable<AccountTypeInfo> SupportedAccountTypes => new[]
    {
        new AccountTypeInfo("local", "Local Instance",
            "Local Ollama server (no authentication required)", false)
    };

    public override IEnumerable<CredentialStorageType> SupportedCredentialStorage => new[]
    {
        CredentialStorageType.None  // Ollama typically runs locally without authentication
    };

    public override ValidationResult ValidateConfiguration(string accountType, string? configurationJson)
    {
        var baseResult = base.ValidateConfiguration(accountType, configurationJson);
        if (!baseResult.IsValid) return baseResult;

        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return ValidationResult.Failure("Configuration is required for Ollama accounts");
        }

        try
        {
            var config = JsonSerializer.Deserialize<OllamaConfiguration>(configurationJson);
            var errors = new List<string>();

            if (config == null)
            {
                return ValidationResult.Failure("Invalid configuration JSON");
            }

            if (string.IsNullOrWhiteSpace(config.base_url))
            {
                errors.Add("Base URL is required");
            }
            else if (!Uri.TryCreate(config.base_url, UriKind.Absolute, out var uri) ||
                     (uri.Scheme != "https" && uri.Scheme != "http"))
            {
                errors.Add("Base URL must be a valid HTTP/HTTPS URL");
            }

            if (string.IsNullOrWhiteSpace(config.model))
            {
                errors.Add("Model name is required");
            }

            return errors.Count > 0 ? ValidationResult.Failure(errors) : ValidationResult.Success();
        }
        catch (JsonException ex)
        {
            return ValidationResult.Failure($"Invalid JSON: {ex.Message}");
        }
    }

    public override string GetConfigurationSchema(string accountType)
    {
        return """
        {
            "type": "object",
            "properties": {
                "base_url": { "type": "string", "description": "Ollama server URL (e.g., http://localhost:11434)" },
                "model": { "type": "string", "description": "Model name (e.g., llama3.1, mistral)" },
                "temperature": { "type": "number", "description": "Sampling temperature (0.0 to 2.0)", "default": 0.1 }
            },
            "required": ["base_url", "model"]
        }
        """;
    }

    public override string GetConfigurationExample(string accountType)
    {
        return """
        {
            "base_url": "http://localhost:11434",
            "model": "llama3.1",
            "temperature": "0.1"
        }
        """;
    }

    public override async Task<HealthCheckResult> TestConnectivityAsync(
        ServiceAccount account, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<OllamaConfiguration>(account.Configuration ?? "{}");
            if (string.IsNullOrEmpty(config?.base_url))
                return HealthCheckResult.Unhealthy("Base URL not configured");

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await httpClient.GetAsync($"{config.base_url.TrimEnd('/')}/api/tags", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return HealthCheckResult.Unhealthy($"Ollama server returned {response.StatusCode}");

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var tagsResponse = JsonSerializer.Deserialize<OllamaTagsResponse>(content);

            // Check if configured model exists
            if (!string.IsNullOrEmpty(config.model))
            {
                var modelExists = tagsResponse?.models?.Any(m =>
                    m.name?.StartsWith(config.model, StringComparison.OrdinalIgnoreCase) == true) ?? false;

                if (!modelExists)
                    return HealthCheckResult.Unhealthy($"Model '{config.model}' not found on Ollama server");
            }

            var modelCount = tagsResponse?.models?.Count ?? 0;
            return HealthCheckResult.Healthy(
                $"Connected to Ollama at {config.base_url} ({modelCount} models available)");
        }
        catch (TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("Connection timed out");
        }
        catch (HttpRequestException ex)
        {
            return HealthCheckResult.Unhealthy($"Connection failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Error: {ex.Message}");
        }
    }

    private class OllamaConfiguration
    {
        public string? base_url { get; set; }
        public string? model { get; set; }
        public string? temperature { get; set; }
    }

    private class OllamaTagsResponse
    {
        public List<OllamaModel>? models { get; set; }
    }

    private class OllamaModel
    {
        public string? name { get; set; }
    }
}
