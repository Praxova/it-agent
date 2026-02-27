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
    /// Returns the signing key as a base64 string for static provisioning to tool servers.
    /// This should only be called by the provisioning script, not during normal operation.
    /// </summary>
    public string ExportSigningKeyForProvisioning()
    {
        return Convert.ToBase64String(_jwtKeyManager.GetSigningKey());
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
