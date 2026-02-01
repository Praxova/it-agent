using System.Text.Json;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Models;

namespace LucidAdmin.Infrastructure.Providers;

/// <summary>
/// Provider for AWS Bedrock LLM accounts
/// </summary>
public class BedrockProvider : BaseServiceAccountProvider
{
    public override string ProviderId => "llm-bedrock";
    public override string DisplayName => "AWS Bedrock";
    public override string Description => "AWS Bedrock (Claude, Llama, etc.)";
    public override bool IsImplemented => true;

    public override IEnumerable<AccountTypeInfo> SupportedAccountTypes => new[]
    {
        new AccountTypeInfo("iam", "IAM Credentials",
            "AWS IAM access key and secret", true)
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
            return ValidationResult.Failure("Configuration is required for AWS Bedrock accounts");
        }

        try
        {
            var config = JsonSerializer.Deserialize<BedrockConfiguration>(configurationJson);
            var errors = new List<string>();

            if (config == null)
            {
                return ValidationResult.Failure("Invalid configuration JSON");
            }

            if (string.IsNullOrWhiteSpace(config.region))
            {
                errors.Add("AWS region is required");
            }

            if (string.IsNullOrWhiteSpace(config.model_id))
            {
                errors.Add("Model ID is required");
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
                "region": { "type": "string", "description": "AWS region (e.g., us-east-1)" },
                "model_id": { "type": "string", "description": "Model ID (e.g., anthropic.claude-3-opus-20240229-v1:0)" },
                "temperature": { "type": "number", "description": "Sampling temperature (0.0 to 1.0)", "default": 0.1 }
            },
            "required": ["region", "model_id"]
        }
        """;
    }

    public override string GetConfigurationExample(string accountType)
    {
        return """
        {
            "region": "us-east-1",
            "model_id": "anthropic.claude-3-opus-20240229-v1:0",
            "temperature": "0.1"
        }
        """;
    }

    public override Task<HealthCheckResult> TestConnectivityAsync(
        ServiceAccount account, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<BedrockConfiguration>(account.Configuration ?? "{}");

            if (string.IsNullOrEmpty(config?.region))
                return Task.FromResult(HealthCheckResult.Unhealthy("AWS region not configured"));

            if (string.IsNullOrEmpty(config?.model_id))
                return Task.FromResult(HealthCheckResult.Unhealthy("Model ID not configured"));

            // We can't easily test AWS without the SDK, so validate config and return a note
            return Task.FromResult(HealthCheckResult.Unknown(
                $"Configuration valid for region '{config.region}'. Full connectivity test requires AWS SDK at runtime."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy($"Configuration error: {ex.Message}"));
        }
    }

    private class BedrockConfiguration
    {
        public string? region { get; set; }
        public string? model_id { get; set; }
        public string? temperature { get; set; }
    }
}
