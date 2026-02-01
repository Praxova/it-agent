namespace LucidAdmin.Core.Entities;

/// <summary>
/// Links a ruleset to a specific workflow step.
/// </summary>
public class StepRulesetMapping : BaseEntity
{
    public Guid WorkflowStepId { get; set; }
    public WorkflowStep? WorkflowStep { get; set; }

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
