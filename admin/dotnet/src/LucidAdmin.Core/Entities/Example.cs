namespace LucidAdmin.Core.Entities;

/// <summary>
/// A single few-shot example for classifier training.
/// </summary>
public class Example : BaseEntity
{
    /// <summary>
    /// Foreign key to parent ExampleSet.
    /// </summary>
    public Guid ExampleSetId { get; set; }

    /// <summary>
    /// Parent ExampleSet navigation property.
    /// </summary>
    public ExampleSet? ExampleSet { get; set; }

    /// <summary>
    /// Short name for this example (e.g., "simple-password-reset").
    /// </summary>
    public required string Name { get; set; }

    // === INPUT (the ticket) ===

    /// <summary>
    /// The ticket short description (what appears in the subject line).
    /// </summary>
    public required string TicketShortDescription { get; set; }

    /// <summary>
    /// The ticket full description/body.
    /// </summary>
    public string? TicketDescription { get; set; }

    /// <summary>
    /// Optional: The caller/requester name for context.
    /// </summary>
    public string? CallerName { get; set; }

    // === OUTPUT (expected classification) ===

    /// <summary>
    /// Foreign key to the expected ticket category.
    /// </summary>
    public Guid? TicketCategoryId { get; set; }

    /// <summary>
    /// The expected ticket category classification.
    /// </summary>
    public TicketCategory? TicketCategory { get; set; }

    /// <summary>
    /// Expected confidence level (0.0 to 1.0).
    /// </summary>
    public decimal ExpectedConfidence { get; set; } = 0.95m;

    /// <summary>
    /// Expected affected user (extracted from ticket).
    /// Null if the affected user is the caller themselves.
    /// </summary>
    public string? ExpectedAffectedUser { get; set; }

    /// <summary>
    /// Expected target group (for group access requests).
    /// </summary>
    public string? ExpectedTargetGroup { get; set; }

    /// <summary>
    /// Expected target resource/path (for file permission requests).
    /// </summary>
    public string? ExpectedTargetResource { get; set; }

    /// <summary>
    /// Expected permission level (for file permission requests).
    /// </summary>
    public string? ExpectedPermissionLevel { get; set; }

    /// <summary>
    /// Whether this example should result in escalation.
    /// </summary>
    public bool ExpectedShouldEscalate { get; set; } = false;

    /// <summary>
    /// Reason for escalation (if ExpectedShouldEscalate is true).
    /// </summary>
    public string? ExpectedEscalationReason { get; set; }

    // === METADATA ===

    /// <summary>
    /// Notes explaining why this example is useful for training.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Order within the example set (lower = first).
    /// </summary>
    public int SortOrder { get; set; } = 0;

    /// <summary>
    /// Whether this example is active.
    /// </summary>
    public bool IsActive { get; set; } = true;
}
