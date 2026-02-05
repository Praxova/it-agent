namespace LucidAdmin.Core.Entities;

/// <summary>
/// Links an Agent to a ServiceAccount with a specific role.
/// Allows dynamic service account assignment based on workflow requirements.
/// Supplements (does not replace) the existing fixed FK fields on Agent.
/// </summary>
public class AgentServiceAccountBinding : BaseEntity
{
    public Guid AgentId { get; set; }
    public Agent Agent { get; set; } = null!;

    public Guid ServiceAccountId { get; set; }
    public ServiceAccount ServiceAccount { get; set; } = null!;

    /// <summary>
    /// The role this service account plays for this agent.
    /// Values: "trigger", "llm", "execution", "notification"
    /// </summary>
    public required string Role { get; set; }

    /// <summary>
    /// Optional qualifier when multiple accounts of the same role exist.
    /// E.g., role="trigger", qualifier="servicenow" or role="trigger", qualifier="jira"
    /// </summary>
    public string? Qualifier { get; set; }
}
