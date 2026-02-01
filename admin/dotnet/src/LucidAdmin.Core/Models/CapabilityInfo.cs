namespace LucidAdmin.Core.Models;

/// <summary>
/// Summary information about a capability
/// </summary>
public record CapabilityInfo(
    string CapabilityId,
    string Version,
    string Category,
    string DisplayName,
    string? Description,
    bool RequiresServiceAccount,
    IEnumerable<string> RequiredProviders,
    IEnumerable<string> Dependencies,
    string? MinToolServerVersion,
    bool IsBuiltIn,
    bool IsEnabled
);
