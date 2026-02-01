using System.Net.Http;
using System.Text.Json;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Credentials;
using LucidAdmin.Core.Models;
using Microsoft.Extensions.Logging;

namespace LucidAdmin.Infrastructure.Providers;

/// <summary>
/// Provider for Anthropic API accounts
/// </summary>
public class AnthropicProvider : BaseServiceAccountProvider
{
    private readonly ICredentialService? _credentialService;
    private readonly ILogger<AnthropicProvider>? _logger;

    public AnthropicProvider()
    {
    }

    public AnthropicProvider(ICredentialService credentialService, ILogger<AnthropicProvider> logger)
    {
        _credentialService = credentialService;
        _logger = logger;
    }

    public override string ProviderId => "llm-anthropic";
    public override string DisplayName => "Anthropic";
    public override string Description => "Anthropic API (Claude models)";
    public override bool IsImplemented => true;

    public override IEnumerable<AccountTypeInfo> SupportedAccountTypes => new[]
    {
        new AccountTypeInfo("api-key", "API Key",
            "Anthropic API key authentication", true)
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
            return ValidationResult.Failure("Configuration is required for Anthropic accounts");
        }

        try
        {
            var config = JsonSerializer.Deserialize<AnthropicConfiguration>(configurationJson);
            var errors = new List<string>();

            if (config == null)
            {
                return ValidationResult.Failure("Invalid configuration JSON");
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
                "model": { "type": "string", "description": "Model name (e.g., claude-3-opus-20240229, claude-3-sonnet-20240229)" },
                "temperature": { "type": "number", "description": "Sampling temperature (0.0 to 1.0)", "default": 0.1 }
            },
            "required": ["model"]
        }
        """;
    }

    public override string GetConfigurationExample(string accountType)
    {
        return """
        {
            "model": "claude-3-opus-20240229",
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
            httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
            httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            // Use a simple request to verify the API key works
            var response = await httpClient.GetAsync("https://api.anthropic.com/v1/models", cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return HealthCheckResult.Unhealthy("Invalid API key");

            if (!response.IsSuccessStatusCode)
                return HealthCheckResult.Unhealthy($"Anthropic API returned {response.StatusCode}");

            return HealthCheckResult.Healthy("Connected to Anthropic API");
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

    private class AnthropicConfiguration
    {
        public string? model { get; set; }
        public string? temperature { get; set; }
    }
}
