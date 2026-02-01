using LucidAdmin.Core.Interfaces.Providers;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Core.Models;

namespace LucidAdmin.Infrastructure.Services;

public class CapabilityRegistry : ICapabilityRegistry
{
    private readonly Dictionary<string, ICapabilityProvider> _providers;

    public CapabilityRegistry(IEnumerable<ICapabilityProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.CapabilityId, StringComparer.OrdinalIgnoreCase);
    }

    public IEnumerable<ICapabilityProvider> GetAllProviders() => _providers.Values;

    public ICapabilityProvider? GetProvider(string capabilityId)
        => _providers.TryGetValue(capabilityId, out var provider) ? provider : null;

    public IEnumerable<CapabilityInfo> GetCapabilityInfos()
    {
        return _providers.Values.Select(p => new CapabilityInfo(
            p.CapabilityId,
            p.Version,
            p.Category,
            p.DisplayName,
            p.Description,
            p.RequiresServiceAccount,
            p.RequiredProviders,
            p.Dependencies,
            p.MinToolServerVersion,
            true, // IsBuiltIn
            true  // IsEnabled
        ));
    }

    public IEnumerable<string> GetCategories()
        => _providers.Values.Select(p => p.Category).Distinct().OrderBy(c => c);
}
