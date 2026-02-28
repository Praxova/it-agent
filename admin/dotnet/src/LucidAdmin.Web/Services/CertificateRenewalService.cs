using System.Text.Json;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Infrastructure.Data;
using LucidAdmin.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Web.Services;

/// <summary>
/// Background service that monitors certificate expiry daily.
/// - Renews the admin portal TLS cert when within 30 days of expiry.
/// - Logs warnings for tool server certs approaching expiry.
/// </summary>
public class CertificateRenewalService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IInternalPkiService _pkiService;
    private readonly ISealManager _sealManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CertificateRenewalService> _logger;

    public CertificateRenewalService(
        IServiceScopeFactory scopeFactory,
        IInternalPkiService pkiService,
        ISealManager sealManager,
        IConfiguration configuration,
        ILogger<CertificateRenewalService> logger)
    {
        _scopeFactory = scopeFactory;
        _pkiService = pkiService;
        _sealManager = sealManager;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Certificate renewal service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunChecksAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Certificate renewal check failed — will retry in 24 hours");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task RunChecksAsync(CancellationToken ct)
    {
        if (!_sealManager.IsUnsealed)
        {
            _logger.LogDebug("Secrets store is sealed — skipping certificate renewal check");
            return;
        }

        if (!_pkiService.IsInitialized)
        {
            _logger.LogDebug("PKI not initialized — skipping certificate renewal check");
            return;
        }

        await CheckPortalCertRenewalAsync(ct);
        await CheckToolServerCertExpiryAsync(ct);
    }

    private async Task CheckPortalCertRenewalAsync(CancellationToken ct)
    {
        const string portalCertName = "admin-portal";
        const int renewalThresholdDays = 30;
        const int renewalLifetimeDays = 365;

        if (!await _pkiService.NeedsRenewalAsync(portalCertName, renewalThresholdDays))
        {
            _logger.LogDebug("Portal TLS certificate is not due for renewal");
            return;
        }

        _logger.LogInformation("Portal TLS certificate is within {Threshold} days of expiry — renewing",
            renewalThresholdDays);

        // Capture old thumbprint for audit
        string? oldThumbprint = null;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LucidDbContext>();
            var existing = await db.IssuedCertificates
                .FirstOrDefaultAsync(c => c.Name == portalCertName && c.IsActive, ct);
            oldThumbprint = existing?.Thumbprint;
        }

        // Renew
        var (certPem, keyPem) = await _pkiService.RenewCertificateAsync(portalCertName, renewalLifetimeDays);

        // Write cert files to disk (same paths Kestrel is watching)
        var dataDir = _configuration.GetValue<string>("DataDirectory") ?? "/app/data";
        var certPath = Path.Combine(dataDir, "certs", "portal-cert.pem");
        var keyPath = Path.Combine(dataDir, "certs", "portal-key.pem");

        await File.WriteAllTextAsync(certPath, certPem, ct);
        await File.WriteAllTextAsync(keyPath, keyPem, ct);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        // Look up new cert record for audit
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LucidDbContext>();
            var newCert = await db.IssuedCertificates
                .FirstOrDefaultAsync(c => c.Name == portalCertName && c.IsActive, ct);

            // Write audit event
            var auditService = scope.ServiceProvider.GetRequiredService<AuditChainService>();
            await auditService.InsertAsync(new AuditEvent
            {
                Action = AuditAction.CertificateRenewed,
                PerformedBy = "CertificateRenewalService",
                TargetResource = portalCertName,
                Success = true,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    component = portalCertName,
                    old_thumbprint = oldThumbprint ?? "unknown",
                    new_thumbprint = newCert?.Thumbprint ?? "unknown",
                    new_expires_at = newCert?.NotAfter.ToString("O") ?? "unknown"
                })
            }, ct);

            _logger.LogInformation(
                "Portal TLS certificate renewed. New cert expires {ExpiresAt}. Kestrel will reload automatically.",
                newCert?.NotAfter);
        }
    }

    private async Task CheckToolServerCertExpiryAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LucidDbContext>();
        var auditService = scope.ServiceProvider.GetRequiredService<AuditChainService>();

        var serverCerts = await db.IssuedCertificates
            .Where(c => c.Usage == "server-tls" && c.IsActive && c.IssuedTo != "admin-portal")
            .ToListAsync(ct);

        foreach (var cert in serverCerts)
        {
            var daysRemaining = (cert.NotAfter - DateTime.UtcNow).TotalDays;

            if (daysRemaining > 30)
                continue;

            var auditEvent = new AuditEvent
            {
                Action = AuditAction.CertificateExpiryWarning,
                PerformedBy = "CertificateRenewalService",
                TargetResource = cert.IssuedTo,
                Success = true,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    cert_name = cert.Name,
                    thumbprint = cert.Thumbprint,
                    expires_at = cert.NotAfter.ToString("O"),
                    days_remaining = (int)daysRemaining
                })
            };

            if (daysRemaining < 7)
            {
                _logger.LogError(
                    "Certificate '{Name}' (issued to {IssuedTo}) expires in {Days} days! Manual renewal required.",
                    cert.Name, cert.IssuedTo, (int)daysRemaining);
            }
            else
            {
                _logger.LogWarning(
                    "Certificate '{Name}' (issued to {IssuedTo}) expires in {Days} days",
                    cert.Name, cert.IssuedTo, (int)daysRemaining);
            }

            await auditService.InsertAsync(auditEvent, ct);
        }
    }
}
