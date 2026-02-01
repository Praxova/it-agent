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
    
    // Navigation
    public ToolServer? ToolServer { get; set; }
    public Agent? Agent { get; set; }
}
