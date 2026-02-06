using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Entities;

/// <summary>
/// A human-in-the-loop approval request created when a workflow reaches an Approval step.
/// The agent posts proposed actions here; humans approve/reject via the portal.
/// </summary>
public class ApprovalRequest : BaseEntity
{
    // === Workflow context ===

    /// <summary>Name of the workflow that created this request.</summary>
    public required string WorkflowName { get; set; }

    /// <summary>Name of the Approval step that paused the workflow.</summary>
    public required string StepName { get; set; }

    /// <summary>Name of the agent running the workflow.</summary>
    public required string AgentName { get; set; }

    /// <summary>ServiceNow ticket ID (e.g., INC0010016).</summary>
    public required string TicketId { get; set; }

    /// <summary>Short description from the ticket for display.</summary>
    public string? TicketShortDescription { get; set; }

    // === Proposed action ===

    /// <summary>Rendered description of what the agent wants to do.</summary>
    public required string ProposedAction { get; set; }

    /// <summary>Full execution context serialized as JSON for resume.</summary>
    public required string ContextSnapshotJson { get; set; }

    /// <summary>Step name the workflow should resume from after approval.</summary>
    public required string ResumeAfterStep { get; set; }

    /// <summary>Optional FK to the workflow definition.</summary>
    public Guid? WorkflowDefinitionId { get; set; }

    // === Auto-approval ===

    /// <summary>Confidence threshold for auto-approval (e.g., 0.95).</summary>
    public decimal? AutoApproveThreshold { get; set; }

    /// <summary>Classification confidence score for this request.</summary>
    public decimal? Confidence { get; set; }

    /// <summary>Whether this request was auto-approved based on threshold.</summary>
    public bool WasAutoApproved { get; set; }

    // === Decision ===

    /// <summary>Current approval status.</summary>
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

    /// <summary>Approver's note or reason for rejection.</summary>
    public string? Decision { get; set; }

    /// <summary>Username of the approver, or "system" for auto-approval.</summary>
    public string? DecidedBy { get; set; }

    /// <summary>When the decision was made.</summary>
    public DateTime? DecidedAt { get; set; }

    // === Timeout ===

    /// <summary>Number of minutes before this request times out.</summary>
    public int? TimeoutMinutes { get; set; }

    /// <summary>Computed expiration time (CreatedAt + TimeoutMinutes).</summary>
    public DateTime? ExpiresAt { get; set; }

    // === Agent consumption ===

    /// <summary>When the agent consumed (acknowledged) the decision.</summary>
    public DateTime? AcknowledgedAt { get; set; }
}
