using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace LucidAdmin.Web.Services;

public interface ITrustedCertificateStore
{
    /// <summary>
    /// Import a PEM certificate into the runtime trust store.
    /// Saves to data volume and updates OS CA certificates.
    /// </summary>
    Task<TrustImportResult> ImportCertificateAsync(string pem, string friendlyName);

    /// <summary>
    /// List all certificates imported via the portal (not OS built-in certs).
    /// </summary>
    Task<IReadOnlyList<TrustedCertEntry>> ListTrustedCertsAsync();

    /// <summary>
    /// Remove a previously imported certificate by thumbprint.
    /// </summary>
    Task<bool> RemoveCertificateAsync(string thumbprint);

    /// <summary>
    /// Restore all saved certificates into the OS trust store.
    /// Called at container startup.
    /// </summary>
    Task RestoreAllAsync();
}

public record TrustImportResult(bool Success, string? Error, string? Thumbprint);

public record TrustedCertEntry(
    string FriendlyName,
    string Subject,
    string Issuer,
    DateTime NotBefore,
    DateTime NotAfter,
    string Thumbprint,
    bool IsExpired,
    int DaysUntilExpiry,
    DateTime ImportedAt);

public class TrustedCertificateStore : ITrustedCertificateStore
{
    private const string SystemCertDir = "/usr/local/share/ca-certificates";
    private const string CertPrefix = "praxova-";

    private readonly string _storeDir;
    private readonly ILogger<TrustedCertificateStore> _logger;

    public TrustedCertificateStore(IConfiguration configuration, ILogger<TrustedCertificateStore> logger)
    {
        var dataDir = configuration.GetValue<string>("DataDirectory") ?? "/app/data";
        _storeDir = Path.Combine(dataDir, "trusted-certs");
        _logger = logger;
    }

    public async Task<TrustImportResult> ImportCertificateAsync(string pem, string friendlyName)
    {
        // Validate PEM
        X509Certificate2 cert;
        try
        {
            cert = new X509Certificate2(
                Convert.FromBase64String(ExtractBase64FromPem(pem)));
        }
        catch (Exception ex)
        {
            return new TrustImportResult(false, $"Invalid certificate PEM: {ex.Message}", null);
        }

        var thumbprint = Convert.ToHexString(cert.GetCertHash(HashAlgorithmName.SHA256)).ToLowerInvariant();

        // Check for duplicate
        Directory.CreateDirectory(_storeDir);
        var pemPath = Path.Combine(_storeDir, $"{thumbprint}.pem");
        if (File.Exists(pemPath))
        {
            return new TrustImportResult(false, "This certificate is already imported.", thumbprint);
        }

        // Save PEM to data volume
        await File.WriteAllTextAsync(pemPath, pem);

        // Save metadata sidecar
        var metadata = new CertMetadata
        {
            FriendlyName = friendlyName,
            ImportedAt = DateTime.UtcNow,
            Subject = cert.Subject,
            Issuer = cert.Issuer
        };
        var metadataPath = Path.Combine(_storeDir, $"{thumbprint}.json");
        await File.WriteAllTextAsync(metadataPath,
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

        // Install into OS trust store
        var installResult = await InstallToOsTrustStoreAsync(thumbprint, pem);
        if (!installResult.Success)
        {
            _logger.LogWarning("Certificate saved but OS trust update failed: {Error}", installResult.Error);
            return new TrustImportResult(true,
                $"Certificate saved but OS trust store update failed: {installResult.Error}. It will be applied on next restart.",
                thumbprint);
        }

        _logger.LogInformation("Certificate imported and trusted: {FriendlyName} ({Thumbprint})",
            friendlyName, thumbprint[..16]);

        return new TrustImportResult(true, null, thumbprint);
    }

    public Task<IReadOnlyList<TrustedCertEntry>> ListTrustedCertsAsync()
    {
        var entries = new List<TrustedCertEntry>();

        if (!Directory.Exists(_storeDir))
            return Task.FromResult<IReadOnlyList<TrustedCertEntry>>(entries);

        foreach (var pemFile in Directory.GetFiles(_storeDir, "*.pem"))
        {
            var thumbprint = Path.GetFileNameWithoutExtension(pemFile);
            var metadataPath = Path.Combine(_storeDir, $"{thumbprint}.json");

            try
            {
                var pem = File.ReadAllText(pemFile);
                var cert = new X509Certificate2(
                    Convert.FromBase64String(ExtractBase64FromPem(pem)));

                var friendlyName = thumbprint[..16];
                var importedAt = File.GetCreationTimeUtc(pemFile);

                if (File.Exists(metadataPath))
                {
                    var metadata = JsonSerializer.Deserialize<CertMetadata>(File.ReadAllText(metadataPath));
                    if (metadata != null)
                    {
                        friendlyName = metadata.FriendlyName;
                        importedAt = metadata.ImportedAt;
                    }
                }

                var daysUntilExpiry = (int)(cert.NotAfter.ToUniversalTime() - DateTime.UtcNow).TotalDays;

                entries.Add(new TrustedCertEntry(
                    FriendlyName: friendlyName,
                    Subject: cert.Subject,
                    Issuer: cert.Issuer,
                    NotBefore: cert.NotBefore.ToUniversalTime(),
                    NotAfter: cert.NotAfter.ToUniversalTime(),
                    Thumbprint: thumbprint,
                    IsExpired: cert.NotAfter.ToUniversalTime() < DateTime.UtcNow,
                    DaysUntilExpiry: Math.Max(daysUntilExpiry, 0),
                    ImportedAt: importedAt));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load trusted cert from {Path}", pemFile);
            }
        }

        return Task.FromResult<IReadOnlyList<TrustedCertEntry>>(entries.OrderByDescending(e => e.ImportedAt).ToList());
    }

    public async Task<bool> RemoveCertificateAsync(string thumbprint)
    {
        var pemPath = Path.Combine(_storeDir, $"{thumbprint}.pem");
        var metadataPath = Path.Combine(_storeDir, $"{thumbprint}.json");
        var systemPath = Path.Combine(SystemCertDir, $"{CertPrefix}{thumbprint}.crt");

        if (!File.Exists(pemPath))
            return false;

        File.Delete(pemPath);
        if (File.Exists(metadataPath))
            File.Delete(metadataPath);

        if (File.Exists(systemPath))
        {
            File.Delete(systemPath);
            await RunUpdateCaCertificatesAsync();
        }

        _logger.LogInformation("Trusted certificate removed: {Thumbprint}", thumbprint[..16]);
        return true;
    }

    public async Task RestoreAllAsync()
    {
        if (!Directory.Exists(_storeDir))
        {
            _logger.LogDebug("No trusted-certs directory found — nothing to restore");
            return;
        }

        var pemFiles = Directory.GetFiles(_storeDir, "*.pem");
        if (pemFiles.Length == 0)
        {
            _logger.LogDebug("No trusted certificates to restore");
            return;
        }

        var copied = 0;
        foreach (var pemFile in pemFiles)
        {
            var thumbprint = Path.GetFileNameWithoutExtension(pemFile);
            var systemPath = Path.Combine(SystemCertDir, $"{CertPrefix}{thumbprint}.crt");

            try
            {
                File.Copy(pemFile, systemPath, overwrite: true);
                copied++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to copy trusted cert {File} to system store", pemFile);
            }
        }

        if (copied > 0)
        {
            await RunUpdateCaCertificatesAsync();
            _logger.LogInformation("Restored {Count} trusted certificates into OS trust store", copied);
        }
    }

    private async Task<(bool Success, string? Error)> InstallToOsTrustStoreAsync(string thumbprint, string pem)
    {
        try
        {
            var systemPath = Path.Combine(SystemCertDir, $"{CertPrefix}{thumbprint}.crt");
            Directory.CreateDirectory(SystemCertDir);
            await File.WriteAllTextAsync(systemPath, pem);

            return await RunUpdateCaCertificatesAsync();
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private async Task<(bool Success, string? Error)> RunUpdateCaCertificatesAsync()
    {
        try
        {
            // Try common paths for update-ca-certificates
            var binary = File.Exists("/usr/sbin/update-ca-certificates")
                ? "/usr/sbin/update-ca-certificates"
                : "update-ca-certificates";

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = binary,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });

            if (process == null)
                return (false, "Failed to start update-ca-certificates process");

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var stderr = await process.StandardError.ReadToEndAsync();
                _logger.LogWarning("update-ca-certificates exited with code {Code}: {Stderr}",
                    process.ExitCode, stderr);
                return (false, $"update-ca-certificates exited with code {process.ExitCode}: {stderr}");
            }

            var stdout = await process.StandardOutput.ReadToEndAsync();
            _logger.LogDebug("update-ca-certificates: {Output}", stdout.Trim());
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to run update-ca-certificates");
            return (false, ex.Message);
        }
    }

    private static string ExtractBase64FromPem(string pem)
    {
        return pem
            .Replace("-----BEGIN CERTIFICATE-----", "")
            .Replace("-----END CERTIFICATE-----", "")
            .Replace("\r", "")
            .Replace("\n", "")
            .Trim();
    }

    private class CertMetadata
    {
        public string FriendlyName { get; set; } = "";
        public DateTime ImportedAt { get; set; }
        public string Subject { get; set; } = "";
        public string Issuer { get; set; } = "";
    }
}
