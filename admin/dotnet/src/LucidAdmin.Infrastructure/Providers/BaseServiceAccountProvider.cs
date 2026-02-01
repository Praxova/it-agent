using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Providers;
using LucidAdmin.Core.Models;

namespace LucidAdmin.Infrastructure.Providers;

/// <summary>
/// Base class for service account providers with common functionality
/// </summary>
public abstract class BaseServiceAccountProvider : IServiceAccountProvider
{
    public abstract string ProviderId { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public abstract bool IsImplemented { get; }
    public abstract IEnumerable<AccountTypeInfo> SupportedAccountTypes { get; }
    public abstract IEnumerable<CredentialStorageType> SupportedCredentialStorage { get; }

    public virtual ValidationResult ValidateConfiguration(string accountType, string? configurationJson)
    {
        if (!SupportedAccountTypes.Any(t => t.TypeId == accountType))
        {
            return ValidationResult.Failure($"Account type '{accountType}' is not supported by provider '{ProviderId}'");
        }

        return ValidationResult.Success();
    }

    public virtual Task<HealthCheckResult> TestConnectivityAsync(ServiceAccount account, CancellationToken cancellationToken = default)
    {
        if (!IsImplemented)
        {
            return Task.FromResult(HealthCheckResult.Unknown($"Provider '{ProviderId}' is not yet implemented"));
        }

        return Task.FromResult(HealthCheckResult.Unknown("Health check not implemented"));
    }

    public abstract string GetConfigurationSchema(string accountType);
    public abstract string GetConfigurationExample(string accountType);
}
