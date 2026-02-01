using LucidAdmin.Core.Interfaces.Providers;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Core.Models;

namespace LucidAdmin.Infrastructure.Services;

/// <summary>
/// Registry for service account providers
/// </summary>
public class ProviderRegistry : IProviderRegistry
{
    private readonly Dictionary<string, IServiceAccountProvider> _providers;

    public ProviderRegistry(IEnumerable<IServiceAccountProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.ProviderId, StringComparer.OrdinalIgnoreCase);
    }

    public IEnumerable<IServiceAccountProvider> GetAllProviders() => _providers.Values;

    public IEnumerable<IServiceAccountProvider> GetImplementedProviders()
        => _providers.Values.Where(p => p.IsImplemented);

    public IServiceAccountProvider? GetProvider(string providerId)
        => _providers.TryGetValue(providerId, out var provider) ? provider : null;

    public IEnumerable<ProviderInfo> GetProviderInfos()
    {
        return _providers.Values.Select(p => new ProviderInfo(
            p.ProviderId,
            p.DisplayName,
            p.Description,
            p.IsImplemented,
            p.SupportedAccountTypes,
            p.SupportedCredentialStorage.Select(c => c.ToString())
        ));
    }
}
