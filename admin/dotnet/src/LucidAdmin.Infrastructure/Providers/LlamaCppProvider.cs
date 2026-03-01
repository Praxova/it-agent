using System.Net.Http;
using System.Text.Json;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Models;

namespace LucidAdmin.Infrastructure.Providers;

/// <summary>
/// Provider for local llama.cpp server with OpenAI-compatible API
/// </summary>
public class LlamaCppProvider : BaseServiceAccountProvider
{
    public override string ProviderId => "llm-llamacpp";
    public override string DisplayName => "llama.cpp Server";
    public override string Description => "Local LLM server via llama.cpp (OpenAI-compatible API)";
    public override bool IsImplemented => true;

    public override IEnumerable<AccountTypeInfo> SupportedAccountTypes => new[]
    {
        new AccountTypeInfo("local", "Local Instance",
            "Local llama.cpp server with OpenAI-compatible API", false)
    };

    public override IEnumerable<CredentialStorageType> SupportedCredentialStorage => new[]
    {
        CredentialStorageType.None
    };

    public override ValidationResult ValidateConfiguration(string accountType, string? configurationJson)
    {
        var baseResult = base.ValidateConfiguration(accountType, configurationJson);
        if (!baseResult.IsValid) return baseResult;

        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return ValidationResult.Failure("Configuration is required for llama.cpp accounts");
        }

        try
        {
            var config = JsonSerializer.Deserialize<LlamaCppConfiguration>(configurationJson);
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
                "base_url": { "type": "string", "description": "llama.cpp server URL (e.g., https://llm:8443/v1)" },
                "model": { "type": "string", "description": "Model name (e.g., llama3.1)" },
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
            "base_url": "https://llm:8443/v1",
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
            var config = JsonSerializer.Deserialize<LlamaCppConfiguration>(account.Configuration ?? "{}");
            if (string.IsNullOrEmpty(config?.base_url))
                return HealthCheckResult.Unhealthy("Base URL not configured");

            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

            var modelsUrl = $"{config.base_url.TrimEnd('/')}/models";
            var response = await httpClient.GetAsync(modelsUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return HealthCheckResult.Unhealthy($"llama.cpp server returned {response.StatusCode}");

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var modelsResponse = JsonSerializer.Deserialize<OpenAiModelsResponse>(content);

            if (!string.IsNullOrEmpty(config.model))
            {
                var modelExists = modelsResponse?.data?.Any(m =>
                    m.id?.Contains(config.model, StringComparison.OrdinalIgnoreCase) == true) ?? false;

                if (!modelExists)
                    return HealthCheckResult.Unhealthy($"Model '{config.model}' not found on llama.cpp server");
            }

            return HealthCheckResult.Healthy(
                $"Connected to llama.cpp at {config.base_url}");
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

    private class LlamaCppConfiguration
    {
        public string? base_url { get; set; }
        public string? model { get; set; }
        public string? temperature { get; set; }
    }

    private class OpenAiModelsResponse
    {
        public List<OpenAiModel>? data { get; set; }
    }

    private class OpenAiModel
    {
        public string? id { get; set; }
    }
}
