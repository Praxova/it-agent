using Microsoft.EntityFrameworkCore;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Infrastructure.Data;

namespace LucidAdmin.Infrastructure.Repositories;

public class ExampleSetRepository : Repository<ExampleSet>, IExampleSetRepository
{
    public ExampleSetRepository(LucidDbContext context) : base(context) { }

    public async Task<ExampleSet?> GetWithExamplesAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbSet
            .Include(e => e.TicketCategory)
            .Include(e => e.Examples.OrderBy(ex => ex.SortOrder))
                .ThenInclude(ex => ex.TicketCategory)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<IEnumerable<ExampleSet>> GetByCategoryIdAsync(Guid categoryId, CancellationToken ct = default)
    {
        return await _dbSet
            .Where(e => e.TicketCategoryId == categoryId)
            .OrderBy(e => e.Name)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<ExampleSet>> GetAllActiveWithExamplesAsync(CancellationToken ct = default)
    {
        return await _dbSet
            .Where(e => e.IsActive)
            .Include(e => e.TicketCategory)
            .Include(e => e.Examples.Where(ex => ex.IsActive).OrderBy(ex => ex.SortOrder))
                .ThenInclude(ex => ex.TicketCategory)
            .OrderBy(e => e.TicketCategoryId)
            .ThenBy(e => e.Name)
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsAsync(string name, CancellationToken ct = default)
    {
        return await _dbSet.AnyAsync(e => e.Name == name, ct);
    }
}
