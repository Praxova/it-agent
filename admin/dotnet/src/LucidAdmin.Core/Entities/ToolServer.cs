using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Entities;

public class ToolServer : BaseEntity
{
    public required string Name { get; set; }
    public string? DisplayName { get; set; }
    public required string Endpoint { get; set; }
    public required string Domain { get; set; }
    public string? Description { get; set; }
    
    public HealthStatus Status { get; set; } = HealthStatus.Unknown;
    public DateTime? LastHeartbeat { get; set; }
    public string? Version { get; set; }
    
    /// <summary>
    /// API key for tool server authentication (hashed)
    /// </summary>
    public string? ApiKeyHash { get; set; }
    
    public bool IsEnabled { get; set; } = true;
    
    // Navigation
    public ICollection<CapabilityMapping> CapabilityMappings { get; set; } = new List<CapabilityMapping>();
    public ICollection<AuditEvent> AuditEvents { get; set; } = new List<AuditEvent>();
}
