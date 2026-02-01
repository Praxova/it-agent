using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Entities;

/// <summary>
/// Represents an AI agent instance that processes tickets.
/// Agents get their LLM and ServiceNow configuration from ServiceAccount references.
/// Tool Server access is resolved at runtime via Capability Routing.
/// </summary>
public class Agent : BaseEntity
{
    // === Identity ===

    /// <summary>
    /// Unique identifier for the agent (e.g., "helpdesk-agent-01")
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Friendly display name for the agent
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Description of the agent's purpose
    /// </summary>
    public string? Description { get; set; }

    // === Runtime Information ===

    /// <summary>
    /// Server hostname where the agent runs (populated at runtime)
    /// </summary>
    public string? HostName { get; set; }

    /// <summary>
    /// Current operational status of the agent
    /// </summary>
    public AgentStatus Status { get; set; } = AgentStatus.Unknown;

    /// <summary>
    /// Timestamp of last ticket processed
    /// </summary>
    public DateTime? LastActivity { get; set; }

    /// <summary>
    /// Timestamp of last health check/heartbeat
    /// </summary>
    public DateTime? LastHeartbeat { get; set; }

    /// <summary>
    /// Total number of tickets processed by this agent
    /// </summary>
    public int TicketsProcessed { get; set; } = 0;

    // === LLM Configuration (via ServiceAccount) ===

    /// <summary>
    /// Reference to the LLM provider ServiceAccount (Provider = "llm-ollama", "llm-openai", etc.)
    /// </summary>
    public Guid? LlmServiceAccountId { get; set; }

    /// <summary>
    /// Navigation property to LLM ServiceAccount
    /// </summary>
    public ServiceAccount? LlmServiceAccount { get; set; }

    // === ServiceNow Configuration (via ServiceAccount) ===

    /// <summary>
    /// Reference to the ServiceNow ServiceAccount (Provider = "servicenow-basic" or "servicenow-oauth")
    /// </summary>
    public Guid? ServiceNowAccountId { get; set; }

    /// <summary>
    /// Navigation property to ServiceNow ServiceAccount
    /// </summary>
    public ServiceAccount? ServiceNowAccount { get; set; }

    /// <summary>
    /// Assignment group or ticket queue to monitor in ServiceNow
    /// </summary>
    public string? AssignmentGroup { get; set; }

    // === Workflow Configuration ===

    /// <summary>
    /// Reference to the workflow definition that defines how this agent processes tickets
    /// </summary>
    public Guid? WorkflowDefinitionId { get; set; }

    /// <summary>
    /// Navigation property to workflow definition
    /// </summary>
    public WorkflowDefinition? WorkflowDefinition { get; set; }

    // === Behavior ===

    /// <summary>
    /// Whether the agent is enabled for operation
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    // === Navigation Properties ===

    public ICollection<AuditEvent> AuditEvents { get; set; } = new List<AuditEvent>();
}
