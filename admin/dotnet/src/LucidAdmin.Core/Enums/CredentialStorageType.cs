namespace LucidAdmin.Core.Enums;

/// <summary>
/// Types of credential storage backends
/// </summary>
public enum CredentialStorageType
{
    /// <summary>No credentials needed (gMSA, CurrentUser, local services)</summary>
    None = 0,

    /// <summary>Encrypted storage in Admin Portal database (recommended default)</summary>
    Database = 1,

    /// <summary>Environment variables (legacy, development)</summary>
    Environment = 2,

    /// <summary>HashiCorp Vault</summary>
    Vault = 3,

    /// <summary>Azure Key Vault</summary>
    AzureKeyVault = 4,

    /// <summary>AWS Secrets Manager</summary>
    AwsSecretsManager = 5
}
