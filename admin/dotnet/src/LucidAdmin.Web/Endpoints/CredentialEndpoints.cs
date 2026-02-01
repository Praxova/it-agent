using LucidAdmin.Core.Interfaces.Credentials;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Models;
using LucidAdmin.Web.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LucidAdmin.Web.Endpoints;

public static class CredentialEndpoints
{
    public static void MapCredentialEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/service-accounts")
            .WithTags("Credentials");

        // Get credentials for a service account
        // Agents can only access their allowed service accounts
        // Admins can access all
        group.MapGet("/{id:guid}/credentials", GetCredentials)
            .WithName("GetServiceAccountCredentials")
            .WithDescription("Retrieve credentials for a service account. Agents can only access their assigned accounts.")
            .RequireAuthorization(AuthorizationPolicies.CanAccessServiceAccountCredentials)
            .Produces<CredentialResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // Check if credentials exist (without returning them)
        group.MapGet("/{id:guid}/credentials/exists", CheckCredentialsExist)
            .WithName("CheckServiceAccountCredentialsExist")
            .WithDescription("Check if credentials are configured for a service account")
            .RequireAuthorization(AuthorizationPolicies.CanReadServiceAccounts)
            .Produces<CredentialExistsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        // Store credentials for a service account (Admin only)
        group.MapPut("/{id:guid}/credentials", StoreCredentials)
            .WithName("StoreServiceAccountCredentials")
            .WithDescription("Store or update credentials for a service account")
            .RequireAuthorization(AuthorizationPolicies.CanWriteCredentials)
            .Produces<CredentialStoreResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        // Delete credentials for a service account (Admin only)
        group.MapDelete("/{id:guid}/credentials", DeleteCredentials)
            .WithName("DeleteServiceAccountCredentials")
            .WithDescription("Delete credentials for a service account")
            .RequireAuthorization(AuthorizationPolicies.CanWriteCredentials)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetCredentials(
        Guid id,
        ICredentialService credentialService,
        IServiceAccountRepository repository,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var account = await repository.GetByIdAsync(id, ct);
        if (account == null)
        {
            return Results.NotFound(new { error = "Service account not found" });
        }

        var credentials = await credentialService.GetCredentialsAsync(account, ct);
        if (credentials == null || credentials.IsEmpty)
        {
            return Results.NotFound(new { error = "No credentials configured for this service account" });
        }

        return Results.Ok(new CredentialResponse(
            ServiceAccountId: id,
            ServiceAccountName: account.Name,
            Provider: account.Provider,
            AccountType: account.AccountType,
            Credentials: credentials.Values,
            ExpiresAt: credentials.ExpiresAt
        ));
    }

    private static async Task<IResult> CheckCredentialsExist(
        Guid id,
        ICredentialService credentialService,
        IServiceAccountRepository repository,
        CancellationToken ct)
    {
        var account = await repository.GetByIdAsync(id, ct);
        if (account == null)
        {
            return Results.NotFound(new { error = "Service account not found" });
        }

        var hasCredentials = await credentialService.HasCredentialsAsync(id, ct);

        return Results.Ok(new CredentialExistsResponse(
            ServiceAccountId: id,
            HasCredentials: hasCredentials,
            CredentialStorage: account.CredentialStorage.ToString(),
            LastUpdated: account.CredentialsUpdatedAt
        ));
    }

    private static async Task<IResult> StoreCredentials(
        Guid id,
        [FromBody] StoreCredentialsRequest request,
        ICredentialService credentialService,
        IServiceAccountRepository repository,
        CancellationToken ct)
    {
        var account = await repository.GetByIdAsync(id, ct);
        if (account == null)
        {
            return Results.NotFound(new { error = "Service account not found" });
        }

        if (request.Credentials == null || request.Credentials.Count == 0)
        {
            return Results.BadRequest(new { error = "Credentials are required" });
        }

        var credentialSet = new CredentialSet(request.Credentials);
        var result = await credentialService.StoreCredentialsAsync(id, credentialSet, ct);

        if (!result.Success)
        {
            return Results.BadRequest(new { error = result.ErrorMessage });
        }

        return Results.Ok(new CredentialStoreResponse(
            Success: true,
            Message: "Credentials stored successfully",
            ServiceAccountId: id
        ));
    }

    private static async Task<IResult> DeleteCredentials(
        Guid id,
        ICredentialService credentialService,
        IServiceAccountRepository repository,
        CancellationToken ct)
    {
        var account = await repository.GetByIdAsync(id, ct);
        if (account == null)
        {
            return Results.NotFound(new { error = "Service account not found" });
        }

        var result = await credentialService.DeleteCredentialsAsync(id, ct);

        return result
            ? Results.NoContent()
            : Results.BadRequest(new { error = "Failed to delete credentials" });
    }
}

// Request/Response DTOs
public record CredentialResponse(
    Guid ServiceAccountId,
    string ServiceAccountName,
    string Provider,
    string AccountType,
    Dictionary<string, string> Credentials,
    DateTime? ExpiresAt
);

public record CredentialExistsResponse(
    Guid ServiceAccountId,
    bool HasCredentials,
    string CredentialStorage,
    DateTime? LastUpdated
);

public record StoreCredentialsRequest(
    Dictionary<string, string> Credentials
);

public record CredentialStoreResponse(
    bool Success,
    string Message,
    Guid ServiceAccountId
);
