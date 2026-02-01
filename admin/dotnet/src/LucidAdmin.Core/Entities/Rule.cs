namespace LucidAdmin.Core.Entities;

/// <summary>
/// A single rule that provides behavioral guidance to the agent.
/// Maps to Griptape's Rule class.
/// </summary>
public class Rule : BaseEntity
{
    /// <summary>
    /// Foreign key to parent Ruleset.
    /// </summary>
    public Guid RulesetId { get; set; }

    /// <summary>
    /// Parent Ruleset navigation property.
    /// </summary>
    public Ruleset? Ruleset { get; set; }

    /// <summary>
    /// Short name for the rule (e.g., "no-admin-password-reset").
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The actual rule text that will be included in the agent prompt.
    /// Example: "Never reset passwords for accounts that are members of the Domain Admins group."
    /// </summary>
    public required string RuleText { get; set; }

    /// <summary>
    /// Optional description explaining why this rule exists.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Priority/order within the ruleset (lower = higher priority).
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>
    /// Whether this rule is active.
    /// </summary>
    public bool IsActive { get; set; } = true;
}
