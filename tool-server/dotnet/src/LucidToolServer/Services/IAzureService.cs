using LucidToolServer.Models.Responses;

namespace LucidToolServer.Services;

public interface IAzureService
{
    /// <summary>
    /// Look up a user in Entra ID by UPN or object ID.
    /// </summary>
    Task<AzureUserResponse> GetUserAsync(string userPrincipalNameOrId);

    /// <summary>
    /// Look up a VM by name, searching across subscriptions/resource groups.
    /// </summary>
    Task<AzureVmResponse> GetVmAsync(string vmName, string? resourceGroup = null, string? subscriptionId = null);

    /// <summary>
    /// Test Azure connectivity (verify token acquisition).
    /// </summary>
    Task<bool> TestConnectionAsync();
}
