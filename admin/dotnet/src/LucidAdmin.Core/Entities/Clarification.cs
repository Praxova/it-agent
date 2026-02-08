using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Entities;

public class Clarification : BaseEntity
{
    // Who created it
    public required string AgentName { get; set; }

    // Workflow context for resume
    public required string WorkflowName { get; set; }
    public required string StepName { get; set; }

    // Ticket reference
    public required string TicketId { get; set; }      // e.g., "INC0010042"
    public string? TicketSysId { get; set; }            // ServiceNow sys_id

    // The question asked
    public required string Question { get; set; }

    // Status tracking
    public ClarificationStatus Status { get; set; } = ClarificationStatus.Pending;
    public DateTime PostedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }

    // User's reply (populated when resolved)
    public string? UserReply { get; set; }

    // Full context snapshot for workflow resume (JSON)
    public string ContextSnapshotJson { get; set; } = "{}";

    // Resume info
    public string? ResumeAfterStep { get; set; }

    // Acknowledgement (agent has processed this)
    public bool IsAcknowledged { get; set; } = false;
    public DateTime? AcknowledgedAt { get; set; }
}
