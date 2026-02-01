using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Entities;

/// <summary>
/// API Key for programmatic authentication (agents, tool servers)
/// </summary>
public class ApiKey : BaseEntity
{
    /// <summary>Friendly name</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Description/notes</summary>
    public string? Description { get; set; }

    /// <summary>SHA256 hash of the API key (never store plaintext)</summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>Prefix of the key (e.g., "luc_" + first 8 chars) for display/logging</summary>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>Role granted to this API key</summary>
    public UserRole Role { get; set; }

    /// <summary>Optional expiration date</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Last time the key was used</summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>IP address the key was last used from</summary>
    public string? LastUsedFromIp { get; set; }

    /// <summary>Whether the key is currently active</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>When the key was revoked (if applicable)</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>Who revoked the key</summary>
    public string? RevokedBy { get; set; }

    /// <summary>Reason for revocation</summary>
    public string? RevocationReason { get; set; }

    /// <summary>Who/what created this key</summary>
    public string? CreatedBy { get; set; }

    /// <summary>IP address restrictions (comma-separated CIDRs, null = no restriction)</summary>
    public string? IpRestrictions { get; set; }

    /// <summary>Additional metadata as JSON</summary>
    public string? Metadata { get; set; }

    // === Agent/Tool Server Association ===
    /// <summary>Agent this key belongs to (if applicable)</summary>
    public Guid? AgentId { get; set; }

    /// <summary>Navigation property to agent</summary>
    public Agent? Agent { get; set; }

    /// <summary>Tool server this key belongs to (if applicable)</summary>
    public Guid? ToolServerId { get; set; }

    /// <summary>Navigation property to tool server</summary>
    public ToolServer? ToolServer { get; set; }

    /// <summary>JSON-serialized list of service account IDs this agent key can access</summary>
    public string? AllowedServiceAccountIds { get; set; }

    /// <summary>Check if the key is currently valid</summary>
    public bool IsValid()
    {
        if (!IsActive) return false;
        if (RevokedAt != null) return false;
        if (ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow) return false;
        return true;
    }
}
