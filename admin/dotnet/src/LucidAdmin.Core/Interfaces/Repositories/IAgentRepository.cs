using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for Agent entity operations.
/// </summary>
public interface IAgentRepository : IRepository<Agent>
{
    /// <summary>
    /// Gets an agent by its unique name.
    /// </summary>
    /// <param name="name">The agent name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The agent if found, otherwise null.</returns>
    Task<Agent?> GetByNameAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Gets all agents with a specific status.
    /// </summary>
    /// <param name="status">The agent status to filter by.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of agents with the specified status.</returns>
    Task<IEnumerable<Agent>> GetByStatusAsync(AgentStatus status, CancellationToken ct = default);

    /// <summary>
    /// Gets all enabled agents.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of enabled agents.</returns>
    Task<IEnumerable<Agent>> GetEnabledAsync(CancellationToken ct = default);

    /// <summary>
    /// Updates the heartbeat timestamp for an agent.
    /// </summary>
    /// <param name="id">The agent ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateHeartbeatAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Updates the status of an agent.
    /// </summary>
    /// <param name="id">The agent ID.</param>
    /// <param name="status">The new status.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateStatusAsync(Guid id, AgentStatus status, CancellationToken ct = default);
}
