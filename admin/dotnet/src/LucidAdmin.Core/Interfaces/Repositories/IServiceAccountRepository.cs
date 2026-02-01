using LucidAdmin.Core.Entities;

namespace LucidAdmin.Core.Interfaces.Repositories;

public interface IServiceAccountRepository : IRepository<ServiceAccount>
{
    Task<ServiceAccount?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<IEnumerable<ServiceAccount>> GetByProviderAsync(string provider, CancellationToken ct = default);
    Task<IEnumerable<ServiceAccount>> GetEnabledAsync(CancellationToken ct = default);
}
