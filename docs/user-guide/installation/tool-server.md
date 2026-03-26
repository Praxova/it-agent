# Tool Server Installation

The Tool Server is a Windows service that runs on your domain-joined server
and executes Active Directory operations on behalf of the IT Agent. It is
the only Praxova component that communicates with Active Directory.

**Time required:** 20–30 minutes

**Before you start:**
- The Docker host and Admin Portal must be running (see
  [Docker Host Setup](docker-host.md))
- The Tool Server machine must be domain-joined
- You must have Administrator access to the Tool Server machine

---

## Step 1 — Copy the Installer

Copy the Tool Server installer package from your build artifacts to the
Tool Server machine. The file is named `praxova-toolserver.zip`.

From a machine with access to both:

```powershell
# Copy the zip to the tool server (adjust path as needed)
Copy-Item "praxova-toolserver.zip" -Destination "\\toolserver01\C$\Temp\"
```

Or use `scp` from the Docker host:

```bash
scp build/artifacts/praxova-toolserver.zip Administrator@toolserver01:C:/Temp/
```

---

## Step 2 — Install the Tool Server

Log in to the Tool Server machine and open PowerShell as Administrator.

```powershell
# Create the installation directory
$dest = "C:\Program Files\Praxova\ToolServer"
New-Item -ItemType Directory -Force -Path $dest

# Extract the installer
Expand-Archive -Path "C:\Temp\praxova-toolserver.zip" `
               -DestinationPath $dest -Force

# Run the service installer
cd $dest
.\install-service.ps1
```

Verify the service is running:

```powershell
Get-Service PraxovaToolServer
```

Expected output:

```
Status   Name                DisplayName
------   ----                -----------
Running  PraxovaToolServer   Praxova Tool Server
```

Check the health endpoint (HTTP — before TLS certificates are provisioned):

```powershell
Invoke-RestMethod -Uri "http://localhost:8080/api/v1/health"
```

Expected response:

```json
{
  "status": "Healthy",
  "adConnected": false
}
```

`adConnected: false` is expected at this stage — the Tool Server has not yet
received its credentials from the portal. That happens in Step 4.

---

## Step 3 — Provision TLS Certificates

Praxova secures the connection between the IT Agent and Tool Server using
TLS certificates issued from the Admin Portal's internal certificate authority.
The provisioning script handles this automatically.

Run this from any machine that can reach both the Admin Portal and the Tool Server:

```powershell
.\scripts\provision-toolserver-certs.ps1 `
    -PortalUrl https://<docker-host-ip>:5001 `
    -ToolServerHost toolserver01.yourdomain.com
```

The script will prompt for your Admin Portal credentials, then:

1. Issue a TLS certificate for the Tool Server from the Praxova internal CA
2. Deploy the certificate, key, and CA to
   `C:\Program Files\Praxova\ToolServer\certs\`
3. Install the Praxova CA into the Windows trusted root certificate store
4. Restrict key file permissions to SYSTEM and Administrators only
5. Restart the PraxovaToolServer service
6. Verify HTTPS is working on port 8443

After the script completes, verify HTTPS is working:

```powershell
Invoke-RestMethod -Uri "https://toolserver01.yourdomain.com:8443/api/v1/health"
```

---

## Step 4 — Register the Tool Server in the Portal

Open the Admin Portal and navigate to **Tool Servers → New**.

| Field | Value |
|-------|-------|
| Name | `toolserver01` (or a descriptive name) |
| URL | `https://toolserver01.yourdomain.com:8443` |

Click **Test Connectivity**. The portal will attempt to reach the Tool Server
and verify the TLS certificate. You should see a green status indicator.

If the connectivity test fails, see the
[Troubleshooting](#troubleshooting) section below.

---

## Step 5 — Configure Capability Mappings

Capability mappings tell the agent which Tool Server to use for each type
of Active Directory operation, and which service account credentials to use.

Navigate to **Capability Mappings** and create one mapping for each capability:

| Capability | Tool Server | Service Account |
|------------|-------------|-----------------|
| `ad-password-reset` | toolserver01 | Active Directory |
| `ad-group-add` | toolserver01 | Active Directory |
| `ad-group-remove` | toolserver01 | Active Directory |
| `ad-account-unlock` | toolserver01 | Active Directory |
| `ntfs-permission-grant` | toolserver01 | Active Directory |
| `ntfs-permission-revoke` | toolserver01 | Active Directory |

The "Active Directory" service account here is the one you created in step 7.4
of the Docker host setup — the account backed by your `svc-praxova` AD credentials.

---

## Step 6 — Verify AD Connectivity

Navigate to **Tool Servers** in the portal and check the status of your
Tool Server entry. Once the capability mappings are saved and the Tool Server
has received credentials from the portal, the AD connection status should
update to connected.

You can also check directly from the Tool Server:

```powershell
Invoke-RestMethod -Uri "https://toolserver01.yourdomain.com:8443/api/v1/health"
```

Expected response when fully configured:

```json
{
  "status": "Healthy",
  "adConnected": true
}
```

If `adConnected` is still `false`, see [Troubleshooting LDAPS](verify-ldaps.md).

---

## Troubleshooting

### Connectivity test fails in the portal

The portal cannot reach the Tool Server on port 8443.

- Confirm the PraxovaToolServer service is running:
  `Get-Service PraxovaToolServer`
- Confirm port 8443 is not blocked by Windows Firewall on the Tool Server:

```powershell
# Check if the port is listening
netstat -an | findstr 8443

# If needed, add a firewall rule
New-NetFirewallRule -DisplayName "Praxova Tool Server" `
    -Direction Inbound -Protocol TCP -LocalPort 8443 -Action Allow
```

- Confirm the Docker host can reach the Tool Server on port 8443:

```bash
# From the Docker host
curl -sk https://toolserver01.yourdomain.com:8443/api/v1/health
```

### TLS certificate error during connectivity test

The portal cannot verify the Tool Server's certificate.

- Confirm the provisioning script ran successfully and the certificate files
  exist in `C:\Program Files\Praxova\ToolServer\certs\`
- Confirm the Praxova CA is installed in the Windows trusted root store
  on the Tool Server (the provisioning script does this automatically)
- Re-run the provisioning script if in doubt — it is safe to run again

### `adConnected: false` after configuration

The Tool Server cannot reach the Domain Controller on port 636.

See [Verifying and Troubleshooting LDAPS](verify-ldaps.md) for a complete
diagnostic walkthrough.

---

## Next Steps

The Tool Server is installed and connected. Verify your end-to-end setup
by processing a test ticket.

→ [End-to-End Verification](verification.md)
