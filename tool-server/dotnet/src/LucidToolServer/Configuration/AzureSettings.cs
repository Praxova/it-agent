namespace LucidToolServer.Configuration;

public class AzureSettings
{
    /// <summary>
    /// Azure AD / Entra ID Tenant ID
    /// </summary>
    public required string TenantId { get; set; }

    /// <summary>
    /// App Registration Client ID
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// App Registration Client Secret (prefer environment variable AZURE_CLIENT_SECRET)
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Default Azure Subscription ID for resource lookups
    /// </summary>
    public string? DefaultSubscriptionId { get; set; }
}
