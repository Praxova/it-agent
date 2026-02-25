# Claude Code Prompt: ADR-014 Phase 0+1 — Internal PKI & Automated TLS

## Step 0: Git Branch Setup

```bash
cd /home/alton/Documents/lucid-it-agent
git checkout master
git checkout -b feature/adr014-internal-pki
```

All work for this prompt should be committed to this branch. Use meaningful commit messages prefixed with `ADR-014:` (e.g., `ADR-014: Add InternalPkiService with CA generation`). Commit after each logical unit of work.

## Context

Read these files first to understand the current architecture:
- `CLAUDE_CONTEXT.md` (project overview)
- `docs/adr/ADR-014-certificate-management-agent.md` (full ADR — focus on Part A: Internal PKI)
- `docs/prompts/PROMPT-ADR015-PHASE0-SECRETS-FOUNDATION.md` (secrets foundation — already implemented)
- `docs/prompts/PROMPT-ADR015-PHASE1-ENVELOPE-ENCRYPTION.md` (envelope encryption — already implemented)
- `admin/dotnet/src/LucidAdmin.Infrastructure/Services/SealManager.cs` (the seal/unseal system)
- `admin/dotnet/src/LucidAdmin.Infrastructure/Services/EncryptionService.cs` (AES-256-GCM, uses SealManager)
- `admin/dotnet/src/LucidAdmin.Core/Entities/SystemSecret.cs` (where the CA private key will be stored)
- `admin/dotnet/src/LucidAdmin.Infrastructure/Services/JwtKeyManager.cs` (reference pattern — CA key manager follows this same pattern)
- `admin/dotnet/src/LucidAdmin.Web/Program.cs` (startup flow — understand seal/unseal ordering)
- `docker-compose.yml` (current cert mount setup)
- `admin/dotnet/Dockerfile` (current ca-trust handling)

### Current State

**Secrets infrastructure (ADR-015 complete):**
- SealManager provides envelope encryption: passphrase → Argon2id → Master Key → encrypts KEK → KEK encrypts everything
- EncryptionService uses SealManager.GetEncryptionKey() for all AES-256-GCM operations
- SystemSecrets table stores encrypted secrets (JWT signing key already stored here)
- Auto-unseal via `PRAXOVA_UNSEAL_PASSPHRASE` env var at startup

**Current certificate situation (manual, fragile):**
- `docker/certs/cert.pem` and `key.pem` — manually generated via mkcert
- `docker/certs/ca-trust/` — manually collected CA certs (mkcert CA, DC LDAPS cert, toolserver cert)
- Dockerfile copies ca-trust files and runs `update-ca-certificates`
- docker-compose.yml mounts `./docker/certs:/app/certs:ro`
- Kestrel configured via env vars: `Kestrel__Certificates__Default__Path=/app/certs/cert.pem`

**Problem:** Adding a new component or rotating a cert requires manual file creation, Docker rebuilds, and restart. This is exactly what ADR-014 Part A solves.

---

## What This Prompt Implements

### Goal: Zero-Touch TLS for All Praxova Components

After this prompt, on first startup with a fresh database:
1. SealManager initializes (creates KEK, encrypted by master passphrase) — **already works**
2. **NEW:** InternalPkiService generates a root CA keypair, stores private key encrypted in SystemSecrets
3. **NEW:** InternalPkiService issues an HTTPS certificate for the admin portal
4. **NEW:** Cert + key PEM files written to the data volume, Kestrel picks them up
5. JwtKeyManager initializes (stores JWT key encrypted) — **already works**
6. Portal starts serving HTTPS with its own auto-generated certificate

On subsequent startups:
1. SealManager unseals (derives MK from passphrase, decrypts KEK) — **already works**
2. **NEW:** InternalPkiService loads existing CA from SystemSecrets, checks cert expiry
3. **NEW:** If portal cert expires within 30 days, auto-renew it
4. JwtKeyManager loads existing JWT key — **already works**
5. Portal serves HTTPS with existing or renewed certificate

Other containers (agent, tool-server) can:
- Fetch the CA trust bundle from `GET /api/pki/trust-bundle` (no auth required)
- **Future:** Request their own certs via authenticated API (not in this prompt — manual certs still work for them)

---

## Implementation Items

### 1. IssuedCertificate Entity

Create `LucidAdmin.Core/Entities/IssuedCertificate.cs`:

```csharp
/// <summary>
/// Tracks a certificate issued by the internal PKI.
/// The actual cert/key PEM is stored on disk (data volume);
/// this entity tracks metadata, lifecycle, and health.
/// </summary>
public class IssuedCertificate : BaseEntity
{
    /// <summary>Unique name for this cert (e.g., "admin-portal", "agent-helpdesk-01")</summary>
    public required string Name { get; set; }

    /// <summary>Certificate subject CN</summary>
    public required string SubjectCN { get; set; }

    /// <summary>Comma-separated Subject Alternative Names (DNS names, IPs)</summary>
    public string? SubjectAlternativeNames { get; set; }

    /// <summary>SHA-256 thumbprint of the certificate (hex, lowercase)</summary>
    public required string Thumbprint { get; set; }

    /// <summary>Serial number (hex)</summary>
    public required string SerialNumber { get; set; }

    /// <summary>When the certificate becomes valid</summary>
    public required DateTime NotBefore { get; set; }

    /// <summary>When the certificate expires</summary>
    public required DateTime NotAfter { get; set; }

    /// <summary>Certificate usage: "server-tls", "client-mtls", "ca-root"</summary>
    public required string Usage { get; set; }

    /// <summary>Which component this cert was issued to</summary>
    public string? IssuedTo { get; set; }

    /// <summary>Whether this is the currently active cert for its purpose (vs. a previous/rotated cert)</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>File path where the PEM cert is written (relative to data dir)</summary>
    public string? CertPath { get; set; }

    /// <summary>File path where the PEM private key is written (relative to data dir)</summary>
    public string? KeyPath { get; set; }

    /// <summary>When this cert was renewed/replaced (null if still active original)</summary>
    public DateTime? RenewedAt { get; set; }

    /// <summary>Thumbprint of the replacement cert (if renewed)</summary>
    public string? ReplacedByThumbprint { get; set; }
}
```

Add `DbSet<IssuedCertificate> IssuedCertificates` to `LucidDbContext` and create a fluent configuration with a unique index on `Name` + `IsActive` (only one active cert per name).

### 2. IInternalPkiService Interface

Create `LucidAdmin.Core/Interfaces/Services/IInternalPkiService.cs`:

```csharp
public interface IInternalPkiService
{
    /// <summary>Whether the internal CA has been initialized.</summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Initialize the internal CA. Generates RSA 4096 root CA keypair,
    /// stores private key encrypted in SystemSecrets.
    /// Only called once — subsequent startups use LoadAsync.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Load the existing CA from SystemSecrets. Called on subsequent startups.
    /// </summary>
    Task LoadAsync();

    /// <summary>
    /// Issue a TLS server certificate signed by the internal CA.
    /// </summary>
    /// <param name="name">Logical name (e.g., "admin-portal")</param>
    /// <param name="commonName">Subject CN</param>
    /// <param name="sanDnsNames">Subject Alternative Name DNS entries</param>
    /// <param name="sanIpAddresses">Subject Alternative Name IP entries</param>
    /// <param name="lifetimeDays">Certificate lifetime in days (default: 90)</param>
    /// <returns>Tuple of (certPem, keyPem) as strings</returns>
    Task<(string CertPem, string KeyPem)> IssueCertificateAsync(
        string name,
        string commonName,
        string[]? sanDnsNames = null,
        string[]? sanIpAddresses = null,
        int lifetimeDays = 90);

    /// <summary>
    /// Get the CA public certificate in PEM format (trust bundle).
    /// </summary>
    string GetCaCertificatePem();

    /// <summary>
    /// Check if a certificate needs renewal (expires within thresholdDays).
    /// </summary>
    Task<bool> NeedsRenewalAsync(string name, int thresholdDays = 30);

    /// <summary>
    /// Renew a certificate — issues a new one with the same parameters,
    /// marks the old one as inactive.
    /// </summary>
    Task<(string CertPem, string KeyPem)> RenewCertificateAsync(string name, int lifetimeDays = 90);
}
```

### 3. InternalPkiService Implementation

Create `LucidAdmin.Infrastructure/Services/InternalPkiService.cs`:

**CA Generation:**
- Use `RSA.Create(4096)` for the CA key
- Use `CertificateRequest` with `X509BasicConstraintsExtension(true, true, 0, true)` to mark it as a CA
- Self-sign with `CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(10))`
- Subject: `CN=Praxova Internal CA, O=Praxova`
- Add `X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true)`
- Export the private key via `rsa.ExportRSAPrivateKey()` → encrypt via `IEncryptionService` → store in `SystemSecrets` as `"internal-ca-private-key"`
- Export the CA cert PEM via `cert.ExportCertificatePem()` → store in `SystemSecrets` as `"internal-ca-certificate"` (not encrypted — the CA public cert is not secret, but storing it in SystemSecrets keeps it accessible; store it with Nonce as empty byte array and EncryptedValue as UTF8 bytes, or use the Metadata field to flag it as plaintext — implementer's choice on cleanest approach)

**Certificate Issuance:**
- Generate a new RSA 2048 key for the leaf cert (2048 is fine for component certs, faster than 4096)
- Use `CertificateRequest` with the specified CN and SANs
- Add `X509BasicConstraintsExtension(false, false, 0, false)` — not a CA
- Add `X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true)`
- Add `X509EnhancedKeyUsageExtension` with `Oid("1.3.6.1.5.5.7.3.1")` for server auth (TLS)
- Add SANs via `SubjectAlternativeNameBuilder` — include DNS names, IP addresses, and always include `localhost` and `127.0.0.1`
- Sign with the CA key using `.Create(caCert, ...)`
- Assign a random serial number
- Export cert PEM and key PEM
- Write PEM files to disk (data volume: `/app/data/certs/`)
- Track in `IssuedCertificate` table

**PEM Export helpers (for .NET 8):**
```csharp
// Certificate to PEM
string certPem = cert.ExportCertificatePem();

// RSA private key to PEM
string keyPem = rsa.ExportRSAPrivateKeyPem();
```

Note: These PEM export methods are available in .NET 8. If they're not available for some reason, use:
```csharp
string certPem = "-----BEGIN CERTIFICATE-----\n" +
    Convert.ToBase64String(cert.RawData, Base64FormattingOptions.InsertLineBreaks) +
    "\n-----END CERTIFICATE-----";
```

**Loading existing CA:**
- Retrieve `"internal-ca-private-key"` from SystemSecrets → decrypt via IEncryptionService
- Retrieve `"internal-ca-certificate"` from SystemSecrets
- Reconstruct the X509Certificate2 with the private key using `RSA.ImportRSAPrivateKey` and `cert.CopyWithPrivateKey(rsa)`

**Registration:** Singleton (holds CA cert in memory). Uses `IServiceProvider` to create scoped DbContext (same pattern as JwtKeyManager and SealManager).

**Thread safety:** The CA cert and key are loaded once at startup and only used for signing (read-only after init). Issue/renew operations should use a `SemaphoreSlim` to prevent concurrent issuance race conditions.

### 4. Startup Integration

Add to `Program.cs` startup, AFTER SealManager unseal and BEFORE JwtKeyManager init:

```csharp
// Initialize Internal PKI (after unseal, before JWT)
if (sealManager.IsUnsealed)
{
    var pkiService = app.Services.GetRequiredService<IInternalPkiService>();

    // Load or initialize CA
    if (!pkiService.IsInitialized)
    {
        // Check if CA exists in database
        // (IsInitialized may be false just because we haven't loaded yet)
        await pkiService.LoadAsync();
    }

    if (!pkiService.IsInitialized)
    {
        // First-time: generate CA and portal cert
        await pkiService.InitializeAsync();
        Log.Information("Internal PKI initialized — CA generated and stored encrypted");
    }

    // Ensure admin portal has a valid certificate
    var portalCertName = "admin-portal";
    var dataDir = builder.Configuration.GetValue<string>("DataDirectory") ?? "/app/data";
    var certDir = Path.Combine(dataDir, "certs");
    Directory.CreateDirectory(certDir);

    var certPath = Path.Combine(certDir, "portal-cert.pem");
    var keyPath = Path.Combine(certDir, "portal-key.pem");

    if (await pkiService.NeedsRenewalAsync(portalCertName))
    {
        var (certPem, keyPem) = await pkiService.RenewCertificateAsync(portalCertName);
        await File.WriteAllTextAsync(certPath, certPem);
        await File.WriteAllTextAsync(keyPath, keyPem);
        Log.Information("Admin portal TLS certificate renewed");
    }
    else if (!File.Exists(certPath))
    {
        // First time: issue portal cert
        // SANs should include common ways the portal is accessed
        var (certPem, keyPem) = await pkiService.IssueCertificateAsync(
            name: portalCertName,
            commonName: "praxova-admin-portal",
            sanDnsNames: new[] { "praxova-admin-portal", "admin-portal", "localhost" },
            sanIpAddresses: new[] { "127.0.0.1", "::1" },
            lifetimeDays: 90);
        await File.WriteAllTextAsync(certPath, certPem);
        await File.WriteAllTextAsync(keyPath, keyPem);
        Log.Information("Admin portal TLS certificate issued");
    }

    // Also write CA cert for trust bundle
    var caCertPath = Path.Combine(certDir, "ca.pem");
    await File.WriteAllTextAsync(caCertPath, pkiService.GetCaCertificatePem());
}
```

**Kestrel configuration:** Update the HTTPS cert path environment variables in docker-compose.yml to point to the auto-generated certs:
```yaml
Kestrel__Certificates__Default__Path: /app/data/certs/portal-cert.pem
Kestrel__Certificates__Default__KeyPath: /app/data/certs/portal-key.pem
```

**Important ordering consideration:** The portal cert is generated/checked BEFORE `app.Run()`, but AFTER the app is built and the database is migrated. Kestrel reads the cert files at startup. Since the files are written before `app.Run()`, Kestrel should pick them up. However, Kestrel's cert configuration may be evaluated at build time. If that's the case, you may need to use `KestrelServerOptions.ConfigureHttpsDefaults` with a callback that reads the files, or configure Kestrel programmatically rather than via env vars. Test this and handle it appropriately — the cert must be available when Kestrel binds to the HTTPS port.

### 5. PKI API Endpoints

Create `LucidAdmin.Web/Endpoints/PkiEndpoints.cs`:

```
GET  /api/pki/trust-bundle       — Returns the CA public certificate PEM (no auth required)
GET  /api/pki/certificates        — List all issued certificates (admin only)
GET  /api/pki/certificates/{name} — Get details for a specific cert (admin only)
POST /api/pki/certificates/{name}/renew — Force renewal of a certificate (admin only)
```

**Trust bundle endpoint is unauthenticated.** Other containers need to fetch the CA cert at startup before they can authenticate. This is just the CA public certificate — not secret. Same trust model as a web browser downloading a root CA cert.

The trust bundle response should be `text/plain` with PEM content (not JSON). This makes it easy for containers to `curl` it directly:
```bash
curl http://admin-portal:5000/api/pki/trust-bundle > /usr/local/share/ca-certificates/praxova-ca.crt
update-ca-certificates
```

### 6. Docker Compose Updates

**docker-compose.yml changes:**

```yaml
admin-portal:
    environment:
      # Point to auto-generated certs in the data volume (not manual certs)
      Kestrel__Certificates__Default__Path: /app/data/certs/portal-cert.pem
      Kestrel__Certificates__Default__KeyPath: /app/data/certs/portal-key.pem
    volumes:
      - admin-data:/app/data
      - admin-logs:/app/logs
      # Remove: ./docker/certs:/app/certs:ro (no longer needed for portal TLS)
```

**Keep the ca-trust Dockerfile mechanism** for now — external certs (DC LDAPS, etc.) still use that path. The auto-generated internal CA cert is separate.

**Agent container trust:** Add a startup script or entrypoint addition to the agent container that fetches the trust bundle from the portal:
```yaml
agent-helpdesk-01:
    # The agent should trust the portal's CA
    # Add to agent entrypoint/startup:
    # curl -sf http://admin-portal:5000/api/pki/trust-bundle > /usr/local/share/ca-certificates/praxova-ca.crt
    # update-ca-certificates
```

This can be a simple addition to the agent's `Dockerfile` entrypoint wrapper script, or documented as a TODO for the next prompt. Don't block this prompt on agent container changes — the portal generating its own certs is the critical path.

### 7. EF Core Migration

Create a migration named `InternalPki` that adds:
- `IssuedCertificates` table with all columns from the entity
- Unique index on `(Name, IsActive)` filtered to `IsActive = true` (only one active cert per name)
  - Note: SQLite doesn't support filtered indexes. Use a regular unique index on `(Name, IsActive)` or just a regular index + application-level enforcement. Choose the pragmatic path.

---

## Implementation Order

1. **IssuedCertificate entity + DbSet + migration** — Schema first
2. **IInternalPkiService interface** — No dependencies
3. **InternalPkiService implementation** — CA generation, cert issuance, PEM export, SystemSecrets storage
4. **DI registration** — Singleton, same as JwtKeyManager
5. **Program.cs startup integration** — After unseal, before JWT init
6. **PKI endpoints** — Trust bundle (no auth), cert management (admin only)
7. **Docker compose updates** — Point to auto-generated cert paths
8. **Kestrel configuration** — Ensure programmatic cert loading works with auto-generated files
9. **Build and verify**

## Files to Create
- `admin/dotnet/src/LucidAdmin.Core/Entities/IssuedCertificate.cs`
- `admin/dotnet/src/LucidAdmin.Core/Interfaces/Services/IInternalPkiService.cs`
- `admin/dotnet/src/LucidAdmin.Infrastructure/Services/InternalPkiService.cs`
- `admin/dotnet/src/LucidAdmin.Web/Endpoints/PkiEndpoints.cs`
- New EF Core migration: `InternalPki`

## Files to Modify
- `admin/dotnet/src/LucidAdmin.Infrastructure/Data/LucidDbContext.cs` — Add IssuedCertificates DbSet
- `admin/dotnet/src/LucidAdmin.Infrastructure/DependencyInjection.cs` — Register IInternalPkiService
- `admin/dotnet/src/LucidAdmin.Web/Program.cs` — Add PKI initialization to startup flow, map PKI endpoints
- `docker-compose.yml` — Update cert paths to auto-generated location

## Files NOT to Modify
- **SealManager** — Complete, provides the encryption foundation
- **EncryptionService** — Complete, used by InternalPkiService for CA key storage
- **JwtKeyManager** — Complete, but the pattern is a reference for InternalPkiService
- **DatabaseCredentialProvider** — Unrelated to PKI
- **Dockerfile** — Keep existing ca-trust mechanism for external certs; internal CA is handled at runtime

## What This Prompt Does NOT Do (Future Prompts)
- **mTLS for agent → portal communication** — Future prompt
- **Agent container automatic trust bootstrap** — Document the curl command, implement in future prompt
- **Enterprise CA integration** (AD CS, Vault PKI) — ADR-014 Part B, post-launch
- **Certificate Management Agent** — ADR-014 Part C, product expansion
- **Setup wizard UI** (Automatic/Enterprise/BYO) — Post-launch UX
- **CRL/OCSP** — Not needed for internal PKI with short-lived certs

## Testing

After implementation:
1. Delete the database: `docker compose down && docker volume rm praxova-admin-data`
2. Ensure `PRAXOVA_UNSEAL_PASSPHRASE` is set (default in docker-compose.yml)
3. `docker compose up -d --build admin-portal`
4. Watch logs: `docker compose logs -f admin-portal`
5. Verify logs show:
   - "Secrets store initialized and unsealed"
   - "Internal PKI initialized — CA generated"
   - "Admin portal TLS certificate issued"
   - "Generated new JWT signing key"
6. Verify HTTPS works: `curl -v https://localhost:5001` — should show a cert issued by "Praxova Internal CA"
   - It will be untrusted by your browser/curl (expected — it's a self-signed CA)
   - Use `curl -k https://localhost:5001` or import the CA cert
7. Fetch trust bundle: `curl http://localhost:5000/api/pki/trust-bundle` — should return PEM
8. Verify trust bundle works: `curl --cacert <(curl -s http://localhost:5000/api/pki/trust-bundle) https://localhost:5001/api/health/`
9. Restart portal: `docker compose restart admin-portal` — verify it loads existing CA and cert (not regenerating)
10. Check cert list: `curl http://localhost:5000/api/pki/certificates` (with auth token)

## Notes for Claude Code

- Use `System.Security.Cryptography` and `System.Security.Cryptography.X509Certificates` — these are built into .NET 8, no additional NuGet packages needed.
- `CertificateRequest` is the modern .NET API for creating X509 certificates. Don't use BouncyCastle or other third-party crypto libraries.
- The CA private key stored in SystemSecrets should be the raw RSA private key bytes (`rsa.ExportRSAPrivateKey()`), encrypted by `IEncryptionService`. On load, use `RSA.Create()` then `rsa.ImportRSAPrivateKey(decryptedBytes, out _)`.
- PEM export: .NET 8 has `ExportCertificatePem()` on X509Certificate2 and `ExportRSAPrivateKeyPem()` on RSA. Use these.
- For `CertificateRequest.Create()` to sign with the CA, you need the CA as an X509Certificate2 with a private key. The pattern is: load RSA key, load cert, `cert.CopyWithPrivateKey(rsa)` to get a cert+key combo, then use that as the issuer.
- The `SubjectAlternativeNameBuilder` is used to add SANs. Call `builder.AddDnsName()` and `builder.AddIpAddress()` then `certRequest.CertificateExtensions.Add(builder.Build())`.
- Serial numbers: use `RandomNumberGenerator.GetBytes(16)` and pass to `CertificateRequest.Create()` or set manually.
- File permissions on private key PEM files: on Linux, set `chmod 600` equivalent. In .NET, use `File.SetUnixFileMode()` if available, or just ensure the container runs as root (current setup).
