using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Credentials;
using LucidAdmin.Core.Models;
using Microsoft.Extensions.Logging;

namespace LucidAdmin.Infrastructure.Providers;

/// <summary>
/// Provider for OpenAI API accounts
/// </summary>
public class OpenAiProvider : BaseServiceAccountProvider
{
    private readonly ICredentialService? _credentialService;
    private readonly ILogger<OpenAiProvider>? _logger;

    public OpenAiProvider()
    {
    }

    public OpenAiProvider(ICredentialService credentialService, ILogger<OpenAiProvider> logger)
    {
        _credentialService = credentialService;
        _logger = logger;
    }

    public override string ProviderId => "llm-openai";
    public override string DisplayName => "OpenAI";
    public override string Description => "OpenAI API (GPT-4, GPT-3.5, etc.)";
    public override bool IsImplemented => true;

    public override IEnumerable<AccountTypeInfo> SupportedAccountTypes => new[]
    {
        new AccountTypeInfo("api-key", "API Key",
            "OpenAI API key authentication", true)
    };

    public override IEnumerable<CredentialStorageType> SupportedCredentialStorage => new[]
    {
        CredentialStorageType.Database,
        CredentialStorageType.Environment,
        CredentialStorageType.Vault
    };

    public override ValidationResult ValidateConfiguration(string accountType, string? configurationJson)
    {
        var baseResult = base.ValidateConfiguration(accountType, configurationJson);
        if (!baseResult.IsValid) return baseResult;

        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return ValidationResult.Failure("Configuration is required for OpenAI accounts");
        }

        try
        {
            var config = JsonSerializer.Deserialize<OpenAiConfiguration>(configurationJson);
            var errors = new List<string>();

            if (config == null)
            {
                return ValidationResult.Failure("Invalid configuration JSON");
            }

            if (string.IsNullOrWhiteSpace(config.model))
            {
                errors.Add("Model name is required");
            }

            // base_url is optional (defaults to OpenAI's API)
            if (!string.IsNullOrWhiteSpace(config.base_url))
            {
                if (!Uri.TryCreate(config.base_url, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != "https" && uri.Scheme != "http"))
                {
                    errors.Add("Base URL must be a valid HTTP/HTTPS URL");
                }
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
                "model": { "type": "string", "description": "Model name (e.g., gpt-4, gpt-3.5-turbo)" },
                "temperature": { "type": "number", "description": "Sampling temperature (0.0 to 2.0)", "default": 0.1 },
                "base_url": { "type": "string", "description": "Optional custom API endpoint" }
            },
            "required": ["model"]
        }
        """;
    }

    public override string GetConfigurationExample(string accountType)
    {
        return """
        {
            "model": "gpt-4",
            "temperature": "0.1"
        }
        """;
    }

    public override async Task<HealthCheckResult> TestConnectivityAsync(
        ServiceAccount account, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get API key from credential service
            string? apiKey = null;

            if (_credentialService != null)
            {
                var credentials = await _credentialService.GetCredentialsAsync(account, cancellationToken);
                apiKey = credentials?.Get(CredentialSet.Keys.ApiKey);
            }

            // Fallback to environment variable
            if (string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(account.CredentialReference))
            {
                apiKey = Environment.GetEnvironmentVariable(account.CredentialReference);
            }

            if (string.IsNullOrEmpty(apiKey))
                return HealthCheckResult.Unhealthy("API key not configured. Store credentials in the Admin Portal or set the environment variable.");

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);

            var response = await httpClient.GetAsync("https://api.openai.com/v1/models", cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return HealthCheckResult.Unhealthy("Invalid API key");

            if (!response.IsSuccessStatusCode)
                return HealthCheckResult.Unhealthy($"OpenAI API returned {response.StatusCode}");

            return HealthCheckResult.Healthy("Connected to OpenAI API");
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

    private class OpenAiConfiguration
    {
        public string? model { get; set; }
        public string? temperature { get; set; }
        public string? base_url { get; set; }
    }
}
