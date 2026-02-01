namespace LucidAdmin.Core.Models;

/// <summary>
/// Information about a supported account type within a provider
/// </summary>
public record AccountTypeInfo(
    string TypeId,
    string DisplayName,
    string Description,
    bool RequiresCredential
);
