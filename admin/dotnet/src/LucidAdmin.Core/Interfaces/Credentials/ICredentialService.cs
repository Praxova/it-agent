using LucidAdmin.Core.Entities;

namespace LucidAdmin.Core.Interfaces.Credentials;

/// <summary>
/// High-level service for credential operations with authorization
/// </summary>
public interface ICredentialService
{
    /// <summary>
    /// Store credentials for a service account
    /// </summary>
    Task<CredentialStoreResult> StoreCredentialsAsync(
        Guid serviceAccountId,
        CredentialSet credentials,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieve credentials for a service account.
    /// Requires appropriate authorization.
    /// </summary>
    Task<CredentialSet?> GetCredentialsAsync(
        Guid serviceAccountId,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieve credentials using the ServiceAccount entity directly.
    /// For internal use (health checks, etc.)
    /// </summary>
    Task<CredentialSet?> GetCredentialsAsync(
        ServiceAccount account,
        CancellationToken ct = default);

    /// <summary>
    /// Delete credentials for a service account
    /// </summary>
    Task<bool> DeleteCredentialsAsync(
        Guid serviceAccountId,
        CancellationToken ct = default);

    /// <summary>
    /// Check if a service account has credentials stored
    /// </summary>
    Task<bool> HasCredentialsAsync(
        Guid serviceAccountId,
        CancellationToken ct = default);
}
