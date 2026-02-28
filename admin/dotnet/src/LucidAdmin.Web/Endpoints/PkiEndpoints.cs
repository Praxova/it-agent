using System.Security.Claims;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Infrastructure.Data;
using LucidAdmin.Web.Authorization;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Web.Endpoints;

public static class PkiEndpoints
{
    public static void MapPkiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pki").WithTags("PKI");

        // Trust bundle — no auth required (other containers fetch CA cert before they can authenticate)
        group.MapGet("/trust-bundle", (IInternalPkiService pkiService) =>
        {
            if (!pkiService.IsInitialized)
            {
                return Results.StatusCode(503); // Service Unavailable — PKI not ready
            }

            var pem = pkiService.GetCaCertificatePem();
            return Results.Text(pem, "text/plain");
        }).WithName("GetTrustBundle");

        // List all issued certificates — admin only
        group.MapGet("/certificates", async (LucidDbContext db) =>
        {
            var certs = await db.IssuedCertificates
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new CertificateSummary(
                    c.Id,
                    c.Name,
                    c.SubjectCN,
                    c.SubjectAlternativeNames,
                    c.Thumbprint,
                    c.SerialNumber,
                    c.NotBefore,
                    c.NotAfter,
                    c.Usage,
                    c.IssuedTo,
                    c.IsActive,
                    c.CertPath,
                    c.RenewedAt,
                    c.ReplacedByThumbprint))
                .ToListAsync();

            return Results.Ok(certs);
        })
        .WithName("ListCertificates")
        .RequireAuthorization(AuthorizationPolicies.RequireAdmin);

        // Certificate health summary — admin only
        group.MapGet("/certificates/health-summary", async (LucidDbContext db) =>
        {
            var certs = await db.IssuedCertificates
                .Where(c => c.IsActive)
                .OrderBy(c => c.NotAfter)
                .ToListAsync();

            var now = DateTime.UtcNow;
            var entries = certs.Select(c =>
            {
                var daysRemaining = (int)(c.NotAfter - now).TotalDays;
                var status = daysRemaining < 0 ? "expired"
                    : daysRemaining < 7 ? "critical"
                    : daysRemaining < 30 ? "warning"
                    : "ok";

                var renewalMode = c.Usage switch
                {
                    "ca-root" => (string?)null,
                    "server-tls" when string.Equals(c.IssuedTo, "admin-portal", StringComparison.OrdinalIgnoreCase) => "auto",
                    "client-tls" => "auto",
                    _ => "manual"
                };

                return new CertificateHealthEntry(
                    c.Name,
                    c.SubjectCN,
                    c.Usage,
                    c.IssuedTo,
                    c.NotAfter,
                    daysRemaining,
                    status,
                    renewalMode,
                    c.Thumbprint,
                    c.SerialNumber);
            }).ToList();

            return Results.Ok(entries);
        })
        .WithName("GetCertificateHealthSummary")
        .RequireAuthorization(AuthorizationPolicies.RequireAdmin);

        // Get specific certificate by name — admin only
        group.MapGet("/certificates/{name}", async (string name, LucidDbContext db) =>
        {
            var cert = await db.IssuedCertificates
                .Where(c => c.Name == name)
                .OrderByDescending(c => c.IsActive)
                .ThenByDescending(c => c.CreatedAt)
                .Select(c => new CertificateSummary(
                    c.Id,
                    c.Name,
                    c.SubjectCN,
                    c.SubjectAlternativeNames,
                    c.Thumbprint,
                    c.SerialNumber,
                    c.NotBefore,
                    c.NotAfter,
                    c.Usage,
                    c.IssuedTo,
                    c.IsActive,
                    c.CertPath,
                    c.RenewedAt,
                    c.ReplacedByThumbprint))
                .ToListAsync();

            if (cert.Count == 0)
                return Results.NotFound(new { error = $"No certificates found with name '{name}'" });

            return Results.Ok(cert);
        })
        .WithName("GetCertificateByName")
        .RequireAuthorization(AuthorizationPolicies.RequireAdmin);

        // Force renewal — admin only
        group.MapPost("/certificates/{name}/renew", async (
            string name,
            IInternalPkiService pkiService,
            ILogger<IInternalPkiService> logger) =>
        {
            if (!pkiService.IsInitialized)
            {
                return Results.StatusCode(503);
            }

            try
            {
                var dataDir = app.ServiceProvider.GetRequiredService<IConfiguration>()
                    .GetValue<string>("DataDirectory") ?? "/app/data";
                var certDir = Path.Combine(dataDir, "certs");

                var (certPem, keyPem) = await pkiService.RenewCertificateAsync(name);

                // Write renewed cert to disk
                var certPath = Path.Combine(certDir, $"{name}-cert.pem");
                var keyPath = Path.Combine(certDir, $"{name}-key.pem");

                // For admin-portal, use the standard names
                if (name == "admin-portal")
                {
                    certPath = Path.Combine(certDir, "portal-cert.pem");
                    keyPath = Path.Combine(certDir, "portal-key.pem");
                }

                await File.WriteAllTextAsync(certPath, certPem);
                await File.WriteAllTextAsync(keyPath, keyPem);

                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }

                logger.LogInformation("Certificate '{Name}' force-renewed via API", name);
                return Results.Ok(new { message = $"Certificate '{name}' renewed. Restart may be required for Kestrel to pick up the new certificate." });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("RenewCertificate")
        .RequireAuthorization(AuthorizationPolicies.RequireAdmin);

        // POST /api/pki/certificates/issue — Issue a new certificate (admin only)
        group.MapPost("/certificates/issue", async (
            IssueCertificateRequest request,
            IInternalPkiService pkiService) =>
        {
            if (!pkiService.IsInitialized)
                return Results.StatusCode(503);

            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.CommonName))
                return Results.BadRequest(new { error = "Name and CommonName are required" });

            try
            {
                var (certPem, keyPem) = await pkiService.IssueCertificateAsync(
                    name: request.Name,
                    commonName: request.CommonName,
                    sanDnsNames: request.DnsNames,
                    sanIpAddresses: request.IpAddresses,
                    lifetimeDays: request.LifetimeDays ?? 90);

                return Results.Ok(new IssueCertificateResponse(
                    Name: request.Name,
                    CertificatePem: certPem,
                    PrivateKeyPem: keyPem,
                    CaCertificatePem: pkiService.GetCaCertificatePem()));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("IssueCertificate")
        .WithDescription("Issue a new TLS certificate signed by the Praxova internal CA")
        .RequireAuthorization(AuthorizationPolicies.RequireAdmin)
        .Produces<IssueCertificateResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        // GET /api/pki/certificates/agent/{agentName} — get or issue agent mTLS client cert
        // Requires agent API key auth — agents call this at startup.
        // Security: fail closed — agent can only fetch its own cert.
        group.MapGet("/certificates/agent/{agentName}", async (
            string agentName,
            IInternalPkiService pkiService,
            LucidDbContext db,
            HttpContext httpContext) =>
        {
            if (!pkiService.IsInitialized)
                return Results.StatusCode(503);

            // Fail closed: extract agent identity from API key claims.
            // If we can't determine the requesting agent's name, deny the request.
            var agentIdClaim = httpContext.User.FindFirst("agent_id")?.Value;
            if (string.IsNullOrEmpty(agentIdClaim) || !Guid.TryParse(agentIdClaim, out var agentId))
            {
                return Results.Json(
                    new { error = "agent_identity_required", detail = "API key is not associated with an agent" },
                    statusCode: 403);
            }

            // Look up the agent to get its name
            var agent = await db.Agents.FirstOrDefaultAsync(a => a.Id == agentId);
            if (agent == null)
            {
                return Results.Json(
                    new { error = "agent_not_found", detail = "Agent associated with this API key no longer exists" },
                    statusCode: 403);
            }

            // Verify the requesting agent matches the requested cert name
            if (!string.Equals(agent.Name, agentName, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(
                    new { error = "agent_mismatch", detail = "You can only request your own client certificate" },
                    statusCode: 403);
            }

            try
            {
                var (certPem, keyPem, expiresAt) = await pkiService.GetOrIssueAgentClientCertAsync(agentName);

                return Results.Ok(new AgentClientCertResponse(
                    AgentName: agentName,
                    CertificatePem: certPem,
                    PrivateKeyPem: keyPem,
                    CaCertificatePem: pkiService.GetCaCertificatePem(),
                    ExpiresAt: expiresAt));
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        })
        .WithName("GetAgentClientCert")
        .WithDescription("Get or issue an mTLS client certificate for an agent")
        .RequireAuthorization(AuthorizationPolicies.RequireAgent);

        // POST /api/pki/certificates/renew — agent-initiated cert renewal
        // Agents call this when their client cert is approaching expiry.
        // Distinct from the admin force-renew endpoint (POST /certificates/{name}/renew).
        group.MapPost("/certificates/renew", async (
            RenewCertificateRequest request,
            IInternalPkiService pkiService,
            LucidDbContext db,
            HttpContext httpContext) =>
        {
            if (!pkiService.IsInitialized)
                return Results.StatusCode(503);

            // Extract agent identity from API key claims
            var agentIdClaim = httpContext.User.FindFirst("agent_id")?.Value;
            if (string.IsNullOrEmpty(agentIdClaim) || !Guid.TryParse(agentIdClaim, out var agentId))
            {
                return Results.Json(
                    new { error = "agent_identity_required", detail = "API key is not associated with an agent" },
                    statusCode: 403);
            }

            // Look up agent
            var agent = await db.Agents.FirstOrDefaultAsync(a => a.Id == agentId);
            if (agent == null || !agent.IsEnabled)
            {
                return Results.Json(
                    new { error = "agent_not_found", detail = "Agent not found or not active" },
                    statusCode: 403);
            }

            // Validate agent name matches
            if (!string.Equals(agent.Name, request.AgentName, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(
                    new { error = "agent_mismatch", detail = "You can only renew your own client certificate" },
                    statusCode: 403);
            }

            // Look up the cert being renewed
            var serialNormalized = request.CurrentCertSerial.ToLowerInvariant();
            var cert = await db.IssuedCertificates
                .FirstOrDefaultAsync(c => c.SerialNumber == serialNormalized && c.IsActive);

            if (cert == null)
            {
                return Results.NotFound(new { error = "cert_not_found", detail = $"No active certificate with serial '{request.CurrentCertSerial}'" });
            }

            // Validate cert belongs to this agent
            var expectedIssuedTo = agent.Name;
            if (!string.Equals(cert.IssuedTo, expectedIssuedTo, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(
                    new { error = "cert_ownership_mismatch", detail = "Certificate does not belong to this agent" },
                    statusCode: 403);
            }

            // Renewal window check
            var daysRemaining = (cert.NotAfter - DateTime.UtcNow).TotalDays;
            if (daysRemaining > 30)
            {
                return Results.BadRequest(new
                {
                    error = "not_in_renewal_window",
                    detail = "Certificate does not expire within 30 days",
                    expires_at = cert.NotAfter.ToString("O"),
                    days_remaining = (int)daysRemaining
                });
            }

            // Issue renewed cert
            try
            {
                var (certPem, keyPem, expiresAt) = await pkiService.GetOrIssueAgentClientCertAsync(agent.Name);

                // Look up the new cert record for serial number
                var certName = $"agent-client-{agent.Name}";
                var newCert = await db.IssuedCertificates
                    .FirstOrDefaultAsync(c => c.Name == certName && c.IsActive);

                return Results.Ok(new RenewCertificateResponse(
                    CertificatePem: certPem,
                    PrivateKeyPem: keyPem,
                    CaCertificatePem: pkiService.GetCaCertificatePem(),
                    ExpiresAt: newCert?.NotAfter ?? expiresAt,
                    SerialNumber: newCert?.SerialNumber ?? ""));
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        })
        .WithName("RenewAgentClientCert")
        .WithDescription("Agent-initiated renewal of its mTLS client certificate")
        .RequireAuthorization(AuthorizationPolicies.RequireAgent);
    }
}

public record IssueCertificateRequest(
    string Name,
    string CommonName,
    string[]? DnsNames = null,
    string[]? IpAddresses = null,
    int? LifetimeDays = null);

public record IssueCertificateResponse(
    string Name,
    string CertificatePem,
    string PrivateKeyPem,
    string CaCertificatePem);

public record AgentClientCertResponse(
    string AgentName,
    string CertificatePem,
    string PrivateKeyPem,
    string CaCertificatePem,
    DateTime ExpiresAt);

public record CertificateSummary(
    Guid Id,
    string Name,
    string SubjectCN,
    string? SubjectAlternativeNames,
    string Thumbprint,
    string SerialNumber,
    DateTime NotBefore,
    DateTime NotAfter,
    string Usage,
    string? IssuedTo,
    bool IsActive,
    string? CertPath,
    DateTime? RenewedAt,
    string? ReplacedByThumbprint);

public record RenewCertificateRequest(
    string CurrentCertSerial,
    string AgentName);

public record RenewCertificateResponse(
    string CertificatePem,
    string PrivateKeyPem,
    string CaCertificatePem,
    DateTime ExpiresAt,
    string SerialNumber);

public record CertificateHealthEntry(
    string Name,
    string SubjectCN,
    string Usage,
    string? IssuedTo,
    DateTime ExpiresAt,
    int DaysRemaining,
    string Status,
    string? RenewalMode,
    string Thumbprint,
    string SerialNumber);
