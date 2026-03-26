# Verifying and Troubleshooting LDAPS

Praxova connects to Active Directory over LDAPS — LDAP on port 636 with TLS
encryption. Plain LDAP on port 389 is not supported, as it would carry AD
credentials in the clear.

This page walks you through confirming LDAPS is working on your domain
controller, and diagnosing the most common causes of connection failures.

---

## Why LDAPS Needs a Certificate

LDAPS requires the domain controller to present a TLS certificate when a
client connects. If the DC has no valid certificate, the LDAPS port will
not be available even if port 636 is open.

Most domain controllers in environments running Active Directory Certificate
Services (AD CS) already have a certificate automatically enrolled. In
environments without AD CS, you may need to install one manually.

---

## Step 1 — Confirm Port 636 Is Open

Run this from the **Tool Server** machine:

```powershell
Test-NetConnection -ComputerName dc01.yourdomain.com -Port 636
```

Expected output when LDAPS is working:

```
ComputerName     : dc01.yourdomain.com
RemoteAddress    : 10.0.0.200
RemotePort       : 636
TcpTestSucceeded : True
```

If `TcpTestSucceeded` is `False`:
- Port 636 may be blocked by a firewall between the Tool Server and DC
- The DC may not have LDAPS configured (no valid certificate)
- Confirm the DC hostname or IP address is correct

---

## Step 2 — Test the LDAPS Bind

Confirming the port is open is not enough — you also need to confirm the
certificate is valid and the service account credentials work. Run this
from the Tool Server, substituting your actual values:

```powershell
$domain   = "yourdomain.com"
$dc       = "dc01.yourdomain.com"
$username = "svc-praxova@yourdomain.com"
$password = "YourServiceAccountPassword"

$ldap = New-Object System.DirectoryServices.DirectoryEntry(
    "LDAP://${dc}:636",
    $username,
    $password,
    [System.DirectoryServices.AuthenticationTypes]::SecureSocketsLayer
)

try {
    $name = $ldap.Name
    Write-Host "SUCCESS: Connected to $name" -ForegroundColor Green
} catch {
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
}
```

**Success** means port 636 is open, the DC's certificate is trusted, and
the service account credentials are correct.

**Failure messages and what they mean:**

| Error Message | Likely Cause |
|---------------|-------------|
| `The server is not operational` | Port 636 is blocked or DC has no LDAPS cert |
| `The authentication mechanism is unknown` | Certificate not trusted by Tool Server |
| `Invalid credentials` | Wrong username or password for `svc-praxova` |
| `Logon failure: unknown user name or bad password` | Same as above |

---

## Step 3 — Check the DC Has an LDAPS Certificate

If port 636 is open but connections fail with a certificate error, the DC
may not have a valid certificate for LDAPS.

Run this on the **Domain Controller**:

```powershell
# List certificates in the DC's personal store that could serve LDAPS
Get-ChildItem Cert:\LocalMachine\My | Where-Object {
    $_.EnhancedKeyUsageList -match "Server Authentication"
} | Select-Object Subject, NotAfter, Thumbprint
```

If no certificates are listed, or all certificates are expired, the DC
cannot serve LDAPS. You have two options:

**Option A — Request a certificate from AD CS (recommended if you have AD CS):**

On the DC, open the Certificates MMC (`certlm.msc`), right-click
**Personal → Certificates**, and choose **All Tasks → Request New Certificate**.
Select the **Domain Controller** or **Kerberos Authentication** template.
The DC will present this certificate for LDAPS automatically after a restart
of the NTDS service (or a reboot).

**Option B — Install a certificate from the Praxova internal CA:**

After the Admin Portal is running, you can issue a certificate from the
Praxova CA and install it on the DC. Contact your Praxova administrator
for the certificate provisioning procedure.

---

## Step 4 — Confirm the DC Certificate Is Trusted on the Tool Server

Even if the DC has a valid certificate, the Tool Server must trust the CA
that issued it. Run this on the **Tool Server**:

```powershell
# Check if the issuing CA is in the trusted root store
Get-ChildItem Cert:\LocalMachine\Root | Where-Object {
    $_.Subject -match "yourdomain" -or $_.Subject -match "Praxova"
} | Select-Object Subject, NotAfter
```

If the issuing CA is not listed, install it:

```powershell
# For an AD CS enterprise CA — export the root CA cert from AD CS first,
# then import it on the Tool Server:
Import-Certificate -FilePath "C:\Temp\YourRootCA.cer" `
    -CertStoreLocation Cert:\LocalMachine\Root
```

After installing the CA certificate, re-run the LDAPS bind test in Step 2.

---

## Step 5 — Restart the NTDS Service (If You Just Added a Certificate)

After adding a certificate to the DC's personal store, the LDAP service
may need to be restarted before it picks up the new certificate:

```powershell
# Run on the Domain Controller
Restart-Service NTDS -Force
```

> **This briefly interrupts domain authentication.** Schedule this during
> a maintenance window in production environments.

Wait 30 seconds, then re-run the port test and bind test from Steps 1 and 2.

---

## Still Not Working?

If you have worked through all the steps above and LDAPS still fails:

1. Check the DC's **System event log** and **Directory Service event log**
   for LDAP-related errors
2. Confirm there is no network-layer TLS inspection between the Tool Server
   and DC that could be intercepting port 636
3. Verify the `svc-praxova` account is not locked out or disabled:

```powershell
Get-ADUser svc-praxova -Properties LockedOut, Enabled
```

→ Back to [Tool Server Installation](tool-server.md)
