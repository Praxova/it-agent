using System.Text.Json.Serialization;

namespace LucidToolServer.Models.Responses;

/// <summary>
/// Response containing information about an Active Directory group.
/// </summary>
public record GroupInfoResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("group_name")] string GroupName,
    [property: JsonPropertyName("group_dn")] string GroupDn,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("members")] List<string> Members
);
