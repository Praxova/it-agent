using LucidAdmin.Core.Models;

namespace LucidAdmin.Infrastructure.Providers.Capabilities;

public class AzureUserLookupCapabilityProvider : BaseCapabilityProvider
{
    public override string CapabilityId => "azure-user-lookup";
    public override string Version => "1.0.0";
    public override string Category => "azure";
    public override string DisplayName => "Azure AD / Entra ID User Lookup";
    public override string Description => "Query Microsoft Entra ID for user details via Microsoft Graph API";
    public override bool RequiresServiceAccount => true;
    public override IEnumerable<string> RequiredProviders => new[] { "azure" };

    public override string GetConfigurationSchema() => """
        {
            "type": "object",
            "properties": {
                "selectFields": { "type": "array", "items": { "type": "string" }, "description": "Graph $select fields to retrieve" }
            }
        }
        """;

    public override string GetConfigurationExample() => """
        {
            "selectFields": ["displayName", "jobTitle", "department", "manager", "assignedLicenses", "signInActivity"]
        }
        """;
}
