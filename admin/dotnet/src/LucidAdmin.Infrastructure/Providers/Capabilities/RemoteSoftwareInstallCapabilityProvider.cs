using LucidAdmin.Core.Models;

namespace LucidAdmin.Infrastructure.Providers.Capabilities;

public class RemoteSoftwareInstallCapabilityProvider : BaseCapabilityProvider
{
    public override string CapabilityId => "remote-software-install";
    public override string Version => "1.0.0";
    public override string Category => "workstation-management";
    public override string DisplayName => "Remote Software Install";
    public override string Description => "Install software packages on remote Windows computers via PowerShell Remoting (WinRM)";
    public override bool RequiresServiceAccount => true;
    public override IEnumerable<string> RequiredProviders => new[] { "windows-ad" };

    public override string GetConfigurationSchema() => """
        {
            "type": "object",
            "properties": {
                "allowedPaths": { "type": "array", "items": { "type": "string" }, "description": "Allowed UNC paths for software packages" },
                "defaultArgsMsi": { "type": "string", "description": "Default MSI installer arguments" },
                "defaultArgsExe": { "type": "string", "description": "Default EXE installer arguments" }
            }
        }
        """;

    public override string GetConfigurationExample() => """
        {
            "allowedPaths": ["\\\\fileserver\\software\\*"],
            "defaultArgsMsi": "/qn /norestart",
            "defaultArgsExe": "/S"
        }
        """;
}
