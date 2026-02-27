using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LucidAdmin.Infrastructure.Services;

/// <summary>
/// Manages the internal PKI — generates a root CA, issues and renews TLS certificates.
/// CA private key is stored encrypted in SystemSecrets (via EncryptionService/SealManager).
/// Registered as singleton — holds CA cert+key in memory for signing.
/// Uses IServiceProvider to create scoped DbContext for database access.
/// </summary>
public class InternalPkiService : IInternalPkiService
{
    private const string CaPrivateKeySecretName = "internal-ca-private-key";
    private const string CaCertificateSecretName = "internal-ca-certificate";

    private readonly IServiceProvider _serviceProvider;
    private readonly IEncryptionService _encryptionService;
    private readonly ILogger<InternalPkiService> _logger;
    private readonly SemaphoreSlim _issueLock = new(1, 1);

    private X509Certificate2? _caCert;
    private RSA? _caKey;
    private string? _caCertPem;

    public InternalPkiService(
        IServiceProvider serviceProvider,
        IEncryptionService encryptionService,
        ILogger<InternalPkiService> logger)
    {
        _serviceProvider = serviceProvider;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public bool IsInitialized => _caCert != null && _caKey != null;

    public async Task InitializeAsync()
    {
        if (IsInitialized)
            return;

        _logger.LogInformation("Generating internal CA keypair (RSA 4096)...");

        // 1. Generate RSA 4096 CA key
        var caKey = RSA.Create(4096);

        // 2. Create self-signed CA certificate
        var subject = new X500DistinguishedName("CN=Praxova Internal CA, O=Praxova");
        var request = new CertificateRequest(subject, caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // CA extensions
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: true,
                hasPathLengthConstraint: true,
                pathLengthConstraint: 0,
                critical: true));

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                critical: true));

        // Subject Key Identifier
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var caCert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddYears(10));

        // 3. Store CA private key encrypted in SystemSecrets
        var caKeyBytes = caKey.ExportRSAPrivateKey();
        var (encryptedKey, nonce) = _encryptionService.Encrypt(caKeyBytes);
        Array.Clear(caKeyBytes); // Zero plaintext key bytes

        // 4. Store CA public certificate (not secret, but kept in SystemSecrets for convenience)
        var caCertPem = caCert.ExportCertificatePem();
        var caCertBytes = Encoding.UTF8.GetBytes(caCertPem);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LucidDbContext>();

        db.SystemSecrets.Add(new SystemSecret
        {
            Name = CaPrivateKeySecretName,
            EncryptedValue = encryptedKey,
            Nonce = nonce,
            Purpose = "RSA 4096 private key for Praxova Internal CA"
        });

        // Store CA cert as plaintext — Nonce is empty, Metadata flags it as plaintext
        db.SystemSecrets.Add(new SystemSecret
        {
            Name = CaCertificateSecretName,
            EncryptedValue = caCertBytes,
            Nonce = Array.Empty<byte>(),
            Purpose = "X.509 PEM certificate for Praxova Internal CA (public, not encrypted)",
            Metadata = "plaintext"
        });

        // Track the CA cert in IssuedCertificates
        var thumbprint = Convert.ToHexString(caCert.GetCertHash(HashAlgorithmName.SHA256)).ToLowerInvariant();
        db.IssuedCertificates.Add(new IssuedCertificate
        {
            Name = "internal-ca",
            SubjectCN = "Praxova Internal CA",
            Thumbprint = thumbprint,
            SerialNumber = caCert.SerialNumber.ToLowerInvariant(),
            NotBefore = caCert.NotBefore.ToUniversalTime(),
            NotAfter = caCert.NotAfter.ToUniversalTime(),
            Usage = "ca-root",
            IssuedTo = "self",
            IsActive = true
        });

        await db.SaveChangesAsync();

        // Hold in memory
        _caCert = caCert;
        _caKey = caKey;
        _caCertPem = caCertPem;

        _logger.LogInformation(
            "Internal CA generated — Subject: {Subject}, Expires: {Expires}, Thumbprint: {Thumbprint}",
            caCert.Subject, caCert.NotAfter, thumbprint);
    }

    public async Task LoadAsync()
    {
        if (IsInitialized)
            return;

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LucidDbContext>();

        // Load CA private key
        var keyRecord = await db.SystemSecrets.FirstOrDefaultAsync(s => s.Name == CaPrivateKeySecretName);
        if (keyRecord == null)
        {
            _logger.LogDebug("No internal CA found in database — initialization required");
            return;
        }

        // Load CA certificate
        var certRecord = await db.SystemSecrets.FirstOrDefaultAsync(s => s.Name == CaCertificateSecretName);
        if (certRecord == null)
        {
            _logger.LogError("CA private key exists but CA certificate is missing — database may be corrupted");
            return;
        }

        try
        {
            // Decrypt CA private key
            var caKeyBytes = _encryptionService.Decrypt(keyRecord.EncryptedValue, keyRecord.Nonce);

            // Load CA certificate PEM
            string caCertPem;
            if (certRecord.Metadata == "plaintext")
            {
                caCertPem = Encoding.UTF8.GetString(certRecord.EncryptedValue);
            }
            else
            {
                var certBytes = _encryptionService.Decrypt(certRecord.EncryptedValue, certRecord.Nonce);
                caCertPem = Encoding.UTF8.GetString(certBytes);
            }

            // Reconstruct X509Certificate2 with private key
            var caCert = new X509Certificate2(
                Convert.FromBase64String(ExtractBase64FromPem(caCertPem)));

            var caKey = RSA.Create();
            caKey.ImportRSAPrivateKey(caKeyBytes, out _);
            Array.Clear(caKeyBytes);

            _caCert = caCert.CopyWithPrivateKey(caKey);
            _caKey = caKey;
            _caCertPem = caCertPem;

            _logger.LogInformation(
                "Loaded existing internal CA — Subject: {Subject}, Expires: {Expires}",
                _caCert.Subject, _caCert.NotAfter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load internal CA from database");
        }
    }

    public async Task<(string CertPem, string KeyPem)> IssueCertificateAsync(
        string name,
        string commonName,
        string[]? sanDnsNames = null,
        string[]? sanIpAddresses = null,
        int lifetimeDays = 90)
    {
        if (!IsInitialized)
            throw new InvalidOperationException("Internal CA is not initialized. Call InitializeAsync or LoadAsync first.");

        await _issueLock.WaitAsync();
        try
        {
            return await IssueCertificateInternalAsync(name, commonName, sanDnsNames, sanIpAddresses, lifetimeDays);
        }
        finally
        {
            _issueLock.Release();
        }
    }

    public string GetCaCertificatePem()
    {
        return _caCertPem ?? throw new InvalidOperationException(
            "Internal CA is not initialized. Call InitializeAsync or LoadAsync first.");
    }

    public async Task<bool> NeedsRenewalAsync(string name, int thresholdDays = 30)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LucidDbContext>();

        var activeCert = await db.IssuedCertificates
            .FirstOrDefaultAsync(c => c.Name == name && c.IsActive);

        if (activeCert == null)
            return true; // No cert exists — needs issuance

        return activeCert.NotAfter <= DateTime.UtcNow.AddDays(thresholdDays);
    }

    public async Task<(string CertPem, string KeyPem)> RenewCertificateAsync(string name, int lifetimeDays = 90)
    {
        if (!IsInitialized)
            throw new InvalidOperationException("Internal CA is not initialized.");

        await _issueLock.WaitAsync();
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LucidDbContext>();

            // Find existing active cert to get its parameters
            var existing = await db.IssuedCertificates
                .FirstOrDefaultAsync(c => c.Name == name && c.IsActive);

            if (existing == null)
                throw new InvalidOperationException($"No active certificate found with name '{name}' to renew.");

            // Parse SANs from existing cert
            string[]? dnsNames = null;
            string[]? ipAddresses = null;
            if (!string.IsNullOrEmpty(existing.SubjectAlternativeNames))
            {
                var sans = existing.SubjectAlternativeNames.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var dnsList = new List<string>();
                var ipList = new List<string>();
                foreach (var san in sans)
                {
                    if (san.StartsWith("DNS:"))
                        dnsList.Add(san[4..]);
                    else if (san.StartsWith("IP:"))
                        ipList.Add(san[3..]);
                }
                dnsNames = dnsList.Count > 0 ? dnsList.ToArray() : null;
                ipAddresses = ipList.Count > 0 ? ipList.ToArray() : null;
            }

            // Issue new cert with same parameters
            var (certPem, keyPem) = await IssueCertificateInternalAsync(
                name, existing.SubjectCN, dnsNames, ipAddresses, lifetimeDays);

            // Mark old cert as inactive
            var newCert = await db.IssuedCertificates
                .FirstOrDefaultAsync(c => c.Name == name && c.IsActive && c.Id != existing.Id);

            existing.IsActive = false;
            existing.RenewedAt = DateTime.UtcNow;
            existing.ReplacedByThumbprint = newCert?.Thumbprint;

            await db.SaveChangesAsync();

            _logger.LogInformation("Certificate '{Name}' renewed — old thumbprint: {Old}, new thumbprint: {New}",
                name, existing.Thumbprint, newCert?.Thumbprint);

            return (certPem, keyPem);
        }
        finally
        {
            _issueLock.Release();
        }
    }

    private async Task<(string CertPem, string KeyPem)> IssueCertificateInternalAsync(
        string name,
        string commonName,
        string[]? sanDnsNames,
        string[]? sanIpAddresses,
        int lifetimeDays)
    {
        _logger.LogInformation("Issuing certificate '{Name}' (CN={CN}, lifetime={Days}d)",
            name, commonName, lifetimeDays);

        // Generate leaf key (RSA 2048 — fast, sufficient for component certs)
        using var leafKey = RSA.Create(2048);

        var subject = new X500DistinguishedName($"CN={commonName}");
        var request = new CertificateRequest(subject, leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // Not a CA
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));

        // Key usage: digital signature + key encipherment (TLS)
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));

        // Extended key usage: server authentication (TLS)
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new("1.3.6.1.5.5.7.3.1") }, // id-kp-serverAuth
                false));

        // Subject Alternative Names
        var sanBuilder = new SubjectAlternativeNameBuilder();

        // Always include localhost
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(IPAddress.Loopback);       // 127.0.0.1
        sanBuilder.AddIpAddress(IPAddress.IPv6Loopback);    // ::1

        if (sanDnsNames != null)
        {
            foreach (var dns in sanDnsNames)
            {
                if (!string.Equals(dns, "localhost", StringComparison.OrdinalIgnoreCase))
                    sanBuilder.AddDnsName(dns);
            }
        }

        if (sanIpAddresses != null)
        {
            foreach (var ip in sanIpAddresses)
            {
                if (IPAddress.TryParse(ip, out var addr) &&
                    !IPAddress.Loopback.Equals(addr) &&
                    !IPAddress.IPv6Loopback.Equals(addr))
                {
                    sanBuilder.AddIpAddress(addr);
                }
            }
        }

        request.CertificateExtensions.Add(sanBuilder.Build());

        // Authority Key Identifier (links to CA)
        request.CertificateExtensions.Add(
            X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
                _caCert!, includeKeyIdentifier: true, includeIssuerAndSerial: false));

        // Random serial number
        var serialBytes = RandomNumberGenerator.GetBytes(16);
        // Ensure positive (set high bit of first byte to 0)
        serialBytes[0] &= 0x7F;

        var notBefore = DateTimeOffset.UtcNow;
        var notAfter = DateTimeOffset.UtcNow.AddDays(lifetimeDays);

        // Sign with CA
        using var leafCert = request.Create(
            _caCert!,
            notBefore,
            notAfter,
            serialBytes);

        // Export PEMs
        var certPem = leafCert.ExportCertificatePem();
        var keyPem = leafKey.ExportRSAPrivateKeyPem();

        // Build SAN string for database tracking
        var sanParts = new List<string>();
        sanParts.Add("DNS:localhost");
        if (sanDnsNames != null)
        {
            foreach (var dns in sanDnsNames)
            {
                if (!string.Equals(dns, "localhost", StringComparison.OrdinalIgnoreCase))
                    sanParts.Add($"DNS:{dns}");
            }
        }
        sanParts.Add("IP:127.0.0.1");
        sanParts.Add("IP:::1");
        if (sanIpAddresses != null)
        {
            foreach (var ip in sanIpAddresses)
            {
                if (ip != "127.0.0.1" && ip != "::1")
                    sanParts.Add($"IP:{ip}");
            }
        }

        // Track in database
        var thumbprint = Convert.ToHexString(leafCert.GetCertHash(HashAlgorithmName.SHA256)).ToLowerInvariant();
        var serialHex = Convert.ToHexString(serialBytes).ToLowerInvariant();

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LucidDbContext>();

        db.IssuedCertificates.Add(new IssuedCertificate
        {
            Name = name,
            SubjectCN = commonName,
            SubjectAlternativeNames = string.Join(", ", sanParts),
            Thumbprint = thumbprint,
            SerialNumber = serialHex,
            NotBefore = notBefore.UtcDateTime,
            NotAfter = notAfter.UtcDateTime,
            Usage = "server-tls",
            IssuedTo = name,
            IsActive = true,
            CertPath = $"certs/{name}-cert.pem",
            KeyPath = $"certs/{name}-key.pem"
        });

        await db.SaveChangesAsync();

        _logger.LogInformation(
            "Certificate issued — Name: {Name}, CN: {CN}, Expires: {Expires}, Thumbprint: {Thumbprint}",
            name, commonName, notAfter, thumbprint);

        return (certPem, keyPem);
    }

    public async Task<(string CertPem, string KeyPem, DateTime ExpiresAt)> GetOrIssueAgentClientCertAsync(
        string agentName)
    {
        if (!IsInitialized)
            throw new InvalidOperationException("Internal CA is not initialized.");

        var certName = $"agent-client-{agentName}";
        var keySecretName = $"agent-client-key-{agentName}";
        var certSecretName = $"agent-client-cert-{agentName}";
        const int renewalThresholdDays = 30;

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LucidDbContext>();

        // Check for existing active cert
        var existing = await db.IssuedCertificates
            .FirstOrDefaultAsync(c => c.Name == certName && c.IsActive);

        if (existing != null && existing.NotAfter > DateTime.UtcNow.AddDays(renewalThresholdDays))
        {
            // Valid cert exists — load stored private key and cert PEM from SystemSecrets
            var keySecret = await db.SystemSecrets
                .FirstOrDefaultAsync(s => s.Name == keySecretName);

            if (keySecret != null)
            {
                var keyBytes = _encryptionService.Decrypt(keySecret.EncryptedValue, keySecret.Nonce);
                var keyPem = Encoding.UTF8.GetString(keyBytes);
                Array.Clear(keyBytes);

                var certPem = await LoadCertPemFromSecrets(db, certSecretName);
                if (certPem != null)
                {
                    _logger.LogDebug(
                        "Returning existing agent client cert '{Name}', expires {Expiry}",
                        certName, existing.NotAfter);
                    return (certPem, keyPem, existing.NotAfter);
                }
            }

            // Key or cert PEM is missing — recover by reissuing
            _logger.LogWarning(
                "Agent client cert exists in IssuedCertificates but key/cert PEM missing in SystemSecrets. Reissuing.");
        }

        // Issue new cert (first call or renewal)
        await _issueLock.WaitAsync();
        try
        {
            // Re-check under lock to avoid races
            existing = await db.IssuedCertificates
                .FirstOrDefaultAsync(c => c.Name == certName && c.IsActive);

            if (existing != null && existing.NotAfter > DateTime.UtcNow.AddDays(renewalThresholdDays))
            {
                // Another thread issued while we waited — try loading again
                var keySecret = await db.SystemSecrets
                    .FirstOrDefaultAsync(s => s.Name == keySecretName);
                if (keySecret != null)
                {
                    var keyBytes = _encryptionService.Decrypt(keySecret.EncryptedValue, keySecret.Nonce);
                    var keyPem = Encoding.UTF8.GetString(keyBytes);
                    Array.Clear(keyBytes);
                    var certPem = await LoadCertPemFromSecrets(db, certSecretName);
                    if (certPem != null)
                        return (certPem, keyPem, existing.NotAfter);
                }
            }

            var (newCertPem, newKeyPem) = await IssueClientCertificateInternalAsync(agentName, certName, db);

            // Mark old cert inactive if renewing
            if (existing != null)
            {
                existing.IsActive = false;
                existing.RenewedAt = DateTime.UtcNow;
            }

            // Store private key encrypted in SystemSecrets (upsert)
            var existingKeySecret = await db.SystemSecrets
                .FirstOrDefaultAsync(s => s.Name == keySecretName);

            var newKeyBytes = Encoding.UTF8.GetBytes(newKeyPem);
            var (encryptedKey, nonce) = _encryptionService.Encrypt(newKeyBytes);
            Array.Clear(newKeyBytes);

            if (existingKeySecret != null)
            {
                existingKeySecret.EncryptedValue = encryptedKey;
                existingKeySecret.Nonce = nonce;
            }
            else
            {
                db.SystemSecrets.Add(new SystemSecret
                {
                    Name = keySecretName,
                    EncryptedValue = encryptedKey,
                    Nonce = nonce,
                    Purpose = $"mTLS client private key for agent '{agentName}'"
                });
            }

            // Store cert PEM in SystemSecrets (plaintext — not sensitive)
            var existingCertSecret = await db.SystemSecrets
                .FirstOrDefaultAsync(s => s.Name == certSecretName);

            var certBytes = Encoding.UTF8.GetBytes(newCertPem);
            if (existingCertSecret != null)
            {
                existingCertSecret.EncryptedValue = certBytes;
                existingCertSecret.Nonce = Array.Empty<byte>();
                existingCertSecret.Metadata = "plaintext";
            }
            else
            {
                db.SystemSecrets.Add(new SystemSecret
                {
                    Name = certSecretName,
                    EncryptedValue = certBytes,
                    Nonce = Array.Empty<byte>(),
                    Purpose = $"mTLS client certificate PEM for agent '{agentName}' (public, not encrypted)",
                    Metadata = "plaintext"
                });
            }

            await db.SaveChangesAsync();

            // Get the NotAfter from the newly issued cert record
            var newRecord = await db.IssuedCertificates
                .FirstOrDefaultAsync(c => c.Name == certName && c.IsActive);

            return (newCertPem, newKeyPem, newRecord?.NotAfter ?? DateTime.UtcNow.AddDays(90));
        }
        finally
        {
            _issueLock.Release();
        }
    }

    /// <summary>
    /// Issue a client authentication certificate for an agent (EKU: clientAuth only).
    /// Does NOT call SaveChangesAsync — the caller is responsible for that.
    /// </summary>
    private async Task<(string CertPem, string KeyPem)> IssueClientCertificateInternalAsync(
        string agentName, string certName, LucidDbContext db)
    {
        const int lifetimeDays = 90;

        _logger.LogInformation("Issuing agent client certificate '{Name}' (CN={CN}, lifetime={Days}d)",
            certName, agentName, lifetimeDays);

        using var leafKey = RSA.Create(2048);

        var subject = new X500DistinguishedName($"CN={agentName}");
        var request = new CertificateRequest(subject, leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // Not a CA
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));

        // Key usage: digital signature + key encipherment
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));

        // Extended key usage: client authentication ONLY (not serverAuth)
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new("1.3.6.1.5.5.7.3.2") }, // id-kp-clientAuth
                false));

        // Authority Key Identifier (links to CA)
        request.CertificateExtensions.Add(
            X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
                _caCert!, includeKeyIdentifier: true, includeIssuerAndSerial: false));

        // Random serial number
        var serialBytes = RandomNumberGenerator.GetBytes(16);
        serialBytes[0] &= 0x7F; // Ensure positive

        var notBefore = DateTimeOffset.UtcNow;
        var notAfter = DateTimeOffset.UtcNow.AddDays(lifetimeDays);

        // Sign with CA
        using var leafCert = request.Create(
            _caCert!,
            notBefore,
            notAfter,
            serialBytes);

        // Export PEMs
        var certPem = leafCert.ExportCertificatePem();
        var keyPem = leafKey.ExportRSAPrivateKeyPem();

        // Track in database
        var thumbprint = Convert.ToHexString(leafCert.GetCertHash(HashAlgorithmName.SHA256)).ToLowerInvariant();
        var serialHex = Convert.ToHexString(serialBytes).ToLowerInvariant();

        db.IssuedCertificates.Add(new IssuedCertificate
        {
            Name = certName,
            SubjectCN = agentName,
            Thumbprint = thumbprint,
            SerialNumber = serialHex,
            NotBefore = notBefore.UtcDateTime,
            NotAfter = notAfter.UtcDateTime,
            Usage = "client-tls",
            IssuedTo = agentName,
            IsActive = true
        });

        _logger.LogInformation(
            "Agent client certificate issued — Name: {Name}, CN: {CN}, Expires: {Expires}, Thumbprint: {Thumbprint}",
            certName, agentName, notAfter, thumbprint);

        return (certPem, keyPem);
    }

    /// <summary>
    /// Load a certificate PEM from SystemSecrets (stored as plaintext).
    /// </summary>
    private static async Task<string?> LoadCertPemFromSecrets(LucidDbContext db, string secretName)
    {
        var certSecret = await db.SystemSecrets
            .FirstOrDefaultAsync(s => s.Name == secretName);

        if (certSecret == null)
            return null;

        // Cert PEM is stored as plaintext (Metadata = "plaintext")
        return Encoding.UTF8.GetString(certSecret.EncryptedValue);
    }

    /// <summary>
    /// Extracts the Base64 content from a PEM string (strips header/footer and whitespace).
    /// </summary>
    private static string ExtractBase64FromPem(string pem)
    {
        return pem
            .Replace("-----BEGIN CERTIFICATE-----", "")
            .Replace("-----END CERTIFICATE-----", "")
            .Replace("\r", "")
            .Replace("\n", "")
            .Trim();
    }
}
