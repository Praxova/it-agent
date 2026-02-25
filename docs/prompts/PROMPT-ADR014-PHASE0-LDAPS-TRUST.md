# Claude Code Prompt: ADR-014 Phase 0 — LDAPS Trust & Certificate Diagnostics

## Step 0: Git Branch Setup

```bash
cd /home/alton/Documents/lucid-it-agent
git checkout master
git checkout -b feature/adr014-ldaps-trust
```

Commit after each logical unit of work with messages prefixed `ADR-014:`.

## Context

Read these files first:
- `CLAUDE_CONTEXT.md` (project overview)
- `docs/adr/ADR-014-certificate-management-agent.md` (Section 3 → "External Certificate Trust (e.g., LDAPS)")
- `admin/dotnet/src/LucidAdmin.Web/Services/AdSettingsService.cs` (current LDAP connection logic)
- `admin/dotnet/src/LucidAdmin.Web/Components/Pages/Settings/ActiveDirectory.razor` (AD settings UI)
- `admin/dotnet/src/LucidAdmin.Web/Endpoints/SettingsEndpoints.cs` (AD settings API)
- `admin/dotnet/src/LucidAdmin.Web/Models/ActiveDirectoryOptions.cs` (AD config model)
- `admin/dotnet/Dockerfile` (current ca-trust handling at build time)
- `docker-compose.yml` (current cert volume mounts)

### Current Problem

Connecting to Active Directory over LDAPS (port 636) requires the DC's TLS certificate to be trusted by the container. Currently this is a painful manual process:

1. Admin must manually export the DC cert (`openssl s_client -connect dc:636`)
2. Copy the PEM file to `docker/certs/ca-trust/`
3. Rebuild the Docker image (`docker compose up -d --build`)
4. Restart the container

This is exactly the kind of operational friction ADR-014 Phase 0 is meant to eliminate.

### What This Prompt Implements

After this prompt, the AD Settings page will:
1. **Diagnose LDAPS issues** — When Test Connection runs, probe port 636 and retrieve the TLS cert
2. **Show the cert** — Display issuer, subject, expiry, thumbprint in the UI
3. **One-click trust** — "Trust This Certificate" button saves the cert and updates the OS trust store at runtime
4. **Persist across restarts** — Trusted certs saved to the data volume, restored on container startup
5. **Security warnings** — Banner when using plain LDAP suggesting upgrade to LDAPS

---

## Implementation Items

### 1. TLS Certificate Probe Service

Create `LucidAdmin.Web/Services/TlsCertificateProbeService.cs`:

```csharp
public interface ITlsCertificateProbeService
{
    /// <summary>
    /// Connect to a host:port via TLS and retrieve the server's certificate chain.
    /// Does NOT validate the certificate — we want to see it even if untrusted.
    /// </summary>
    Task<TlsProbeResult> ProbeCertificateAsync(string host, int port, int timeoutMs = 5000);
}

public record TlsProbeResult(
    bool Connected,
    string? Error,
    TlsCertificateInfo? ServerCertificate,
    List<TlsCertificateInfo>? ChainCertificates);

public record TlsCertificateInfo(
    string Subject,
    string Issuer,
    DateTime NotBefore,
    DateTime NotAfter,
    string Thumbprint,          // SHA-256, hex lowercase
    string SerialNumber,
    bool IsExpired,
    bool IsSelfSigned,
    int DaysUntilExpiry,
    string Pem                  // Full PEM-encoded certificate
);
```

**Implementation notes:**
- Use `TcpClient` + `SslStream` with a custom `RemoteCertificateValidationCallback` that always returns `true` (we want to capture the cert even when untrusted)
- Extract cert info from the `X509Certificate2` provided by the callback
- Convert to PEM using `cert.ExportCertificatePem()`
- Capture the full chain from `X509Chain` if available
- Handle connection refused, timeout, and TLS handshake failure with descriptive error messages

```csharp
// Pattern for capturing the cert even when untrusted:
X509Certificate2? serverCert = null;
var sslStream = new SslStream(tcpClient.GetStream(), false,
    (sender, certificate, chain, sslPolicyErrors) =>
    {
        if (certificate != null)
            serverCert = new X509Certificate2(certificate);
        // Capture chain certs too
        return true; // Accept any cert — we're probing, not validating
    });
await sslStream.AuthenticateAsClientAsync(host);
```

Register as `Scoped` in DI.

### 2. Trusted Certificate Store Service

Create `LucidAdmin.Web/Services/TrustedCertificateStore.cs`:

```csharp
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
```

**Implementation:**

**Storage location:** `/app/data/trusted-certs/` (inside the named Docker volume — persists across container restarts and rebuilds)

**File naming:** `{thumbprint}.pem` (one file per trusted cert). Also store a metadata JSON sidecar: `{thumbprint}.json` containing the friendly name and import timestamp.

**OS trust update (Linux):**
```csharp
// 1. Copy PEM to the system CA trust directory
var systemCertPath = $"/usr/local/share/ca-certificates/praxova-{thumbprint}.crt";
await File.WriteAllTextAsync(systemCertPath, pem);

// 2. Run update-ca-certificates
var process = Process.Start(new ProcessStartInfo
{
    FileName = "/usr/sbin/update-ca-certificates",
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false
});
await process!.WaitForExitAsync();
```

**Validation before import:**
- Parse the PEM to verify it's a valid X509 certificate
- Check it's a CA certificate or an end-entity cert (both are useful for trust)
- Reject if already imported (same thumbprint)

**RestoreAllAsync():**
- Enumerate all `.pem` files in `/app/data/trusted-certs/`
- Copy each to `/usr/local/share/ca-certificates/praxova-{name}.crt`
- Run `update-ca-certificates` once after all files are copied
- Called from Program.cs at startup (see Item 5)

Register as `Singleton` (manages filesystem state).

### 3. Enhanced Test Connection — LDAPS Diagnostics

Modify `AdSettingsService.TestConnectionAsync()` to return richer diagnostics:

**New return type:**
```csharp
public record AdTestResult(
    bool Enabled,
    string Server,
    string Domain,
    bool Reachable,
    long LatencyMs,
    // New fields:
    LdapsStatus? Ldaps);

public record LdapsStatus(
    bool PortOpen,
    bool TlsHandshakeSuccess,
    bool CertTrusted,
    string? TlsError,
    TlsCertificateInfo? ServerCertificate);
```

**Behavior changes:**

When `TestConnectionAsync()` runs:

1. **Always test the configured connection first** (port 389 or 636, whatever is configured). This is the existing behavior — keep it.

2. **Then, if LDAPS is NOT currently configured (using port 389), additionally probe port 636** to check if LDAPS is available. This drives the "Upgrade to LDAPS" recommendation.

3. **If LDAPS IS configured (port 636), probe and capture the TLS certificate** for display and potential trust import.

The LDAPS probe uses the `ITlsCertificateProbeService` to connect and capture the cert without requiring it to be trusted first.

**Important:** The LDAP connection test (step 1) may fail when using LDAPS if the cert isn't trusted yet. That's expected — the diagnostics should say "TLS certificate not trusted" rather than a generic "cannot connect" error. Parse the exception message to provide a clear diagnosis:
- `LdapException` with "The LDAP server is unavailable" on port 636 → probably cert trust issue
- `AuthenticationException: The remote certificate is invalid` → cert not trusted, show the cert and offer trust import
- Connection refused on 636 → LDAPS not enabled on DC
- Timeout on 636 → firewall blocking

### 4. AD Settings UI Enhancements

Modify `ActiveDirectory.razor` to add:

**A. Security Warning Banner (top of page, before the status banner):**

When AD is enabled and `UseLdaps == false`:
```razor
<MudAlert Severity="Severity.Warning" Variant="Variant.Filled" Class="mb-4"
          Icon="@Icons.Material.Filled.Warning">
    <MudText>
        <strong>Insecure Connection:</strong> LDAP traffic (including passwords during authentication)
        is sent in plain text. Enable LDAPS to encrypt the connection.
    </MudText>
    @if (_ldapsAvailable)
    {
        <MudButton Variant="Variant.Text" Color="Color.Inherit" Size="Size.Small"
                   OnClick="EnableLdaps" Class="mt-1">
            Upgrade to LDAPS
        </MudButton>
    }
</MudAlert>
```

Set `_ldapsAvailable = true` if the test connection probe found port 636 open.

**B. LDAPS Diagnostics Card (new, below Connection Test):**

After Test Connection runs and LDAPS data is available:

```razor
@if (_testResult?.Ldaps != null)
{
    <MudItem xs="12">
        <MudCard Elevation="2">
            <MudCardHeader>
                <CardHeaderContent>
                    <MudText Typo="Typo.h6">LDAPS Certificate</MudText>
                </CardHeaderContent>
            </MudCardHeader>
            <MudCardContent>
                @if (_testResult.Ldaps.ServerCertificate != null)
                {
                    var cert = _testResult.Ldaps.ServerCertificate;
                    <MudSimpleTable Dense="true">
                        <tbody>
                            <tr><td><strong>Subject</strong></td><td>@cert.Subject</td></tr>
                            <tr><td><strong>Issuer</strong></td><td>@cert.Issuer</td></tr>
                            <tr>
                                <td><strong>Valid Until</strong></td>
                                <td>
                                    @cert.NotAfter.ToString("yyyy-MM-dd")
                                    @if (cert.IsExpired)
                                    {
                                        <MudChip T="string" Color="Color.Error" Size="Size.Small">Expired</MudChip>
                                    }
                                    else if (cert.DaysUntilExpiry < 30)
                                    {
                                        <MudChip T="string" Color="Color.Warning" Size="Size.Small">
                                            @cert.DaysUntilExpiry days remaining
                                        </MudChip>
                                    }
                                    else
                                    {
                                        <MudChip T="string" Color="Color.Success" Size="Size.Small">
                                            @cert.DaysUntilExpiry days remaining
                                        </MudChip>
                                    }
                                </td>
                            </tr>
                            <tr><td><strong>Thumbprint</strong></td><td><code>@cert.Thumbprint[..16]...</code></td></tr>
                            <tr>
                                <td><strong>Trusted</strong></td>
                                <td>
                                    @if (_testResult.Ldaps.CertTrusted)
                                    {
                                        <MudChip T="string" Color="Color.Success" Size="Size.Small"
                                                 Icon="@Icons.Material.Filled.Verified">Trusted</MudChip>
                                    }
                                    else
                                    {
                                        <MudChip T="string" Color="Color.Error" Size="Size.Small"
                                                 Icon="@Icons.Material.Filled.GppBad">Not Trusted</MudChip>
                                    }
                                </td>
                            </tr>
                        </tbody>
                    </MudSimpleTable>

                    @if (!_testResult.Ldaps.CertTrusted)
                    {
                        <MudAlert Severity="Severity.Info" Class="mt-3" Variant="Variant.Outlined">
                            This certificate is not in Praxova's trust store. LDAPS connections will fail
                            until the certificate (or its issuing CA) is trusted.
                        </MudAlert>
                        <MudButton Variant="Variant.Filled" Color="Color.Primary"
                                   StartIcon="@Icons.Material.Filled.VerifiedUser"
                                   OnClick="TrustDcCertificate" Class="mt-2"
                                   Disabled="_importing">
                            @if (_importing)
                            {
                                <MudProgressCircular Size="Size.Small" Indeterminate="true" Class="mr-2" />
                                <span>Importing...</span>
                            }
                            else
                            {
                                <span>Trust This Certificate</span>
                            }
                        </MudButton>
                    }
                }
                else if (_testResult.Ldaps.TlsError != null)
                {
                    <MudAlert Severity="Severity.Error" Variant="Variant.Outlined">
                        @_testResult.Ldaps.TlsError
                    </MudAlert>
                }
                else if (!_testResult.Ldaps.PortOpen)
                {
                    <MudAlert Severity="Severity.Warning" Variant="Variant.Outlined">
                        Port 636 is not reachable on @_testResult.Server. LDAPS may not be enabled
                        on the domain controller, or a firewall is blocking the port.
                    </MudAlert>
                }
            </MudCardContent>
        </MudCard>
    </MudItem>
}
```

**C. Trusted Certificates Management Card (new, bottom of page):**

Show all certificates imported via the portal:

```razor
<MudItem xs="12">
    <MudCard Elevation="2">
        <MudCardHeader>
            <CardHeaderContent>
                <MudText Typo="Typo.h6">Trusted Certificates</MudText>
            </CardHeaderContent>
            <CardHeaderActions>
                <MudIconButton Icon="@Icons.Material.Filled.Refresh"
                               Size="Size.Small" OnClick="LoadTrustedCerts" />
            </CardHeaderActions>
        </MudCardHeader>
        <MudCardContent>
            @if (_trustedCerts == null || _trustedCerts.Count == 0)
            {
                <MudText Color="Color.Secondary">
                    No certificates have been manually imported.
                    Built-in OS certificates and the Praxova internal CA are always trusted.
                </MudText>
            }
            else
            {
                <MudTable Items="_trustedCerts" Dense="true" Hover="true">
                    <HeaderContent>
                        <MudTh>Name</MudTh>
                        <MudTh>Subject</MudTh>
                        <MudTh>Expires</MudTh>
                        <MudTh>Imported</MudTh>
                        <MudTh></MudTh>
                    </HeaderContent>
                    <RowTemplate>
                        <MudTd>@context.FriendlyName</MudTd>
                        <MudTd><code>@context.Subject</code></MudTd>
                        <MudTd>
                            @context.NotAfter.ToString("yyyy-MM-dd")
                            @if (context.IsExpired)
                            {
                                <MudChip T="string" Color="Color.Error" Size="Size.Small">Expired</MudChip>
                            }
                        </MudTd>
                        <MudTd>@context.ImportedAt.ToString("yyyy-MM-dd")</MudTd>
                        <MudTd>
                            <MudIconButton Icon="@Icons.Material.Filled.Delete"
                                           Color="Color.Error" Size="Size.Small"
                                           OnClick="@(() => RemoveTrustedCert(context.Thumbprint))" />
                        </MudTd>
                    </RowTemplate>
                </MudTable>
            }
        </MudCardContent>
    </MudCard>
</MudItem>
```

**D. Code-behind additions:**

```csharp
// New fields
private bool _importing = false;
private bool _ldapsAvailable = false;
private List<TrustedCertEntry>? _trustedCerts;

protected override async Task OnInitializedAsync()
{
    // Existing: load service accounts
    var accounts = await ServiceAccountRepo.GetByProviderAsync("windows-ad");
    _adServiceAccounts = accounts.Where(a => a.IsEnabled).ToList();
    
    // New: load trusted certs
    await LoadTrustedCerts();
}

private async Task LoadTrustedCerts()
{
    var store = /* inject ITrustedCertificateStore */;
    _trustedCerts = (await store.ListTrustedCertsAsync()).ToList();
    StateHasChanged();
}

private async Task TrustDcCertificate()
{
    if (_testResult?.Ldaps?.ServerCertificate == null) return;
    _importing = true;
    StateHasChanged();

    try
    {
        var cert = _testResult.Ldaps.ServerCertificate;
        var store = /* inject ITrustedCertificateStore */;
        var result = await store.ImportCertificateAsync(
            cert.Pem,
            $"DC LDAPS - {_testResult.Server}");

        if (result.Success)
        {
            Snackbar.Add("Certificate imported and trusted. LDAPS connections should now work.", Severity.Success);
            await LoadTrustedCerts();
            // Re-run test to verify LDAPS now works
            await TestConnection();
        }
        else
        {
            Snackbar.Add($"Import failed: {result.Error}", Severity.Error);
        }
    }
    catch (Exception ex)
    {
        Snackbar.Add($"Import failed: {ex.Message}", Severity.Error);
    }
    finally
    {
        _importing = false;
        StateHasChanged();
    }
}

private async Task RemoveTrustedCert(string thumbprint)
{
    // Confirm dialog first
    var confirmed = await DialogService.ShowMessageBox(
        "Remove Trusted Certificate",
        "This will remove the certificate from the trust store. LDAPS connections using this certificate may stop working.",
        "Remove", cancelText: "Cancel");
    if (confirmed != true) return;

    var store = /* inject ITrustedCertificateStore */;
    await store.RemoveCertificateAsync(thumbprint);
    Snackbar.Add("Certificate removed from trust store.", Severity.Info);
    await LoadTrustedCerts();
}

private void EnableLdaps()
{
    // Switch to edit mode with LDAPS enabled
    EnterEditMode();
    _editModel!.UseLdaps = true;
    _editModel.LdapPort = 636;
}
```

### 5. Startup Trust Restoration

Add to `Program.cs`, early in the startup sequence (before any LDAP connections are attempted), AFTER database migration but BEFORE seal/unseal:

```csharp
// Restore trusted certificates from data volume into OS trust store
// This must happen early — before any TLS connections (LDAPS, tool servers, etc.)
var trustedCertStore = app.Services.GetRequiredService<ITrustedCertificateStore>();
await trustedCertStore.RestoreAllAsync();
```

This ensures that any certs imported via the UI in a previous session are trusted again after a container restart.

**Also add to Dockerfile** — ensure the `trusted-certs` directory exists:
```dockerfile
RUN mkdir -p /app/data /app/logs /app/data/trusted-certs /app/data/certs
```

### 6. Settings API Endpoint for Trust Operations

Add to `SettingsEndpoints.cs` (or create a separate `TrustEndpoints.cs`):

```
POST /api/settings/trusted-certs           — Import a PEM certificate (admin only)
GET  /api/settings/trusted-certs           — List imported certificates (admin only)
DELETE /api/settings/trusted-certs/{thumbprint} — Remove a trusted cert (admin only)
POST /api/settings/active-directory/test-ldaps  — Probe LDAPS and return cert info (admin only)
```

The Blazor UI should call these via the injected HttpClient. If the Blazor components prefer to inject services directly (as the current code does), that's also fine — the API endpoints provide a parallel path for automation/scripting.

### 7. SettingsEndpoints: Update Test Connection Endpoint

The current `TestConnectionAsync` in `AdSettingsService` should be updated to include the LDAPS probe. Since the Blazor page calls the service directly (not via HTTP), the service method change is what matters. The API endpoint in `SettingsEndpoints.cs` should also be updated if a `/api/settings/active-directory/test` endpoint exists.

---

## Implementation Order

1. **TlsCertificateProbeService** — Standalone, no dependencies
2. **TrustedCertificateStore** — Filesystem operations, standalone
3. **DI registration** for both services
4. **Program.cs startup trust restoration** — Call `RestoreAllAsync`
5. **AdSettingsService enhancements** — Update `TestConnectionAsync` with LDAPS probe
6. **API endpoints** — Trust management + LDAPS probe
7. **ActiveDirectory.razor UI** — Security banner, cert display, trust button, trusted certs table
8. **Build and test**

## Files to Create
- `admin/dotnet/src/LucidAdmin.Web/Services/TlsCertificateProbeService.cs`
- `admin/dotnet/src/LucidAdmin.Web/Services/TrustedCertificateStore.cs`

## Files to Modify
- `admin/dotnet/src/LucidAdmin.Web/Services/AdSettingsService.cs` — Enhanced test with LDAPS diagnostics
- `admin/dotnet/src/LucidAdmin.Web/Components/Pages/Settings/ActiveDirectory.razor` — UI additions
- `admin/dotnet/src/LucidAdmin.Web/Endpoints/SettingsEndpoints.cs` — Trust management endpoints
- `admin/dotnet/src/LucidAdmin.Web/Program.cs` — Startup trust restoration
- `admin/dotnet/Dockerfile` — Ensure data/trusted-certs directory exists

## Files NOT to Modify
- **SealManager / EncryptionService** — Not involved in external cert trust
- **InternalPkiService** — That's the internal CA (Prompt 3), separate from external cert trust
- **docker-compose.yml** — No changes needed; trusted-certs live in the existing admin-data volume

## What This Prompt Does NOT Do (Future)
- **Auto-detect all LDAP connections and push trust** — Manual per-connection for now
- **Certificate expiry monitoring/alerting** — ADR-014 Phase 3+ (Certificate Agent)
- **mTLS client certificates** — Future prompt
- **Enterprise CA integration** — ADR-014 Part B
- **Automatic LDAPS cert renewal** — Would need AD CS integration

---

## Testing

### Prerequisite
- Domain controller at 172.16.119.20 with LDAPS enabled (port 636)
- The DC's LDAPS cert is currently trusted via build-time mechanism in `docker/certs/ca-trust/dc-ldaps.crt`

### Test Sequence

**Test 1: Clean slate (no pre-trusted certs)**
1. Remove `docker/certs/ca-trust/dc-ldaps.crt` temporarily (or rebuild without it)
2. Start portal: `docker compose up -d --build admin-portal`
3. Log in, go to Settings → Active Directory
4. Configure AD with LDAPS (port 636), save
5. Click "Test Connection"
6. Verify: Connection fails, but LDAPS diagnostics card appears showing the DC's cert with "Not Trusted" badge
7. Click "Trust This Certificate"
8. Verify: Snackbar says "Certificate imported and trusted"
9. Verify: Test Connection re-runs automatically and now succeeds
10. Verify: "Trusted Certificates" table shows the imported cert

**Test 2: Restart persistence**
1. `docker compose restart admin-portal`
2. Wait for healthy
3. Test Connection — should still work (cert was restored from data volume)

**Test 3: Plain LDAP warning**
1. Switch to plain LDAP (port 389, disable LDAPS)
2. Save settings
3. Verify: Yellow warning banner appears about insecure connection
4. Verify: If port 636 was reachable, "Upgrade to LDAPS" button appears

**Test 4: Certificate removal**
1. In "Trusted Certificates" table, click delete on the DC cert
2. Confirm the dialog
3. Test Connection — LDAPS should fail again with "Not Trusted"

**Test 5: Expired cert handling (informational)**
- If the DC cert is near expiry, verify the UI shows the yellow/red expiry warning chip

## Notes for Claude Code

- `SslStream` with `RemoteCertificateValidationCallback` is the standard .NET way to probe TLS certs without requiring trust. The callback receives the cert even when validation fails.
- `X509Certificate2.ExportCertificatePem()` is available in .NET 8.
- `Process.Start("update-ca-certificates")` requires the binary to be in the container. The current Dockerfile installs it via `apt-get install ca-certificates` (part of the base aspnet image). Verify it's at `/usr/sbin/update-ca-certificates`.
- For the SHA-256 thumbprint, use `cert.GetCertHashString(HashAlgorithmName.SHA256)` or compute manually from `cert.RawData`.
- The default `X509Certificate2(byte[])` constructor on Linux may need the cert data in DER format. If receiving PEM, strip headers and base64-decode, or use `X509Certificate2.CreateFromPem()` (.NET 8).
- The trusted cert store is a simple filesystem-based approach. No database table needed — the data volume is the source of truth. The metadata JSON sidecar keeps it self-contained.
- When running `update-ca-certificates`, capture stderr — if it fails (permission denied, binary not found), log the error and return it to the UI rather than silently failing.
