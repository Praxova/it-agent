namespace LucidAdmin.Core.Enums;

/// <summary>
/// Status of an AI agent processing tickets.
/// </summary>
public enum AgentStatus
{
    /// <summary>
    /// Actively processing tickets
    /// </summary>
    Running,

    /// <summary>
    /// Gracefully stopped
    /// </summary>
    Stopped,

    /// <summary>
    /// Crashed or unhealthy
    /// </summary>
    Error,

    /// <summary>
    /// In startup sequence
    /// </summary>
    Starting,

    /// <summary>
    /// Graceful shutdown in progress
    /// </summary>
    Stopping,

    /// <summary>
    /// No recent heartbeat
    /// </summary>
    Unknown
}
