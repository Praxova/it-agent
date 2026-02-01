using Microsoft.EntityFrameworkCore;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Infrastructure.Data;

namespace LucidAdmin.Infrastructure.Repositories;

public class WorkflowDefinitionRepository : Repository<WorkflowDefinition>, IWorkflowDefinitionRepository
{
    public WorkflowDefinitionRepository(LucidDbContext context) : base(context) { }

    public async Task<WorkflowDefinition?> GetFullWorkflowAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbSet
            .Include(w => w.ExampleSet)
            .Include(w => w.Steps.OrderBy(s => s.SortOrder))
                .ThenInclude(s => s.OutgoingTransitions)
            .Include(w => w.Steps)
                .ThenInclude(s => s.RulesetMappings)
                    .ThenInclude(m => m.Ruleset)
            .Include(w => w.RulesetMappings.OrderBy(m => m.Priority))
                .ThenInclude(m => m.Ruleset)
            .AsSplitQuery()
            .FirstOrDefaultAsync(w => w.Id == id, ct);
    }

    public async Task<IEnumerable<WorkflowDefinition>> GetAllActiveAsync(CancellationToken ct = default)
    {
        return await _dbSet
            .Where(w => w.IsActive)
            .OrderBy(w => w.Name)
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsAsync(string name, CancellationToken ct = default)
    {
        return await _dbSet.AnyAsync(w => w.Name == name, ct);
    }

    /// <summary>
    /// Override GetAllAsync to include Steps for accurate step count.
    /// </summary>
    public new async Task<IEnumerable<WorkflowDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        return await _dbSet
            .Include(w => w.ExampleSet)
            .Include(w => w.Steps)
            .OrderBy(w => w.Name)
            .ToListAsync(ct);
    }
}
