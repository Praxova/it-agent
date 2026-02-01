using System.Text.Json.Serialization;

namespace LucidToolServer.Models.Responses;

/// <summary>
/// Response from a password reset operation.
/// </summary>
public record PasswordResetResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("user_dn")] string? UserDn
);
