namespace LucidAdmin.Core.Models;

public record DashboardStats(
    EntityStats ToolServers,
    EntityStats Agents,
    ServiceAccountStats ServiceAccounts,
    CapabilityStats Capabilities,
    MappingStats CapabilityMappings,
    IEnumerable<RecentAuditEvent> RecentAuditEvents,
    DateTime LastUpdated
);

public record EntityStats(
    int Total,
    Dictionary<string, int> ByStatus
);

public record ServiceAccountStats(
    int Total,
    int Enabled,
    int Disabled,
    Dictionary<string, int> ByProvider
);

public record CapabilityStats(
    int Total,
    int Enabled,
    Dictionary<string, int> ByCategory
);

public record MappingStats(
    int Total,
    int Enabled,
    Dictionary<string, int> ByHealthStatus
);

public record RecentAuditEvent(
    Guid Id,
    string Action,
    DateTime Timestamp,
    bool Success,
    string? TargetUser,
    string? TargetResource,
    string? PerformedBy
);
