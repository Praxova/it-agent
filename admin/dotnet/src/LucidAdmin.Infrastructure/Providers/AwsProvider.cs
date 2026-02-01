using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Models;

namespace LucidAdmin.Infrastructure.Providers;

/// <summary>
/// Provider for AWS IAM identities (STUB - not yet implemented)
/// </summary>
public class AwsProvider : BaseServiceAccountProvider
{
    public override string ProviderId => "aws";
    public override string DisplayName => "Amazon Web Services (AWS)";
    public override string Description => "AWS IAM roles and access keys";
    public override bool IsImplemented => false;  // STUB

    public override IEnumerable<AccountTypeInfo> SupportedAccountTypes => new[]
    {
        new AccountTypeInfo("iam-role", "IAM Role", "AWS IAM role with assumed permissions", false),
        new AccountTypeInfo("access-key", "Access Key", "IAM user access key credentials", true)
    };

    public override IEnumerable<CredentialStorageType> SupportedCredentialStorage => new[]
    {
        CredentialStorageType.None,  // For IAM roles
        CredentialStorageType.Environment,
        CredentialStorageType.Vault
    };

    public override string GetConfigurationSchema(string accountType) => "{}";
    public override string GetConfigurationExample(string accountType) => "{}";
}
