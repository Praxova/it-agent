using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Models;

namespace LucidAdmin.Core.Interfaces.Providers;

/// <summary>
/// Interface for service account providers that handle different identity systems
/// </summary>
public interface IServiceAccountProvider
{
    /// <summary>
    /// Unique identifier for this provider (e.g., "windows-ad", "linux", "servicenow")
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Human-friendly display name (e.g., "Windows Active Directory")
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Description of this provider
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Whether this provider is fully implemented (false for stubs)
    /// </summary>
    bool IsImplemented { get; }

    /// <summary>
    /// Account types supported by this provider
    /// </summary>
    IEnumerable<AccountTypeInfo> SupportedAccountTypes { get; }

    /// <summary>
    /// Credential storage types that make sense for this provider
    /// </summary>
    IEnumerable<CredentialStorageType> SupportedCredentialStorage { get; }

    /// <summary>
    /// Validate the configuration JSON for a specific account type
    /// </summary>
    ValidationResult ValidateConfiguration(string accountType, string? configurationJson);

    /// <summary>
    /// Test connectivity/health of a service account
    /// </summary>
    Task<HealthCheckResult> TestConnectivityAsync(ServiceAccount account, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a JSON schema for the configuration (for UI form generation)
    /// </summary>
    string GetConfigurationSchema(string accountType);

    /// <summary>
    /// Get example configuration JSON for a specific account type
    /// </summary>
    string GetConfigurationExample(string accountType);
}
