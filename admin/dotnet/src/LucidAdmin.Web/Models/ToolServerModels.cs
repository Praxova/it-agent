using System.Text.Json.Serialization;

namespace LucidAdmin.Web.Models;

public record CreateToolServerRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("endpoint")] string Endpoint,
    [property: JsonPropertyName("domain")] string Domain,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("api_key")] string? ApiKey
);

public record UpdateToolServerRequest(
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("is_enabled")] bool? IsEnabled
);

public record ToolServerResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("endpoint")] string Endpoint,
    [property: JsonPropertyName("domain")] string Domain,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("is_enabled")] bool IsEnabled,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("last_heartbeat")] DateTime? LastHeartbeat,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTime UpdatedAt
);

public record HeartbeatRequest(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("hostname")] string? Hostname,
    [property: JsonPropertyName("capabilities")] IEnumerable<CapabilityStatusReport>? Capabilities
);

public record CapabilityStatusReport(
    [property: JsonPropertyName("capability_id")] string CapabilityId,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string? Message
);

public record TestConnectivityResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("checked_at")] DateTime CheckedAt,
    [property: JsonPropertyName("details")] Dictionary<string, object>? Details
);

public class ToolServerFormModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = "";
    public string? DisplayName { get; set; }
    public string Endpoint { get; set; } = "";
    public string Domain { get; set; } = "";
    public string? Description { get; set; }
    public string? ApiKey { get; set; }
    public bool IsEnabled { get; set; } = true;

    public CreateToolServerRequest ToCreateRequest() => new(
        Name: Name,
        DisplayName: DisplayName,
        Endpoint: Endpoint,
        Domain: Domain,
        Description: Description,
        ApiKey: ApiKey
    );

    public UpdateToolServerRequest ToUpdateRequest() => new(
        DisplayName: DisplayName,
        Description: Description,
        IsEnabled: IsEnabled
    );

    public static ToolServerFormModel FromResponse(ToolServerResponse response) => new()
    {
        Id = response.Id,
        Name = response.Name,
        DisplayName = response.DisplayName,
        Endpoint = response.Endpoint,
        Domain = response.Domain,
        Description = response.Description,
        IsEnabled = response.IsEnabled
    };
}
