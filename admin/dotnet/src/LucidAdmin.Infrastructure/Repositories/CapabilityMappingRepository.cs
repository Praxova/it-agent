using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Infrastructure.Repositories;

public class CapabilityMappingRepository : Repository<CapabilityMapping>, ICapabilityMappingRepository
{
    public CapabilityMappingRepository(LucidDbContext context) : base(context) { }

    public async Task<IEnumerable<CapabilityMapping>> GetByToolServerIdAsync(Guid toolServerId, CancellationToken ct = default)
    {
        return await _dbSet
            .Include(cm => cm.ToolServer)
            .Include(cm => cm.ServiceAccount)
            .Where(cm => cm.ToolServerId == toolServerId)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<CapabilityMapping>> GetByServiceAccountIdAsync(Guid serviceAccountId, CancellationToken ct = default)
    {
        return await _dbSet
            .Include(cm => cm.ToolServer)
            .Include(cm => cm.ServiceAccount)
            .Where(cm => cm.ServiceAccountId == serviceAccountId)
            .ToListAsync(ct);
    }

    public async Task<CapabilityMapping?> GetByToolServerAndCapabilityAsync(Guid toolServerId, string capabilityId, CancellationToken ct = default)
    {
        return await _dbSet
            .Include(cm => cm.ToolServer)
            .Include(cm => cm.ServiceAccount)
            .FirstOrDefaultAsync(cm => cm.ToolServerId == toolServerId && cm.CapabilityId == capabilityId, ct);
    }
}
