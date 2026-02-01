using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Models;

namespace LucidAdmin.Core.Interfaces.Providers;

/// <summary>
/// Interface for capability providers that handle validation and health checks
/// </summary>
public interface ICapabilityProvider
{
    /// <summary>
    /// Unique capability identifier
    /// </summary>
    string CapabilityId { get; }

    /// <summary>
    /// Current version of this capability implementation
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Category for grouping
    /// </summary>
    string Category { get; }

    /// <summary>
    /// Human-friendly display name
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Description of what this capability does
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Whether a service account is required
    /// </summary>
    bool RequiresServiceAccount { get; }

    /// <summary>
    /// Compatible service account provider IDs
    /// </summary>
    IEnumerable<string> RequiredProviders { get; }

    /// <summary>
    /// Other capability IDs this depends on
    /// </summary>
    IEnumerable<string> Dependencies { get; }

    /// <summary>
    /// Minimum Tool Server version required
    /// </summary>
    string? MinToolServerVersion { get; }

    /// <summary>
    /// Validate capability configuration JSON
    /// </summary>
    ValidationResult ValidateConfiguration(string? configurationJson);

    /// <summary>
    /// Get JSON schema for configuration
    /// </summary>
    string GetConfigurationSchema();

    /// <summary>
    /// Get example configuration JSON
    /// </summary>
    string GetConfigurationExample();

    /// <summary>
    /// Convert provider to Capability entity for seeding
    /// </summary>
    Capability ToEntity();
}
