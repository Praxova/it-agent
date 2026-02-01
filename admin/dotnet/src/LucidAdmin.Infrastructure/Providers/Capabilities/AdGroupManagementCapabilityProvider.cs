using System.Text.Json;
using LucidAdmin.Core.Models;

namespace LucidAdmin.Infrastructure.Providers.Capabilities;

public class AdGroupManagementCapabilityProvider : BaseCapabilityProvider
{
    public override string CapabilityId => "ad-group-mgmt";
    public override string Version => "1.0.0";
    public override string Category => "active-directory";
    public override string DisplayName => "AD Group Management";
    public override string Description => "Add and remove users from Active Directory groups";
    public override bool RequiresServiceAccount => true;
    public override IEnumerable<string> RequiredProviders => new[] { "windows-ad" };
    public override IEnumerable<string> Dependencies => new[] { "ad-user-lookup" };

    public override ValidationResult ValidateConfiguration(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
            return ValidationResult.Success();

        try
        {
            var config = JsonSerializer.Deserialize<GroupMgmtConfig>(configurationJson);
            var errors = new List<string>();

            if (config?.MaxGroupsPerRequest < 1)
                errors.Add("Max groups per request must be at least 1");

            return errors.Count > 0 ? ValidationResult.Failure(errors) : ValidationResult.Success();
        }
        catch (JsonException ex)
        {
            return ValidationResult.Failure($"Invalid JSON: {ex.Message}");
        }
    }

    public override string GetConfigurationSchema() => """
        {
            "type": "object",
            "properties": {
                "allowedGroupPatterns": { "type": "array", "items": { "type": "string" } },
                "deniedGroups": { "type": "array", "items": { "type": "string" } },
                "maxGroupsPerRequest": { "type": "integer", "minimum": 1, "default": 5 },
                "requireApprovalForSecurity": { "type": "boolean", "default": false }
            }
        }
        """;

    public override string GetConfigurationExample() => """
        {
            "allowedGroupPatterns": ["APP-*", "DL-*", "SEC-*"],
            "deniedGroups": ["Domain Admins", "Enterprise Admins", "Schema Admins"],
            "maxGroupsPerRequest": 5,
            "requireApprovalForSecurity": true
        }
        """;

    private class GroupMgmtConfig
    {
        public int MaxGroupsPerRequest { get; set; } = 5;
    }
}
