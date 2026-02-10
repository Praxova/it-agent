using LucidAdmin.Core.Entities;

namespace LucidAdmin.Core.Interfaces.Repositories;

public interface IExampleSetRepository : IRepository<ExampleSet>
{
    /// <summary>
    /// Get example set with all its examples loaded.
    /// </summary>
    Task<ExampleSet?> GetWithExamplesAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Get all example sets for a specific ticket category.
    /// </summary>
    Task<IEnumerable<ExampleSet>> GetByCategoryIdAsync(Guid categoryId, CancellationToken ct = default);

    /// <summary>
    /// Get all active example sets with their examples for classifier training.
    /// </summary>
    Task<IEnumerable<ExampleSet>> GetAllActiveWithExamplesAsync(CancellationToken ct = default);

    /// <summary>
    /// Check if an example set name already exists.
    /// </summary>
    Task<bool> ExistsAsync(string name, CancellationToken ct = default);
}
