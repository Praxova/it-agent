using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Interfaces.Providers;
using LucidAdmin.Core.Models;

namespace LucidAdmin.Infrastructure.Providers.Capabilities;

public abstract class BaseCapabilityProvider : ICapabilityProvider
{
    public abstract string CapabilityId { get; }
    public abstract string Version { get; }
    public abstract string Category { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public abstract bool RequiresServiceAccount { get; }
    public abstract IEnumerable<string> RequiredProviders { get; }
    public virtual IEnumerable<string> Dependencies => Array.Empty<string>();
    public virtual string? MinToolServerVersion => "1.0.0";

    public virtual ValidationResult ValidateConfiguration(string? configurationJson)
    {
        return ValidationResult.Success();
    }

    public abstract string GetConfigurationSchema();
    public abstract string GetConfigurationExample();

    public Capability ToEntity() => new()
    {
        CapabilityId = CapabilityId,
        Version = Version,
        Category = Category,
        DisplayName = DisplayName,
        Description = Description,
        RequiresServiceAccount = RequiresServiceAccount,
        RequiredProvidersJson = System.Text.Json.JsonSerializer.Serialize(RequiredProviders),
        DependenciesJson = System.Text.Json.JsonSerializer.Serialize(Dependencies),
        MinToolServerVersion = MinToolServerVersion,
        ConfigurationSchema = GetConfigurationSchema(),
        ConfigurationExample = GetConfigurationExample(),
        IsBuiltIn = true,
        IsEnabled = true
    };
}
