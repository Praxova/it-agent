namespace LucidAdmin.Core.Enums;

public enum HealthStatus
{
    /// <summary>
    /// All systems operational
    /// </summary>
    Healthy,
    
    /// <summary>
    /// Some capabilities impaired but operational
    /// </summary>
    Degraded,
    
    /// <summary>
    /// Major functionality broken
    /// </summary>
    Unhealthy,
    
    /// <summary>
    /// No recent heartbeat or status unknown
    /// </summary>
    Unknown
}
