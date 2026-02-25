# Praxova IT Agent — Deployment Guide

This guide covers deploying Praxova IT Agent for the first time in a customer environment.
It assumes a fresh installation with no existing Praxova components.

For developer setup and day-to-day build/deploy workflows, see `docs/DEV-QUICKREF.md`.  
For architecture background, see `docs/ARCHITECTURE.md`.

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Before You Begin — Planning](#2-before-you-begin--planning)
3. [Step 1 — Prepare the Docker Host](#3-step-1--prepare-the-docker-host)
4. [Step 2 — Deploy the Stack](#4-step-2--deploy-the-stack)
5. [Step 3 — Initial Portal Configuration](#5-step-3--initial-portal-configuration)
6. [Step 4 — Deploy the Tool Server](#6-step-4--deploy-the-tool-server)
7. [Step 5 — Connect the Agent](#7-step-5--connect-the-agent)
8. [Step 6 — Verify End-to-End](#8-step-6--verify-end-to-end)
9. [Upgrading](#9-upgrading)
10. [Known Limitations](#10-known-limitations)

---

## 1. Prerequisites

### Infrastructure Requirements

| Component | Requirement |
|-----------|------------|
| Docker host | Linux (Ubuntu 22.04+ recommended), 4 vCPU, 8 GB RAM, 40 GB disk |
| Docker | Docker CE 24+ with Compose plugin |
| GPU (optional) | NVIDIA GPU with 8 GB+ VRAM for local Ollama; required for llama3.1 8B |
| Tool server | Windows Server 2022, domain-joined, 2 vCPU, 4 GB RAM |
| Domain Controller | Windows Server 2016+, AD DS, DNS |
| ServiceNow | Personal Developer Instance or licensed instance (Rome release or later) |
| Network | Docker host and tool server must be able to reach the DC on port 636 (LDAPS) |

### Software Requirements (Docker host)

```bash
# Docker CE
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER   # log out and back in

# NVIDIA Container Toolkit (if using GPU)
curl -fsSL https://nvidia.github.io/libnvidia-container/gpgkey | \
  sudo gpg --dearmor -o /usr/share/keyrings/nvidia-container-toolkit-keyring.gpg
# Follow: https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/install-guide.html
```

### Active Directory Service Account

Before deploying, create the service account the tool server will use to execute AD operations.
This account requires **delegated permissions** — not Domain Admin rights.

```powershell
# On the Domain Controller — run as Domain Admin

# 1. Create the service account
New-ADUser -Name "svc-praxova" `
  -SamAccountName "svc-praxova" `
  -UserPrincipalName "svc-praxova@yourdomain.com" `
  -AccountPassword (ConvertTo-SecureString "StrongPassword123!" -AsPlainText -Force) `
  -PasswordNeverExpires $true `
  -CannotChangePassword $true `
  -Enabled $true `
  -Description "Praxova IT Agent service account — do not delete"
```

Then delegate permissions on the OUs that contain the users Praxova will manage.
Use **Active Directory Users and Computers → right-click OU → Delegate Control**:

| Permission | Purpose |
|------------|---------|
| Reset Password | Password reset tickets |
| Read and write pwdLastSet | Force password change at next logon |
| Read and write lockoutTime | Account unlock |
| Read and write member (on group objects) | Add/remove group membership |
| Read all properties (on user objects) | User lookup and validation |

> **Scope matters.** Delegate only to the OUs containing managed users, not the entire domain.
> The principle of least privilege applies — `svc-praxova` should be able to reset passwords
> for Help Desk-managed users, not every account in the directory.

### LDAPS Certificate Trust

The tool server connects to AD on port 636 (LDAPS). The DC's TLS certificate must be trusted
by the tool server.

**Option A — DC uses an AD CS certificate (most common):**  
Install the enterprise root CA certificate into the tool server's
`LocalMachine\Root` certificate store.

**Option B — DC uses the Praxova internal CA:**  
After deploying the portal (Step 2), issue a cert from the Praxova CA to the DC,
or provision the Praxova CA onto the DC's LDAPS listener.
See `scripts/provision-toolserver-certs.ps1` for the cert provisioning workflow.

---

## 2. Before You Begin — Planning

Answer these questions before starting the deployment:

**Network connectivity:**
- [ ] Can the Docker host reach the tool server (HTTPS, port 8443)?
- [ ] Can the Docker host reach ServiceNow (HTTPS, port 443)?
- [ ] Can the Docker host reach Ollama (if running on a separate host)?
- [ ] Can the tool server reach the DC (LDAPS, port 636)?
- [ ] Is there a firewall between the Docker host and tool server? If so, open port 8443.

**Active Directory:**
- [ ] What OUs contain the users Praxova should manage?
- [ ] Is there an existing group for Praxova administrators? (For portal role assignment — TD-007)
- [ ] Is LDAPS (port 636) enabled and working on the DC?

**ServiceNow:**
- [ ] What assignment group does Praxova monitor?
- [ ] Does your ServiceNow account have permission to read/write the `incident` table?

**LLM:**
- [ ] Local Ollama (GPU required) or cloud provider (OpenAI / Anthropic)?
- [ ] If local: does the Docker host have a compatible NVIDIA GPU?

---

## 3. Step 1 — Prepare the Docker Host

### 3.1 Clone the Repository

```bash
git clone https://github.com/your-org/praxova-it-agent.git
cd praxova-it-agent
```

### 3.2 Create the Environment File

```bash
cp .env.example .env
```

Edit `.env` with your values. Minimum required:

```bash
# ServiceNow credentials
SERVICENOW_PASSWORD=your_servicenow_password

# Seal passphrase — used to derive the master key for secrets encryption.
# Pick a strong passphrase and store it securely (password manager, vault).
# You will need this any time the portal restarts.
PRAXOVA_UNSEAL_PASSPHRASE=pick-a-strong-passphrase-min-32-chars

# Agent API key — leave empty for now; you will fill this in during Step 5
LUCID_API_KEY=
```

> **Keep `.env` out of version control.** It is already in `.gitignore`. Never commit it.

### 3.3 Pull or Build Container Images

**Option A — Pull pre-built images (when a registry is configured):**
```bash
docker compose pull
```

**Option B — Build from source:**
```bash
docker compose build
```

Building from source takes 5–10 minutes on first run (downloads base images, compiles .NET).
Subsequent builds use the Docker layer cache and are much faster.

### 3.4 Pull the LLM Model (local Ollama only)

If using local Ollama, pull the model before starting the stack so the agent doesn't time out
waiting for the first download:

```bash
# Start Ollama alone first
docker compose up -d ollama

# Pull the model (this can take several minutes — llama3.1 is ~4.7 GB)
docker compose exec ollama ollama pull llama3.1

# Confirm the model is available
docker compose exec ollama ollama list
```

---

## 4. Step 2 — Deploy the Stack

```bash
docker compose up -d
```

Watch the portal initialize:

```bash
docker compose logs -f admin-portal
```

On first start, look for these initialization messages (order may vary):

```
Secrets store initialized and unsealed via PRAXOVA_UNSEAL_PASSPHRASE
Generated new JWT signing key and stored encrypted in database
Internal PKI initialized — Praxova root CA generated
Admin portal TLS certificate issued from internal CA
Default admin user created — password change required on first login
Database migration complete
Application started. Listening on: http://+:5000, https://+:5001
```

**Verify the portal is healthy:**

```bash
curl -s http://localhost:5000/api/health/ | jq
# Expected: { "status": "Healthy", "sealed": false }
```

If `"sealed": true` appears, the `PRAXOVA_UNSEAL_PASSPHRASE` environment variable was not set
or was wrong. Check your `.env` file and restart the portal:

```bash
docker compose restart admin-portal
```

---

## 5. Step 3 — Initial Portal Configuration

Open the Admin Portal UI in your browser: `https://<docker-host-ip>:5001`

You will get a TLS certificate warning — the portal uses its own auto-generated CA which your
browser doesn't know about yet. Proceed past the warning, or import the CA cert:

```bash
# Download the portal CA cert
curl -s http://localhost:5000/api/pki/trust-bundle -o praxova-ca.pem

# macOS: double-click praxova-ca.pem and trust it in Keychain
# Windows: right-click → Install Certificate → Local Machine → Trusted Root CAs
# Firefox: Settings → Privacy → Certificates → Import
```

### 5.1 Change the Default Admin Password

Log in with `admin` / `admin`. You will be redirected to a mandatory password change screen
before any other portal operations are available.

> **Important:** The local `admin` account is the break-glass recovery account. Store its
> password securely — it is the only way to access the portal if Active Directory is unreachable.

### 5.2 Register the LLM Service Account

Navigate to **Service Accounts → New**.

**For local Ollama:**
- Provider type: `llm-ollama`
- Name: `Local Ollama`
- Endpoint: `http://ollama:11434`
- Model: `llama3.1`
- Credential storage: `none` (Ollama requires no credentials)

**For OpenAI:**
- Provider type: `llm-openai`
- Name: `OpenAI GPT-4o`
- Model: `gpt-4o`
- Credential storage: `database`
- API Key: your OpenAI API key

### 5.3 Register the ServiceNow Service Account

Navigate to **Service Accounts → New**.

- Provider type: `servicenow-basic`
- Name: `ServiceNow Production` (or `ServiceNow PDI` for dev)
- Instance URL: `https://your-instance.service-now.com`
- Credential storage: `database`
- Username: your ServiceNow admin username
- Password: your ServiceNow password

### 5.4 Register the Active Directory Service Account

Navigate to **Service Accounts → New**.

- Provider type: `windows-ad`
- Name: `AD svc-praxova`
- Domain: `yourdomain.com`
- DC hostname: `dc01.yourdomain.com` (or IP)
- Port: `636`
- Use SSL: `true`
- Credential storage: `database`
- Username: `svc-praxova`
- Password: the password you set when creating the AD account

### 5.5 Create the Agent

Navigate to **Agents → New**.

- Name: `helpdesk-agent` (must match `AGENT_NAME` in `.env`)
- Display name: `Helpdesk Agent`
- LLM provider: select the service account from 5.2
- ServiceNow connection: select the service account from 5.3
- Assignment group: the ServiceNow group Praxova should monitor (e.g., `Helpdesk`)

### 5.6 Create an API Key for the Agent

Navigate to **API Keys → New**.

- Name: `helpdesk-agent`
- Role: `Agent`

Copy the plaintext key — it is shown only once. Add it to `.env`:

```bash
LUCID_API_KEY=prx_xxxxxxxxxxxxxxxxxxxxxxxx
```

---

## 6. Step 4 — Deploy the Tool Server

The tool server runs as a Windows Service on the domain-joined `tool01` server.

### 6.1 Install the Tool Server

Copy `build/artifacts/praxova-toolserver.zip` to the tool server and extract it:

```powershell
# From a machine with access to both the build artifacts and tool01
$dest = "C:\Program Files\Praxova\ToolServer"
New-Item -ItemType Directory -Force -Path $dest
Expand-Archive -Path "praxova-toolserver.zip" -DestinationPath $dest -Force
```

Install and start the Windows Service:

```powershell
cd "C:\Program Files\Praxova\ToolServer"

# Install as a Windows Service (runs on port 8080/8443)
.\install-service.ps1

# Or install manually:
New-Service -Name "PraxovaToolServer" `
  -BinaryPathName "$dest\PraxovaToolServer.exe" `
  -DisplayName "Praxova Tool Server" `
  -StartupType Automatic `
  -Description "Praxova IT Agent — AD and file system operations"

Start-Service PraxovaToolServer
```

Verify it started:

```powershell
Get-Service PraxovaToolServer
# Status should be: Running

# Check the health endpoint (HTTP before cert provisioning)
Invoke-RestMethod -Uri "http://localhost:8080/api/v1/health"
```

### 6.2 Provision TLS Certificates

Run the certificate provisioning script from any machine that can reach both the portal and `tool01`:

```powershell
.\scripts\provision-toolserver-certs.ps1 `
    -PortalUrl https://<docker-host-ip>:5001 `
    -ToolServerHost tool01.yourdomain.com
```

This script:
1. Authenticates to the portal
2. Issues a TLS certificate for the tool server from the Praxova internal CA
3. Deploys the cert, key, and CA to `C:\Program Files\Praxova\ToolServer\certs\`
4. Installs the Praxova CA into the Windows trusted root store
5. Restricts key file permissions to SYSTEM and Administrators only
6. Restarts the PraxovaToolServer service
7. Verifies HTTPS on port 8443

After successful provisioning:

```powershell
Invoke-RestMethod -Uri "https://tool01.yourdomain.com:8443/api/v1/health"
# Expected: { "status": "Healthy", "adConnected": true }
```

If `adConnected` is `false`, the tool server cannot reach the DC on port 636 — see the
AD connectivity troubleshooting section below.

### 6.3 Register the Tool Server in the Portal

Navigate to **Tool Servers → New**.

- Name: `tool01`
- URL: `https://tool01.yourdomain.com:8443`
- Click **Test Connectivity** — should return green

Then navigate to **Capability Mappings** and create mappings:

| Capability | Tool Server | Service Account |
|------------|-------------|-----------------|
| `ad-password-reset` | tool01 | AD svc-praxova |
| `ad-group-add` | tool01 | AD svc-praxova |
| `ad-group-remove` | tool01 | AD svc-praxova |
| `ad-account-unlock` | tool01 | AD svc-praxova |
| `ntfs-permission-grant` | tool01 | AD svc-praxova |
| `ntfs-permission-revoke` | tool01 | AD svc-praxova |

---

## 7. Step 5 — Connect the Agent

Restart the agent to pick up the API key added in Step 3.6:

```bash
docker compose restart agent-helpdesk-01
```

Watch the agent startup:

```bash
docker compose logs -f agent-helpdesk-01
```

Expected output:

```
Praxova IT Agent starting...
  Admin Portal: https://admin-portal:5001
  Agent Name:   helpdesk-agent
Waiting for Admin Portal at http://admin-portal:5000...
Admin Portal is healthy.
Fetching portal CA trust bundle...
  Portal CA trusted (combined bundle at /tmp/combined-ca-bundle.pem)
Starting agent...
Loading agent configuration from https://admin-portal:5001/api/agents/...
Agent 'helpdesk-agent' configuration loaded (LLM: llm-ollama, ServiceNow: servicenow-basic)
Running in daemon mode. Polling every 30s.
```

> **Note on SSL trust:** The CA trust line may instead say `WARNING: Could not fetch trust
> bundle`. This is a known issue (portal redirect on the HTTP endpoint) — the shared volume
> workaround in docker-compose.yml provides trust in that case. The agent will still connect
> correctly. See `docs/infra/PRAXOVA-DEV-ENVIRONMENT-RUNBOOK.md` Known Issues for details.

---

## 8. Step 6 — Verify End-to-End

### 8.1 Load the Default Workflows

The default dispatcher workflow and sub-workflows are seeded automatically on first run.
Verify they loaded in the portal under **Workflows**.

You should see:
- `it-helpdesk-dispatcher` — main dispatcher
- `password-reset-sub` — password reset sub-workflow
- `group-membership-sub` — group membership sub-workflow
- `file-permissions-sub` — file permissions sub-workflow

### 8.2 Create a Test Ticket in ServiceNow

Create an incident in your ServiceNow instance assigned to the monitored assignment group:

```
Short description: Please reset my password
Description: I've been locked out of my account. My username is jdoe.
```

Wait up to 60 seconds (one poll interval). Watch the agent logs:

```bash
docker compose logs -f agent-helpdesk-01
```

You should see the ticket classified, routed to the password-reset sub-workflow,
and either resolved (if AD delegation is correct) or escalated with a clear error.

### 8.3 Check the Audit Log

In the portal, navigate to **Audit Log** to see the full record of what the agent did.

### 8.4 Verify the Approval Queue

Create a ticket type that triggers a Human Approval step (default: any group membership change).
Verify the approval request appears in the portal under **Approvals**.

Approve it and confirm the agent processes the approved action within one poll cycle.

---

## 9. Upgrading

### Container Stack

```bash
# Pull or build new images
docker compose pull   # if using a registry
# or
docker compose build  # if building from source

# Restart with new images (portal handles DB migrations automatically)
docker compose up -d

# Verify
docker compose logs -f admin-portal   # watch for "Migration complete"
curl -s http://localhost:5000/api/health/ | jq
```

No data loss — the `admin-data` volume persists the database, certificates, and encryption keys
across restarts and image updates.

### Tool Server

```powershell
# Stop the service, extract new build, start
Stop-Service PraxovaToolServer
Expand-Archive -Path "praxova-toolserver.zip" `
  -DestinationPath "C:\Program Files\Praxova\ToolServer" -Force
Start-Service PraxovaToolServer

# Existing TLS certificates and service configuration are preserved
# (the zip does not include the certs\ subdirectory)
```

If the upgrade changes the tool server API contract, re-run the capability mapping
verification in the portal after the upgrade.

---

## 10. Known Limitations

These are known issues in the current release. See `docs/TECHNICAL_DEBT.md` for full details.

**TD-007 — Default admin credentials (HIGH)**  
The portal ships with `admin` / `admin` credentials. Active Directory authentication is not
yet implemented. For production deployments, change the default password immediately and
restrict network access to the portal to trusted admin machines only.

**TD-004 — Failed workflows silently resolve tickets (MEDIUM)**  
If a sub-workflow fails and the dispatcher has no `outcome == 'failed'` transition, the ticket
may be incorrectly marked resolved. Monitor the Audit Log for unexpected resolutions.

**TD-008 — Ollama traffic is unencrypted (MEDIUM)**  
Ollama serves plain HTTP only. LLM inference traffic between the agent and Ollama is not
encrypted. Acceptable when both containers are on the same isolated Docker network (the default),
but must be addressed before deployment in high-security environments. Options: TLS-terminating
reverse proxy (Nginx/Caddy sidecar), switch to llama.cpp server (supports TLS natively), or
use a cloud LLM provider (OpenAI, Anthropic) which uses HTTPS.

**SSL trust bootstrap workaround**  
The agent's CA trust bootstrap relies on a shared Docker volume as a workaround for a portal
redirect issue. This is transparent to normal operation but represents infrastructure coupling
that will be removed in a future release.

---

## Troubleshooting

### Portal won't start / stays sealed

Check `PRAXOVA_UNSEAL_PASSPHRASE` is set in `.env` and matches the value used when the
database was first created. If you've lost the passphrase and the data is not critical,
do a full reset:

```bash
docker compose down
docker volume rm praxova-admin-data praxova-admin-logs
docker compose up -d
```

This destroys all configuration and generated certificates. You will need to redo Steps 3–5.

### Agent can't connect to portal

```bash
# Check portal is healthy
curl -s http://localhost:5000/api/health/ | jq

# Check the agent has an API key
docker compose exec agent-helpdesk-01 printenv LUCID_API_KEY

# Check agent logs for specific error
docker compose logs --tail=50 agent-helpdesk-01
```

### Tool server reports `adConnected: false`

```powershell
# On tool01 — test LDAPS connectivity to the DC
Test-NetConnection -ComputerName dc01.yourdomain.com -Port 636

# Test LDAPS bind with service account credentials
$cred = Get-Credential svc-praxova
$ldap = [System.DirectoryServices.DirectoryEntry]::new(
    "LDAP://dc01.yourdomain.com:636",
    $cred.UserName,
    $cred.GetNetworkCredential().Password
)
$ldap.name  # Should return the domain name if successful
```

If the bind fails with a certificate error, the DC's LDAPS certificate is not trusted by
the tool server. Install the issuing CA certificate into `LocalMachine\Root`.

### Password reset fails (AD permissions)

The tool server reaches the DC but `SetPassword()` is rejected. This is a delegation issue —
`svc-praxova` does not have the `Reset Password` extended right on the target OU.

```powershell
# On the DC — check the current delegated permissions for svc-praxova
# Use Active Directory Users and Computers → View → Advanced Features
# → Navigate to the target OU → Properties → Security tab
# Look for svc-praxova with "Reset Password" and "Write pwdLastSet" permissions
```

If they are missing, re-run the Delegate Control wizard on the target OUs (see Section 1).

### ServiceNow tickets not being picked up

- Confirm the `SERVICENOW_PASSWORD` in `.env` is correct
- Confirm the assignment group name exactly matches what's in ServiceNow (case-sensitive)
- Check agent logs for poll errors: `docker compose logs -f agent-helpdesk-01`
- Verify the ServiceNow service account has read/write access to the `incident` table
