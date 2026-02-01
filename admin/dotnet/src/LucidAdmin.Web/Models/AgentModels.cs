using System.Text.Json.Serialization;

namespace LucidAdmin.Web.Models;

public record CreateAgentRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("host_name")] string? HostName,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("llm_service_account_id")] Guid? LlmServiceAccountId,
    [property: JsonPropertyName("servicenow_account_id")] Guid? ServiceNowAccountId,
    [property: JsonPropertyName("assignment_group")] string? AssignmentGroup
);

public record UpdateAgentRequest(
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("llm_service_account_id")] Guid? LlmServiceAccountId,
    [property: JsonPropertyName("servicenow_account_id")] Guid? ServiceNowAccountId,
    [property: JsonPropertyName("assignment_group")] string? AssignmentGroup,
    [property: JsonPropertyName("is_enabled")] bool? IsEnabled
);

public record AgentResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("host_name")] string? HostName,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("llm_service_account_id")] Guid? LlmServiceAccountId,
    [property: JsonPropertyName("servicenow_account_id")] Guid? ServiceNowAccountId,
    [property: JsonPropertyName("assignment_group")] string? AssignmentGroup,
    [property: JsonPropertyName("last_activity")] DateTime? LastActivity,
    [property: JsonPropertyName("last_heartbeat")] DateTime? LastHeartbeat,
    [property: JsonPropertyName("tickets_processed")] int TicketsProcessed,
    [property: JsonPropertyName("is_enabled")] bool IsEnabled,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTime UpdatedAt
);

public record AgentHeartbeatRequest(
    [property: JsonPropertyName("status")] string Status
);

public record AgentStatusUpdateRequest(
    [property: JsonPropertyName("status")] string Status
);

public class AgentFormModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? HostName { get; set; }
    public string? Description { get; set; }
    public Guid? LlmServiceAccountId { get; set; }
    public Guid? ServiceNowAccountId { get; set; }
    public string? AssignmentGroup { get; set; }
    public bool IsEnabled { get; set; } = true;

    public CreateAgentRequest ToCreateRequest() => new(
        Name: Name,
        DisplayName: DisplayName,
        HostName: HostName,
        Description: Description,
        LlmServiceAccountId: LlmServiceAccountId,
        ServiceNowAccountId: ServiceNowAccountId,
        AssignmentGroup: AssignmentGroup
    );

    public UpdateAgentRequest ToUpdateRequest() => new(
        DisplayName: DisplayName,
        Description: Description,
        LlmServiceAccountId: LlmServiceAccountId,
        ServiceNowAccountId: ServiceNowAccountId,
        AssignmentGroup: AssignmentGroup,
        IsEnabled: IsEnabled
    );

    public static AgentFormModel FromResponse(AgentResponse response) => new()
    {
        Id = response.Id,
        Name = response.Name,
        DisplayName = response.DisplayName,
        HostName = response.HostName,
        Description = response.Description,
        LlmServiceAccountId = response.LlmServiceAccountId,
        ServiceNowAccountId = response.ServiceNowAccountId,
        AssignmentGroup = response.AssignmentGroup,
        IsEnabled = response.IsEnabled
    };
}
