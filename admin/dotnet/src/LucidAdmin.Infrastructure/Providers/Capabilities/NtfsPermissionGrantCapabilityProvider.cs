namespace LucidAdmin.Infrastructure.Providers.Capabilities;

public class NtfsPermissionGrantCapabilityProvider : BaseCapabilityProvider
{
    public override string CapabilityId => "ntfs-permission-grant";
    public override string Version => "1.0.0";
    public override string Category => "file-system";
    public override string DisplayName => "NTFS Permission Grant";
    public override string Description => "Grant NTFS permissions on files and folders";
    public override bool RequiresServiceAccount => true;
    public override IEnumerable<string> RequiredProviders => new[] { "windows-ad" };
    public override IEnumerable<string> Dependencies => new[] { "ad-user-lookup" };

    public override string GetConfigurationSchema() => """
        {
            "type": "object",
            "properties": {
                "allowedPaths": { "type": "array", "items": { "type": "string" } },
                "deniedPaths": { "type": "array", "items": { "type": "string" } },
                "maxPermissionLevel": { "type": "string", "enum": ["Read", "ReadWrite", "FullControl"], "default": "ReadWrite" },
                "requireApproval": { "type": "boolean", "default": false }
            }
        }
        """;

    public override string GetConfigurationExample() => """
        {
            "allowedPaths": ["\\\\fileserver\\shares\\*", "\\\\fileserver\\home\\*"],
            "deniedPaths": ["\\\\fileserver\\admin$", "\\\\fileserver\\c$"],
            "maxPermissionLevel": "ReadWrite",
            "requireApproval": false
        }
        """;
}
