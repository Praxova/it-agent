using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Interfaces.Repositories;

public interface ICapabilityMappingRepository : IRepository<CapabilityMapping>
{
    Task<IEnumerable<CapabilityMapping>> GetByToolServerIdAsync(Guid toolServerId, CancellationToken ct = default);
    Task<IEnumerable<CapabilityMapping>> GetByServiceAccountIdAsync(Guid serviceAccountId, CancellationToken ct = default);
    Task<CapabilityMapping?> GetByToolServerAndCapabilityAsync(Guid toolServerId, string capabilityId, CancellationToken ct = default);
}
