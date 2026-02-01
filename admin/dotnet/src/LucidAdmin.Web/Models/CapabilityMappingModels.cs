using System.Text.Json.Serialization;
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Web.Models;

public record CreateCapabilityMappingRequest(
    [property: JsonPropertyName("service_account_id")] Guid ServiceAccountId,
    [property: JsonPropertyName("tool_server_id")] Guid ToolServerId,
    [property: JsonPropertyName("capability_id")] string CapabilityId,
    [property: JsonPropertyName("capability_version")] string? CapabilityVersion,
    [property: JsonPropertyName("configuration")] string? Configuration,
    [property: JsonPropertyName("allowed_scopes_json")] string? AllowedScopesJson,
    [property: JsonPropertyName("denied_scopes_json")] string? DeniedScopesJson
);

public record UpdateCapabilityMappingRequest(
    [property: JsonPropertyName("capability_version")] string? CapabilityVersion,
    [property: JsonPropertyName("configuration")] string? Configuration,
    [property: JsonPropertyName("allowed_scopes_json")] string? AllowedScopesJson,
    [property: JsonPropertyName("denied_scopes_json")] string? DeniedScopesJson,
    [property: JsonPropertyName("is_enabled")] bool? IsEnabled
);

public record CapabilityMappingResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("service_account_id")] Guid ServiceAccountId,
    [property: JsonPropertyName("tool_server_id")] Guid ToolServerId,
    [property: JsonPropertyName("capability_id")] string CapabilityId,
    [property: JsonPropertyName("capability_version")] string? CapabilityVersion,
    [property: JsonPropertyName("configuration")] string? Configuration,
    [property: JsonPropertyName("allowed_scopes_json")] string? AllowedScopesJson,
    [property: JsonPropertyName("denied_scopes_json")] string? DeniedScopesJson,
    [property: JsonPropertyName("is_enabled")] bool IsEnabled,
    [property: JsonPropertyName("health_status")] string HealthStatus,
    [property: JsonPropertyName("last_health_check")] DateTime? LastHealthCheck,
    [property: JsonPropertyName("last_health_message")] string? LastHealthMessage,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTime UpdatedAt,
    // Display fields for UI (populated from navigation properties)
    [property: JsonPropertyName("service_account_name")] string? ServiceAccountName,
    [property: JsonPropertyName("tool_server_name")] string? ToolServerName,
    [property: JsonPropertyName("capability_display_name")] string? CapabilityDisplayName,
    [property: JsonPropertyName("capability_category")] string? CapabilityCategory
);
