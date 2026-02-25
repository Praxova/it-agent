# Praxova Lab — Windows Infrastructure Runbook

**Scope:** Packer template build → OpenTofu deployment → Running DC + Tool Servers on Proxmox  
**Last Updated:** 2026-02-21  
**Infra Path:** `/home/alton/Documents/lucid-it-agent/infra/`

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                     Proxmox (pia-dev)                        │
│                                                              │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐   │
│  │  Template     │    │   DC01        │    │   TOOL01      │   │
│  │  VM 9000      │───▶│   VM 200      │───▶│   VM 201      │   │
│  │  (Packer)     │    │   10.0.0.200  │    │   10.0.0.201  │   │
│  │              │    │   AD DS + DNS  │    │   Domain Mbr  │   │
│  └──────────────┘    └──────────────┘    └──────────────┘   │
│                                                              │
│  Domain: montanifarms.com   NetBIOS: MONTANIFARMS           │
│  Gateway: 10.0.0.1         Build IP: 10.0.0.90              │
└─────────────────────────────────────────────────────────────┘
```

**Pipeline flow:** Windows ISO → Packer template (VM 9000) → OpenTofu clones → DC01 promotes → TOOL01 joins domain

**Critical constraint:** All clones boot with the sysprep answer file's static IP of `10.0.0.90`. Only one Windows clone can be built at a time until it gets renamed and re-IP'd. OpenTofu handles this sequencing automatically (dc01 completes before tool01 starts).

---

## Prerequisites

**On your Linux workstation:**
- Packer ≥ 1.9 with `proxmox` and `windows-update` plugins
- OpenTofu ≥ 1.11
- SSH key access to Proxmox host (for `build-answer-iso.sh` upload)
- `genisoimage` package (for building the autounattend ISO)

**On Proxmox (pia-dev):**
- ISOs uploaded to `local:iso/`:
  - `SERVER_EVAL_x64FRE_en-us.iso` — Windows Server 2022 evaluation
  - `virtio-win-0.1.285.iso` — VirtIO drivers
  - `ws2022-autounattend.iso` — Built by `build-answer-iso.sh` (see below)
- API token: `tofu@pve!automation` with VM creation/clone permissions
- Sufficient storage on `local-lvm` for 60G disk templates + clones
---

## Section 1: Build from Scratch

Use this when you have no template (VM 9000) and need to build everything from the ground up.

### 1.1 — Build the Autounattend ISO

The autounattend ISO contains the Windows unattended install answer file and helper scripts. Packer's `cd_content` approach was unreliable with Windows PE, so we pre-build an ISO and upload it to Proxmox.

```bash
cd /home/alton/Documents/lucid-it-agent/infra/packer/windows-server-2022

# Review/edit build-time variables if needed
# Defaults: BUILD_IP=10.0.0.250, WINRM_PASSWORD=P@cker-Bu1ld!
./build-answer-iso.sh
```

This script:
1. Renders `autounattend.xml` with build-time variables (IP, password)
2. Bundles it with `enable-winrm.ps1` and `install-virtio-guest-agent.ps1`
3. Creates `ws2022-autounattend.iso` with Joliet/Rock Ridge (OEMDRV volume label)
4. Uploads to Proxmox at `local:iso/ws2022-autounattend.iso` via SCP

### 1.2 — Set Up Packer Credentials

```bash
cd /home/alton/Documents/lucid-it-agent/infra/packer/windows-server-2022

# If credentials.auto.pkrvars.hcl doesn't exist, create from example:
cp credentials.auto.pkrvars.hcl.example credentials.auto.pkrvars.hcl
# Edit with your Proxmox API token and desired WinRM password
```

**File: `credentials.auto.pkrvars.hcl`**
```hcl
proxmox_url      = "https://10.0.0.2:8006/api2/json"
proxmox_username = "tofu@pve!automation"
proxmox_token    = "<your-token-uuid>"
proxmox_node     = "pia-dev"
winrm_password   = "P@cker-Bu1ld!"
```

The `.auto.pkrvars.hcl` extension means Packer loads it automatically — no `-var-file` flag needed.
### 1.3 — Initialize and Build the Packer Template

```bash
cd /home/alton/Documents/lucid-it-agent/infra/packer/windows-server-2022

# Download required plugins
packer init .

# Validate the template
packer validate .

# Build (takes 30-60 minutes depending on Windows Update)
packer build .

# For verbose debugging:
PACKER_LOG=1 packer build . 2>&1 | tee packer-build.log
```

**What happens during the build:**

1. **VM Creation** — Packer creates VM 9000 on Proxmox with:
   - SeaBIOS, host CPU, 4 cores, 8GB RAM
   - 60G IDE disk on `local-lvm` (IDE because VirtIO needs drivers first)
   - e1000 NIC (native Windows driver — no VirtIO network driver needed at boot)
   - Three ISOs mounted: Windows Server ISO, VirtIO drivers ISO, autounattend ISO

2. **Unattended Install** — `autounattend.xml` handles:
   - Disk partitioning (350MB system reserved + rest for Windows)
   - Image index 2 (Standard with Desktop Experience)
   - Admin password set, auto-logon enabled
   - Static IP `10.0.0.90` configured via FirstLogonCommands
   - Network set to Private profile
   - Firewall disabled
   - WinRM enabled (Basic auth, unencrypted for lab use)

3. **Packer Provisioners** — After WinRM connects:
   - Validation script confirms OS and WinRM status
   - `install-virtio.ps1` installs VirtIO drivers + QEMU Guest Agent from the mounted ISO
   - (Optional) Windows Update runs two passes with reboots between
   - `cleanup.ps1` clears temp files, update cache, event logs
   - `sysprep-unattend.xml` is copied to the VM
   - Sysprep runs (`/generalize /oobe /shutdown`)

4. **Template Conversion** — Packer converts the stopped VM to a Proxmox template

**Result:** Template VM 9000 (`ws2022-template`) ready for cloning.
### 1.4 — Set Up OpenTofu Credentials

```bash
cd /home/alton/Documents/lucid-it-agent/infra/opentofu

# If terraform.tfvars doesn't exist, create from example:
cp terraform.tfvars.example terraform.tfvars
# Edit with your values
```

**File: `terraform.tfvars`**
```hcl
proxmox_url       = "https://10.0.0.2:8006"
proxmox_api_token = "tofu@pve!automation=<your-token-uuid>"
proxmox_node      = "pia-dev"
```

Other variables (IPs, domain name, passwords) have sensible defaults in `variables.tf`. Override them here if needed.

### 1.5 — Deploy the Domain with OpenTofu

```bash
cd /home/alton/Documents/lucid-it-agent/infra/opentofu

# Initialize providers
tofu init

# Review the plan
tofu plan

# Deploy everything (takes ~15-20 minutes)
tofu apply -auto-approve
```

**What happens during deployment:**

The entire process is sequenced by OpenTofu dependencies. Here's the timeline:
#### DC01 Deployment (~10 minutes)

**Clone + Boot** (0:00–2:00)
- OpenTofu clones VM 9000 → VM 200 (`dc01`)
- Sysprep OOBE runs, applying `sysprep-unattend.xml`
- VM boots to `10.0.0.90` with WinRM enabled
- `time_sleep` waits 120s for OOBE to complete

**Phase 1 — via WinRM** (2:00–4:00)
- Tofu connects to `10.0.0.90` via WinRM
- Copies `setup-dc.ps1` to `C:\setup\`
- Script installs AD-Domain-Services role
- Writes Phase 2a, 2b, and cleanup scripts to `C:\setup\`
- Registers Phase 2a as a scheduled task (runs at startup)
- Reboots

**Phase 2a — Scheduled Task** (4:00–5:00)
- Changes IP from `.90` → `10.0.0.200`
- Sets DNS to `127.0.0.1` (will be its own DNS) + `10.0.0.1` (fallback)
- Renames computer to `DC01`
- Registers Phase 2b scheduled task
- Reboots (required for rename to take effect)

**Phase 2b — Scheduled Task** (5:00–8:00)
- Promotes to Domain Controller (`Install-ADDSForest`)
- Creates `montanifarms.com` forest with DNS
- Automatic reboot after promotion
**Cleanup — Scheduled Task** (8:00–10:00)
- Waits for AD DS to fully start (polls `Get-ADDomainController`)
- Disables firewall (DC promotion re-enables it)
- Re-configures WinRM (belt and suspenders)
- Removes all scheduled tasks

**Verification** (10:00+)
- `time_sleep` waits 300s for all phases to complete
- Tofu connects to `10.0.0.200` via WinRM
- Polls `Get-ADDomainController` up to 30 times (every 20s)
- Confirms hostname, IP, and domain are correct

#### TOOL01 Deployment (~8 minutes)

Only starts after DC01 verification succeeds.

**Clone + Boot** (0:00–3:00)
- Clones VM 9000 → VM 201 (`tool01`)
- Same OOBE process, boots to `10.0.0.90`
- `time_sleep` waits 180s

**Phase 1 — via WinRM** (3:00–4:00)
- Copies `setup-member.ps1` to `C:\setup\`
- Writes Phase 2a and 2b scripts
- Registers Phase 2a scheduled task
- Reboots
**Phase 2a — Scheduled Task** (4:00–5:00)
- Changes IP from `.90` → `10.0.0.201`
- Sets DNS to `10.0.0.200` (DC01) + `10.0.0.1` (fallback)
- Renames computer to `TOOL01`
- Registers Phase 2b scheduled task
- Reboots

**Phase 2b — Scheduled Task** (5:00–7:00)
- Waits for DNS resolution to `montanifarms.com` (polls up to 30 times)
- Joins domain using `Administrator@montanifarms.com` credentials
- Disables firewall, re-configures WinRM
- Removes scheduled tasks
- Reboots

**Verification** (7:00+)
- `time_sleep` waits 180s
- Tofu connects to `10.0.0.201` via WinRM
- Confirms hostname, IP, and domain membership

### 1.6 — Verify the Environment

After `tofu apply` completes, you should see the environment summary output. Manually verify:

```powershell
# RDP or WinRM to DC01 (10.0.0.200)
# Check AD is healthy
Get-ADDomainController
Get-ADDomain

# Check DNS
Resolve-DnsName montanifarms.com

# RDP or WinRM to TOOL01 (10.0.0.201)
# Confirm domain membership
(Get-WmiObject Win32_ComputerSystem).Domain
# Should return: montanifarms.com
```
---

## Section 2: Build from Existing Templates (Most Common)

Use this when VM 9000 already exists and you just need to (re)deploy the domain environment. This is the day-to-day workflow.

### 2.1 — Deploy or Redeploy VMs

```bash
cd /home/alton/Documents/lucid-it-agent/infra/opentofu

# If first time or after provider changes:
tofu init

# Deploy everything
tofu apply -auto-approve
```

That's it. OpenTofu handles the entire clone → configure → promote → join sequence.

### 2.2 — Redeploy Just TOOL01

If DC01 is healthy but you need a fresh tool server:

```bash
cd /home/alton/Documents/lucid-it-agent/infra/opentofu

# Destroy only tool01 resources
tofu destroy -target=null_resource.tool01_verify -auto-approve
tofu destroy -target=time_sleep.tool01_join_wait -auto-approve
tofu destroy -target=null_resource.tool01_setup -auto-approve
tofu destroy -target=time_sleep.tool01_boot_wait -auto-approve
tofu destroy -target=proxmox_virtual_environment_vm.tool01 -auto-approve

# Redeploy
tofu apply -auto-approve
```

**Important:** Delete the old VM from Proxmox if the destroy didn't clean it up, and make sure nothing else is using `10.0.0.90` before the new clone boots.

### 2.3 — Rebuild the Packer Template (Update Base Image)

When you need to update the template (new Windows patches, driver updates, etc.):

```bash
# Delete the old template from Proxmox first
# (Packer won't overwrite an existing VM with the same ID)
# Either via Proxmox UI or:
# ssh root@10.0.0.2 "qm destroy 9000"

cd /home/alton/Documents/lucid-it-agent/infra/packer/windows-server-2022
packer build .
```

After the new template is built, redeploy VMs per Section 2.1.
---

## Section 3: Nuke and Rebuild

Use this when you want to tear everything down and start completely fresh.

### 3.1 — Destroy All OpenTofu-Managed Resources

```bash
cd /home/alton/Documents/lucid-it-agent/infra/opentofu

# Destroy all VMs (dc01, tool01, and all associated resources)
tofu destroy -auto-approve
```

This removes VM 200 (dc01) and VM 201 (tool01) from Proxmox. The template (VM 9000) is NOT affected since it's managed by Packer, not Tofu.

### 3.2 — Destroy the Packer Template (Optional)

Only do this if the template itself is bad and needs a full rebuild from ISO.

```bash
# Remove the template from Proxmox
ssh root@10.0.0.2 "qm destroy 9000"
```

### 3.3 — Clean Local State (If Needed)

```bash
# OpenTofu state reset (only if state is corrupted)
cd /home/alton/Documents/lucid-it-agent/infra/opentofu
rm -f terraform.tfstate terraform.tfstate.backup

# Packer doesn't have persistent state — it's stateless by design
```
### 3.4 — Full Rebuild

```bash
# Step 1: Rebuild autounattend ISO (if answer file changed)
cd /home/alton/Documents/lucid-it-agent/infra/packer/windows-server-2022
./build-answer-iso.sh

# Step 2: Build new Packer template
packer init .
packer build .

# Step 3: Deploy domain environment
cd /home/alton/Documents/lucid-it-agent/infra/opentofu
tofu init
tofu apply -auto-approve
```

Total time from zero: ~45-75 minutes (mostly Windows install + updates + DC promotion).

### 3.5 — Quick Nuke & Rebuild (Template Preserved)

The most common "nuke" scenario — destroy all VMs but keep the template:

```bash
cd /home/alton/Documents/lucid-it-agent/infra/opentofu
tofu destroy -auto-approve && tofu apply -auto-approve
```

Total time: ~15-20 minutes.
---

## File Reference

### Packer Directory (`infra/packer/windows-server-2022/`)

| File | Purpose |
|------|---------|
| `windows-server-2022.pkr.hcl` | Main Packer template (uses pre-built ISO, includes Windows Update) |
| `ws2022-template.pkr.hcl` | Alternate template (inline `cd_files` for autounattend) |
| `variables.pkr.hcl` | Variable declarations with defaults |
| `credentials.auto.pkrvars.hcl` | Secrets — auto-loaded, gitignored |
| `ws2022.pkrvars.hcl` | Additional variable overrides |
| `autounattend.xml` | Windows unattended install answer file |
| `sysprep-unattend.xml` | Sysprep generalization answer file (sets static IP + WinRM for post-clone boot) |
| `build-answer-iso.sh` | Builds and uploads the autounattend ISO to Proxmox |
| `scripts/install-virtio.ps1` | Installs VirtIO drivers + QEMU Guest Agent (uses guest-tools installer) |
| `scripts/install-virtio-guest-agent.ps1` | Alternate VirtIO install (uses pnputil for individual drivers) |
| `scripts/enable-winrm.ps1` | WinRM configuration (used in autounattend ISO) |
| `scripts/cleanup.ps1` | Pre-sysprep cleanup (temp files, logs, disk optimization) |

### OpenTofu Directory (`infra/opentofu/`)

| File | Purpose |
|------|---------|
| `main.tf` | Provider config (bpg/proxmox), architecture documentation |
| `variables.tf` | All variable declarations (Proxmox, network, credentials, domain) |
| `terraform.tfvars` | Secret values — gitignored |
| `dc01.tf` | Domain Controller: clone → setup → verify |
| `tool01.tf` | Tool Server: clone → setup → verify (depends on dc01) |
| `outputs.tf` | Environment summary output |
| `scripts/setup-dc.ps1` | Three-phase DC promotion (install role → rename/re-IP → promote → cleanup) |
| `scripts/setup-member.ps1` | Three-phase domain join (rename/re-IP → DNS wait → join) |
---

## IP Address Map

| Host | IP | VM ID | Role |
|------|----|-------|------|
| Proxmox | 10.0.0.2 | — | Hypervisor |
| Gateway | 10.0.0.1 | — | Network gateway |
| Build IP | 10.0.0.90 | — | Temporary IP for clones during setup |
| DC01 | 10.0.0.200 | 200 | Domain Controller (AD DS + DNS) |
| TOOL01 | 10.0.0.201 | 201 | Domain member server |
| Ollama | 10.0.0.100 | TBD | LLM service (container) |
| Admin Portal | 10.0.0.101 | TBD | Web admin UI (container) |
| IT Agent | 10.0.0.102 | TBD | Helpdesk agent (container) |

---

## Credentials Reference

| Context | Username | Password | Notes |
|---------|----------|----------|-------|
| Proxmox API | `tofu@pve!automation` | Token UUID in tfvars | Used by both Packer and Tofu |
| Packer WinRM (build) | `Administrator` | `P@cker-Bu1ld!` | Only during template build |
| Template/Clone WinRM | `Administrator` | `P@ssw0rd123!` | Baked into sysprep-unattend.xml |
| AD DSRM | — | `P@ssw0rd123!` | Directory Services Restore Mode |
| Domain Admin | `MONTANIFARMS\Administrator` | `P@ssw0rd123!` | After DC promotion |

**⚠️ All passwords are lab-only defaults. Change for any non-isolated environment.**
---

## Troubleshooting

**Packer can't connect via WinRM**
- Verify nothing else is using `10.0.0.90`
- Check that the autounattend ISO is current (`build-answer-iso.sh`)
- Look at the VM console in Proxmox — is it stuck at OOBE?
- Increase `winrm_timeout` if Windows is slow to configure

**Tofu can't connect to clone at .90**
- Only one clone can use `.90` at a time — check for leftover VMs
- The `time_sleep` may be too short for your hardware — increase `create_duration`
- Check Proxmox console: is the clone still in OOBE?

**DC promotion fails**
- Check `C:\setup\phase2b.log` on DC01 for errors
- AD DS role must be installed before promotion (Phase 1 handles this)
- DNS must be reachable for forest creation

**TOOL01 can't join domain**
- Check `C:\setup\phase2b.log` on TOOL01
- Verify DC01 is fully promoted: `Get-ADDomainController` on DC01
- Verify DNS: from TOOL01, `Resolve-DnsName montanifarms.com -Server 10.0.0.200`
- The `time_sleep` after DC01 may need to be longer if AD takes time to stabilize

**State drift / orphaned VMs**
- If Tofu state doesn't match reality: `tofu state list` to see what's tracked
- Remove orphaned state: `tofu state rm <resource>`
- Delete orphaned VMs from Proxmox UI, then `tofu apply` to recreate
---

## Design Decisions

**Why three-phase scheduled tasks instead of inline WinRM?**
Windows operations like computer rename and DC promotion require reboots. WinRM connections drop during reboot, and Tofu can't reconnect to a new IP. Scheduled tasks run autonomously across reboots, then Tofu reconnects at the final IP to verify.

**Why IDE disk instead of VirtIO/SCSI?**
Windows Server 2022 doesn't have native VirtIO storage drivers. The installer can't see VirtIO disks without pre-loading drivers during WinPE. IDE works out of the box. VirtIO drivers are installed post-boot by the provisioner.

**Why e1000 NIC instead of VirtIO?**
Same reason — Windows has a native e1000 driver. VirtIO network drivers are installed by the provisioner after boot. For a lab environment the performance difference is negligible.

**Why a pre-built autounattend ISO?**
Packer's `cd_content` / `cd_files` approach creates ISOs on the fly, but Windows PE can't always find them depending on the drive letter assignment. Pre-building with `genisoimage` and the OEMDRV volume label is reliable.

**Why static IP in sysprep-unattend.xml?**
Proxmox doesn't have a guest customization mechanism like vSphere. Without the QEMU Guest Agent running (it starts after boot), there's no way to inject IP configuration. The sysprep answer file sets `.90` so Tofu knows where to find each clone, then the setup scripts change it to the final IP.

**Why two Packer template files?**
`ws2022-template.pkr.hcl` was the first working version that uses Packer's `cd_files` to deliver autounattend inline. `windows-server-2022.pkr.hcl` is the refined version that uses the pre-built ISO approach and includes Windows Update passes. The pre-built ISO approach (`windows-server-2022.pkr.hcl`) is the recommended one going forward.