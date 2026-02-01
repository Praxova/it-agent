using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Interfaces.Repositories;

/// <summary>
/// Repository for API key management
/// </summary>
public interface IApiKeyRepository : IRepository<ApiKey>
{
    /// <summary>Find API key by hash (for authentication)</summary>
    Task<ApiKey?> GetByHashAsync(string keyHash, CancellationToken ct = default);

    /// <summary>Find API key by prefix (for display/logging)</summary>
    Task<ApiKey?> GetByPrefixAsync(string keyPrefix, CancellationToken ct = default);

    /// <summary>Get all active API keys</summary>
    Task<IEnumerable<ApiKey>> GetActiveKeysAsync(CancellationToken ct = default);

    /// <summary>Get expired API keys</summary>
    Task<IEnumerable<ApiKey>> GetExpiredKeysAsync(CancellationToken ct = default);

    /// <summary>Get API keys by agent ID</summary>
    Task<IEnumerable<ApiKey>> GetByAgentIdAsync(Guid agentId, CancellationToken ct = default);

    /// <summary>Get API keys by tool server ID</summary>
    Task<IEnumerable<ApiKey>> GetByToolServerIdAsync(Guid toolServerId, CancellationToken ct = default);

    /// <summary>Get API keys by role</summary>
    Task<IEnumerable<ApiKey>> GetByRoleAsync(UserRole role, CancellationToken ct = default);

    /// <summary>Update last used timestamp</summary>
    Task UpdateLastUsedAsync(Guid id, DateTime lastUsedAt, CancellationToken ct = default);
}
