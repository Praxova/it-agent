using System.Text.Json.Serialization;

namespace LucidToolServer.Models.Requests;

/// <summary>
/// Request to grant file/folder permissions to a user.
/// </summary>
public record FilePermissionRequest(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("permission")] string Permission,  // "Read" or "Write"
    [property: JsonPropertyName("ticket_number")] string TicketNumber
);
