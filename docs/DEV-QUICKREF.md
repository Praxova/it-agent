# Praxova IT Agent — Dev Quick Reference

> Build, deploy, and test cheat sheet for the Praxova stack.
> Last updated: 2026-02-27

---

## Infrastructure

| Host | IP | OS | Role |
|------|----|----|------|
| dev workstation | 10.0.0.10 | Ubuntu 22.04 | Development, Docker host, builds containers |
| dc01 | 10.0.0.200 | Windows Server 2022 | Domain controller (montanifarms.com) |
| tool01 | 10.0.0.201 | Windows Server 2022 | Tool server (domain-joined, runs PraxovaToolServer service) |
| build01 | 10.0.0.12 | Windows Server 2022 | .NET build VM (SDK + WiX installed) |

**Docker containers** (on dev workstation):

| Container | Ports | Image |
|-----------|-------|-------|
| praxova-admin-portal | 5000 (HTTP), 5001 (HTTPS) | praxova-admin:latest |
| praxova-agent-helpdesk-01 | — | praxova-agent:latest |
| praxova-llm | 8443 (HTTPS) | praxova-llm:latest |

**ServiceNow PDI**: https://dev341394.service-now.com

---

## 1. Build Everything

### Containers (Admin Portal + Agent)

```bash
cd /home/alton/Documents/lucid-it-agent

# Build images and save as tarballs in build/artifacts/
scripts/build-containers.sh

# Skip LLM server build (large CUDA build, rarely changes)
scripts/build-containers.sh --skip-llm
```

Outputs:
- `build/artifacts/praxova-admin-<tag>.tar`
- `build/artifacts/praxova-agent-<tag>.tar`
- `build/artifacts/praxova-llm-<tag>.tar` (unless skipped)

### Tool Server (.NET, built on Windows)

```bash
# Syncs source to build01, compiles self-contained win-x64, pulls zip back
scripts/build-toolserver.sh

# With MSI installer
scripts/build-toolserver.sh --msi
```

Output: `build/artifacts/praxova-toolserver.zip`

### Quick Local Rebuild (no tarballs, just images)

```bash
docker compose build                  # Rebuild portal + agent images
docker compose build admin-portal     # Portal only
docker compose build agent-helpdesk-01  # Agent only
```

---

## 2. Deploy

### Containers (Local Docker)

```bash
cd /home/alton/Documents/lucid-it-agent

# Start/restart the stack (uses locally built images)
docker compose up -d

# Or rebuild + start in one shot
docker compose up -d --build

# Restart just the agent (picks up new portal config, .env changes)
docker restart praxova-agent-helpdesk-01

# Stop everything
docker compose down

# Nuke volumes too (full reset — see "Nuke and Re-Seed" below)
docker compose down -v
```

### Containers (Remote Docker Host)

```bash
# Deploy tarballs to a remote host via SSH
scripts/deploy-containers.sh deploy@10.0.0.10 v1.0.0

# Skip LLM server (already deployed)
scripts/deploy-containers.sh --skip-llm deploy@10.0.0.10 v1.0.0

# Custom .env file
scripts/deploy-containers.sh deploy@10.0.0.10 v1.0.0 /path/to/.env
```

### Tool Server (to tool01)

Manual deployment — stop the service, extract the zip, start:

```powershell
# From a machine with SMB access to tool01
# 1. Stop the service
Invoke-Command -ComputerName tool01 -ScriptBlock { Stop-Service PraxovaToolServer }

# 2. Extract new build (from dev workstation or build01)
$dest = "\\tool01\C$\Program Files\Praxova\ToolServer"
Expand-Archive -Path build/artifacts/praxova-toolserver.zip -DestinationPath $dest -Force

# 3. Start the service
Invoke-Command -ComputerName tool01 -ScriptBlock { Start-Service PraxovaToolServer }

# 4. Verify
Invoke-RestMethod -Uri "http://tool01:8080/api/v1/health"
```

Or from the dev workstation (scp + ssh):

```bash
# Copy zip to tool01
scp build/artifacts/praxova-toolserver.zip Administrator@tool01:C:/temp/

# SSH in and deploy
ssh Administrator@tool01
# Then in PowerShell:
Stop-Service PraxovaToolServer
Expand-Archive -Path C:\temp\praxova-toolserver.zip -DestinationPath "C:\Program Files\Praxova\ToolServer" -Force
Start-Service PraxovaToolServer
```

---

## 3. TLS Certificate Provisioning (Tool Server)

The admin portal runs an internal PKI. Portal and agent certs are auto-generated.
The tool server needs its cert provisioned separately.

```powershell
# Run from any Windows machine with network access to portal + tool01
.\scripts\provision-toolserver-certs.ps1 `
    -PortalUrl https://10.0.0.10:5001 `
    -ToolServerHost tool01
```

This script:
1. Authenticates to the portal (prompts for admin credentials)
2. Calls `POST /api/pki/certificates/issue` to mint a cert with proper SANs
3. Deploys cert + key + CA to `tool01:\Program Files\Praxova\ToolServer\certs\`
4. Imports the Praxova CA into the Windows trusted root store on tool01
5. Restricts key file permissions to SYSTEM + Administrators
6. Restarts the PraxovaToolServer service
7. Verifies HTTPS on port 8443

**After provisioning**, update the tool server URL in the Admin Portal UI:
`http://tool01:8080` → `https://tool01:8443`

---

## 4. API Key Setup

The agent authenticates to the portal using an API key. Without it, heartbeats, credential fetches, and approval polling all return 401.

1. Log into portal UI: `https://10.0.0.10:5001`
2. Navigate to **API Keys** page
3. Create a new key:
   - Name: `test-agent`
   - Role: `Agent`
4. Copy the plaintext key (shown only once)
5. Add to `.env`:
   ```
   LUCID_API_KEY=prx_xxxxxxxxxxxxxxxx
   ```
6. Restart the agent:
   ```bash
   docker restart praxova-agent-helpdesk-01
   ```

---

## 5. Environment File (.env)

Location: `/home/alton/Documents/lucid-it-agent/.env`

```bash
# Agent API key (from portal UI → API Keys)
LUCID_API_KEY=

# Seal/unseal passphrase (auto-set in docker-compose.yml with dev default)
# PRAXOVA_UNSEAL_PASSPHRASE=dev-passphrase-change-in-production

# AD bind password (if portal authenticates against AD)
# PRAXOVA_AD_BIND_PASSWORD=

# ServiceNow password (fallback if portal credential fetch fails)
# SERVICENOW_PASSWORD=
```

---

## 6. LLM Server (llama.cpp)

The LLM server runs llama.cpp with native TLS. It auto-provisions a certificate
from the portal PKI on first startup.

```bash
# Place your GGUF model file in the llm-models volume
docker volume inspect praxova-llm-models   # Find mount point
# Copy model into the volume (e.g., from a download):
sudo cp llama-3.1-8b-instruct.Q4_K_M.gguf \
    /var/lib/docker/volumes/praxova-llm-models/_data/model.gguf

# Check LLM server logs
docker compose logs -f llm

# Health check (uses self-signed cert from portal CA)
curl -sk https://localhost:8443/health
```

> The LLM server image includes a full CUDA build of llama.cpp. First build takes
> 10-15 minutes. Subsequent builds use Docker layer cache.

---

## 7. Verify / Health Checks

```bash
# ── Portal ────────────────────────────────────────────────
curl -s http://localhost:5000/api/health/ | jq           # HTTP
curl -sk https://localhost:5001/api/health/ | jq         # HTTPS

# ── Portal API (authenticated) ───────────────────────────
# Get a JWT first:
TOKEN=$(curl -sk https://localhost:5001/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"YOUR_PASSWORD"}' | jq -r .token)

curl -sk https://localhost:5001/api/agents \
  -H "Authorization: Bearer $TOKEN" | jq

# ── Agent config export (anonymous) ──────────────────────
curl -s http://localhost:5000/api/agents/by-name/test-agent/export | jq

# ── Tool server ──────────────────────────────────────────
curl -s http://tool01:8080/api/v1/health | jq            # HTTP
curl -sk https://tool01:8443/api/v1/health | jq          # HTTPS (after cert provisioning)

# ── LLM Server ────────────────────────────────────────────
curl -sk https://localhost:8443/health | jq

# ── ServiceNow ───────────────────────────────────────────
curl -s -u admin:$SERVICENOW_PASSWORD \
  'https://dev341394.service-now.com/api/now/table/incident?sysparm_limit=1' | jq '.result | length'

# ── Container status ─────────────────────────────────────
docker compose ps
docker compose logs -f admin-portal       # Portal logs
docker compose logs -f agent-helpdesk-01  # Agent logs
docker compose logs -f llm               # LLM server logs
```

---

## 8. Test Tickets

```bash
cd /home/alton/Documents/lucid-it-agent
source .venv/bin/activate

python agent/scripts/create_test_tickets.py --all       # Create all test tickets
python agent/scripts/create_test_tickets.py --list      # List current tickets
python agent/scripts/create_test_tickets.py --cleanup   # Remove test tickets
```

---

## 9. Running Tests

```bash
# Python agent tests
cd /home/alton/Documents/lucid-it-agent/agent
source ../.venv/bin/activate
pytest                         # All tests
pytest -v                      # Verbose
pytest tests/unit/ -v          # Unit tests only

# .NET admin portal
cd /home/alton/Documents/lucid-it-agent/admin/dotnet
dotnet test

# .NET tool server
cd /home/alton/Documents/lucid-it-agent/tool-server/dotnet
dotnet test

# LLM integration tests
cd /home/alton/Documents/lucid-it-agent/agent
python scripts/test_llm_integration.py
```

---

## 10. Full Deploy Sequence (From Scratch)

When everything needs to go out together:

```bash
cd /home/alton/Documents/lucid-it-agent

# 1. Build containers (make sure to record the <TAG>)
scripts/build-containers.sh

# 2. Build tool server
scripts/build-toolserver.sh

# 3.a Deploy containers (local)
docker compose down
docker compose up -d

# 3.b Deploy containers (remote)
# 2. Deploy to VM 110 (use whatever tag the build outputs)
./scripts/deploy-containers.sh packer@10.0.0.51 <TAG>

# 4. Wait for portal to be healthy
docker compose logs -f admin-portal  # Watch for "Application started"

# 5. Deploy tool server to tool01 (see section 2)

# 6. Provision tool server TLS cert (see section 3)
#    (from a Windows machine)

# 7. Generate API key in portal UI → add to .env (see section 4)

# 8. Copy GGUF model into LLM volume (first time only)
#    See section 6 for details on placing the model file

# 9. Restart agent to pick up API key
docker restart praxova-agent-helpdesk-01

# 10. Verify
docker compose ps
docker compose logs -f agent-helpdesk-01
```

---

## 11. Nuke and Re-Seed

Full reset — wipes the portal database, encryption keys, PKI, and all config:

```bash
docker compose down
docker volume rm praxova-admin-data    # Wipes DB, certs, seal keys
docker compose up -d --build

# Watch portal initialize fresh:
docker compose logs -f admin-portal
```

You should see:
```
Default admin user created — password change required on first login
Secrets store initialized and unsealed via PRAXOVA_UNSEAL_PASSPHRASE
Generated new JWT signing key and stored encrypted in database
Internal PKI initialized — CA generated and stored encrypted
Admin portal TLS certificate issued
```

After reset you must:
- Change the default admin password (admin/admin) at first login
- Re-create service accounts (ServiceNow, LLM, AD) in the portal UI
- Re-create tool server registrations + capability mappings
- Re-generate the agent API key (old one is gone)
- Re-provision tool server TLS cert (old CA is gone)

> LLM models persist in a separate volume (`praxova-llm-models`) — not affected by reset.
> LLM server certs (`praxova-llm-certs`) will be re-provisioned automatically on next startup.

---

## 12. Git

```bash
cd /home/alton/Documents/lucid-it-agent
git status
git log --oneline -10
git add .
git commit -m "description"
```

---

## Appendix: Port Map

| Service | HTTP | HTTPS | Notes |
|---------|------|-------|-------|
| Admin Portal | 5000 | 5001 | Blazor UI + REST API |
| Tool Server | 8080 | 8443 | AD/file operations (on tool01) |
| LLM Server | — | 8443 | llama.cpp with native TLS |
| ServiceNow PDI | — | 443 | External SaaS |

## Appendix: Trust Chain

All internal TLS uses certs from the portal's internal PKI (RSA 4096 CA, 90-day leaf certs).

| Connection | Trust Mechanism |
|---|---|
| Agent → Portal (HTTPS) | Entrypoint fetches CA via HTTP (`GET /api/pki/trust-bundle`), sets `SSL_CERT_FILE` before agent starts. **Note:** shared volume workaround still active in docker-compose.yml pending portal redirect fix — see infra runbook. |
| Agent → LLM Server (HTTPS) | Same CA — trusted from bootstrap fetch. LLM server also bootstraps CA trust + provisions its own cert from portal PKI. |
| Agent → Tool Server (HTTPS) | Same CA — trusted from bootstrap fetch |
| Portal → Portal (self-calls) | Custom X509Chain validation against own CA in Program.cs |
| Portal → Tool Server (health checks) | OS trust store (`update-ca-certificates` in container entrypoint) |
| External (browser, curl) | Must add `-k` / skip verification, or import CA manually |
