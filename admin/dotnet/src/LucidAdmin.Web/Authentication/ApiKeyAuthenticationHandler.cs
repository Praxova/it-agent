using System.Security.Claims;
using System.Text.Encodings.Web;
using LucidAdmin.Core.Authorization;
using LucidAdmin.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LucidAdmin.Web.Authentication;

/// <summary>
/// Authentication handler for API key-based authentication
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private const string ApiKeyHeaderName = "X-API-Key";
    private const string AuthorizationHeaderName = "Authorization";
    private const string ApiKeySchemePrefix = "ApiKey ";

    private readonly IApiKeyService _apiKeyService;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyService apiKeyService)
        : base(options, logger, encoder)
    {
        _apiKeyService = apiKeyService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Try to get API key from X-API-Key header
        var apiKey = Request.Headers[ApiKeyHeaderName].FirstOrDefault();

        // If not found, try Authorization: ApiKey header
        if (string.IsNullOrEmpty(apiKey))
        {
            var authHeader = Request.Headers[AuthorizationHeaderName].FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith(ApiKeySchemePrefix, StringComparison.OrdinalIgnoreCase))
            {
                apiKey = authHeader[ApiKeySchemePrefix.Length..].Trim();
            }
        }

        // No API key found
        if (string.IsNullOrEmpty(apiKey))
        {
            return AuthenticateResult.NoResult();
        }

        // Validate the API key
        var validationResult = await _apiKeyService.ValidateKeyAsync(apiKey, Context.RequestAborted);

        if (!validationResult.IsValid || validationResult.Key == null)
        {
            Logger.LogWarning("API key validation failed: {Reason}", validationResult.ErrorMessage);
            return AuthenticateResult.Fail(validationResult.ErrorMessage ?? "Invalid API key");
        }

        var key = validationResult.Key;

        // Build claims
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, key.Id.ToString()),
            new(ClaimTypes.Name, key.Name),
            new(ClaimTypes.Role, key.Role.ToString()),
            new("key_id", key.Id.ToString()),
            new("key_prefix", key.KeyPrefix)
        };

        // Add agent or tool server ID
        if (key.AgentId.HasValue)
        {
            claims.Add(new Claim("agent_id", key.AgentId.Value.ToString()));
        }

        if (key.ToolServerId.HasValue)
        {
            claims.Add(new Claim("tool_server_id", key.ToolServerId.Value.ToString()));
        }

        // Add permissions based on role
        var permissions = RolePermissions.GetPermissions(key.Role);
        foreach (var permission in permissions)
        {
            claims.Add(new("permission", permission));
        }

        // Add allowed service accounts for agent keys
        if (!string.IsNullOrEmpty(key.AllowedServiceAccountIds))
        {
            claims.Add(new Claim("allowed_service_accounts", key.AllowedServiceAccountIds));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        // Update last used timestamp asynchronously (fire and forget)
        var ipAddress = Context.Connection.RemoteIpAddress?.ToString();
        _ = Task.Run(async () =>
        {
            try
            {
                await _apiKeyService.UpdateLastUsedAsync(key.Id, ipAddress, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to update last used timestamp for API key {KeyId}", key.Id);
            }
        });

        Logger.LogInformation("API key {KeyPrefix} authenticated successfully for {Role}", key.KeyPrefix, key.Role);

        return AuthenticateResult.Success(ticket);
    }
}
