using System.Text.Json.Serialization;

namespace LucidToolServer.Models.Requests;

/// <summary>
/// Request to add or remove a user from an Active Directory group.
/// </summary>
public record GroupMembershipRequest(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("group_name")] string GroupName,
    [property: JsonPropertyName("ticket_number")] string TicketNumber
);
