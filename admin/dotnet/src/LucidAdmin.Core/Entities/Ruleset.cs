namespace LucidAdmin.Core.Entities;

/// <summary>
/// A collection of rules that govern agent behavior.
/// Maps to Griptape's Ruleset class.
/// </summary>
public class Ruleset : BaseEntity
{
    /// <summary>
    /// Unique name for the ruleset (e.g., "security-rules", "escalation-rules").
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Human-friendly display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Description of what this ruleset does.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Category for organization (Security, Validation, Communication, Escalation, Custom).
    /// </summary>
    public required string Category { get; set; }

    /// <summary>
    /// Whether this is a built-in ruleset (read-only, can be copied but not edited).
    /// </summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// Whether this ruleset is active and available for use.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Rules in this ruleset.
    /// </summary>
    public ICollection<Rule> Rules { get; set; } = new List<Rule>();
}
