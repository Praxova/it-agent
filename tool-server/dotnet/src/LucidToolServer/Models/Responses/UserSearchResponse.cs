using System.Text.Json.Serialization;

namespace LucidToolServer.Models.Responses;

/// <summary>
/// Response containing user search results from Active Directory.
/// </summary>
public record UserSearchResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("results")] List<UserSearchResult> Results,
    [property: JsonPropertyName("count")] int Count
);
