using System.Text.Json.Serialization;

namespace LucidToolServer.Models.Requests;

/// <summary>
/// Request to reset a user's password.
/// </summary>
public record PasswordResetRequest(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("new_password")] string NewPassword
);
