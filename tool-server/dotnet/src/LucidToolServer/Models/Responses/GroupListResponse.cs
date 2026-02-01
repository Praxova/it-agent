using System.Text.Json.Serialization;

namespace LucidToolServer.Models.Responses;

/// <summary>
/// Response containing list of groups from Active Directory.
/// </summary>
public record GroupListResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("groups")] List<GroupListItem> Groups,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("category_filter")] string? CategoryFilter
);
