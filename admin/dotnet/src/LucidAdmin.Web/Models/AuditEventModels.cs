using System.Text.Json.Serialization;

namespace LucidAdmin.Web.Models;

public record AuditEventResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("tool_server_id")] Guid? ToolServerId,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("capability")] string? Capability,
    [property: JsonPropertyName("performed_by")] string? PerformedBy,
    [property: JsonPropertyName("target_resource")] string? TargetResource,
    [property: JsonPropertyName("ticket_number")] string? TicketNumber,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("error_message")] string? ErrorMessage,
    [property: JsonPropertyName("details_json")] string? DetailsJson
);
