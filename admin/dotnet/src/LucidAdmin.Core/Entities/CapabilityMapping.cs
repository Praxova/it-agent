using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Entities;

/// <summary>
/// Links a service account to a capability on a specific tool server
/// </summary>
public class CapabilityMapping : BaseEntity
{
    // === Relationships ===
    public Guid ServiceAccountId { get; set; }
    public Guid ToolServerId { get; set; }

    /// <summary>
    /// Reference to capability (e.g., "ad-password-reset")
    /// </summary>
    public required string CapabilityId { get; set; }

    /// <summary>
    /// Pinned capability version (null = use latest)
    /// </summary>
    public string? CapabilityVersion { get; set; }

    // === Configuration ===
    /// <summary>
    /// Capability-specific configuration JSON
    /// </summary>
    public string? Configuration { get; set; }

    /// <summary>
    /// JSON array of allowed scopes (OUs, groups, paths, etc.)
    /// </summary>
    public string? AllowedScopesJson { get; set; }

    /// <summary>
    /// JSON array of explicitly denied scopes
    /// </summary>
    public string? DeniedScopesJson { get; set; }

    // === Status ===
    public bool IsEnabled { get; set; } = true;
    public HealthStatus HealthStatus { get; set; } = HealthStatus.Unknown;
    public DateTime? LastHealthCheck { get; set; }
    public string? LastHealthMessage { get; set; }

    // === Navigation ===
    public ServiceAccount? ServiceAccount { get; set; }
    public ToolServer? ToolServer { get; set; }
    public Capability? Capability { get; set; }
}
