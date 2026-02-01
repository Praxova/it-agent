namespace LucidAdmin.Core.Models;

/// <summary>
/// Summary information about a provider (for API responses)
/// </summary>
public record ProviderInfo(
    string ProviderId,
    string DisplayName,
    string Description,
    bool IsImplemented,
    IEnumerable<AccountTypeInfo> AccountTypes,
    IEnumerable<string> CredentialStorageTypes
);
