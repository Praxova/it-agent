using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Infrastructure.Repositories;

public class ToolServerRepository : Repository<ToolServer>, IToolServerRepository
{
    public ToolServerRepository(LucidDbContext context) : base(context) { }

    public async Task<ToolServer?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        return await _dbSet
            .AsNoTracking()
            .FirstOrDefaultAsync(ts => ts.Name == name, ct);
    }

    public async Task<ToolServer?> GetByEndpointAsync(string endpoint, CancellationToken ct = default)
    {
        return await _dbSet
            .AsNoTracking()
            .FirstOrDefaultAsync(ts => ts.Endpoint == endpoint, ct);
    }

    public async Task<IEnumerable<ToolServer>> GetByStatusAsync(HealthStatus status, CancellationToken ct = default)
    {
        return await _dbSet
            .Include(ts => ts.CapabilityMappings)
            .Where(ts => ts.Status == status)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<ToolServer>> GetEnabledAsync(CancellationToken ct = default)
    {
        return await _dbSet
            .Include(ts => ts.CapabilityMappings)
            .Where(ts => ts.IsEnabled)
            .ToListAsync(ct);
    }

    public async Task UpdateHeartbeatAsync(Guid id, HealthStatus status, CancellationToken ct = default)
    {
        var server = await GetByIdAsync(id, ct);
        if (server != null)
        {
            server.LastHeartbeat = DateTime.UtcNow;
            server.Status = status;
            await UpdateAsync(server, ct);
        }
    }
}
