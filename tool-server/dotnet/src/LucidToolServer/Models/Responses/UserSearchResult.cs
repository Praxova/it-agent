using System.Text.Json.Serialization;

namespace LucidToolServer.Models.Responses;

/// <summary>
/// Individual user search result from Active Directory.
/// </summary>
public record UserSearchResult(
    [property: JsonPropertyName("sam_account_name")] string SamAccountName,
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("department")] string? Department,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("is_enabled")] bool IsEnabled
);
