using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Interfaces.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LucidAdmin.Infrastructure.Data.Seeding;

public class CapabilitySeeder
{
    private readonly LucidDbContext _context;
    private readonly IEnumerable<ICapabilityProvider> _providers;
    private readonly ILogger<CapabilitySeeder> _logger;

    public CapabilitySeeder(
        LucidDbContext context,
        IEnumerable<ICapabilityProvider> providers,
        ILogger<CapabilitySeeder> logger)
    {
        _context = context;
        _providers = providers;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        foreach (var provider in _providers)
        {
            var existing = await _context.Capabilities
                .FirstOrDefaultAsync(c => c.CapabilityId == provider.CapabilityId);

            if (existing == null)
            {
                var capability = provider.ToEntity();
                _context.Capabilities.Add(capability);
                _logger.LogInformation("Seeded capability: {CapabilityId} v{Version}",
                    capability.CapabilityId, capability.Version);
            }
            else if (existing.Version != provider.Version)
            {
                // Update existing capability to new version
                existing.Version = provider.Version;
                existing.DisplayName = provider.DisplayName;
                existing.Description = provider.Description;
                existing.RequiresServiceAccount = provider.RequiresServiceAccount;
                existing.RequiredProvidersJson = System.Text.Json.JsonSerializer.Serialize(provider.RequiredProviders);
                existing.DependenciesJson = System.Text.Json.JsonSerializer.Serialize(provider.Dependencies);
                existing.MinToolServerVersion = provider.MinToolServerVersion;
                existing.ConfigurationSchema = provider.GetConfigurationSchema();
                existing.ConfigurationExample = provider.GetConfigurationExample();
                existing.UpdatedAt = DateTime.UtcNow;

                _logger.LogInformation("Updated capability: {CapabilityId} to v{Version}",
                    existing.CapabilityId, provider.Version);
            }
        }

        // Clean up renamed capabilities from previous versions
        var obsoleteIds = new[] { "ad-group-mgmt", "fs-permissions" };
        var obsoleteMappings = await _context.CapabilityMappings
            .Where(cm => obsoleteIds.Contains(cm.CapabilityId))
            .ToListAsync();
        if (obsoleteMappings.Any())
        {
            _context.CapabilityMappings.RemoveRange(obsoleteMappings);
            _logger.LogWarning(
                "Removed {Count} capability mapping(s) referencing obsolete capabilities ({Ids}). " +
                "Recreate these mappings with the new capability IDs in the Admin Portal.",
                obsoleteMappings.Count, string.Join(", ", obsoleteIds));
        }

        var obsolete = await _context.Capabilities
            .Where(c => obsoleteIds.Contains(c.CapabilityId))
            .ToListAsync();
        if (obsolete.Any())
        {
            _context.Capabilities.RemoveRange(obsolete);
            _logger.LogInformation("Removed {Count} obsolete capability records: {Ids}",
                obsolete.Count, string.Join(", ", obsolete.Select(c => c.CapabilityId)));
        }

        await _context.SaveChangesAsync();
    }
}
