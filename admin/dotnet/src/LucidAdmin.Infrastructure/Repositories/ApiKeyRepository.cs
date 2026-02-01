using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Infrastructure.Repositories;

public class ApiKeyRepository : Repository<ApiKey>, IApiKeyRepository
{
    public ApiKeyRepository(LucidDbContext context) : base(context)
    {
    }

    public async Task<ApiKey?> GetByHashAsync(string keyHash, CancellationToken ct = default)
    {
        return await _dbSet
            .Include(k => k.Agent)
            .Include(k => k.ToolServer)
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash, ct);
    }

    public async Task<ApiKey?> GetByPrefixAsync(string keyPrefix, CancellationToken ct = default)
    {
        return await _dbSet
            .Include(k => k.Agent)
            .Include(k => k.ToolServer)
            .FirstOrDefaultAsync(k => k.KeyPrefix == keyPrefix, ct);
    }

    public async Task<IEnumerable<ApiKey>> GetByAgentIdAsync(Guid agentId, CancellationToken ct = default)
    {
        return await _dbSet
            .Where(k => k.AgentId == agentId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<ApiKey>> GetByToolServerIdAsync(Guid toolServerId, CancellationToken ct = default)
    {
        return await _dbSet
            .Where(k => k.ToolServerId == toolServerId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<ApiKey>> GetActiveKeysAsync(CancellationToken ct = default)
    {
        return await _dbSet
            .Where(k => k.IsActive && k.RevokedAt == null)
            .Where(k => k.ExpiresAt == null || k.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<ApiKey>> GetExpiredKeysAsync(CancellationToken ct = default)
    {
        return await _dbSet
            .Where(k => k.ExpiresAt != null && k.ExpiresAt <= DateTime.UtcNow)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<ApiKey>> GetByRoleAsync(UserRole role, CancellationToken ct = default)
    {
        return await _dbSet
            .Where(k => k.Role == role)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);
    }

    public new async Task<IEnumerable<ApiKey>> GetAllAsync(CancellationToken ct = default)
    {
        return await _dbSet
            .Include(k => k.Agent)
            .Include(k => k.ToolServer)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);
    }

    public new async Task<ApiKey?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbSet
            .Include(k => k.Agent)
            .Include(k => k.ToolServer)
            .FirstOrDefaultAsync(k => k.Id == id, ct);
    }

    public async Task UpdateLastUsedAsync(Guid id, DateTime lastUsedAt, CancellationToken ct = default)
    {
        var key = await _dbSet.FindAsync(new object[] { id }, ct);
        if (key != null)
        {
            key.LastUsedAt = lastUsedAt;
            key.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }
    }
}
