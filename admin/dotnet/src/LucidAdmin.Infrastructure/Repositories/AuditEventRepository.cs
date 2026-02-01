using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Infrastructure.Repositories;

public class AuditEventRepository : Repository<AuditEvent>, IAuditEventRepository
{
    public AuditEventRepository(LucidDbContext context) : base(context) { }

    public async Task<IEnumerable<AuditEvent>> GetRecentAsync(int limit = 10, CancellationToken ct = default)
    {
        return await _dbSet
            .OrderByDescending(ae => ae.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<AuditEvent>> GetByToolServerIdAsync(Guid toolServerId, int limit = 100, CancellationToken ct = default)
    {
        return await _dbSet
            .Where(ae => ae.ToolServerId == toolServerId)
            .OrderByDescending(ae => ae.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<AuditEvent>> GetByActionAsync(AuditAction action, int limit = 100, CancellationToken ct = default)
    {
        return await _dbSet
            .Where(ae => ae.Action == action)
            .OrderByDescending(ae => ae.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<AuditEvent>> GetByDateRangeAsync(DateTime from, DateTime to, int limit = 1000, CancellationToken ct = default)
    {
        return await _dbSet
            .Where(ae => ae.CreatedAt >= from && ae.CreatedAt <= to)
            .OrderByDescending(ae => ae.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<AuditEvent>> SearchAsync(
        string? targetResource,
        AuditAction? action,
        Guid? toolServerId,
        DateTime? from,
        DateTime? to,
        int limit = 100,
        CancellationToken ct = default)
    {
        var query = _dbSet.AsQueryable();

        if (!string.IsNullOrEmpty(targetResource))
        {
            query = query.Where(ae => ae.TargetResource != null && ae.TargetResource.Contains(targetResource));
        }

        if (action.HasValue)
        {
            query = query.Where(ae => ae.Action == action.Value);
        }

        if (toolServerId.HasValue)
        {
            query = query.Where(ae => ae.ToolServerId == toolServerId.Value);
        }

        if (from.HasValue)
        {
            query = query.Where(ae => ae.CreatedAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(ae => ae.CreatedAt <= to.Value);
        }

        return await query
            .OrderByDescending(ae => ae.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }
}
