using System.Text.Json.Serialization;

namespace LucidToolServer.Models.Responses;

/// <summary>
/// Response containing a list of permissions on a file or folder.
/// </summary>
public record FilePermissionListResponse(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("permissions")] List<PermissionEntry> Permissions
);

/// <summary>
/// Represents a single permission entry on a file or folder.
/// </summary>
public record PermissionEntry(
    [property: JsonPropertyName("identity")] string Identity,
    [property: JsonPropertyName("access_type")] string AccessType,
    [property: JsonPropertyName("rights")] string Rights,
    [property: JsonPropertyName("is_inherited")] bool IsInherited
);
