using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Agent entity.
/// </summary>
public class AgentRepository : Repository<Agent>, IAgentRepository
{
    public AgentRepository(LucidDbContext context) : base(context) { }

    public async Task<Agent?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        return await _dbSet
            .Include(a => a.AuditEvents)
            .FirstOrDefaultAsync(a => a.Name == name, ct);
    }

    public async Task<IEnumerable<Agent>> GetByStatusAsync(AgentStatus status, CancellationToken ct = default)
    {
        return await _dbSet
            .Where(a => a.Status == status)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Agent>> GetEnabledAsync(CancellationToken ct = default)
    {
        return await _dbSet
            .Where(a => a.IsEnabled)
            .ToListAsync(ct);
    }

    public async Task UpdateHeartbeatAsync(Guid id, CancellationToken ct = default)
    {
        var agent = await GetByIdAsync(id, ct);
        if (agent != null)
        {
            agent.LastHeartbeat = DateTime.UtcNow;
            await UpdateAsync(agent, ct);
        }
    }

    public async Task UpdateStatusAsync(Guid id, AgentStatus status, CancellationToken ct = default)
    {
        var agent = await GetByIdAsync(id, ct);
        if (agent != null)
        {
            agent.Status = status;
            agent.UpdatedAt = DateTime.UtcNow;
            await UpdateAsync(agent, ct);
        }
    }
}
