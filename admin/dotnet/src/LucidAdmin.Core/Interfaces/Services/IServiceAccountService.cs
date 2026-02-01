using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Interfaces.Services;

public interface IServiceAccountService
{
    Task<IEnumerable<ServiceAccount>> GetAllAsync(CancellationToken ct = default);
    Task<ServiceAccount?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ServiceAccount> CreateAsync(ServiceAccount account, CancellationToken ct = default);
    Task<ServiceAccount> UpdateAsync(ServiceAccount account, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<(HealthStatus Status, string Message, DateTime CheckedAt, Dictionary<string, object>? Details)> TestConnectivityAsync(Guid id, CancellationToken ct = default);
}
