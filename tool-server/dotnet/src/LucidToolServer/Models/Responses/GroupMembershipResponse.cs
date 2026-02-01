using System.Text.Json.Serialization;

namespace LucidToolServer.Models.Responses;

/// <summary>
/// Response from a group membership operation (add or remove).
/// </summary>
public record GroupMembershipResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("group_name")] string GroupName,
    [property: JsonPropertyName("ticket_number")] string TicketNumber
);
