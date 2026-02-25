# Praxova IT Agent — Full Dev Environment Runbook

**Scope:** Ubuntu Docker host (Packer template) → Container build pipeline → Deployed Praxova stack  
**Last Updated:** 2026-02-21  
**Infra Path:** `/home/alton/Documents/lucid-it-agent/`  
**Related:** `infra/PRAXOVA-WINDOWS-INFRA-RUNBOOK.md` (Windows DC + TOOL01)

---

## Architecture Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│                        Proxmox (pia-dev, 10.0.0.2)                  │
│                                                                      │
│  ┌───────────────┐    ┌──────────────┐    ┌──────────────────────┐  │
│  │  Win Template  │    │   DC01        │    │  Ubuntu Docker Host  │  │
│  │  VM 9000       │───▶│   VM 200      │    │  VM 9010 (template)  │  │
│  │  (WS2022 Base) │    │   10.0.0.200  │    │  VM 110 (clone)      │  │
│  └───────────────┘    │   AD + DNS    │    │  10.0.0.51           │  │
│                        └──────────────┘    │  Docker CE + NVIDIA  │  │
│  ┌───────────────┐    ┌──────────────┐    │  Container Toolkit   │  │
│  │  Ubuntu Tmpl   │    │   TOOL01      │    │  GPU: Titan Xp 12GB  │  │
│  │  VM 9010       │───▶│   VM 201      │    └──────────────────────┘  │
│  │  (Ubuntu 24.04)│    │   10.0.0.201  │                              │
│  └───────────────┘    │   Domain Mbr  │                              │
│                        └──────────────┘                              │
│                                                                      │
│  Domain: montanifarms.com   NetBIOS: MONTANIFARMS                   │
│  Gateway: 10.0.0.1                                                   │
└──────────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────┐
│                 Dev Workstation (10.0.0.10, Ubuntu 22.04)            │
│                 Ryzen 9 7950X · 128GB RAM · RTX 5080                 │
│                                                                      │
│  ┌─────────────────────┐  ┌──────────────────┐  ┌───────────────┐  │
│  │  praxova-admin-portal│  │praxova-agent-    │  │praxova-ollama │  │
│  │  ports 5000/5001     │  │helpdesk-01       │  │port 11434     │  │
│  │  (Blazor + REST API) │  │(Python/Griptape) │  │(LLM server)   │  │
│  └─────────────────────┘  └──────────────────┘  └───────────────┘  │
│                    All on lucid-net (praxova-network)                │
└──────────────────────────────────────────────────────────────────────┘
```

**Pipeline summary:**  
Ubuntu ISO → Packer template (VM 9010) → Clone to VM 110 (test-docker-host) → GPU passthrough attached → `build-containers.sh` builds and saves image tarballs → `deploy-containers.sh` rsync + loads images → `docker compose up -d`

---

## Prerequisites

**On your Linux workstation:**
- Packer ≥ 1.9 with `proxmox` plugin
- Docker CE + Docker Compose plugin
- `rsync`, `ssh` with key-based auth to target VMs
- SSH key access to Proxmox host (`root@10.0.0.2`)
- Git (for tag-based image versioning)

**On Proxmox (pia-dev):**
- ISOs uploaded to `local:iso/`:
  - `ubuntu-24.04-live-server-amd64.iso` — Ubuntu 24.04 LTS
  - (optional) `virtio-win-0.1.285.iso` — for Windows VMs
- API token: `tofu@pve!automation` with VM creation/clone/configure permissions
- GPU passthrough configured (see GPU Passthrough section)
- Sufficient storage on `local-lvm`

---

## Section 1: Build the Ubuntu Docker Host Template

Use this when VM 9010 does not exist and you need to build the base template from scratch.

### 1.1 — Set Up Packer Credentials

```bash
cd ~/Documents/lucid-it-agent/infra/packer/ubuntu-docker-host

# Create credentials file from example if it doesn't exist
cp ubuntu-docker-host.pkrvars.hcl.example ubuntu-docker-host.pkrvars.hcl
# Edit with your Proxmox API token
```

**File: `ubuntu-docker-host.pkrvars.hcl`**
```hcl
proxmox_url      = "https://10.0.0.2:8006/api2/json"
proxmox_username = "tofu@pve!automation"
proxmox_token    = "<your-token-uuid>"
proxmox_node     = "pia-dev"
```

### 1.2 — Initialize and Build

```bash
cd ~/Documents/lucid-it-agent/infra/packer/ubuntu-docker-host

# Make provisioner scripts executable (do this once)
chmod +x scripts/*.sh

# Download required plugins
packer init .

# Validate syntax
packer validate -var-file="ubuntu-docker-host.pkrvars.hcl" .

# Build (takes 10-20 minutes)
packer build -var-file="ubuntu-docker-host.pkrvars.hcl" .

# Verbose debug logging
PACKER_LOG=1 packer build -var-file="ubuntu-docker-host.pkrvars.hcl" . 2>&1 | tee packer-build.log
```

**What happens during the build:**

1. **VM Creation** — Packer creates VM 9010 on Proxmox with:
   - SeaBIOS, 2 cores, 4GB RAM, 40GB disk
   - Cloud-init drive (for IP injection on clones)
   - Ubuntu 24.04 LTS ISO mounted
   - Packer HTTP server serves `http/user-data` for autoinstall

2. **Unattended OS Install** — `http/user-data` (cloud-init autoinstall) handles:
   - Disk layout and partitioning
   - Base packages
   - `packer` user account with sudo (build-time only)
   - SSH enabled, password auth enabled for Packer to connect
   - Static IP `10.0.0.90` during build

3. **Packer Provisioners** — After SSH connects:
   - `setup-docker.sh` — installs Docker CE + Compose plugin, adds packer user to docker group
   - `setup-nvidia.sh` — installs `nvidia-driver-550`, NVIDIA Container Toolkit, configures Docker daemon for NVIDIA runtime
   - `harden.sh` — SSH hardening, disables password auth for post-clone security
   - `cleanup.sh` — removes temp files, apt cache, cloud-init state

4. **Template Conversion** — Packer converts the stopped VM to a Proxmox template (VM 9010)

**Result:** Template VM 9010 (`ubuntu-docker-host`) ready for cloning.

**Build time notes:** NVIDIA driver install (DKMS kernel module compilation) takes the longest. There is no GPU in the VM during build — this is expected and harmless. Modules load on first boot when GPU passthrough is attached.

### 1.3 — Key Template Attributes

- **VM ID:** 9010
- **Template name:** `ubuntu-docker-host`
- **OS:** Ubuntu 24.04 LTS
- **Pre-installed:** Docker CE, Docker Compose plugin, NVIDIA driver 550, NVIDIA Container Toolkit
- **Cloud-init drive:** Yes (`cloud_init = true`) — required for IP injection on clones
- **SSH:** Key-based only post-clone (password auth disabled by harden.sh)

---

## Section 2: Clone and Configure the Docker Host VM

Use this when VM 9010 exists and you need a running Docker host for testing.

### 2.1 — Clone from Template

```bash
# On Proxmox host — clone template to new VM
ssh root@10.0.0.2

# Clone VM 9010 → VM 110 (test-docker-host)
qm clone 9010 110 --name test-docker-host --full

# Inject IP configuration via cloud-init
qm set 110 --ipconfig0 ip=10.0.0.51/24,gw=10.0.0.1
qm set 110 --nameserver 8.8.8.8
qm set 110 --searchdomain local

# Attach GPU via vfio-pci (Titan Xp in IOMMU group 30)
qm set 110 -hostpci0 42:00,pcie=1,x-vga=0

# Start the VM
qm start 110

# Wait ~30-60s for boot, then verify
ssh packer@10.0.0.51
```

### 2.2 — Post-Clone Fixes

Two known issues with the template that require a one-time fix on each clone until the template is rebuilt:

**Fix 1: Docker group membership**

The `packer` user needs to be in the `docker` group (or you need to create a dedicated deploy user). Until the template is updated:

```bash
ssh packer@10.0.0.51
sudo usermod -aG docker packer
# Log out and back in for group to take effect
```

**Fix 2: Sudoers cleanup**

If the packer-provisioned sudoers file was cleaned up during `cleanup.sh`, verify sudo still works:

```bash
sudo docker ps   # Should not prompt for password
```

If it fails, re-add the sudoers entry:
```bash
echo 'packer ALL=(ALL) NOPASSWD:ALL' | sudo tee /etc/sudoers.d/packer
```

### 2.3 — Verify GPU Passthrough

```bash
ssh packer@10.0.0.51

# Check driver loaded
nvidia-smi

# Check Container Toolkit sees it
docker run --rm --gpus all nvidia/cuda:12.4.0-base-ubuntu22.04 nvidia-smi
```

Expected output: Titan Xp with 12GB VRAM, CUDA 13.0, Driver 580+.

---

## Section 3: Build Container Images

Use this to build fresh container images from source on your workstation.

```bash
cd ~/Documents/lucid-it-agent

# Build all images + save as tarballs (uses git describe for tag)
scripts/build-containers.sh

# Build with explicit tag
scripts/build-containers.sh v1.2.0

# Skip saving ollama tarball (it's ~3GB and rarely changes)
scripts/build-containers.sh --skip-ollama
```

**What the script does:**
1. Builds `praxova-admin:latest` and `praxova-admin:<tag>` from `admin/dotnet/Dockerfile`
2. Builds `praxova-agent:latest` and `praxova-agent:<tag>` from `agent/Dockerfile`
3. Pulls `ollama/ollama:latest` (unless skipped or tarball already cached)
4. Saves all images as tarballs in `build/artifacts/`:
   - `praxova-admin-<tag>.tar`
   - `praxova-agent-<tag>.tar`
   - `ollama-latest.tar` (cached; only re-saved if missing)
5. Prints artifact summary with sizes and SHA256 hashes

**Quick local rebuild (no tarballs):**
```bash
docker compose build                    # Rebuild all
docker compose build admin-portal       # Portal only
docker compose build agent-helpdesk-01  # Agent only
```

---

## Section 4: Deploy Stack Locally (Workstation)

The most common development workflow. All containers run on the dev workstation.

```bash
cd ~/Documents/lucid-it-agent

# Start the full stack (uses locally built images)
docker compose up -d

# Rebuild images and start in one shot
docker compose up -d --build

# Restart just the agent (picks up .env changes, portal config)
docker restart praxova-agent-helpdesk-01

# Stop everything
docker compose down

# Full reset (wipes database, certs, PKI — see Nuke section)
docker compose down -v
```

---

## Section 5: Deploy Stack to Remote Docker Host (VM 110)

Use this to test deployments on the dedicated Ubuntu Docker host with GPU passthrough.

### 5.1 — Prerequisites

- VM 110 running and reachable at `10.0.0.51`
- SSH key-based access to `packer@10.0.0.51`
- Container tarballs in `build/artifacts/` (run `build-containers.sh` first)
- `.env` file populated (see Section 6)

### 5.2 — Deploy

```bash
cd ~/Documents/lucid-it-agent

# Full deploy (all three services)
scripts/deploy-containers.sh packer@10.0.0.51 <TAG>

# Skip ollama if already deployed and model not changed
scripts/deploy-containers.sh --skip-ollama packer@10.0.0.51 <TAG>

# Use a custom .env file
scripts/deploy-containers.sh packer@10.0.0.51 <TAG> /path/to/other.env
```

**What the script does:**
1. Validates that required tarballs exist locally
2. `rsync`s tarballs to `/opt/praxova/` on the remote host (resume-capable for large files)
3. Copies `docker-compose.yml` and `.env` to `/opt/praxova/`
4. SSH into remote, runs `docker load` for each image
5. `docker compose down --timeout 30` (graceful stop of old stack)
6. `docker compose up -d --no-build` (starts with loaded images)
7. Polls `http://localhost:5000/api/health/` for up to 60 seconds
8. On success, prints confirmation; on failure, dumps recent portal logs

### 5.3 — Full Deploy Sequence from Scratch

When the remote host has nothing deployed:

```bash
cd ~/Documents/lucid-it-agent

# Step 1: Build images
scripts/build-containers.sh

# Note the TAG printed at the end (or use: git describe --tags --always)
TAG=$(git describe --tags --always)

# Step 2: Deploy to VM 110
scripts/deploy-containers.sh packer@10.0.0.51 ${TAG}

# Step 3: Wait for portal to show healthy, then pull the LLM model
ssh packer@10.0.0.51 "cd /opt/praxova && docker compose exec ollama ollama pull llama3.1"

# Step 4: Create API key in portal UI
# Visit https://10.0.0.51:5001 (skip TLS warning or import CA)
# → API Keys → New Key → Name: test-agent, Role: Agent → Copy the key

# Step 5: Add API key to .env and redeploy agent
echo "LUCID_API_KEY=prx_xxxxxxxxxxxxxxxx" >> .env
scripts/deploy-containers.sh --skip-ollama packer@10.0.0.51 ${TAG}
```

---

## Section 6: Environment File (.env)

Location: `/home/alton/Documents/lucid-it-agent/.env`

Minimum required content:

```bash
# Agent API key (from portal UI → API Keys)
LUCID_API_KEY=prx_xxxxxxxxxxxxxxxx

# ServiceNow PDI credentials
SERVICENOW_PASSWORD=<your-sn-password>

# Seal passphrase (keep dev default for lab use)
# PRAXOVA_UNSEAL_PASSPHRASE=dev-passphrase-change-in-production

# AD bind password (if portal authenticates to AD)
# PRAXOVA_AD_BIND_PASSWORD=
```

---

## Section 7: Container Stack Reference

### Services

| Service | Container Name | Image | Ports | Notes |
|---------|---------------|-------|-------|-------|
| Admin Portal | praxova-admin-portal | praxova-admin:latest | 5000 (HTTP), 5001 (HTTPS) | Blazor Server + REST API |
| Helpdesk Agent | praxova-agent-helpdesk-01 | praxova-agent:latest | — | Python/Griptape, polls portal |
| Ollama | praxova-ollama | ollama/ollama:latest | 11434 | LLM inference, GPU-enabled |

### Volumes

| Volume | Name | Content |
|--------|------|---------|
| admin-data | praxova-admin-data | SQLite DB, certs (incl. CA), seal keys |
| admin-logs | praxova-admin-logs | Portal log files |
| ollama-models | praxova-ollama-models | Downloaded LLM model files |

The `admin-data` volume is critical — it contains the auto-generated internal CA certificate. The agent reads the CA from this shared volume as the SSL trust bootstrap workaround (see Known Issues).

### Network

All three services are on `praxova-network` (bridge driver). The agent reaches the portal at `http://admin-portal:5000` (bootstrap/health) and `https://admin-portal:5001` (runtime). Ollama is at `http://ollama:11434`.

---

## Section 8: GPU Configuration

### Proxmox Host Setup (one-time)

The Titan Xp lives in IOMMU group 30 at PCI `42:00`. Device IDs: `10de:1b02` (GPU), `10de:10ef` (audio).

On the Proxmox host (`/etc/modprobe.d/vfio.conf`):
```
options vfio-pci ids=10de:1b02,10de:10ef
```

GRUB kernel parameters in `/etc/default/grub`:
```
GRUB_CMDLINE_LINUX_DEFAULT="quiet amd_iommu=on iommu=pt"
```

`nvidia` and `nouveau` are blacklisted on the host so the GPU is owned by `vfio-pci`.

### Attaching GPU to a VM

```bash
# One-time attach (survives reboots)
qm set <vmid> -hostpci0 42:00,pcie=1,x-vga=0

# Verify from inside the VM
nvidia-smi
```

### Docker GPU Access

The NVIDIA Container Toolkit is installed in the template. Docker's `daemon.json` is configured to use the NVIDIA runtime. The `docker-compose.yml` requests GPU access for Ollama:

```yaml
deploy:
  resources:
    reservations:
      devices:
        - driver: nvidia
          count: 1
          capabilities: [gpu]
```

No additional configuration needed on the VM — it works automatically when the GPU is passed through.

---

## Section 9: Known Issues

### SSL Certificate Trust Bootstrap

**Status:** Partially implemented. Entrypoint logic is correct; portal-side redirect fix pending.

**Intended mechanism:** The agent uses two separate portal URLs:
- `ADMIN_PORTAL_BOOTSTRAP_URL` — HTTP, used only during startup to fetch the CA and check health
- `ADMIN_PORTAL_URL` — HTTPS, used for all runtime API calls

At startup, `docker-entrypoint.sh` fetches `${ADMIN_PORTAL_BOOTSTRAP_URL}/api/pki/trust-bundle`
over plain HTTP, validates the PEM response, combines it with the system CA bundle, then sets
`SSL_CERT_FILE` and `REQUESTS_CA_BUNDLE` to the combined bundle before launching the agent
process. This is the correct architecture — no shared volumes, no tight coupling between containers.

**Current gap:** The portal's HTTP→HTTPS redirect middleware fires before the
`/api/pki/trust-bundle` endpoint can serve its response. The entrypoint receives a 301/307
redirect, the curl fetch fails, and the script emits a warning and continues. The HTTPS
runtime calls then fail because the portal's CA is not trusted.

**Active workaround (in docker-compose.yml):**

```yaml
# Agent service environment — TEMPORARY, remove when portal fix is applied
SSL_CERT_FILE: /portal-data/certs/ca.pem
REQUESTS_CA_BUNDLE: /portal-data/certs/ca.pem
volumes:
  - admin-data:/portal-data:ro
```

The shared volume works reliably but is an infrastructure-layer coupling that should not be
permanent. The correct fix is a one-line change in the portal's ASP.NET middleware pipeline:
exempt `/api/pki/trust-bundle` from the HTTPS redirect. Once done, remove the three lines
above from docker-compose.yml.

**Required portal fix:** In `LucidAdmin.Web/Program.cs`, exclude the trust-bundle endpoint
from `UseHttpsRedirection()`:

```csharp
// Before UseHttpsRedirection, exclude the bootstrap endpoint:
app.UseWhen(
    ctx => !ctx.Request.Path.StartsWithSegments("/api/pki/trust-bundle"),
    branch => branch.UseHttpsRedirection()
);
```

After applying this fix, the entrypoint fetch will succeed and the shared volume workaround
can be removed from docker-compose.yml.

### Docker Group on Template Clones

**Status:** Must be fixed manually on each clone until template is rebuilt.

The cleanup script in the Packer build removes the docker group membership from the `packer` user. After cloning, run:

```bash
sudo usermod -aG docker packer
```

Then log out and back in for the group to take effect.

---

## Section 10: Health Checks

```bash
# ── Portal ─────────────────────────────────────────────────────
curl -s http://localhost:5000/api/health/ | jq          # HTTP (workstation)
curl -sk https://localhost:5001/api/health/ | jq        # HTTPS (workstation)
curl -s http://10.0.0.51:5000/api/health/ | jq         # HTTP (VM 110)

# ── Ollama ─────────────────────────────────────────────────────
curl -s http://localhost:11434/api/tags | jq '.models[].name'

# ── Container status ───────────────────────────────────────────
docker compose ps
docker compose logs -f admin-portal
docker compose logs -f agent-helpdesk-01
docker compose logs -f ollama

# ── GPU from inside container (workstation - local ollama) ──────
docker compose exec ollama nvidia-smi

# ── GPU from VM 110 ────────────────────────────────────────────
ssh packer@10.0.0.51 "nvidia-smi"
ssh packer@10.0.0.51 "cd /opt/praxova && docker compose exec ollama nvidia-smi"
```

---

## Section 11: Nuke and Rebuild

### Nuke Containers Only (keep volumes — preserve DB and certs)

```bash
docker compose down
docker compose up -d
```

### Full Reset (wipe everything — fresh portal state)

```bash
docker compose down
docker volume rm praxova-admin-data praxova-admin-logs
docker compose up -d

# Watch initialization:
docker compose logs -f admin-portal
```

After a full reset you must:
- Change the default admin password (`admin`/`admin`) at first login
- Re-create all service accounts (ServiceNow, LLM, AD) in the portal UI
- Re-generate the agent API key and update `.env`
- Re-provision tool server TLS cert (old CA is gone)

Ollama models are in a separate volume (`praxova-ollama-models`) and are NOT affected by the reset.

### Nuke the Docker Host VM (VM 110)

```bash
ssh root@10.0.0.2
qm stop 110
qm destroy 110

# Re-clone from template (see Section 2)
qm clone 9010 110 --name test-docker-host --full
qm set 110 --ipconfig0 ip=10.0.0.51/24,gw=10.0.0.1
qm set 110 --nameserver 8.8.8.8
qm set 110 -hostpci0 42:00,pcie=1,x-vga=0
qm start 110
```

### Rebuild the Ubuntu Template (VM 9010)

Only needed when the base template itself is stale (driver updates, Ubuntu security patches, Docker version changes):

```bash
# Destroy old template
ssh root@10.0.0.2 "qm destroy 9010"

# Rebuild
cd ~/Documents/lucid-it-agent/infra/packer/ubuntu-docker-host
packer build -var-file="ubuntu-docker-host.pkrvars.hcl" .

# Re-clone and redeploy (see Sections 2 and 5)
```

---

## File Reference

### Build & Deploy Scripts (`scripts/`)

| File | Purpose |
|------|---------|
| `build-containers.sh` | Build portal + agent images, save as tarballs in `build/artifacts/` |
| `deploy-containers.sh` | rsync tarballs to remote host, load images, start stack |
| `build-toolserver.sh` | Builds .NET tool server on build01 (Windows), pulls zip back |
| `provision-toolserver-certs.ps1` | Issues TLS cert from portal PKI to tool01 |

### Packer Directory (`infra/packer/ubuntu-docker-host/`)

| File | Purpose |
|------|---------|
| `ubuntu-docker-host.pkr.hcl` | Main Packer template (proxmox-iso builder, cloud-init, SSH) |
| `ubuntu-docker-host.pkrvars.hcl` | Secrets — Proxmox API token, auto-loaded, gitignored |
| `http/user-data` | Cloud-init autoinstall config (Ubuntu unattended install) |
| `http/meta-data` | Cloud-init meta-data (empty, required by autoinstall) |
| `scripts/setup-docker.sh` | Installs Docker CE + Compose plugin |
| `scripts/setup-nvidia.sh` | Installs nvidia-driver-550 + Container Toolkit, configures Docker daemon |
| `scripts/harden.sh` | SSH hardening, disables password auth |
| `scripts/cleanup.sh` | Removes temp files, apt cache, cloud-init state |

### Docker Compose Files

| File | Purpose |
|------|---------|
| `docker-compose.yml` | Production stack (three services, named volumes, GPU config) |
| `docker-compose.override.yml` | Dev overrides (source mounts, DEBUG logging) — auto-applied locally |

### Build Artifacts (`build/artifacts/`)

| File | Content | Notes |
|------|---------|-------|
| `praxova-admin-<tag>.tar` | Admin portal image tarball | ~500MB, rebuilt each release |
| `praxova-agent-<tag>.tar` | Agent image tarball | ~300MB, rebuilt each release |
| `ollama-latest.tar` | Ollama image tarball | ~3GB, cached (rebuilt only if missing) |

---

## IP Address Map

| Host | IP | Role |
|------|----|------|
| Proxmox | 10.0.0.2 | Hypervisor |
| Gateway | 10.0.0.1 | Network gateway |
| Dev workstation | 10.0.0.10 | Docker host (local dev), builds containers |
| DC01 | 10.0.0.200 | Domain Controller (AD DS + DNS) |
| TOOL01 | 10.0.0.201 | Domain member server |
| Ubuntu Docker Host | 10.0.0.51 | VM 110, test-docker-host, GPU passthrough |

---

## Credentials Reference

| Context | Username | Password | Notes |
|---------|----------|----------|-------|
| Proxmox API | `tofu@pve!automation` | Token UUID in pkrvars | Used by Packer |
| Ubuntu VM SSH | `packer` | `packer` (password) or SSH key | Password auth disabled post-clone |
| Portal default | `admin` | `admin` | Must change on first login |
| ServiceNow PDI | `admin` | In `.env` | dev341394.service-now.com |

**⚠️ All passwords are lab-only defaults. Do not use in any non-isolated environment.**

---

## Troubleshooting

**Packer build stuck at boot, never connects via SSH**
- Check the Proxmox console — if you see a language selection screen, the autoinstall boot command didn't fire. Verify `http/user-data` is in the `http/` directory (not the project root) and the Packer HTTP server is reachable from the build VM.
- Verify the VM's network can reach Packer's HTTP server on your workstation.
- Check `PACKER_LOG=1` output for the boot command being sent.

**Cloud-init IP not applying on clone**
- Verify `cloud_init = true` is in the Packer template (`ubuntu-docker-host.pkr.hcl`). If a template was built without it, re-add the cloud-init drive manually: `qm set <vmid> --ide2 local-lvm:cloudinit`.
- Run `qm set <vmid> --ipconfig0 ip=...` and verify with `qm config <vmid>`.

**`docker: permission denied` on VM**
- The packer user isn't in the docker group. Fix: `sudo usermod -aG docker packer`, then log out and back in.

**Agent shows SSL certificate errors**
- The shared volume workaround depends on the admin-data volume being mounted and the portal having generated its CA. Check that the portal is healthy before the agent starts.
- Verify the CA file exists: `docker compose exec agent-helpdesk-01 ls /portal-data/certs/`
- If the file is missing, the portal hasn't initialized PKI yet. Wait for it to be healthy and restart the agent.

**`deploy-containers.sh` fails health check**
- Check portal logs: `ssh packer@10.0.0.51 "cd /opt/praxova && docker compose logs --tail=50 admin-portal"`
- Check that the `.env` file was transferred and has correct permissions (`chmod 600`).
- Increase the health check wait time in the script if the host is slow to initialize.

**Ollama container exits immediately**
- The GPU is not attached to the VM. Verify with `nvidia-smi` inside the VM (not inside the container). If that fails too, check vfio-pci binding on Proxmox host.
- If `nvidia-smi` works in the VM but fails inside the container, verify `daemon.json` has the NVIDIA runtime configured: `cat /etc/docker/daemon.json`

**rsync transfer interrupted**
- rsync is idempotent and resumable. Just re-run `deploy-containers.sh` — it will resume the interrupted transfer.

---

## Design Decisions

**Why tarballs instead of a registry?**
The pipeline saves images as `.tar` files and transfers them via rsync instead of pushing to a container registry. For a single-developer lab environment this eliminates the need to manage a registry service. rsync provides progress reporting and resume capability for the large Ollama image. The tradeoff is that the `build/artifacts/` directory grows if old tags aren't cleaned up.

**Why rsync instead of `docker save | ssh | docker load`?**
The Ollama image is ~3GB. rsync's `--append` and block-level delta detection means interrupted transfers resume from where they left off. Piping through SSH has no resume capability and would restart from zero on any interruption.

**Why a shared volume for SSL trust instead of the HTTP bootstrap endpoint?**
The portal's HTTP→HTTPS redirect fires before the `/api/pki/trust-bundle` endpoint can serve
its response. The shared volume is a reliable workaround until the portal exempts that endpoint
from redirect middleware. See Known Issues section for the required portal fix and the exact
workaround lines in docker-compose.yml to remove once it's resolved.

**Why build on the workstation, not on the Docker host VM?**
The workstation has significantly more CPU/RAM, faster storage, and the full development toolchain. The VM exists purely as a test target for validating GPU passthrough and deployment scripts. Building on the workstation keeps build times fast and avoids network dependency during development.

**Why cloud-init for VM configuration instead of Packer variables?**
Packer bakes a static image. Cloud-init runs at first boot after cloning, allowing per-clone configuration (IP address, hostname, SSH keys) without rebuilding the template. This matches the Proxmox clone workflow and is the standard approach for Ubuntu Server VM templates.
