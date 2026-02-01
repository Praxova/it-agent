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
}
