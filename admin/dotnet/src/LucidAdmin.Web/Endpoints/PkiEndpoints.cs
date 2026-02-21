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
