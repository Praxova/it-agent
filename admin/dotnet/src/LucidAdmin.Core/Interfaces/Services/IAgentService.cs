using LucidAdmin.Core.Entities;

namespace LucidAdmin.Core.Interfaces.Services;

public interface IAgentService
{
    Task<IEnumerable<Agent>> GetAllAsync(CancellationToken ct = default);
    Task<Agent?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Agent> CreateAsync(Agent agent, CancellationToken ct = default);
    Task<Agent> UpdateAsync(Agent agent, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
