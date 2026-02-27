using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
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
        try
        {
            _handler.ValidateToken(tokenString, new TokenValidationParameters
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

            // Extract claims from validated token
            var jwtToken = (JwtSecurityToken)validatedToken;
            var capClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "cap")?.Value;
            var targetClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "target")?.Value;
            var targetTypeClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "target_type")?.Value;
            var tsUrlClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "ts_url")?.Value;
            var jtiClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value;

            // 2. Validate capability matches endpoint
            if (!CapabilityMatchesEndpoint(capClaim, requestPath))
            {
                _logger.LogWarning(
                    "Token capability '{Cap}' does not match endpoint '{Path}'",
                    capClaim, requestPath);
                return new ValidationResult(false, "token_capability_mismatch",
                    $"Token capability '{capClaim}' does not match endpoint capability");
            }

            // 3. Validate target matches request body
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
                        "Token target does not match request target");
                }
            }

            // 4. Validate tool server URL matches self
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

            // 5. Replay prevention
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
