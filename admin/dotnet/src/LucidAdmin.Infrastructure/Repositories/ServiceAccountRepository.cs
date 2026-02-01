using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Infrastructure.Repositories;

public class ServiceAccountRepository : Repository<ServiceAccount>, IServiceAccountRepository
{
    public ServiceAccountRepository(LucidDbContext context) : base(context) { }

    public async Task<ServiceAccount?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        return await _dbSet
            .Include(sa => sa.CapabilityMappings)
            .ThenInclude(cm => cm.ToolServer)
            .FirstOrDefaultAsync(sa => sa.Name == name, ct);
    }

    public async Task<IEnumerable<ServiceAccount>> GetByProviderAsync(string provider, CancellationToken ct = default)
    {
        return await _dbSet
            .Include(sa => sa.CapabilityMappings)
            .Where(sa => sa.Provider == provider)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<ServiceAccount>> GetEnabledAsync(CancellationToken ct = default)
    {
        return await _dbSet
            .Include(sa => sa.CapabilityMappings)
            .Where(sa => sa.IsEnabled)
            .ToListAsync(ct);
    }
}
