namespace LucidAdmin.Core.Entities;

/// <summary>
/// A workflow defines how tickets are processed through a series of steps.
/// </summary>
public class WorkflowDefinition : BaseEntity
{
    /// <summary>
    /// Unique name for the workflow (e.g., "password-reset-standard").
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Human-friendly display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Description of what this workflow handles.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Version string for tracking changes.
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Whether this workflow ships with the product.
    /// </summary>
    public bool IsBuiltIn { get; set; } = false;

    /// <summary>
    /// Whether this workflow is active and can be assigned to agents.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// JSON export from Drawflow containing visual layout.
    /// </summary>
    public string? LayoutJson { get; set; }

    /// <summary>
    /// Type of trigger that initiates this workflow (e.g., "ServiceNow", "Email", "Manual").
    /// </summary>
    public string? TriggerType { get; set; }

    /// <summary>
    /// JSON configuration for the trigger (e.g., poll interval, filter criteria).
    /// </summary>
    public string? TriggerConfigJson { get; set; }

    /// <summary>
    /// Optional: Example set used for classifier training in this workflow.
    /// </summary>
    public Guid? ExampleSetId { get; set; }

    /// <summary>
    /// Navigation property to example set.
    /// </summary>
    public ExampleSet? ExampleSet { get; set; }

    /// <summary>
    /// Steps in this workflow.
    /// </summary>
    public ICollection<WorkflowStep> Steps { get; set; } = new List<WorkflowStep>();

    /// <summary>
    /// Rulesets that apply to all steps in this workflow.
    /// </summary>
    public ICollection<WorkflowRulesetMapping> RulesetMappings { get; set; } = new List<WorkflowRulesetMapping>();
}
