using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Infrastructure.Data;
using LucidAdmin.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Infrastructure.Repositories;

public class AuditEventRepository : Repository<AuditEvent>, IAuditEventRepository
{
    private readonly AuditChainService _chainService;

    public AuditEventRepository(LucidDbContext context, AuditChainService chainService)
        : base(context)
    {
        _chainService = chainService;
    }

    public override async Task<AuditEvent> AddAsync(AuditEvent entity, CancellationToken ct = default)
    {
        // Route through chain service to assign sequence number and compute hashes
        await _chainService.InsertAsync(entity, ct);
        return entity;
    }

    public override Task UpdateAsync(AuditEvent entity, CancellationToken ct = default)
        => throw new InvalidOperationException("Audit records are immutable — updates are not permitted.");

    public override Task DeleteAsync(Guid id, CancellationToken ct = default)
        => throw new InvalidOperationException("Audit records are immutable — deletions are not permitted.");

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
