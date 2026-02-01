using System.Text.Json.Serialization;

namespace LucidToolServer.Models.Responses;

/// <summary>
/// Response containing a list of groups a user belongs to.
/// </summary>
public record UserGroupsResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("groups")] List<string> Groups
);
