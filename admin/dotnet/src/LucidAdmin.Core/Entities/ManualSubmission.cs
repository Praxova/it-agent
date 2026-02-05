namespace LucidAdmin.Core.Entities;

/// <summary>
/// A manually submitted work item for testing workflows.
/// Stored in the portal database and polled by agents with manual triggers.
/// </summary>
public class ManualSubmission : BaseEntity
{
    public Guid AgentId { get; set; }
    public Agent Agent { get; set; } = null!;

    // Work item data
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Requester { get; set; }
    public string? ExtraDataJson { get; set; }

    // Status tracking
    public ManualSubmissionStatus Status { get; set; } = ManualSubmissionStatus.Pending;
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PickedUpAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Result data (populated by agent after execution)
    public string? ResultStatus { get; set; }
    public string? ResultMessage { get; set; }
    public string? ResultDetailsJson { get; set; }
}

public enum ManualSubmissionStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Escalated = 3,
    Failed = 4
}
