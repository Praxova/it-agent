using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Credentials;
using Microsoft.Extensions.Logging;

namespace LucidAdmin.Infrastructure.Credentials;

/// <summary>
/// Registry for credential storage providers
/// </summary>
public class CredentialProviderRegistry : ICredentialProviderRegistry
{
    private readonly Dictionary<string, ICredentialProvider> _providers;
    private readonly ICredentialProvider _defaultProvider;
    private readonly ILogger<CredentialProviderRegistry> _logger;

    public CredentialProviderRegistry(
        IEnumerable<ICredentialProvider> providers,
        ILogger<CredentialProviderRegistry> logger)
    {
        _logger = logger;
        _providers = providers.ToDictionary(p => p.ProviderId, StringComparer.OrdinalIgnoreCase);

        // Database is the default provider
        _defaultProvider = _providers.GetValueOrDefault("Database")
            ?? _providers.GetValueOrDefault("None")
            ?? throw new InvalidOperationException("No credential providers registered");

        _logger.LogInformation(
            "Credential provider registry initialized with {Count} providers: {Providers}",
            _providers.Count,
            string.Join(", ", _providers.Keys));
    }

    public ICredentialProvider? GetProvider(CredentialStorageType storageType)
    {
        return GetProvider(storageType.ToString());
    }

    public ICredentialProvider? GetProvider(string providerId)
    {
        if (_providers.TryGetValue(providerId, out var provider))
        {
            return provider;
        }

        _logger.LogWarning("Credential provider not found: {ProviderId}", providerId);
        return null;
    }

    public IEnumerable<ICredentialProvider> GetAllProviders()
    {
        return _providers.Values;
    }

    public IEnumerable<ICredentialProvider> GetAvailableProviders()
    {
        return _providers.Values.Where(p => p.IsAvailable);
    }

    public ICredentialProvider GetDefaultProvider()
    {
        return _defaultProvider;
    }
}
