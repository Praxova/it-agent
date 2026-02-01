using LucidAdmin.Core.Interfaces.Credentials;
using LucidAdmin.Core.Models;
using Microsoft.Extensions.Logging;

namespace LucidAdmin.Infrastructure.Credentials;

/// <summary>
/// Retrieves credentials from environment variables (backward compatibility, development use)
/// </summary>
public class EnvironmentCredentialProvider : ICredentialProvider
{
    private readonly ILogger<EnvironmentCredentialProvider> _logger;

    public EnvironmentCredentialProvider(ILogger<EnvironmentCredentialProvider> logger)
    {
        _logger = logger;
    }

    public string ProviderId => "Environment";
    public string DisplayName => "Environment Variables";
    public string Description => "Read credentials from environment variables (for development and backward compatibility)";
    public bool IsAvailable => true;
    public bool SupportsStorage => false; // We don't write to env vars

    public Task<CredentialStoreResult> StoreAsync(
        Guid accountId,
        CredentialSet credentials,
        string? reference = null,
        CancellationToken ct = default)
    {
        return Task.FromResult(CredentialStoreResult.Failed(
            "Environment provider is read-only. Set credentials as environment variables manually."));
    }

    public Task<CredentialSet?> RetrieveAsync(
        Guid accountId,
        string? reference = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(reference))
        {
            _logger.LogWarning("No environment variable reference specified for account {AccountId}", accountId);
            return Task.FromResult<CredentialSet?>(null);
        }

        var values = new Dictionary<string, string>();

        // Reference can be a single env var name or a comma-separated list
        // Format: "PASSWORD_VAR" or "USERNAME_VAR:username,PASSWORD_VAR:password"
        var parts = reference.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            var keyValue = part.Split(':', 2);

            if (keyValue.Length == 2)
            {
                // Format: "ENV_VAR_NAME:credential_key"
                var envVarName = keyValue[0].Trim();
                var credentialKey = keyValue[1].Trim();
                var value = Environment.GetEnvironmentVariable(envVarName);

                if (!string.IsNullOrEmpty(value))
                {
                    values[credentialKey] = value;
                }
                else
                {
                    _logger.LogWarning("Environment variable {EnvVar} not found for account {AccountId}", envVarName, accountId);
                }
            }
            else
            {
                // Simple format: just the env var name, assume it's the password
                var envVarName = part.Trim();
                var value = Environment.GetEnvironmentVariable(envVarName);

                if (!string.IsNullOrEmpty(value))
                {
                    values[CredentialSet.Keys.Password] = value;
                }
                else
                {
                    _logger.LogWarning("Environment variable {EnvVar} not found for account {AccountId}", envVarName, accountId);
                }
            }
        }

        if (values.Count == 0)
        {
            _logger.LogWarning("No credentials found in environment for account {AccountId}", accountId);
            return Task.FromResult<CredentialSet?>(null);
        }

        return Task.FromResult<CredentialSet?>(new CredentialSet(values));
    }

    public Task<bool> DeleteAsync(
        Guid accountId,
        string? reference = null,
        CancellationToken ct = default)
    {
        // Can't delete env vars
        _logger.LogWarning("Cannot delete environment variables - they must be removed manually");
        return Task.FromResult(false);
    }

    public ValidationResult ValidateReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return ValidationResult.Failure("Environment variable name is required for Environment storage type");
        }

        // Validate format
        var parts = reference.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var envVarName = part.Split(':')[0].Trim();
            if (string.IsNullOrEmpty(envVarName))
            {
                return ValidationResult.Failure("Invalid environment variable reference format");
            }
        }

        return ValidationResult.Success();
    }

    public Task<HealthCheckResult> TestConnectivityAsync(CancellationToken ct = default)
    {
        // Environment provider is always "available" - it's just reading env vars
        return Task.FromResult(HealthCheckResult.Healthy("Environment variable provider is ready"));
    }
}
