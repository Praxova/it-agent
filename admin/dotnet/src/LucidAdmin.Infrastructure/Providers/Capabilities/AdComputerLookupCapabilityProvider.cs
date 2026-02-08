using LucidAdmin.Core.Models;

namespace LucidAdmin.Infrastructure.Providers.Capabilities;

public class AdComputerLookupCapabilityProvider : BaseCapabilityProvider
{
    public override string CapabilityId => "ad-computer-lookup";
    public override string Version => "1.0.0";
    public override string Category => "active-directory";
    public override string DisplayName => "AD Computer Lookup";
    public override string Description => "Look up user's assigned computer(s) in Active Directory";
    public override bool RequiresServiceAccount => true;
    public override IEnumerable<string> RequiredProviders => new[] { "windows-ad" };

    public override string GetConfigurationSchema() => """
        {
            "type": "object",
            "properties": {
                "searchBase": { "type": "string", "description": "LDAP search base DN (optional)" }
            }
        }
        """;

    public override string GetConfigurationExample() => """
        {
            "searchBase": "DC=montanifarms,DC=com"
        }
        """;
}
