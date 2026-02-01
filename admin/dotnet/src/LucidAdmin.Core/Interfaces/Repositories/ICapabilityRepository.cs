using LucidAdmin.Core.Entities;

namespace LucidAdmin.Core.Interfaces.Repositories;

public interface ICapabilityRepository
{
    Task<IEnumerable<Capability>> GetAllAsync();
    Task<IEnumerable<Capability>> GetByCategoryAsync(string category);
    Task<IEnumerable<Capability>> GetEnabledAsync();
    Task<Capability?> GetByIdAsync(string capabilityId);
    Task<Capability?> GetByIdAndVersionAsync(string capabilityId, string version);
    Task<Capability> AddAsync(Capability capability);
    Task<Capability> UpdateAsync(Capability capability);
    Task DeleteAsync(string capabilityId);
    Task<bool> ExistsAsync(string capabilityId);
}
