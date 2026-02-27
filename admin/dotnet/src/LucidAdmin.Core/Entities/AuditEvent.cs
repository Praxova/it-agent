using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Entities;

public class AuditEvent : BaseEntity
{
    public Guid? ToolServerId { get; set; }
    public Guid? AgentId { get; set; }
    public AuditAction Action { get; set; }
    public string? CapabilityId { get; set; }
    
    /// <summary>
    /// The user or service that performed the action
    /// </summary>
    public string? PerformedBy { get; set; }
    
    /// <summary>
    /// Target of the action (username, group name, path, etc.)
    /// </summary>
    public string? TargetResource { get; set; }
    
    /// <summary>
    /// Associated ticket number if applicable
    /// </summary>
    public string? TicketNumber { get; set; }
    
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Additional details as JSON
    /// </summary>
    public string? DetailsJson { get; set; }

    /// <summary>
    /// Monotonically increasing sequence number. Gaps indicate deleted records.
    /// Assigned at insert time by AuditChainService.
    /// </summary>
    public long SequenceNumber { get; set; }

    /// <summary>
    /// SHA-256 hash of this record's canonical content (excluding the hash fields).
    /// Computed at insert time. See AuditChainService.ComputeRecordHash().
    /// </summary>
    public string RecordHash { get; set; } = string.Empty;

    /// <summary>
    /// RecordHash of the immediately preceding audit record.
    /// First record uses SHA-256("PRAXOVA-AUDIT-GENESIS-v1") as the genesis value.
    /// </summary>
    public string PreviousRecordHash { get; set; } = string.Empty;

    // Navigation
    public ToolServer? ToolServer { get; set; }
    public Agent? Agent { get; set; }
}
