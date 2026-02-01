using System.Text.Json;
using LucidAdmin.Core.Models;

namespace LucidAdmin.Infrastructure.Providers.Capabilities;

public class AdPasswordResetCapabilityProvider : BaseCapabilityProvider
{
    public override string CapabilityId => "ad-password-reset";
    public override string Version => "1.0.0";
    public override string Category => "active-directory";
    public override string DisplayName => "AD Password Reset";
    public override string Description => "Reset user passwords in Active Directory";
    public override bool RequiresServiceAccount => true;
    public override IEnumerable<string> RequiredProviders => new[] { "windows-ad" };
    public override IEnumerable<string> Dependencies => new[] { "ad-user-lookup" };

    public override ValidationResult ValidateConfiguration(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
            return ValidationResult.Success(); // Config is optional

        try
        {
            var config = JsonSerializer.Deserialize<PasswordResetConfig>(configurationJson);
            var errors = new List<string>();

            if (config?.MinPasswordLength < 8)
                errors.Add("Minimum password length must be at least 8");

            if (config?.TemporaryPasswordExpireHours < 1)
                errors.Add("Temporary password expiry must be at least 1 hour");

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
                "minPasswordLength": { "type": "integer", "minimum": 8, "default": 12 },
                "requireComplexity": { "type": "boolean", "default": true },
                "temporaryPasswordExpireHours": { "type": "integer", "minimum": 1, "default": 24 },
                "notifyUserViaEmail": { "type": "boolean", "default": true },
                "excludedOUs": { "type": "array", "items": { "type": "string" } }
            }
        }
        """;

    public override string GetConfigurationExample() => """
        {
            "minPasswordLength": 12,
            "requireComplexity": true,
            "temporaryPasswordExpireHours": 24,
            "notifyUserViaEmail": true,
            "excludedOUs": ["OU=Admins,DC=montanifarms,DC=com"]
        }
        """;

    private class PasswordResetConfig
    {
        public int MinPasswordLength { get; set; } = 12;
        public bool RequireComplexity { get; set; } = true;
        public int TemporaryPasswordExpireHours { get; set; } = 24;
    }
}
