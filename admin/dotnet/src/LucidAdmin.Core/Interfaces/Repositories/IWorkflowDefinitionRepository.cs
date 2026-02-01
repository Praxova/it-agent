using LucidAdmin.Core.Entities;

namespace LucidAdmin.Core.Interfaces.Repositories;

public interface IWorkflowDefinitionRepository : IRepository<WorkflowDefinition>
{
    /// <summary>
    /// Get workflow with all steps, transitions, and ruleset mappings.
    /// </summary>
    Task<WorkflowDefinition?> GetFullWorkflowAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Get all active workflows (for dropdown selections).
    /// </summary>
    Task<IEnumerable<WorkflowDefinition>> GetAllActiveAsync(CancellationToken ct = default);

    /// <summary>
    /// Check if workflow name exists.
    /// </summary>
    Task<bool> ExistsAsync(string name, CancellationToken ct = default);
}
