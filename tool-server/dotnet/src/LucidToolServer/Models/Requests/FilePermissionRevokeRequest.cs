using System.Text.Json.Serialization;

namespace LucidToolServer.Models.Requests;

/// <summary>
/// Request to revoke file/folder permissions from a user.
/// </summary>
public record FilePermissionRevokeRequest(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("ticket_number")] string TicketNumber
);
