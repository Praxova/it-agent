using LucidAdmin.Core.Models;

namespace LucidAdmin.Infrastructure.Providers.Capabilities;

public class AdUserLookupCapabilityProvider : BaseCapabilityProvider
{
    public override string CapabilityId => "ad-user-lookup";
    public override string Version => "1.0.0";
    public override string Category => "active-directory";
    public override string DisplayName => "AD User Lookup";
    public override string Description => "Look up user information in Active Directory";
    public override bool RequiresServiceAccount => true;
    public override IEnumerable<string> RequiredProviders => new[] { "windows-ad" };

    public override string GetConfigurationSchema() => """
        {
            "type": "object",
            "properties": {
                "searchBase": { "type": "string", "description": "LDAP search base DN" },
                "attributes": { "type": "array", "items": { "type": "string" }, "description": "Attributes to retrieve" }
            }
        }
        """;

    public override string GetConfigurationExample() => """
        {
            "searchBase": "DC=montanifarms,DC=com",
            "attributes": ["displayName", "mail", "department", "manager"]
        }
        """;
}
