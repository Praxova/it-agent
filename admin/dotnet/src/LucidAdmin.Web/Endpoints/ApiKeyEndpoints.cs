using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Web.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LucidAdmin.Web.Endpoints;

public static class ApiKeyEndpoints
{
    public static void MapApiKeyEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/api-keys")
            .WithTags("API Keys")
            .RequireAuthorization(AuthorizationPolicies.CanManageApiKeys);

        // List all API keys
        group.MapGet("/", GetAllKeys)
            .WithName("GetAllApiKeys")
            .WithDescription("List all API keys")
            .Produces<List<ApiKeyResponse>>(StatusCodes.Status200OK);

        // Get API key by ID
        group.MapGet("/{id:guid}", GetKeyById)
            .WithName("GetApiKeyById")
            .WithDescription("Get API key details by ID")
            .Produces<ApiKeyResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        // Create API key for an agent
        group.MapPost("/agent/{agentId:guid}", CreateAgentKey)
            .WithName("CreateAgentApiKey")
            .WithDescription("Create a new API key for an agent. The plaintext key is only shown once!")
            .Produces<ApiKeyCreateResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        // Create API key for a tool server
        group.MapPost("/tool-server/{toolServerId:guid}", CreateToolServerKey)
            .WithName("CreateToolServerApiKey")
            .WithDescription("Create a new API key for a tool server. The plaintext key is only shown once!")
            .Produces<ApiKeyCreateResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        // Revoke an API key
        group.MapPost("/{id:guid}/revoke", RevokeKey)
            .WithName("RevokeApiKey")
            .WithDescription("Revoke an API key")
            .Produces<ApiKeyResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        // Get keys for a specific agent
        group.MapGet("/agent/{agentId:guid}", GetAgentKeys)
            .WithName("GetAgentApiKeys")
            .WithDescription("Get all API keys for an agent")
            .Produces<List<ApiKeyResponse>>(StatusCodes.Status200OK);

        // Get keys for a specific tool server
        group.MapGet("/tool-server/{toolServerId:guid}", GetToolServerKeys)
            .WithName("GetToolServerApiKeys")
            .WithDescription("Get all API keys for a tool server")
            .Produces<List<ApiKeyResponse>>(StatusCodes.Status200OK);

        // Update allowed service accounts for an agent key
        group.MapPut("/{id:guid}/allowed-accounts", UpdateAllowedAccounts)
            .WithName("UpdateApiKeyAllowedAccounts")
            .WithDescription("Update the service accounts an agent key can access")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetAllKeys(
        IApiKeyService apiKeyService,
        CancellationToken ct)
    {
        var keys = await apiKeyService.GetAllKeysAsync(ct);
        return Results.Ok(keys.Select(ToResponse).ToList());
    }

    private static async Task<IResult> GetKeyById(
        Guid id,
        IApiKeyService apiKeyService,
        CancellationToken ct)
    {
        var keys = await apiKeyService.GetAllKeysAsync(ct);
        var key = keys.FirstOrDefault(k => k.Id == id);

        if (key == null)
        {
            return Results.NotFound(new { error = "API key not found" });
        }

        return Results.Ok(ToResponse(key));
    }

    private static async Task<IResult> CreateAgentKey(
        Guid agentId,
        [FromBody] CreateApiKeyRequest request,
        IApiKeyService apiKeyService,
        CancellationToken ct)
    {
        var result = await apiKeyService.CreateAgentKeyAsync(
            agentId,
            request.Name,
            request.Description,
            request.ExpiresAt,
            ct);

        if (!result.Success)
        {
            return Results.BadRequest(new { error = result.ErrorMessage });
        }

        return Results.Created(
            $"/api/v1/api-keys/{result.Key.Id}",
            new ApiKeyCreateResponse(
                Key: ToResponse(result.Key),
                PlaintextKey: result.PlaintextKey,
                Warning: "This is the only time the full API key will be shown. Store it securely!"
            ));
    }

    private static async Task<IResult> CreateToolServerKey(
        Guid toolServerId,
        [FromBody] CreateApiKeyRequest request,
        IApiKeyService apiKeyService,
        CancellationToken ct)
    {
        var result = await apiKeyService.CreateToolServerKeyAsync(
            toolServerId,
            request.Name,
            request.Description,
            request.ExpiresAt,
            ct);

        if (!result.Success)
        {
            return Results.BadRequest(new { error = result.ErrorMessage });
        }

        return Results.Created(
            $"/api/v1/api-keys/{result.Key.Id}",
            new ApiKeyCreateResponse(
                Key: ToResponse(result.Key),
                PlaintextKey: result.PlaintextKey,
                Warning: "This is the only time the full API key will be shown. Store it securely!"
            ));
    }

    private static async Task<IResult> RevokeKey(
        Guid id,
        [FromBody] RevokeApiKeyRequest request,
        IApiKeyService apiKeyService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var revokedBy = httpContext.User.Identity?.Name ?? "Unknown";

        var success = await apiKeyService.RevokeKeyAsync(id, revokedBy, request.Reason, ct);

        if (!success)
        {
            return Results.NotFound(new { error = "API key not found" });
        }

        var keys = await apiKeyService.GetAllKeysAsync(ct);
        var key = keys.FirstOrDefault(k => k.Id == id);

        return Results.Ok(ToResponse(key!));
    }

    private static async Task<IResult> GetAgentKeys(
        Guid agentId,
        IApiKeyService apiKeyService,
        CancellationToken ct)
    {
        var keys = await apiKeyService.GetAgentKeysAsync(agentId, ct);
        return Results.Ok(keys.Select(ToResponse).ToList());
    }

    private static async Task<IResult> GetToolServerKeys(
        Guid toolServerId,
        IApiKeyService apiKeyService,
        CancellationToken ct)
    {
        var keys = await apiKeyService.GetToolServerKeysAsync(toolServerId, ct);
        return Results.Ok(keys.Select(ToResponse).ToList());
    }

    private static async Task<IResult> UpdateAllowedAccounts(
        Guid id,
        [FromBody] UpdateAllowedAccountsRequest request,
        IApiKeyService apiKeyService,
        CancellationToken ct)
    {
        await apiKeyService.UpdateAllowedServiceAccountsAsync(id, request.ServiceAccountIds, ct);
        return Results.NoContent();
    }

    private static ApiKeyResponse ToResponse(ApiKey key) => new(
        Id: key.Id,
        Name: key.Name,
        Description: key.Description,
        KeyPrefix: key.KeyPrefix,
        Role: key.Role.ToString(),
        AgentId: key.AgentId,
        AgentName: key.Agent?.Name,
        ToolServerId: key.ToolServerId,
        ToolServerName: key.ToolServer?.Name,
        IsActive: key.IsActive,
        IsValid: key.IsValid(),
        ExpiresAt: key.ExpiresAt,
        LastUsedAt: key.LastUsedAt,
        LastUsedFromIp: key.LastUsedFromIp,
        RevokedAt: key.RevokedAt,
        RevokedBy: key.RevokedBy,
        RevocationReason: key.RevocationReason,
        CreatedAt: key.CreatedAt
    );
}

// Request/Response DTOs
public record CreateApiKeyRequest(
    string Name,
    string? Description = null,
    DateTime? ExpiresAt = null
);

public record RevokeApiKeyRequest(
    string? Reason = null
);

public record UpdateAllowedAccountsRequest(
    List<Guid> ServiceAccountIds
);

public record ApiKeyResponse(
    Guid Id,
    string Name,
    string? Description,
    string KeyPrefix,
    string Role,
    Guid? AgentId,
    string? AgentName,
    Guid? ToolServerId,
    string? ToolServerName,
    bool IsActive,
    bool IsValid,
    DateTime? ExpiresAt,
    DateTime? LastUsedAt,
    string? LastUsedFromIp,
    DateTime? RevokedAt,
    string? RevokedBy,
    string? RevocationReason,
    DateTime CreatedAt
);

public record ApiKeyCreateResponse(
    ApiKeyResponse Key,
    string PlaintextKey,
    string Warning
);
