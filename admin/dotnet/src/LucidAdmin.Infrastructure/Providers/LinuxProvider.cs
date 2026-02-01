using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Models;

namespace LucidAdmin.Infrastructure.Providers;

/// <summary>
/// Provider for Linux service accounts (STUB - not yet implemented)
/// </summary>
public class LinuxProvider : BaseServiceAccountProvider
{
    public override string ProviderId => "linux";
    public override string DisplayName => "Linux";
    public override string Description => "Service accounts on Linux systems (PAM, LDAP, SSH keys)";
    public override bool IsImplemented => false;  // STUB

    public override IEnumerable<AccountTypeInfo> SupportedAccountTypes => new[]
    {
        new AccountTypeInfo("local-user", "Local User", "Local Linux user account", true),
        new AccountTypeInfo("ldap-user", "LDAP User", "LDAP-backed user account", true),
        new AccountTypeInfo("ssh-key", "SSH Key", "SSH key-based authentication", true)
    };

    public override IEnumerable<CredentialStorageType> SupportedCredentialStorage => new[]
    {
        CredentialStorageType.Database,
        CredentialStorageType.Environment,
        CredentialStorageType.Vault
    };

    public override string GetConfigurationSchema(string accountType) => "{}";
    public override string GetConfigurationExample(string accountType) => "{}";
}
