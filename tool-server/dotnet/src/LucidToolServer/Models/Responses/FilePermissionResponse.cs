using System.Text.Json.Serialization;

namespace LucidToolServer.Models.Responses;

/// <summary>
/// Response from a file permission grant or revoke operation.
/// </summary>
public record FilePermissionResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("action")] string Action,  // "granted" or "revoked"
    [property: JsonPropertyName("permission")] string? Permission,
    [property: JsonPropertyName("message")] string Message
);
