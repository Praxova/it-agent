using LucidAdmin.Core.Interfaces.Credentials;
using LucidAdmin.Core.Models;

namespace LucidAdmin.Infrastructure.Credentials;

/// <summary>
/// Provider for accounts that don't need credentials (gMSA, CurrentUser, local services)
/// </summary>
public class NoneCredentialProvider : ICredentialProvider
{
    public string ProviderId => "None";
    public string DisplayName => "None";
    public string Description => "No credentials required (for gMSA, CurrentUser, or local services without authentication)";
    public bool IsAvailable => true;
    public bool SupportsStorage => false;

    public Task<CredentialStoreResult> StoreAsync(
        Guid accountId,
        CredentialSet credentials,
        string? reference = null,
        CancellationToken ct = default)
    {
        return Task.FromResult(CredentialStoreResult.Failed(
            "None provider does not support credential storage. This account type does not require credentials."));
    }

    public Task<CredentialSet?> RetrieveAsync(
        Guid accountId,
        string? reference = null,
        CancellationToken ct = default)
    {
        // Return empty credential set
        return Task.FromResult<CredentialSet?>(new CredentialSet(new Dictionary<string, string>()));
    }

    public Task<bool> DeleteAsync(
        Guid accountId,
        string? reference = null,
        CancellationToken ct = default)
    {
        // Nothing to delete
        return Task.FromResult(true);
    }

    public ValidationResult ValidateReference(string? reference)
    {
        return ValidationResult.Success();
    }

    public Task<HealthCheckResult> TestConnectivityAsync(CancellationToken ct = default)
    {
        return Task.FromResult(HealthCheckResult.Healthy("No credentials required for this account type"));
    }
}
