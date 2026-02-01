using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Interfaces.Services;

public interface IToolServerService
{
    Task<IEnumerable<ToolServer>> GetAllAsync(CancellationToken ct = default);
    Task<ToolServer?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ToolServer> CreateAsync(ToolServer server, CancellationToken ct = default);
    Task<ToolServer> UpdateAsync(ToolServer server, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<(HealthStatus Status, string Message, DateTime CheckedAt)> TestConnectivityAsync(Guid id, CancellationToken ct = default);
}
