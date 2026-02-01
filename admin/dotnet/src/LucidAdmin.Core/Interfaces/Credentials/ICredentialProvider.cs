using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Models;

namespace LucidAdmin.Core.Interfaces.Credentials;

/// <summary>
/// Represents a set of credentials (key-value pairs)
/// </summary>
public record CredentialSet(
    Dictionary<string, string> Values,
    DateTime? ExpiresAt = null,
    Dictionary<string, object>? Metadata = null
)
{
    /// <summary>Get a credential value by key</summary>
    public string? Get(string key) => Values.TryGetValue(key, out var value) ? value : null;

    /// <summary>Check if empty</summary>
    public bool IsEmpty => Values.Count == 0;

    /// <summary>Create from single key-value</summary>
    public static CredentialSet FromSingle(string key, string value)
        => new(new Dictionary<string, string> { { key, value } });

    /// <summary>Common credential keys</summary>
    public static class Keys
    {
        public const string Password = "password";
        public const string ApiKey = "api_key";
        public const string ClientSecret = "client_secret";
        public const string PrivateKey = "private_key";
        public const string AccessToken = "access_token";
        public const string RefreshToken = "refresh_token";
        public const string Username = "username";
    }
}

/// <summary>
/// Result of storing credentials
/// </summary>
public record CredentialStoreResult(
    bool Success,
    string? Reference,
    string? ErrorMessage = null
)
{
    public static CredentialStoreResult Succeeded(string? reference = null)
        => new(true, reference);

    public static CredentialStoreResult Failed(string error)
        => new(false, null, error);
}

/// <summary>
/// Interface for credential storage providers
/// </summary>
public interface ICredentialProvider
{
    /// <summary>Unique identifier (matches CredentialStorageType enum value name)</summary>
    string ProviderId { get; }

    /// <summary>Human-friendly display name</summary>
    string DisplayName { get; }

    /// <summary>Description of this provider</summary>
    string Description { get; }

    /// <summary>Whether this provider is available (dependencies met, configured)</summary>
    bool IsAvailable { get; }

    /// <summary>Whether this provider supports storing new credentials (vs read-only)</summary>
    bool SupportsStorage { get; }

    /// <summary>
    /// Store credentials for a service account
    /// </summary>
    Task<CredentialStoreResult> StoreAsync(
        Guid accountId,
        CredentialSet credentials,
        string? reference = null,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieve credentials for a service account
    /// </summary>
    Task<CredentialSet?> RetrieveAsync(
        Guid accountId,
        string? reference = null,
        CancellationToken ct = default);

    /// <summary>
    /// Delete stored credentials
    /// </summary>
    Task<bool> DeleteAsync(
        Guid accountId,
        string? reference = null,
        CancellationToken ct = default);

    /// <summary>
    /// Validate provider-specific configuration/reference
    /// </summary>
    ValidationResult ValidateReference(string? reference);

    /// <summary>
    /// Test connectivity to the credential backend
    /// </summary>
    Task<HealthCheckResult> TestConnectivityAsync(CancellationToken ct = default);
}
