using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Interfaces.Services;

/// <summary>
/// Result of creating an API key - includes the plaintext key (shown only once)
/// </summary>
public record ApiKeyCreateResult(
    ApiKey Key,
    string PlaintextKey,
    bool Success,
    string? ErrorMessage = null
)
{
    public static ApiKeyCreateResult Succeeded(ApiKey key, string plaintextKey)
        => new(key, plaintextKey, true);

    public static ApiKeyCreateResult Failed(string error)
        => new(null!, string.Empty, false, error);
}

/// <summary>
/// Result of validating an API key
/// </summary>
public record ApiKeyValidationResult(
    bool IsValid,
    ApiKey? Key,
    string? ErrorMessage = null
)
{
    public static ApiKeyValidationResult Valid(ApiKey key) => new(true, key);
    public static ApiKeyValidationResult Invalid(string reason) => new(false, null, reason);
}

/// <summary>
/// Service for managing API keys
/// </summary>
public interface IApiKeyService
{
    /// <summary>
    /// Create a new API key for an agent
    /// </summary>
    Task<ApiKeyCreateResult> CreateAgentKeyAsync(
        Guid agentId,
        string name,
        string? description = null,
        DateTime? expiresAt = null,
        CancellationToken ct = default);

    /// <summary>
    /// Create a new API key for a tool server
    /// </summary>
    Task<ApiKeyCreateResult> CreateToolServerKeyAsync(
        Guid toolServerId,
        string name,
        string? description = null,
        DateTime? expiresAt = null,
        CancellationToken ct = default);

    /// <summary>
    /// Validate an API key and return the associated key entity
    /// </summary>
    Task<ApiKeyValidationResult> ValidateKeyAsync(
        string plaintextKey,
        CancellationToken ct = default);

    /// <summary>
    /// Revoke an API key
    /// </summary>
    Task<bool> RevokeKeyAsync(
        Guid keyId,
        string revokedBy,
        string? reason = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get all API keys (for admin UI)
    /// </summary>
    Task<IEnumerable<ApiKey>> GetAllKeysAsync(CancellationToken ct = default);

    /// <summary>
    /// Get API keys for a specific agent
    /// </summary>
    Task<IEnumerable<ApiKey>> GetAgentKeysAsync(Guid agentId, CancellationToken ct = default);

    /// <summary>
    /// Get API keys for a specific tool server
    /// </summary>
    Task<IEnumerable<ApiKey>> GetToolServerKeysAsync(Guid toolServerId, CancellationToken ct = default);

    /// <summary>
    /// Update last used timestamp for a key
    /// </summary>
    Task UpdateLastUsedAsync(Guid keyId, string? ipAddress = null, CancellationToken ct = default);

    /// <summary>
    /// Check if an agent is allowed to access a specific service account's credentials
    /// </summary>
    bool IsServiceAccountAllowed(ApiKey agentKey, Guid serviceAccountId);

    /// <summary>
    /// Update the allowed service accounts for an agent's API key
    /// </summary>
    Task UpdateAllowedServiceAccountsAsync(
        Guid keyId,
        IEnumerable<Guid> serviceAccountIds,
        CancellationToken ct = default);
}
