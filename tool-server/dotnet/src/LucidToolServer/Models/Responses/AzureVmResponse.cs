namespace LucidToolServer.Models.Responses;

public record AzureVmResponse(
    bool Success,
    string Name,
    string ResourceGroup,
    string SubscriptionId,
    string Location,
    string VmSize,
    string PowerState,
    string OsType,
    string? OsName,
    string? OsVersion,
    List<string> PrivateIpAddresses,
    List<string> PublicIpAddresses,
    string? ProvisioningState,
    string Message
);
