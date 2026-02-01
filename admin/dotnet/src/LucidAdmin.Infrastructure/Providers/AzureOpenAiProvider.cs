using System.Net.Http;
using System.Text.Json;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Models;

namespace LucidAdmin.Infrastructure.Providers;

/// <summary>
/// Provider for Azure OpenAI Service accounts
/// </summary>
public class AzureOpenAiProvider : BaseServiceAccountProvider
{
    public override string ProviderId => "llm-azure-openai";
    public override string DisplayName => "Azure OpenAI";
    public override string Description => "Azure OpenAI Service";
    public override bool IsImplemented => true;

    public override IEnumerable<AccountTypeInfo> SupportedAccountTypes => new[]
    {
        new AccountTypeInfo("api-key", "API Key",
            "Azure OpenAI API key authentication", true)
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
            return ValidationResult.Failure("Configuration is required for Azure OpenAI accounts");
        }

        try
        {
            var config = JsonSerializer.Deserialize<AzureOpenAiConfiguration>(configurationJson);
            var errors = new List<string>();

            if (config == null)
            {
                return ValidationResult.Failure("Invalid configuration JSON");
            }

            if (string.IsNullOrWhiteSpace(config.endpoint))
            {
                errors.Add("Endpoint is required");
            }
            else if (!Uri.TryCreate(config.endpoint, UriKind.Absolute, out var uri) ||
                     (uri.Scheme != "https" && uri.Scheme != "http"))
            {
                errors.Add("Endpoint must be a valid HTTP/HTTPS URL");
            }

            if (string.IsNullOrWhiteSpace(config.deployment_name))
            {
                errors.Add("Deployment name is required");
            }

            if (string.IsNullOrWhiteSpace(config.api_version))
            {
                errors.Add("API version is required");
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
                "endpoint": { "type": "string", "description": "Azure OpenAI endpoint URL" },
                "deployment_name": { "type": "string", "description": "Deployment name" },
                "api_version": { "type": "string", "description": "API version (e.g., 2023-05-15)" },
                "temperature": { "type": "number", "description": "Sampling temperature (0.0 to 2.0)", "default": 0.1 }
            },
            "required": ["endpoint", "deployment_name", "api_version"]
        }
        """;
    }

    public override string GetConfigurationExample(string accountType)
    {
        return """
        {
            "endpoint": "https://my-resource.openai.azure.com",
            "deployment_name": "gpt-4",
            "api_version": "2023-05-15",
            "temperature": "0.1"
        }
        """;
    }

    public override async Task<HealthCheckResult> TestConnectivityAsync(
        ServiceAccount account, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<AzureOpenAiConfiguration>(account.Configuration ?? "{}");
            if (string.IsNullOrEmpty(config?.endpoint))
                return HealthCheckResult.Unhealthy("Azure endpoint not configured");

            var apiKey = !string.IsNullOrEmpty(account.CredentialReference)
                ? Environment.GetEnvironmentVariable(account.CredentialReference)
                : null;

            if (string.IsNullOrEmpty(apiKey) && account.AccountType == "api-key")
                return HealthCheckResult.Unhealthy($"API key not found in environment variable '{account.CredentialReference}'");

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            if (!string.IsNullOrEmpty(apiKey))
                httpClient.DefaultRequestHeaders.Add("api-key", apiKey);

            var apiVersion = config.api_version ?? "2024-02-15-preview";
            var url = $"{config.endpoint.TrimEnd('/')}/openai/deployments?api-version={apiVersion}";

            var response = await httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return HealthCheckResult.Unhealthy("Invalid API key or unauthorized");

            if (!response.IsSuccessStatusCode)
                return HealthCheckResult.Unhealthy($"Azure OpenAI returned {response.StatusCode}");

            return HealthCheckResult.Healthy($"Connected to Azure OpenAI at {config.endpoint}");
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

    private class AzureOpenAiConfiguration
    {
        public string? endpoint { get; set; }
        public string? deployment_name { get; set; }
        public string? api_version { get; set; }
        public string? temperature { get; set; }
    }
}
