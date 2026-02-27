using System.Text.Json;
using System.Text.Json.Serialization;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Web.Authorization;
using LucidAdmin.Web.Services;
using Microsoft.AspNetCore.Authorization;

namespace LucidAdmin.Web.Endpoints;

public static class AuthzEndpoints
{
    public static void MapAuthzEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/authz").WithTags("Authorization");

        // Operation token issuance — authenticated via API key or JWT (agent callers)
        group.MapPost("/operation-token",
            [Authorize] async (
                OperationTokenRequest request,
                OperationTokenService tokenService,
                OperationTokenRateLimiter rateLimiter,
                IAgentRepository agentRepository,
                ICapabilityRepository capabilityRepository,
                IToolServerRepository toolServerRepository,
                ICapabilityMappingRepository capabilityMappingRepository,
                IAuditEventRepository auditRepository,
                ILogger<Program> logger,
                CancellationToken ct) =>
        {
            // 1. Validate agent exists and is enabled
            var agent = await agentRepository.GetByNameAsync(request.AgentName, ct);
            if (agent == null)
                return Results.Json(
                    new OperationTokenError("agent_not_found", $"Agent '{request.AgentName}' not found"),
                    statusCode: 400);

            if (!agent.IsEnabled)
                return Results.Json(
                    new OperationTokenError("agent_disabled", $"Agent '{request.AgentName}' is disabled"),
                    statusCode: 403);

            // 2. Validate capability exists (capability ID is the name, e.g. "ad-password-reset")
            var capability = await capabilityRepository.GetByIdAsync(request.Capability);
            if (capability == null)
                return Results.Json(
                    new OperationTokenError("capability_not_found",
                        $"No capability found for '{request.Capability}'"),
                    statusCode: 400);

            // 3. Validate tool server URL matches a registered, enabled server with this capability
            var enabledServers = await toolServerRepository.GetEnabledAsync(ct);
            var matchingServer = enabledServers.FirstOrDefault(ts =>
                UrlsMatch(ts.Endpoint, request.ToolServerUrl));

            if (matchingServer == null)
                return Results.Json(
                    new OperationTokenError("tool_server_not_found",
                        $"No enabled tool server found matching URL '{request.ToolServerUrl}'"),
                    statusCode: 400);

            // Verify this tool server has the requested capability mapped
            var mapping = await capabilityMappingRepository.GetByToolServerAndCapabilityAsync(
                matchingServer.Id, request.Capability, ct);
            if (mapping == null || !mapping.IsEnabled)
                return Results.Json(
                    new OperationTokenError("capability_not_mapped",
                        $"Tool server '{matchingServer.Name}' does not have capability '{request.Capability}' mapped"),
                    statusCode: 400);

            // 4. Validate target is not empty
            if (string.IsNullOrWhiteSpace(request.Target))
                return Results.Json(
                    new OperationTokenError("invalid_request", "Target must not be empty"),
                    statusCode: 400);

            // 5. Rate limit check
            if (!rateLimiter.TryConsume(request.AgentName, request.Capability))
            {
                await auditRepository.AddAsync(new AuditEvent
                {
                    AgentId = agent.Id,
                    Action = AuditAction.OperationTokenDenied,
                    PerformedBy = request.AgentName,
                    CapabilityId = request.Capability,
                    TargetResource = request.Target,
                    TicketNumber = request.WorkflowContext?.TicketNumber,
                    Success = false,
                    ErrorMessage = "rate_limited",
                    DetailsJson = JsonSerializer.Serialize(new { reason = "rate_limited", agentName = request.AgentName })
                }, ct);

                return Results.Json(
                    new OperationTokenError("rate_limited", "Token request rate limit exceeded"),
                    statusCode: 429,
                    contentType: "application/json");
            }

            // 6. Issue token
            try
            {
                var token = await tokenService.IssueTokenAsync(
                    agentName: request.AgentName,
                    agentId: agent.Id,
                    capability: request.Capability,
                    target: request.Target,
                    targetType: request.TargetType ?? "user",
                    toolServerUrl: request.ToolServerUrl,
                    ticketNumber: request.WorkflowContext?.TicketNumber,
                    workflowExecutionId: request.WorkflowContext?.WorkflowExecutionId,
                    approvalId: request.WorkflowContext?.ApprovalId,
                    ct: ct);

                var expiresAt = DateTime.UtcNow.AddSeconds(300);
                return Results.Ok(new OperationTokenResponse(
                    Token: token,
                    ExpiresIn: 300,
                    ExpiresAt: expiresAt.ToString("O")));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to issue operation token for agent {Agent}", request.AgentName);

                await auditRepository.AddAsync(new AuditEvent
                {
                    AgentId = agent.Id,
                    Action = AuditAction.OperationTokenDenied,
                    PerformedBy = request.AgentName,
                    CapabilityId = request.Capability,
                    TargetResource = request.Target,
                    Success = false,
                    ErrorMessage = "internal_error",
                    DetailsJson = JsonSerializer.Serialize(new { reason = "internal_error", error = ex.Message })
                }, ct);

                return Results.Json(
                    new OperationTokenError("internal_error", "Failed to issue operation token"),
                    statusCode: 500);
            }
        });

        // Signing key metadata endpoint (forward compatibility for JWKS)
        group.MapGet("/keys",
            [Authorize] (
                IJwtKeyManager jwtKeyManager) =>
        {
            // For HS256, we return only the key ID — never the actual key over API
            return Results.Ok(new
            {
                keyId = "prx-optoken-v1",
                algorithm = "HS256",
                note = "For v1.0, the signing key is statically provisioned. Key material is not returned via API."
            });
        });

        // Key export endpoint for tool server provisioning — Admin role only
        group.MapGet("/signing-key/export",
            [Authorize(Policy = AuthorizationPolicies.RequireAdmin)] (OperationTokenService tokenService) =>
        {
            var keyBase64 = tokenService.ExportSigningKeyForProvisioning();
            return Results.Ok(new
            {
                keyBase64,
                algorithm = "HS256",
                keyId = "prx-optoken-v1"
            });
        });
    }

    /// <summary>
    /// Compare tool server URLs with normalization (case-insensitive, trailing slash tolerance,
    /// and prefix matching to handle /api/v1 suffix differences).
    /// </summary>
    private static bool UrlsMatch(string endpoint, string requestUrl)
    {
        var a = endpoint.TrimEnd('/');
        var b = requestUrl.TrimEnd('/');

        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return true;

        // Handle case where one URL has /api/v1 suffix and the other doesn't
        if (b.StartsWith(a, StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith(b, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}

// Request/response models

public record OperationTokenRequest(
    [property: JsonPropertyName("agent_name")] string AgentName,
    [property: JsonPropertyName("capability")] string Capability,
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("target_type")] string? TargetType,
    [property: JsonPropertyName("tool_server_url")] string ToolServerUrl,
    [property: JsonPropertyName("workflow_context")] WorkflowContext? WorkflowContext
);

public record WorkflowContext(
    [property: JsonPropertyName("ticket_number")] string? TicketNumber,
    [property: JsonPropertyName("workflow_execution_id")] string? WorkflowExecutionId,
    [property: JsonPropertyName("approval_id")] string? ApprovalId
);

public record OperationTokenResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("expires_at")] string ExpiresAt
);

public record OperationTokenError(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("message")] string Message
);
