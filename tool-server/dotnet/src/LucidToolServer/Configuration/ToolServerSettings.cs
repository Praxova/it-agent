namespace LucidToolServer.Configuration;

/// <summary>
/// Configuration settings for the Tool Server.
/// </summary>
public class ToolServerSettings
{
    /// <summary>
    /// Active Directory groups that cannot be modified.
    /// </summary>
    public string[] ProtectedGroups { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Active Directory accounts that cannot be modified.
    /// </summary>
    public string[] ProtectedAccounts { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Allowed UNC paths for file permission operations.
    /// Supports wildcards (e.g., "\\\\*\\share*").
    /// </summary>
    public string[] AllowedPaths { get; set; } = Array.Empty<string>();

    /// <summary>
    /// API key for authenticating requests (optional).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Active Directory domain name (e.g., "montanifarms.com").
    /// If not specified, uses the default domain context.
    /// </summary>
    public string? DomainName { get; set; }

    /// <summary>
    /// AD service account username for authenticated binds (e.g., "svc-praxova@montanifarms.com").
    /// If not specified, the tool server runs as the machine account (NTLM/Kerberos).
    /// </summary>
    public string? ServiceAccountUsername { get; set; }

    /// <summary>
    /// AD service account password for authenticated binds.
    /// </summary>
    public string? ServiceAccountPassword { get; set; }

    /// <summary>
    /// Allowed UNC paths for software installation packages.
    /// Supports wildcards (e.g., "\\\\fileserver\\software\\*").
    /// If empty, remote software install is disabled.
    /// </summary>
    public string[] AllowedSoftwarePaths { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Azure / Entra ID configuration for cloud operations.
    /// </summary>
    public AzureSettings? Azure { get; set; }
}
