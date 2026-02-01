using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Interfaces.Repositories;

public interface IToolServerRepository : IRepository<ToolServer>
{
    Task<ToolServer?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<ToolServer?> GetByEndpointAsync(string endpoint, CancellationToken ct = default);
    Task<IEnumerable<ToolServer>> GetByStatusAsync(HealthStatus status, CancellationToken ct = default);
    Task<IEnumerable<ToolServer>> GetEnabledAsync(CancellationToken ct = default);
    Task UpdateHeartbeatAsync(Guid id, HealthStatus status, CancellationToken ct = default);
}
