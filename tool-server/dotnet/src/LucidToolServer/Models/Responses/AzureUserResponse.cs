namespace LucidToolServer.Models.Responses;

public record AzureUserResponse(
    bool Success,
    string Id,
    string DisplayName,
    string UserPrincipalName,
    string? JobTitle,
    string? Department,
    string? OfficeLocation,
    string? Mail,
    string? MobilePhone,
    bool AccountEnabled,
    string? Manager,
    List<string> Licenses,
    string? LastSignIn,
    string? CreatedDateTime,
    string Message
);
