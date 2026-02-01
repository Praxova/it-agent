using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Exceptions;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;

namespace LucidAdmin.Web.Services;

public class AgentService : IAgentService
{
    private readonly IAgentRepository _repository;
    private readonly IAuditEventRepository _auditRepository;

    public AgentService(
        IAgentRepository repository,
        IAuditEventRepository auditRepository)
    {
        _repository = repository;
        _auditRepository = auditRepository;
    }

    public async Task<IEnumerable<Agent>> GetAllAsync(CancellationToken ct = default)
    {
        return await _repository.GetAllAsync(ct);
    }

    public async Task<Agent?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _repository.GetByIdAsync(id, ct);
    }

    public async Task<Agent> CreateAsync(Agent agent, CancellationToken ct = default)
    {
        // Validate unique name
        var existingByName = await _repository.GetByNameAsync(agent.Name, ct);
        if (existingByName != null)
        {
            throw new DuplicateEntityException("Agent", agent.Name);
        }

        // Initialize defaults
        agent.Status = AgentStatus.Unknown;

        await _repository.AddAsync(agent, ct);

        // Audit
        await _auditRepository.AddAsync(new AuditEvent
        {
            AgentId = agent.Id,
            Action = AuditAction.AgentCreated,
            PerformedBy = "System",
            TargetResource = agent.Name,
            Success = true
        }, ct);

        return agent;
    }

    public async Task<Agent> UpdateAsync(Agent agent, CancellationToken ct = default)
    {
        var existing = await _repository.GetByIdAsync(agent.Id, ct);
        if (existing == null)
        {
            throw new EntityNotFoundException("Agent", agent.Id);
        }

        await _repository.UpdateAsync(agent, ct);

        // Audit
        await _auditRepository.AddAsync(new AuditEvent
        {
            AgentId = agent.Id,
            Action = AuditAction.AgentUpdated,
            PerformedBy = "System",
            TargetResource = agent.Name,
            Success = true
        }, ct);

        return agent;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var agent = await _repository.GetByIdAsync(id, ct);
        if (agent == null)
        {
            throw new EntityNotFoundException("Agent", id);
        }

        await _repository.DeleteAsync(id, ct);

        // Audit
        await _auditRepository.AddAsync(new AuditEvent
        {
            AgentId = agent.Id,
            Action = AuditAction.AgentDeleted,
            PerformedBy = "System",
            TargetResource = agent.Name,
            Success = true
        }, ct);
    }
}
