using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Infrastructure.Repositories;

public class CapabilityRepository : ICapabilityRepository
{
    private readonly LucidDbContext _context;

    public CapabilityRepository(LucidDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Capability>> GetAllAsync()
        => await _context.Capabilities.OrderBy(c => c.Category).ThenBy(c => c.DisplayName).ToListAsync();

    public async Task<IEnumerable<Capability>> GetByCategoryAsync(string category)
        => await _context.Capabilities.Where(c => c.Category == category).OrderBy(c => c.DisplayName).ToListAsync();

    public async Task<IEnumerable<Capability>> GetEnabledAsync()
        => await _context.Capabilities.Where(c => c.IsEnabled).OrderBy(c => c.Category).ThenBy(c => c.DisplayName).ToListAsync();

    public async Task<Capability?> GetByIdAsync(string capabilityId)
        => await _context.Capabilities.FirstOrDefaultAsync(c => c.CapabilityId == capabilityId);

    public async Task<Capability?> GetByIdAndVersionAsync(string capabilityId, string version)
        => await _context.Capabilities.FirstOrDefaultAsync(c => c.CapabilityId == capabilityId && c.Version == version);

    public async Task<Capability> AddAsync(Capability capability)
    {
        _context.Capabilities.Add(capability);
        await _context.SaveChangesAsync();
        return capability;
    }

    public async Task<Capability> UpdateAsync(Capability capability)
    {
        capability.UpdatedAt = DateTime.UtcNow;
        _context.Capabilities.Update(capability);
        await _context.SaveChangesAsync();
        return capability;
    }

    public async Task DeleteAsync(string capabilityId)
    {
        var capability = await GetByIdAsync(capabilityId);
        if (capability != null)
        {
            _context.Capabilities.Remove(capability);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<bool> ExistsAsync(string capabilityId)
        => await _context.Capabilities.AnyAsync(c => c.CapabilityId == capabilityId);
}
