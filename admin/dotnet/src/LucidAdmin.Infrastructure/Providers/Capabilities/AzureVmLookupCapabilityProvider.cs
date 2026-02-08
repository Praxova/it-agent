using LucidAdmin.Core.Models;

namespace LucidAdmin.Infrastructure.Providers.Capabilities;

public class AzureVmLookupCapabilityProvider : BaseCapabilityProvider
{
    public override string CapabilityId => "azure-vm-lookup";
    public override string Version => "1.0.0";
    public override string Category => "azure";
    public override string DisplayName => "Azure VM Lookup";
    public override string Description => "Query Azure Resource Manager for VM details including status, size, and IPs";
    public override bool RequiresServiceAccount => true;
    public override IEnumerable<string> RequiredProviders => new[] { "azure" };

    public override string GetConfigurationSchema() => """
        {
            "type": "object",
            "properties": {
                "defaultResourceGroup": { "type": "string", "description": "Optional default resource group to search" }
            }
        }
        """;

    public override string GetConfigurationExample() => """
        {
            "defaultResourceGroup": "production-rg"
        }
        """;
}
