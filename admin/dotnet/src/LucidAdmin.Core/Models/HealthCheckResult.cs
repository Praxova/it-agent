using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Models;

/// <summary>
/// Result of a health check operation
/// </summary>
public record HealthCheckResult(
    HealthStatus Status,
    string Message,
    DateTime CheckedAt,
    Dictionary<string, object>? Details = null
)
{
    public static HealthCheckResult Healthy(string message = "Connection successful")
        => new(HealthStatus.Healthy, message, DateTime.UtcNow);

    public static HealthCheckResult Unhealthy(string message)
        => new(HealthStatus.Unhealthy, message, DateTime.UtcNow);

    public static HealthCheckResult Unknown(string message = "Health check not performed")
        => new(HealthStatus.Unknown, message, DateTime.UtcNow);
}
