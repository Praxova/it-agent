using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Interfaces.Repositories;

public interface IAuditEventRepository : IRepository<AuditEvent>
{
    Task<IEnumerable<AuditEvent>> GetRecentAsync(int limit = 10, CancellationToken ct = default);
    Task<IEnumerable<AuditEvent>> GetByToolServerIdAsync(Guid toolServerId, int limit = 100, CancellationToken ct = default);
    Task<IEnumerable<AuditEvent>> GetByActionAsync(AuditAction action, int limit = 100, CancellationToken ct = default);
    Task<IEnumerable<AuditEvent>> GetByDateRangeAsync(DateTime from, DateTime to, int limit = 1000, CancellationToken ct = default);
    Task<IEnumerable<AuditEvent>> SearchAsync(string? targetResource, AuditAction? action, Guid? toolServerId, DateTime? from, DateTime? to, int limit = 100, CancellationToken ct = default);
}
