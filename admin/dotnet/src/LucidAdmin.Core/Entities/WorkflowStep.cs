using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Entities;

/// <summary>
/// A single step/node in a workflow.
/// </summary>
public class WorkflowStep : BaseEntity
{
    /// <summary>
    /// Foreign key to parent workflow.
    /// </summary>
    public Guid WorkflowDefinitionId { get; set; }

    /// <summary>
    /// Parent workflow navigation property.
    /// </summary>
    public WorkflowDefinition? WorkflowDefinition { get; set; }

    /// <summary>
    /// Unique name within the workflow (e.g., "classify-ticket").
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Display label shown on the node.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Type of step (determines behavior and available configuration).
    /// </summary>
    public required StepType StepType { get; set; }

    /// <summary>
    /// JSON configuration specific to this step type.
    /// </summary>
    public string? ConfigurationJson { get; set; }

    /// <summary>
    /// X position on canvas (for visual layout).
    /// </summary>
    public int PositionX { get; set; } = 100;

    /// <summary>
    /// Y position on canvas (for visual layout).
    /// </summary>
    public int PositionY { get; set; } = 100;

    /// <summary>
    /// Drawflow node ID (for syncing with JS).
    /// </summary>
    public int? DrawflowNodeId { get; set; }

    /// <summary>
    /// Order for execution when multiple steps could run.
    /// </summary>
    public int SortOrder { get; set; } = 0;

    /// <summary>
    /// Outgoing transitions from this step.
    /// </summary>
    public ICollection<StepTransition> OutgoingTransitions { get; set; } = new List<StepTransition>();

    /// <summary>
    /// Incoming transitions to this step.
    /// </summary>
    public ICollection<StepTransition> IncomingTransitions { get; set; } = new List<StepTransition>();

    /// <summary>
    /// Rulesets that apply specifically to this step.
    /// </summary>
    public ICollection<StepRulesetMapping> RulesetMappings { get; set; } = new List<StepRulesetMapping>();
}
