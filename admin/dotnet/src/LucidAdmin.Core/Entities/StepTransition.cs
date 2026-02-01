namespace LucidAdmin.Core.Entities;

/// <summary>
/// A connection/transition between two workflow steps.
/// </summary>
public class StepTransition : BaseEntity
{
    /// <summary>
    /// Source step (where the connection starts).
    /// </summary>
    public Guid FromStepId { get; set; }

    /// <summary>
    /// Navigation property to source step.
    /// </summary>
    public WorkflowStep? FromStep { get; set; }

    /// <summary>
    /// Target step (where the connection ends).
    /// </summary>
    public Guid ToStepId { get; set; }

    /// <summary>
    /// Navigation property to target step.
    /// </summary>
    public WorkflowStep? ToStep { get; set; }

    /// <summary>
    /// Label shown on the connection (e.g., "success", "failure", "escalate").
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Condition expression for conditional transitions (e.g., "confidence &lt; 0.6").
    /// </summary>
    public string? Condition { get; set; }

    /// <summary>
    /// Output port index on source node (Drawflow uses numbered outputs).
    /// </summary>
    public int OutputIndex { get; set; } = 0;

    /// <summary>
    /// Input port index on target node.
    /// </summary>
    public int InputIndex { get; set; } = 0;

    /// <summary>
    /// Order when multiple transitions from same output.
    /// </summary>
    public int SortOrder { get; set; } = 0;
}
