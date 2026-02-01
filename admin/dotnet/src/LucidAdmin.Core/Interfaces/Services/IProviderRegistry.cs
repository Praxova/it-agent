using LucidAdmin.Core.Interfaces.Providers;
using LucidAdmin.Core.Models;

namespace LucidAdmin.Core.Interfaces.Services;

/// <summary>
/// Registry for service account providers
/// </summary>
public interface IProviderRegistry
{
    /// <summary>
    /// Get all registered providers
    /// </summary>
    IEnumerable<IServiceAccountProvider> GetAllProviders();

    /// <summary>
    /// Get only implemented (non-stub) providers
    /// </summary>
    IEnumerable<IServiceAccountProvider> GetImplementedProviders();

    /// <summary>
    /// Get a specific provider by ID
    /// </summary>
    IServiceAccountProvider? GetProvider(string providerId);

    /// <summary>
    /// Get summary information about all providers
    /// </summary>
    IEnumerable<ProviderInfo> GetProviderInfos();
}
