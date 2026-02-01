using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Ruleset entity.
/// </summary>
public class RulesetRepository : Repository<Ruleset>, IRulesetRepository
{
    public RulesetRepository(LucidDbContext context) : base(context) { }

    public async Task<Ruleset?> GetWithRulesAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbSet
            .Include(r => r.Rules.OrderBy(rule => rule.Priority))
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<IEnumerable<Ruleset>> GetByCategoryAsync(string category, CancellationToken ct = default)
    {
        return await _dbSet
            .Where(r => r.Category == category)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Ruleset>> GetAllActiveWithRulesAsync(CancellationToken ct = default)
    {
        return await _dbSet
            .Where(r => r.IsActive)
            .Include(r => r.Rules.Where(rule => rule.IsActive).OrderBy(rule => rule.Priority))
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsAsync(string name, CancellationToken ct = default)
    {
        return await _dbSet.AnyAsync(r => r.Name == name, ct);
    }
}
