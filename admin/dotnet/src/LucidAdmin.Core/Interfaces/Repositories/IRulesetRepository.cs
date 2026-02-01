using LucidAdmin.Core.Entities;

namespace LucidAdmin.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for Ruleset entity with specialized query methods.
/// </summary>
public interface IRulesetRepository : IRepository<Ruleset>
{
    /// <summary>
    /// Gets a ruleset by ID with all its rules loaded.
    /// </summary>
    Task<Ruleset?> GetWithRulesAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Gets all rulesets in a specific category.
    /// </summary>
    Task<IEnumerable<Ruleset>> GetByCategoryAsync(string category, CancellationToken ct = default);

    /// <summary>
    /// Gets all active rulesets with their rules loaded.
    /// </summary>
    Task<IEnumerable<Ruleset>> GetAllActiveWithRulesAsync(CancellationToken ct = default);

    /// <summary>
    /// Checks if a ruleset with the given name already exists.
    /// </summary>
    Task<bool> ExistsAsync(string name, CancellationToken ct = default);
}
