using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Interfaces.Credentials;

/// <summary>
/// Registry for credential storage providers
/// </summary>
public interface ICredentialProviderRegistry
{
    /// <summary>Get provider by storage type</summary>
    ICredentialProvider? GetProvider(CredentialStorageType storageType);

    /// <summary>Get provider by string ID</summary>
    ICredentialProvider? GetProvider(string providerId);

    /// <summary>Get all registered providers</summary>
    IEnumerable<ICredentialProvider> GetAllProviders();

    /// <summary>Get all available (configured and ready) providers</summary>
    IEnumerable<ICredentialProvider> GetAvailableProviders();

    /// <summary>Get the default provider (Database)</summary>
    ICredentialProvider GetDefaultProvider();
}
