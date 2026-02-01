namespace LucidAdmin.Core.Entities;

/// <summary>
/// Links a ruleset to an entire workflow (applies to all steps).
/// </summary>
public class WorkflowRulesetMapping : BaseEntity
{
    public Guid WorkflowDefinitionId { get; set; }
    public WorkflowDefinition? WorkflowDefinition { get; set; }

    public Guid RulesetId { get; set; }
    public Ruleset? Ruleset { get; set; }

    /// <summary>
    /// Order in which rulesets are evaluated.
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// Whether this mapping is active.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
