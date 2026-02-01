namespace LucidAdmin.Core.Entities;

/// <summary>
/// Defines a capability that Tool Servers can provide
/// </summary>
public class Capability : BaseEntity
{
    // === Identity ===
    /// <summary>
    /// Unique identifier (e.g., "ad-password-reset", "snow-connector")
    /// </summary>
    public required string CapabilityId { get; set; }

    /// <summary>
    /// Semantic version (e.g., "1.0.0")
    /// </summary>
    public required string Version { get; set; }

    /// <summary>
    /// Category for grouping (e.g., "active-directory", "connector", "file-system")
    /// </summary>
    public required string Category { get; set; }

    /// <summary>
    /// Human-friendly name
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Detailed description
    /// </summary>
    public string? Description { get; set; }

    // === Requirements ===
    /// <summary>
    /// Whether this capability requires a service account
    /// </summary>
    public bool RequiresServiceAccount { get; set; } = true;

    /// <summary>
    /// JSON array of compatible service account provider IDs (e.g., ["windows-ad"])
    /// </summary>
    public string? RequiredProvidersJson { get; set; }

    /// <summary>
    /// JSON array of capability IDs this depends on (e.g., ["ad-user-lookup"])
    /// </summary>
    public string? DependenciesJson { get; set; }

    /// <summary>
    /// Minimum Tool Server version required to support this capability
    /// </summary>
    public string? MinToolServerVersion { get; set; }

    // === Schema ===
    /// <summary>
    /// JSON Schema for configuration validation
    /// </summary>
    public string? ConfigurationSchema { get; set; }

    /// <summary>
    /// Example configuration JSON
    /// </summary>
    public string? ConfigurationExample { get; set; }

    // === Metadata ===
    /// <summary>
    /// True for capabilities shipped with the product
    /// </summary>
    public bool IsBuiltIn { get; set; } = true;

    /// <summary>
    /// Whether this capability is available for use
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Link to documentation
    /// </summary>
    public string? DocumentationUrl { get; set; }

    // === Navigation ===
    public ICollection<CapabilityMapping> Mappings { get; set; } = new List<CapabilityMapping>();
}
