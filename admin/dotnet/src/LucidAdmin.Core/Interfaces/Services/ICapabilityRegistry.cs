using LucidAdmin.Core.Interfaces.Providers;
using LucidAdmin.Core.Models;

namespace LucidAdmin.Core.Interfaces.Services;

/// <summary>
/// Registry for capability providers
/// </summary>
public interface ICapabilityRegistry
{
    IEnumerable<ICapabilityProvider> GetAllProviders();
    ICapabilityProvider? GetProvider(string capabilityId);
    IEnumerable<CapabilityInfo> GetCapabilityInfos();
    IEnumerable<string> GetCategories();
}
