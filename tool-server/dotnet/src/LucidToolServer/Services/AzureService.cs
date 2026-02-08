using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Microsoft.Graph;
using Microsoft.Extensions.Options;
using LucidToolServer.Configuration;
using LucidToolServer.Models.Responses;

namespace LucidToolServer.Services;

public class AzureService : IAzureService
{
    private readonly GraphServiceClient _graphClient;
    private readonly AzureSettings _settings;
    private readonly ILogger<AzureService> _logger;
    private readonly ClientSecretCredential _credential;

    public AzureService(IOptions<ToolServerSettings> settings, ILogger<AzureService> logger)
    {
        _settings = settings.Value.Azure ?? throw new InvalidOperationException("Azure settings not configured");
        _logger = logger;

        // Support client secret from config or environment variable
        var clientSecret = _settings.ClientSecret;
        if (string.IsNullOrEmpty(clientSecret))
            clientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");

        if (string.IsNullOrEmpty(clientSecret))
            throw new InvalidOperationException("Azure ClientSecret not configured. Set in appsettings.json or AZURE_CLIENT_SECRET env var.");

        _credential = new ClientSecretCredential(_settings.TenantId, _settings.ClientId, clientSecret);
        _graphClient = new GraphServiceClient(_credential);
    }

    public async Task<AzureUserResponse> GetUserAsync(string userPrincipalNameOrId)
    {
        var user = await _graphClient.Users[userPrincipalNameOrId]
            .GetAsync(config =>
            {
                config.QueryParameters.Select = new[]
                {
                    "id", "displayName", "userPrincipalName", "jobTitle",
                    "department", "officeLocation", "mail", "mobilePhone",
                    "accountEnabled", "createdDateTime", "signInActivity",
                    "assignedLicenses"
                };
            });

        if (user == null)
            throw new KeyNotFoundException($"User not found: {userPrincipalNameOrId}");

        // Get manager (separate call, may not exist)
        string? managerName = null;
        try
        {
            var manager = await _graphClient.Users[userPrincipalNameOrId].Manager.GetAsync();
            if (manager is Microsoft.Graph.Models.User mgr)
                managerName = mgr.DisplayName;
        }
        catch { /* No manager assigned */ }

        // Map licenses to SKU IDs
        var licenses = user.AssignedLicenses?
            .Select(l => l.SkuId?.ToString() ?? "Unknown")
            .ToList() ?? new List<string>();

        return new AzureUserResponse(
            Success: true,
            Id: user.Id ?? "",
            DisplayName: user.DisplayName ?? "",
            UserPrincipalName: user.UserPrincipalName ?? "",
            JobTitle: user.JobTitle,
            Department: user.Department,
            OfficeLocation: user.OfficeLocation,
            Mail: user.Mail,
            MobilePhone: user.MobilePhone,
            AccountEnabled: user.AccountEnabled ?? false,
            Manager: managerName,
            Licenses: licenses,
            LastSignIn: user.SignInActivity?.LastSignInDateTime?.ToString("o"),
            CreatedDateTime: user.CreatedDateTime?.ToString("o"),
            Message: $"User '{user.DisplayName}' found in Entra ID"
        );
    }

    public async Task<AzureVmResponse> GetVmAsync(string vmName, string? resourceGroup = null, string? subscriptionId = null)
    {
        var subId = subscriptionId ?? _settings.DefaultSubscriptionId;
        if (string.IsNullOrEmpty(subId))
            throw new InvalidOperationException("No subscription ID provided and no default configured");

        var armClient = new ArmClient(_credential);
        var subscription = armClient.GetSubscriptionResource(
            new Azure.Core.ResourceIdentifier($"/subscriptions/{subId}"));

        VirtualMachineResource? vm = null;

        if (!string.IsNullOrEmpty(resourceGroup))
        {
            // Direct lookup
            var rg = subscription.GetResourceGroup(resourceGroup);
            var vmCollection = rg.Value.GetVirtualMachines();
            var vmResponse = await vmCollection.GetAsync(vmName);
            vm = vmResponse.Value;
        }
        else
        {
            // Search across all resource groups
            await foreach (var candidate in subscription.GetVirtualMachinesAsync())
            {
                if (candidate.Data.Name.Equals(vmName, StringComparison.OrdinalIgnoreCase))
                {
                    vm = candidate;
                    break;
                }
            }
        }

        if (vm == null)
            throw new KeyNotFoundException($"VM not found: {vmName}");

        // Get instance view for power state
        var instanceView = await vm.InstanceViewAsync();
        var powerState = instanceView.Value.Statuses?
            .FirstOrDefault(s => s.Code?.StartsWith("PowerState/") == true)?.Code?.Replace("PowerState/", "");

        var privateIps = new List<string>();
        var publicIps = new List<string>();

        return new AzureVmResponse(
            Success: true,
            Name: vm.Data.Name,
            ResourceGroup: vm.Id.ResourceGroupName ?? "",
            SubscriptionId: subId,
            Location: vm.Data.Location.ToString(),
            VmSize: vm.Data.HardwareProfile?.VmSize?.ToString() ?? "Unknown",
            PowerState: powerState ?? "Unknown",
            OsType: vm.Data.StorageProfile?.OSDisk?.OSType?.ToString() ?? "Unknown",
            OsName: null,
            OsVersion: null,
            PrivateIpAddresses: privateIps,
            PublicIpAddresses: publicIps,
            ProvisioningState: vm.Data.ProvisioningState,
            Message: $"VM '{vm.Data.Name}' found in resource group '{vm.Id.ResourceGroupName}'"
        );
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var org = await _graphClient.Organization.GetAsync();
            return org?.Value?.Any() == true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Azure connectivity test failed");
            return false;
        }
    }
}
