using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Core.Models;

namespace LucidAdmin.Infrastructure.Services;

public class DashboardService : IDashboardService
{
    private readonly IToolServerRepository _toolServerRepo;
    private readonly IAgentRepository _agentRepo;
    private readonly IServiceAccountRepository _serviceAccountRepo;
    private readonly ICapabilityRepository _capabilityRepo;
    private readonly ICapabilityMappingRepository _mappingRepo;
    private readonly IAuditEventRepository _auditRepo;

    public DashboardService(
        IToolServerRepository toolServerRepo,
        IAgentRepository agentRepo,
        IServiceAccountRepository serviceAccountRepo,
        ICapabilityRepository capabilityRepo,
        ICapabilityMappingRepository mappingRepo,
        IAuditEventRepository auditRepo)
    {
        _toolServerRepo = toolServerRepo;
        _agentRepo = agentRepo;
        _serviceAccountRepo = serviceAccountRepo;
        _capabilityRepo = capabilityRepo;
        _mappingRepo = mappingRepo;
        _auditRepo = auditRepo;
    }

    public async Task<DashboardStats> GetStatsAsync(int recentAuditCount = 10, CancellationToken ct = default)
    {
        var toolServers = (await _toolServerRepo.GetAllAsync()).ToList();
        var agents = (await _agentRepo.GetAllAsync()).ToList();
        var serviceAccounts = (await _serviceAccountRepo.GetAllAsync()).ToList();
        var capabilities = (await _capabilityRepo.GetAllAsync()).ToList();
        var mappings = (await _mappingRepo.GetAllAsync()).ToList();
        var recentAudits = (await _auditRepo.GetRecentAsync(recentAuditCount)).ToList();

        return new DashboardStats(
            ToolServers: new EntityStats(
                toolServers.Count,
                toolServers.GroupBy(t => t.Status.ToString()).ToDictionary(g => g.Key, g => g.Count())
            ),
            Agents: new EntityStats(
                agents.Count,
                agents.GroupBy(a => a.Status.ToString()).ToDictionary(g => g.Key, g => g.Count())
            ),
            ServiceAccounts: new ServiceAccountStats(
                serviceAccounts.Count,
                serviceAccounts.Count(s => s.IsEnabled),
                serviceAccounts.Count(s => !s.IsEnabled),
                serviceAccounts.GroupBy(s => s.Provider).ToDictionary(g => g.Key, g => g.Count())
            ),
            Capabilities: new CapabilityStats(
                capabilities.Count,
                capabilities.Count(c => c.IsEnabled),
                capabilities.GroupBy(c => c.Category).ToDictionary(g => g.Key, g => g.Count())
            ),
            CapabilityMappings: new MappingStats(
                mappings.Count,
                mappings.Count(m => m.IsEnabled),
                mappings.GroupBy(m => m.HealthStatus.ToString()).ToDictionary(g => g.Key, g => g.Count())
            ),
            RecentAuditEvents: recentAudits.Select(a => new RecentAuditEvent(
                a.Id,
                a.Action.ToString(),
                a.CreatedAt,
                a.Success,
                null, // TargetUser - not tracked separately in AuditEvent
                a.TargetResource,
                a.PerformedBy
            )),
            LastUpdated: DateTime.UtcNow
        );
    }
}
