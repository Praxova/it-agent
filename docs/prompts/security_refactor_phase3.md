You are implementing Phase 3 (Operation Authorization Tokens) for the Praxova IT Agent.
This is the most security-critical feature in v1.0. Read this entire prompt before writing
any code.

## Context

The goal is to close the authorization gap between "who is calling" (identity) and "what
are they authorized to do right now" (operation-scoped authorization). Currently, anyone
who can reach the tool server HTTP endpoint can execute AD operations with no authorization
check. After this change, every operation endpoint requires a short-lived JWT issued by the
portal for a specific capability, target, and tool server.

## CRITICAL: What NOT to do

- Do NOT implement mTLS. The tool server has no mTLS today and adding it is out of scope.
- Do NOT modify the PKI, secrets encryption, ServiceNow connector, or LLM classifier.
- Do NOT implement the composable workflow engine — work with the actual procedural handlers.
- Do NOT add JWKS-based key rotation — static key provisioning only for v1.0.
- Do NOT cache operation tokens. Each tool server call gets a fresh token.

## Files you will modify

Portal (C#):
1. `admin/dotnet/src/LucidAdmin.Core/Enums/AuditAction.cs` — add two new enum values
2. `admin/dotnet/src/LucidAdmin.Web/Endpoints/` — add new file: `AuthzEndpoints.cs`
3. `admin/dotnet/src/LucidAdmin.Web/Program.cs` — register the new endpoint group
4. `admin/dotnet/src/LucidAdmin.Web/` — add new file: `Services/OperationTokenService.cs`
5. `admin/dotnet/src/LucidAdmin.Web/` — add new file: `Services/RateLimiterService.cs`

Agent (Python):
6. `agent/src/agent/tools/base.py` — add token request logic to `_make_request()`
7. `agent/src/agent/config/admin_client.py` — add `request_operation_token()` method

Tool Server (C#):
8. `tool-server/dotnet/src/LucidToolServer/appsettings.json` — add JWT config section
9. `tool-server/dotnet/src/LucidToolServer/Program.cs` — add JWT middleware + authorization
10. `tool-server/dotnet/src/LucidToolServer/Services/` — add new file: `OperationTokenValidator.cs`

Scripts:
11. `scripts/provision-toolserver-certs.ps1` — extend to also deploy the signing key

---

## Part 1: Admin Portal — Token Issuance

### Step 1: Add audit actions

In `LucidAdmin.Core/Enums/AuditAction.cs`, add at the end of the enum:
```csharp
// Operation token operations
OperationTokenIssued,
OperationTokenDenied,
```

### Step 2: Create OperationTokenService

Create `admin/dotnet/src/LucidAdmin.Web/Services/OperationTokenService.cs`:
```csharp
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;
using Microsoft.IdentityModel.Tokens;

namespace LucidAdmin.Web.Services;

/// <summary>
/// Issues short-lived, operation-scoped JWTs for tool server authorization.
/// </summary>
public class OperationTokenService
{
    private readonly IJwtKeyManager _jwtKeyManager;
    private readonly IAuditEventRepository _auditRepository;
    private readonly ILogger<OperationTokenService> _logger;

    // In-memory nonce tracking: jti → expiry time. Cleaned up by background task.
    private readonly ConcurrentDictionary<string, DateTime> _issuedNonces = new();

    private static readonly TimeSpan TokenLifetime = TimeSpan.FromSeconds(300); // 5 minutes
    private static readonly TimeSpan NonceTtl = TimeSpan.FromSeconds(360);      // 6 minutes

    public OperationTokenService(
        IJwtKeyManager jwtKeyManager,
        IAuditEventRepository auditRepository,
        ILogger<OperationTokenService> logger)
    {
        _jwtKeyManager = jwtKeyManager;
        _auditRepository = auditRepository;
        _logger = logger;
    }

    /// <summary>
    /// Issue an operation token for the given request. Returns the JWT string.
    /// The caller is responsible for validating inputs (agent exists, capability exists, etc.)
    /// before calling this method.
    /// </summary>
    public async Task<string> IssueTokenAsync(
        string agentName,
        Guid? agentId,
        string capability,
        string target,
        string targetType,
        string toolServerUrl,
        string? ticketNumber,
        string? workflowExecutionId,
        string? approvalId,
        CancellationToken ct = default)
    {
        var jti = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var expires = now.Add(TokenLifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, jti),
            new(JwtRegisteredClaimNames.Iss, "praxova-portal"),
            new(JwtRegisteredClaimNames.Sub, agentName),
            new("cap", capability),
            new("target", target),
            new("target_type", targetType),
            new("ts_url", toolServerUrl),
            new("purpose", "operation"),  // Distinguishes from session tokens
        };

        if (!string.IsNullOrEmpty(ticketNumber))
            claims.Add(new("ticket", ticketNumber));
        if (!string.IsNullOrEmpty(workflowExecutionId))
            claims.Add(new("wfe", workflowExecutionId));
        if (!string.IsNullOrEmpty(approvalId))
            claims.Add(new("apr", approvalId));

        var key = new SymmetricSecurityKey(_jwtKeyManager.GetSigningKey());
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "praxova-portal",
            audience: null,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials
        );

        // Set kid header
        token.Header["kid"] = "prx-optoken-v1";

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        // Track nonce for audit purposes
        _issuedNonces.TryAdd(jti, expires.Add(TimeSpan.FromSeconds(60)));

        // Audit log
        await _auditRepository.AddAsync(new AuditEvent
        {
            AgentId = agentId,
            Action = AuditAction.OperationTokenIssued,
            PerformedBy = agentName,
            CapabilityId = capability,
            TargetResource = target,
            TicketNumber = ticketNumber,
            Success = true,
            DetailsJson = JsonSerializer.Serialize(new
            {
                jti,
                capability,
                target,
                targetType,
                toolServerUrl,
                ticketNumber,
                workflowExecutionId,
                approvalId,
                expiresAt = expires.ToString("O")
            })
        }, ct);

        _logger.LogInformation(
            "Operation token issued: agent={Agent}, cap={Cap}, target={Target}, jti={Jti}",
            agentName, capability, target, jti);

        return tokenString;
    }

    /// <summary>
    /// Clean up expired nonces. Called by background timer.
    /// </summary>
    public void CleanupExpiredNonces()
    {
        var now = DateTime.UtcNow;
        var expired = _issuedNonces
            .Where(kvp => kvp.Value < now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expired)
            _issuedNonces.TryRemove(key, out _);

        if (expired.Count > 0)
            _logger.LogDebug("Cleaned up {Count} expired operation token nonces", expired.Count);
    }
}
```

### Step 3: Create RateLimiterService

Create `admin/dotnet/src/LucidAdmin.Web/Services/RateLimiterService.cs`:
```csharp
using System.Collections.Concurrent;

namespace LucidAdmin.Web.Services;

/// <summary>
/// Simple in-memory sliding window rate limiter for operation token requests.
/// All state is ephemeral — a portal restart resets all counters.
/// </summary>
public class OperationTokenRateLimiter
{
    private record RateLimitEntry(ConcurrentQueue<DateTime> Timestamps);

    // Per-agent: 60 requests/minute
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> _perAgent = new();
    // Per-agent-capability: 30 requests/minute
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> _perAgentCapability = new();
    // Global: 120 requests/minute
    private readonly ConcurrentQueue<DateTime> _global = new();

    private const int PerAgentLimit = 60;
    private const int PerCapabilityLimit = 30;
    private const int GlobalLimit = 120;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Check and consume a rate limit slot. Returns true if allowed, false if rate limited.
    /// </summary>
    public bool TryConsume(string agentName, string capability)
    {
        var now = DateTime.UtcNow;
        var cutoff = now - Window;

        // Check global
        PruneOlderThan(_global, cutoff);
        if (_global.Count >= GlobalLimit) return false;

        // Check per-agent
        var agentQueue = _perAgent.GetOrAdd(agentName, _ => new ConcurrentQueue<DateTime>());
        PruneOlderThan(agentQueue, cutoff);
        if (agentQueue.Count >= PerAgentLimit) return false;

        // Check per-agent-capability
        var capKey = $"{agentName}:{capability}";
        var capQueue = _perAgentCapability.GetOrAdd(capKey, _ => new ConcurrentQueue<DateTime>());
        PruneOlderThan(capQueue, cutoff);
        if (capQueue.Count >= PerCapabilityLimit) return false;

        // Consume
        _global.Enqueue(now);
        agentQueue.Enqueue(now);
        capQueue.Enqueue(now);
        return true;
    }

    private static void PruneOlderThan(ConcurrentQueue<DateTime> queue, DateTime cutoff)
    {
        while (queue.TryPeek(out var oldest) && oldest < cutoff)
            queue.TryDequeue(out _);
    }
}
```

### Step 4: Create AuthzEndpoints.cs

Create `admin/dotnet/src/LucidAdmin.Web/Endpoints/AuthzEndpoints.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Web.Authentication;
using LucidAdmin.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LucidAdmin.Web.Endpoints;

public static class AuthzEndpoints
{
    public static void MapAuthzEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/authz").WithTags("Authorization");

        // Operation token issuance — authenticated via API key (agent callers)
        group.MapPost("/operation-token",
            [Authorize(Policy = "ApiKeyOrJwt")] async (
                OperationTokenRequest request,
                OperationTokenService tokenService,
                OperationTokenRateLimiter rateLimiter,
                IAgentRepository agentRepository,
                ICapabilityRepository capabilityRepository,
                IToolServerRepository toolServerRepository,
                IAuditEventRepository auditRepository,
                ILogger<Program> logger,
                CancellationToken ct) =>
        {
            // 1. Validate agent exists and is active
            var agent = await agentRepository.GetByNameAsync(request.AgentName, ct);
            if (agent == null)
                return Results.Json(
                    new OperationTokenError("agent_not_found", $"Agent '{request.AgentName}' not found"),
                    statusCode: 400);

            if (agent.Status != AgentStatus.Active)
                return Results.Json(
                    new OperationTokenError("agent_inactive", $"Agent '{request.AgentName}' is not active"),
                    statusCode: 403);

            // 2. Validate capability exists
            var capability = await capabilityRepository.GetByNameAsync(request.Capability, ct);
            if (capability == null)
                return Results.Json(
                    new OperationTokenError("capability_not_found",
                        $"No capability found for '{request.Capability}'"),
                    statusCode: 400);

            // 3. Validate tool server URL matches a registered server for this capability
            var toolServers = await toolServerRepository.GetByCapabilityAsync(capability.Id, ct);
            var matchingServer = toolServers.FirstOrDefault(ts =>
                string.Equals(ts.Url.TrimEnd('/'),
                    request.ToolServerUrl.TrimEnd('/'),
                    StringComparison.OrdinalIgnoreCase));

            if (matchingServer == null)
                return Results.Json(
                    new OperationTokenError("tool_server_mismatch",
                        $"Tool server URL '{request.ToolServerUrl}' not found for capability '{request.Capability}'"),
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

        // Signing key endpoint (forward compatibility for JWKS)
        group.MapGet("/keys",
            [Authorize(Policy = "ApiKeyOrJwt")] (
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
```

**IMPORTANT**: After writing this file, check whether `IAgentRepository`, `ICapabilityRepository`, and `IToolServerRepository` have the methods `GetByNameAsync`, `GetByNameAsync`, and `GetByCapabilityAsync` respectively. If not, look at the existing repository interfaces to determine the correct method names and use those instead. Do not add new methods to repository interfaces — use what exists.

### Step 5: Register services and endpoint in Program.cs

In `admin/dotnet/src/LucidAdmin.Web/Program.cs`:

After the existing scoped service registrations (around line where other `AddScoped` calls are), add:
```csharp
builder.Services.AddScoped<OperationTokenService>();
builder.Services.AddSingleton<OperationTokenRateLimiter>();
```

After the line `app.MapApprovalEndpoints();`, add:
```csharp
app.MapAuthzEndpoints();
```

Also add a background timer for nonce cleanup. After `var app = builder.Build();`, add:
```csharp
// Background cleanup for operation token nonces (runs every minute)
var timer = new System.Threading.Timer(_ =>
{
    try
    {
        using var scope = app.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<OperationTokenService>();
        tokenService.CleanupExpiredNonces();
    }
    catch { /* Swallow — cleanup failures must not crash the portal */ }
}, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
```

### Step 6: Export signing key for tool server provisioning

Add a helper method to `OperationTokenService` that returns the raw signing key bytes for provisioning:
```csharp
/// <summary>
/// Returns the signing key as a base64 string for static provisioning to tool servers.
/// This should only be called by the provisioning script, not during normal operation.
/// </summary>
public string ExportSigningKeyForProvisioning()
{
    return Convert.ToBase64String(_jwtKeyManager.GetSigningKey());
}
```

Add a provisioning endpoint to `AuthzEndpoints.cs` (inside the `MapAuthzEndpoints` method):
```csharp
// Key export endpoint for tool server provisioning — Admin role only
group.MapGet("/signing-key/export",
    [Authorize(Policy = "AdminOnly")] (OperationTokenService tokenService) =>
{
    var keyBase64 = tokenService.ExportSigningKeyForProvisioning();
    return Results.Ok(new
    {
        keyBase64,
        algorithm = "HS256",
        keyId = "prx-optoken-v1"
    });
});
```

---

## Part 2: Python Agent — Token Request

### Step 1: Add `request_operation_token` to AdminPortalClient

In `agent/src/agent/config/admin_client.py`, add this method to the `AdminPortalClient` class:
```python
async def request_operation_token(
    self,
    capability: str,
    target: str,
    target_type: str,
    tool_server_url: str,
    ticket_number: str | None = None,
    workflow_execution_id: str | None = None,
    approval_id: str | None = None,
) -> str:
    """Request an operation authorization token from the portal.

    Args:
        capability: The capability name (e.g., "ad-password-reset").
        target: The target entity (username, group name, or path).
        target_type: Type of target: "user", "group", or "path".
        tool_server_url: The tool server URL this token will be used against.
        ticket_number: ServiceNow ticket number for audit trail.
        workflow_execution_id: Workflow execution ID if tracked.
        approval_id: Approval ID if an approval step was involved.

    Returns:
        JWT token string.

    Raises:
        OperationTokenError: If the portal denies the token or returns an error.
        httpx.RequestError: If the portal is unreachable.
    """
    url = "/api/authz/operation-token"

    payload = {
        "agent_name": self.agent_name,
        "capability": capability,
        "target": target,
        "target_type": target_type,
        "tool_server_url": tool_server_url,
        "workflow_context": {
            "ticket_number": ticket_number,
            "workflow_execution_id": workflow_execution_id,
            "approval_id": approval_id,
        }
    }

    logger.debug(f"Requesting operation token: capability={capability}, target={target}")

    try:
        response = await self._client.post(url, json=payload, timeout=10.0)

        if response.status_code == 200:
            data = response.json()
            token = data.get("token")
            if not token:
                raise OperationTokenError("internal_error", "Portal returned success but no token")
            logger.debug(f"Operation token received for {capability}/{target}")
            return token

        elif response.status_code == 429:
            raise OperationTokenError("rate_limited", "Token request rate limit exceeded")

        else:
            try:
                error_data = response.json()
                error_code = error_data.get("error", "unknown")
                error_msg = error_data.get("message", f"HTTP {response.status_code}")
            except Exception:
                error_code = "http_error"
                error_msg = f"HTTP {response.status_code}: {response.text[:200]}"

            raise OperationTokenError(error_code, error_msg)

    except httpx.TimeoutException:
        # Retry once
        logger.warning(f"Operation token request timed out, retrying...")
        try:
            response = await self._client.post(url, json=payload, timeout=5.0)
            if response.status_code == 200:
                data = response.json()
                token = data.get("token")
                if not token:
                    raise OperationTokenError("internal_error", "Portal returned success but no token")
                return token
            else:
                raise OperationTokenError("portal_unavailable",
                    "Authorization service unavailable after retry")
        except httpx.RequestError as e:
            raise OperationTokenError("portal_unavailable",
                f"Authorization service unreachable: {e}")


class OperationTokenError(Exception):
    """Raised when the portal denies an operation token request."""

    def __init__(self, error_code: str, message: str):
        self.error_code = error_code
        self.message = message
        super().__init__(f"[{error_code}] {message}")
```

Add the `OperationTokenError` class at the **module level** (outside the `AdminPortalClient` class, near the top of the file after imports).

### Step 2: Integrate token request into BaseToolServerTool

In `agent/src/agent/tools/base.py`, modify `_make_request()` to:
1. Accept an optional `admin_client` parameter OR use a stored reference
2. Request a token before the HTTP call
3. Include the token in the Authorization header
4. Handle 403 responses from the tool server

**Important design note**: The `BaseToolServerTool` needs access to the `AdminPortalClient` to request tokens. The cleanest approach is to add an optional `admin_client` field to `BaseToolServerTool`. If it's `None`, skip token request (for backward compat/testing). If it's set, request a token.

Here is the modified `base.py`:
```python
"""Base class for agent tools that interact with Tool Server."""

import logging
from typing import Any

from attrs import Factory, define, field

import httpx
from griptape.artifacts import ErrorArtifact, TextArtifact
from griptape.tools import BaseTool
from pydantic import BaseModel, Field as PydanticField

logger = logging.getLogger(__name__)

# Forward declaration for type hints (avoid circular import)
try:
    from agent.routing import CapabilityRouter
except ImportError:
    CapabilityRouter = None  # type: ignore

try:
    from agent.config.admin_client import AdminPortalClient, OperationTokenError
except ImportError:
    AdminPortalClient = None  # type: ignore
    OperationTokenError = Exception  # type: ignore


class ToolServerConfig(BaseModel):
    """Configuration for Tool Server connection."""

    base_url: str = PydanticField(
        default="http://localhost:8000/api/v1",
        description="Base URL of Tool Server API",
    )
    timeout: float = PydanticField(default=30.0, description="Request timeout in seconds")
    verify_ssl: bool = PydanticField(
        default=True, description="Whether to verify SSL certificates"
    )


@define
class BaseToolServerTool(BaseTool):
    """Base class for tools that interact with the Tool Server."""

    tool_server_config: ToolServerConfig = field(
        default=Factory(lambda: ToolServerConfig()),
        kw_only=True,
    )

    capability_router: Any = field(default=None, kw_only=True)  # CapabilityRouter | None
    capability_name: str | None = field(default=None, kw_only=True)

    # Admin Portal client for operation token requests.
    # If None, token request is skipped (legacy/testing mode).
    admin_client: Any = field(default=None, kw_only=True)  # AdminPortalClient | None

    # Context fields set per-call (not stored on the object — passed to _make_request)
    # These are NOT attrs fields; they're passed as kwargs to _make_request.

    async def _make_request(
        self,
        method: str,
        endpoint: str,
        data: dict[str, Any] | None = None,
        # Operation token context (required when admin_client is set)
        target: str | None = None,
        target_type: str = "user",
        ticket_number: str | None = None,
    ) -> dict[str, Any]:
        """Make HTTP request to Tool Server.

        If admin_client is set, requests an operation token from the portal
        before calling the tool server. The token is included in the
        Authorization header.

        Args:
            method: HTTP method (GET, POST, etc.).
            endpoint: API endpoint (relative to base_url).
            data: Optional request body data.
            target: The target entity for the operation token (username, group, path).
            target_type: Type of target: "user", "group", or "path".
            ticket_number: ServiceNow ticket number for audit trail.

        Returns:
            Response JSON as dictionary.

        Raises:
            OperationTokenError: If the portal denies the token.
            Exception: If request fails or no Tool Server available.
        """
        # Resolve base URL
        if self.capability_router and self.capability_name:
            server_info = await self.capability_router.get_server_for_capability(
                self.capability_name
            )
            if not server_info:
                raise Exception(
                    f"No Tool Server available for capability: {self.capability_name}. "
                    f"Ensure at least one Tool Server is registered and healthy."
                )
            base_url = server_info.url.rstrip("/")
            tool_server_url = base_url
            logger.info(
                f"Resolved {self.capability_name} to Tool Server: {server_info.name} ({base_url})"
            )
        else:
            base_url = self.tool_server_config.base_url.rstrip("/")
            tool_server_url = base_url

        url = f"{base_url}/{endpoint.lstrip('/')}"

        # Request operation token if admin_client is available
        headers = {}
        if self.admin_client is not None and self.capability_name and target:
            try:
                logger.debug(
                    f"Requesting operation token: cap={self.capability_name}, "
                    f"target={target}, target_type={target_type}"
                )
                operation_token = await self.admin_client.request_operation_token(
                    capability=self.capability_name,
                    target=target,
                    target_type=target_type,
                    tool_server_url=tool_server_url,
                    ticket_number=ticket_number,
                )
                headers["Authorization"] = f"Bearer {operation_token}"
                logger.debug("Operation token obtained, proceeding with tool server call")
            except OperationTokenError as e:
                logger.error(
                    f"Operation token request denied by portal: [{e.error_code}] {e.message}"
                )
                raise  # Let handler escalate the ticket
            except Exception as e:
                logger.error(f"Failed to request operation token: {e}")
                raise OperationTokenError(
                    "portal_unavailable",
                    f"Authorization service unavailable: {e}"
                ) from e
        elif self.admin_client is not None and not target:
            logger.warning(
                f"admin_client is set but no target provided for {endpoint} — "
                f"proceeding without operation token (read-only or health endpoint)"
            )

        logger.info(f"Making {method} request to {url}")

        async with httpx.AsyncClient(
            timeout=self.tool_server_config.timeout,
            verify=self.tool_server_config.verify_ssl
        ) as client:
            try:
                if method.upper() == "GET":
                    response = await client.get(url, headers=headers)
                elif method.upper() == "POST":
                    response = await client.post(url, json=data, headers=headers)
                else:
                    raise ValueError(f"Unsupported HTTP method: {method}")

                # Handle 403 from tool server (token validation failed)
                if response.status_code == 403:
                    try:
                        error_data = response.json()
                        error_code = error_data.get("error", "token_invalid")
                        error_detail = error_data.get("detail", "Token validation failed")
                    except Exception:
                        error_code = "token_invalid"
                        error_detail = "Token validation failed"
                    error_msg = f"Tool server rejected request: [{error_code}] {error_detail}"
                    logger.error(error_msg)
                    # Don't retry — token failures are not transient
                    raise OperationTokenError(error_code, error_detail)

                response.raise_for_status()
                result = response.json()
                logger.debug(f"Response: {result}")
                return result

            except httpx.HTTPStatusError as e:
                error_msg = f"HTTP {e.response.status_code}: {e.response.text}"
                logger.error(f"Request failed: {error_msg}")
                raise Exception(error_msg)

            except httpx.RequestError as e:
                error_msg = f"Request error: {str(e)}"
                logger.error(error_msg)
                raise Exception(error_msg)

    def _handle_error(self, operation: str, error: Exception) -> ErrorArtifact:
        error_msg = f"Failed to {operation}: {str(error)}"
        logger.error(error_msg)
        return ErrorArtifact(error_msg)
```

### Step 3: Update handlers to pass token context

Now update each handler to pass `target` and `ticket_number` when calling the tool.

In `agent/src/agent/tools/password_reset.py`, find the call to `self._tool.reset_password(...)` and examine the `PasswordResetTool` to see how it calls `_make_request`. You need to pass the `target` (username) and `ticket_number` through to `_make_request`.

The cleanest approach: the individual tool's action method (e.g., `reset_password`) should accept and forward `target` and `ticket_number` parameters to `_make_request`.

**In each of the three tool files** (`password_reset.py`, `group_management.py`, `file_permissions.py`):
1. Add `target: str | None = None` and `ticket_number: str | None = None` parameters to the action methods
2. Forward these to `_make_request()`

**In each of the three handler files** (`password_reset.py`, `group_access.py`, `file_permission.py`):
1. Extract `ticket.number` and the relevant target from `classification`
2. Pass them to the tool method

**In `TicketExecutor._register_handlers()`**:
After the existing code creates handlers, wire up the `admin_client`. Add after creating the handlers list:
```python
if self._admin_client:
    for handler in handlers:
        # Pass admin_client to each handler so tools can request tokens
        if hasattr(handler, '_tool'):
            handler._tool.admin_client = self._admin_client
```

**Note to Claude Code**: Before implementing the handler/tool wiring, read the actual tool files (`password_reset.py`, `group_management.py`, `file_permissions.py`) to understand their exact structure, then implement the target/ticket_number passing in a way that's consistent with how they currently call `_make_request`.

---

## Part 3: Tool Server — Token Validation

### Step 1: Add NuGet package

Add to `tool-server/dotnet/src/LucidToolServer/LucidToolServer.csproj`:
```xml
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.*" />
```

### Step 2: Add JWT configuration to appsettings.json

In `tool-server/dotnet/src/LucidToolServer/appsettings.json`, add inside the `"ToolServer"` object:
```json
"OperationToken": {
  "SigningKeyBase64": "",
  "Issuer": "praxova-portal",
  "ClockSkewSeconds": 30,
  "TokenSigningKeyPath": "certs/token-signing-key.json",
  "SelfUrl": "https://tool01.montanifarms.com:8443",
  "CapabilityEndpointMap": {
    "ad-password-reset": ["/api/v1/password/reset"],
    "ad-group-add": ["/api/v1/groups/add-member"],
    "ad-group-remove": ["/api/v1/groups/remove-member"],
    "ad-account-unlock": ["/api/v1/account/unlock"],
    "ntfs-permission-grant": ["/api/v1/permissions/grant"],
    "ntfs-permission-revoke": ["/api/v1/permissions/revoke"]
  }
}
```

### Step 3: Create OperationTokenValidator service

Create `tool-server/dotnet/src/LucidToolServer/Services/OperationTokenValidator.cs`:
```csharp
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace LucidToolServer.Services;

/// <summary>
/// Validates operation tokens issued by the Praxova portal.
/// Checks: signature, expiration, issuer, capability, target, tool server URL, and replay.
/// </summary>
public class OperationTokenValidator
{
    private readonly SymmetricSecurityKey _signingKey;
    private readonly string _issuer;
    private readonly string _selfUrl;
    private readonly Dictionary<string, string[]> _capabilityEndpointMap;
    private readonly JwtSecurityTokenHandler _handler = new();
    private readonly ILogger<OperationTokenValidator> _logger;

    // Nonce tracking: jti → expiry+buffer. Prevents replay attacks.
    private readonly ConcurrentDictionary<string, DateTime> _consumedNonces = new();

    public OperationTokenValidator(
        byte[] signingKeyBytes,
        string issuer,
        string selfUrl,
        Dictionary<string, string[]> capabilityEndpointMap,
        ILogger<OperationTokenValidator> logger)
    {
        _signingKey = new SymmetricSecurityKey(signingKeyBytes);
        _issuer = issuer;
        _selfUrl = selfUrl.TrimEnd('/');
        _capabilityEndpointMap = capabilityEndpointMap;
        _logger = logger;
    }

    public record ValidationResult(bool IsValid, string? ErrorCode, string? ErrorDetail);

    /// <summary>
    /// Validate the operation token for the given endpoint and request body.
    /// </summary>
    public ValidationResult Validate(
        string tokenString,
        string requestPath,
        string? requestTarget)
    {
        // 1. Standard JWT validation (signature, expiration, issuer)
        ClaimsPrincipal principal;
        try
        {
            principal = _handler.ValidateToken(tokenString, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = false,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _signingKey,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
            }, out var validatedToken);
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogWarning("Operation token expired for path {Path}", requestPath);
            return new ValidationResult(false, "token_expired", "Token has expired");
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            _logger.LogWarning("Operation token signature invalid for path {Path}", requestPath);
            return new ValidationResult(false, "token_signature_invalid", "Token signature is invalid");
        }
        catch (SecurityTokenInvalidIssuerException)
        {
            _logger.LogWarning("Operation token issuer invalid for path {Path}", requestPath);
            return new ValidationResult(false, "token_issuer_invalid", "Token issuer is invalid");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Operation token validation failed for path {Path}", requestPath);
            return new ValidationResult(false, "token_invalid", "Token validation failed");
        }

        // 2. Extract custom claims
        var capClaim = principal.FindFirst("cap")?.Value;
        var targetClaim = principal.FindFirst("target")?.Value;
        var targetTypeClaim = principal.FindFirst("target_type")?.Value;
        var tsUrlClaim = principal.FindFirst("ts_url")?.Value;
        var jtiClaim = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;

        // 3. Validate capability matches endpoint
        if (!CapabilityMatchesEndpoint(capClaim, requestPath))
        {
            _logger.LogWarning(
                "Token capability '{Cap}' does not match endpoint '{Path}'",
                capClaim, requestPath);
            return new ValidationResult(false, "token_capability_mismatch",
                $"Token capability '{capClaim}' does not match endpoint capability");
        }

        // 4. Validate target matches request body
        if (!string.IsNullOrEmpty(requestTarget) && !string.IsNullOrEmpty(targetClaim))
        {
            var targetTypeIsPath = string.Equals(targetTypeClaim, "path", StringComparison.OrdinalIgnoreCase);
            var matches = targetTypeIsPath
                ? string.Equals(targetClaim, requestTarget, StringComparison.Ordinal)
                : string.Equals(targetClaim, requestTarget, StringComparison.OrdinalIgnoreCase);

            if (!matches)
            {
                _logger.LogWarning(
                    "Token target '{TokenTarget}' does not match request target '{RequestTarget}'",
                    targetClaim, requestTarget);
                return new ValidationResult(false, "token_target_mismatch",
                    $"Token target does not match request target");
            }
        }

        // 5. Validate tool server URL matches self
        if (!string.IsNullOrEmpty(tsUrlClaim))
        {
            if (!string.Equals(tsUrlClaim.TrimEnd('/'), _selfUrl, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Token ts_url '{TokenUrl}' does not match self URL '{SelfUrl}'",
                    tsUrlClaim, _selfUrl);
                return new ValidationResult(false, "token_server_mismatch",
                    "Token is not valid for this tool server");
            }
        }

        // 6. Replay prevention
        if (!string.IsNullOrEmpty(jtiClaim))
        {
            // Try to add — if it already exists, this is a replay
            var expiry = DateTime.UtcNow.AddSeconds(360); // 6 minutes cleanup window
            if (!_consumedNonces.TryAdd(jtiClaim, expiry))
            {
                _logger.LogWarning("Replayed operation token jti={Jti}", jtiClaim);
                return new ValidationResult(false, "token_replayed", "Token has already been used");
            }
        }

        return new ValidationResult(true, null, null);
    }

    private bool CapabilityMatchesEndpoint(string? capability, string requestPath)
    {
        if (string.IsNullOrEmpty(capability)) return false;
        if (!_capabilityEndpointMap.TryGetValue(capability, out var allowedEndpoints)) return false;

        return allowedEndpoints.Any(ep =>
            requestPath.StartsWith(ep, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Remove expired nonces. Call periodically (e.g., every minute).
    /// </summary>
    public void CleanupExpiredNonces()
    {
        var now = DateTime.UtcNow;
        var expired = _consumedNonces
            .Where(kvp => kvp.Value < now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expired)
            _consumedNonces.TryRemove(key, out _);
    }
}
```

### Step 4: Modify Tool Server Program.cs

At the top of `Program.cs`, add:

1. Load signing key from config/file at startup:
```csharp
// Load operation token signing key
byte[]? operationTokenSigningKey = null;
var opTokenSettings = builder.Configuration.GetSection("ToolServer:OperationToken");
var signingKeyBase64 = opTokenSettings["SigningKeyBase64"];
var signingKeyPath = opTokenSettings["TokenSigningKeyPath"];

if (!string.IsNullOrEmpty(signingKeyBase64))
{
    operationTokenSigningKey = Convert.FromBase64String(signingKeyBase64);
    Log.Information("Operation token signing key loaded from configuration");
}
else if (!string.IsNullOrEmpty(signingKeyPath))
{
    var fullKeyPath2 = Path.IsPathRooted(signingKeyPath)
        ? signingKeyPath
        : Path.Combine(builder.Environment.ContentRootPath, signingKeyPath);

    if (File.Exists(fullKeyPath2))
    {
        var keyJson = await File.ReadAllTextAsync(fullKeyPath2);
        var keyData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(keyJson);
        if (keyData?.TryGetValue("keyBase64", out var keyB64) == true)
        {
            operationTokenSigningKey = Convert.FromBase64String(keyB64);
            Log.Information("Operation token signing key loaded from file: {Path}", fullKeyPath2);
        }
    }
    else
    {
        Log.Warning("Operation token signing key file not found: {Path}. Token validation will be disabled.", fullKeyPath2);
    }
}
else
{
    Log.Warning("No operation token signing key configured. Token validation will be disabled. Set ToolServer:OperationToken:SigningKeyBase64 or TokenSigningKeyPath.");
}
```

2. Register the validator as a singleton (after loading key):
```csharp
if (operationTokenSigningKey != null)
{
    var capMapJson = opTokenSettings.GetSection("CapabilityEndpointMap").Get<Dictionary<string, string[]>>()
        ?? new Dictionary<string, string[]>();
    var selfUrl = opTokenSettings["SelfUrl"] ?? "https://localhost:8443";
    var issuer = opTokenSettings["Issuer"] ?? "praxova-portal";

    builder.Services.AddSingleton(sp =>
        new OperationTokenValidator(
            operationTokenSigningKey,
            issuer,
            selfUrl,
            capMapJson,
            sp.GetRequiredService<ILogger<OperationTokenValidator>>()));
}
```

3. Add token validation middleware BEFORE the route definitions (after the global exception handler):
```csharp
// Operation token validation middleware
// Applies to all /api/v1 operation endpoints except /health and read-only query endpoints.
// Health check and GET (query) endpoints are exempt.
var tokenExemptPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "/api/v1/health",
    "/api/health"
};

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    var method = context.Request.Method;

    // Exempt: health checks, and all GET requests (query/read-only operations)
    // Require token: all POST requests to /api/v1/ except health
    var requiresToken =
        method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
        path.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase) &&
        !tokenExemptPaths.Contains(path);

    if (!requiresToken)
    {
        await next(context);
        return;
    }

    // Get the validator (may be null if no signing key configured)
    var validator = context.RequestServices.GetService<OperationTokenValidator>();
    if (validator == null)
    {
        // Token validation not configured — log warning and allow (degraded mode)
        var degradedLogger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        degradedLogger.LogWarning(
            "Operation token validation not configured. Allowing request to {Path} without token validation.",
            path);
        await next(context);
        return;
    }

    // Extract Bearer token
    var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
    if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 403;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "token_missing",
            detail = "Authorization Bearer token required"
        });
        return;
    }

    var tokenString = authHeader["Bearer ".Length..].Trim();

    // We need the request body for target validation.
    // Enable buffering so we can read the body without consuming it.
    context.Request.EnableBuffering();
    string? requestTarget = null;
    try
    {
        // Read body to extract target field
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0; // Reset for actual handler

        if (!string.IsNullOrEmpty(body))
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            // Try common target fields in order of specificity
            if (doc.RootElement.TryGetProperty("username", out var u))
                requestTarget = u.GetString();
            else if (doc.RootElement.TryGetProperty("path", out var p))
                requestTarget = p.GetString();
        }
    }
    catch
    {
        // If body parsing fails, proceed without target validation
    }

    var result = validator.Validate(tokenString, path, requestTarget);
    if (!result.IsValid)
    {
        context.Response.StatusCode = 403;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = result.ErrorCode,
            detail = result.ErrorDetail
        });
        return;
    }

    await next(context);
});
```

4. Add background cleanup timer for nonces (after `var app = builder.Build();`):
```csharp
// Background nonce cleanup (only when validator is registered)
if (operationTokenSigningKey != null)
{
    var nonceCleanupTimer = new System.Threading.Timer(_ =>
    {
        try
        {
            var v = app.Services.GetService<OperationTokenValidator>();
            v?.CleanupExpiredNonces();
        }
        catch { }
    }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
}
```

---

## Part 4: Provisioning Script Update

In `scripts/provision-toolserver-certs.ps1`, after the existing cert deployment steps, add:
```powershell
# ── Deploy operation token signing key ───────────────────────────────────────
Write-Host "Fetching operation token signing key from portal..."

$KeyExportResponse = Invoke-RestMethod `
    -Uri "$PortalUrl/api/authz/signing-key/export" `
    -Method GET `
    -Headers @{ "Authorization" = "Bearer $Token" } `
    -SkipCertificateCheck:($PortalUrl -match 'localhost|127.0.0.1')

$KeyJson = @{
    keyBase64 = $KeyExportResponse.keyBase64
    algorithm = $KeyExportResponse.algorithm
    keyId = $KeyExportResponse.keyId
} | ConvertTo-Json

$KeyFilePath = "\\$ToolServerHost\C$\Program Files\Praxova\ToolServer\certs\token-signing-key.json"
$KeyJson | Set-Content -Path $KeyFilePath -Encoding UTF8 -Force

Write-Host "Operation token signing key deployed to $ToolServerHost"
Write-Host ""
Write-Host "IMPORTANT: Update appsettings.json on $ToolServerHost:"
Write-Host "  ToolServer:OperationToken:SelfUrl = https://$ToolServerHost:8443"
Write-Host "  ToolServer:OperationToken:TokenSigningKeyPath = certs/token-signing-key.json"
```

---

## Part 5: Repository Interface Check (IMPORTANT)

Before compiling the portal, read these files to check that the method names I referenced exist:

1. `admin/dotnet/src/LucidAdmin.Core/Interfaces/Repositories/IAgentRepository.cs` — check for a method to get an agent by name
2. `admin/dotnet/src/LucidAdmin.Core/Interfaces/Repositories/` — check for `ICapabilityRepository.cs` and the method to get capability by name
3. `admin/dotnet/src/LucidAdmin.Core/Interfaces/Repositories/` — check for `IToolServerRepository.cs` and method to get tool servers by capability

If these repositories or methods don't exist with those exact names, look at what does exist and adapt the `AuthzEndpoints.cs` code to use the correct types and method names. You may need to access tool server by capability through a different path (e.g., through `ICapabilityMappingRepository`).

---

## Part 6: Testing

After implementation, verify the following manually:

**Setup**: Make sure the agent is running and the tool server is running.

**Test 1 — Token issued and accepted**:
```bash
# 1. Process a test password reset ticket end-to-end
python agent/scripts/create_test_tickets.py --type password_reset
# Watch agent logs for "Operation token obtained"
# Verify portal audit log shows OperationTokenIssued event
```

**Test 2 — No token → 403**:
```bash
curl -sk -X POST https://tool01:8443/api/v1/password/reset \
  -H 'Content-Type: application/json' \
  -d '{"username": "testuser"}'
# Expected: 403 {"error": "token_missing", ...}
```

**Test 3 — Expired token → 403**:
```bash
# Request a token manually, wait 6 minutes, then use it
# Expected: 403 {"error": "token_expired", ...}
```

**Test 4 — Replayed token → 403**:
```bash
# Use the same token twice in succession
# First call should succeed, second should return 403 token_replayed
```

---

## Git commit guidance

Make one commit per component:
feat(portal): add OperationTokenIssued and OperationTokenDenied audit actions
feat(portal): add operation token issuance endpoint with rate limiting
feat(agent): request operation token before tool server calls
feat(agent): handle token request failures with escalation
feat(toolserver): add JWT operation token validation middleware
feat(toolserver): add nonce replay prevention
test: end-to-end operation token authorization chain
