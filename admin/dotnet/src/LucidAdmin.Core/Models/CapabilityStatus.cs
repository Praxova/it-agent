using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Models;

/// <summary>
/// Capability status reported by Tool Server in heartbeat
/// </summary>
public record CapabilityStatus(
    string CapabilityId,
    string Version,
    HealthStatus Status,
    string? Message = null
);
