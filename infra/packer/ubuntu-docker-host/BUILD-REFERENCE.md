# Ubuntu 24.04 Packer Template on Proxmox — Reference Guide

**Date:** February 18, 2026
**Template ID:** 9010
**Build Time:** ~11 minutes
**Project:** lucid-it-agent / infra / packer / ubuntu-docker-host

---

## What This Template Builds

A cloud-init-ready Ubuntu 24.04 LTS Proxmox template with:

- Docker CE + Compose plugin
- NVIDIA driver (550) + Container Toolkit (for GPU passthrough)
- QEMU guest agent
- SSH hardening + unattended security upgrades
- Cleaned for template cloning (no machine-id, no SSH host keys, no logs)

## File Structure

```
ubuntu-docker-host/
├── ubuntu-docker-host.pkr.hcl        # Main Packer template
├── ubuntu-docker-host.pkrvars.hcl     # Variable values (contains secrets — DO NOT commit)
├── http/
│   ├── user-data                      # Ubuntu autoinstall config (cloud-init format)
│   └── meta-data                      # Empty file (required by cloud-init)
└── scripts/
    ├── setup-docker.sh                # Docker CE + Compose
    ├── setup-nvidia.sh                # NVIDIA driver 550 + Container Toolkit
    ├── harden.sh                      # SSH hardening + unattended-upgrades
    └── cleanup.sh                     # Template cleanup (machine-id, keys, logs, etc.)
```

## How to Build

```bash
cd ~/Documents/lucid-it-agent/infra/packer/ubuntu-docker-host

# First time only — download the proxmox plugin
packer init .

# Validate syntax
packer validate -var-file="ubuntu-docker-host.pkrvars.hcl" .

# Build (destroys existing VM 9010 if present)
ssh root@10.0.0.2 "qm stop 9010 2>/dev/null; qm destroy 9010 2>/dev/null; echo 'cleaned up'"
packer build -var-file="ubuntu-docker-host.pkrvars.hcl" .
```

## How to Verify a Clone

```bash
# Clone the template
qm clone 9010 110 --name test-docker-host --full

# Start and connect
qm start 110
ssh packer@10.0.0.50   # Static IP from build — change before production use

# Verify Docker
docker run hello-world

# Verify NVIDIA toolkit (nvidia-smi needs GPU passthrough to work)
nvidia-ctk --version

# Verify SSH hardening
grep PasswordAuthentication /etc/ssh/sshd_config

# Check cloud-init is clean and ready for re-initialization
cloud-init status
```

---

## Problems Solved & Lessons Learned

This section documents every issue we hit and the fix, in the order we encountered them. These are specific to **Ubuntu 24.04 + Proxmox + Packer proxmox-iso builder**.

### 1. Autoinstall Delivery — cidata CD-ROM, Not HTTP

**Problem:** The initial approach used Packer's built-in HTTP server (`http_directory`) to serve the autoinstall config. This required complex boot command injection with semicolons and URLs that broke in the GRUB editor.

**Solution:** Deliver autoinstall via a cidata-labeled CD-ROM attached as a second IDE device. Packer builds this ISO on the fly from local files.

```hcl
additional_iso_files {
  type              = "ide"
  index             = 1
  iso_storage_pool  = "local"
  unmount           = true
  keep_cdrom_device = false
  cd_files = [
    "./http/meta-data",
    "./http/user-data"
  ]
  cd_label = "cidata"
}
```

**Why it matters:** Ubuntu's Subiquity installer automatically detects `cidata`-labeled media. No URLs, no special characters, no HTTP server firewall rules. The boot command only needs the `autoinstall` keyword.

**Note:** We kept the directory named `http/` from the original approach. The name is arbitrary — Packer just reads the files.

### 2. Boot Order — Disk First, ISO Fallback

**Problem:** With `boot = "order=ide2"`, the VM always booted from the Ubuntu ISO — even after installation. This caused an infinite boot loop where the installer would start over after every reboot.

**Root Cause:** `order=ide2` forces permanent boot from the ISO with no fallback to the installed disk.

**Solution:**

```hcl
boot = "order=scsi0;ide2"
```

**How it works:**

- **First boot:** scsi0 (disk) is empty → falls through to ide2 (Ubuntu ISO) → install runs
- **Post-install reboot:** scsi0 has Ubuntu installed → boots from disk → Packer connects via SSH

This is the same pattern used in the TeKanAid article and mirrors how physical hardware works with boot priority.

### 3. GRUB Editor Navigation — <esc> Entry, Not Spacebar

**Problem:** Multiple approaches to injecting `autoinstall` onto the kernel command line failed:

- `Ctrl+E` (Emacs end-of-line) — didn't transmit through Proxmox VNC
- `<end>` key — worked inconsistently
- GRUB command line (`c`) — kernel booted but autoinstall wasn't detected by Subiquity
- Spacebar to stop autoboot — unreliable timing

**Solution:** The TeKanAid approach uses `<esc>` to enter the GRUB menu, then `e` to open the editor:

```hcl
boot_command = [
  "<esc><wait>",
  "e<wait>",
  "<down><down><down><end>",
  " autoinstall quiet ds=nocloud",
  "<f10><wait>",
  ...
]
```

**Key details:**

- `<esc>` reliably enters the GRUB menu from the countdown timer
- `e` opens the boot entry editor
- `<down><down><down>` navigates to the `linux` kernel line (line 3 in the GRUB editor)
- `<end>` moves cursor to end of that line — this DID work with the `<esc>` entry method
- `<f10>` boots with the modified parameters
- `quiet` suppresses kernel boot messages (cleaner console)
- `ds=nocloud` tells cloud-init to look for local datasources (the cidata CD)

**Why `<esc>` works but spacebar doesn't:** The spacebar approach depends on hitting a narrow timing window during the GRUB countdown. `<esc>` interrupts the countdown immediately regardless of timing, giving consistent results.

### 4. Ubuntu 24.04 Confirmation Prompt — By Design, Must Answer

**Problem:** Even with `autoinstall` on the kernel command line, Subiquity displayed:

```
Continue with autoinstall? (yes/no)
```

The build hung waiting for a response that never came.

**Root Cause:** This is **intentional behavior** in Ubuntu 24.04's Subiquity installer. The confirmation prompt appears by design as a safety measure, even when `autoinstall` is explicitly on the kernel command line.

**Reference:** Confirmed by TeKanAid article on Ubuntu 24.04 autoinstall with Proxmox (tekanaid.com).

**Solution:** Send `yes<enter>` after a timed delay:

```hcl
boot_command = [
  "<esc><wait>",
  "e<wait>",
  "<down><down><down><end>",
  " autoinstall quiet ds=nocloud",
  "<f10><wait>",
  "<wait3m>",
  "yes<enter>",
  "<wait30>yes<enter>"
]
```

**Timing notes:**

- The prompt appears approximately **3 minutes** after F10 on our hardware (4-core, 16GB)
- `<wait3m>` pauses 3 minutes before sending `yes<enter>`
- `<wait30>yes<enter>` is a safety backup in case the prompt appears slightly later
- Faster or slower hardware may need timing adjustment — watch the Proxmox console during the first build
- If the build hangs at the installer, increase the wait; if `yes` appears before the prompt, decrease it

### 5. SSH Discovery — Static IP Bypasses Guest Agent Requirement

**Problem:** After successful install and reboot, Packer was stuck at "Waiting for SSH to become available..." indefinitely.

**Root Cause:** The Proxmox Packer builder uses `qemu-guest-agent` to discover the VM's IP address for SSH. But `qemu-guest-agent` couldn't be installed during autoinstall because the VM had no internet access during the install phase (apt mirrors unreachable).

**Failed attempts:**

- Installing `qemu-guest-agent` in the `packages:` section of user-data → `apt-get` returned exit status 100 (no network)
- Installing via `late-commands` with `curtin in-target` → same failure

**Solution:** Assign a static IP in the autoinstall config and tell Packer exactly where to connect:

**In user-data (autoinstall network section):**
```yaml
network:
  version: 2
  ethernets:
    id0:
      match:
        driver: virtio*
      addresses:
        - 10.0.0.50/24
      routes:
        - to: default
          via: 10.0.0.1
      nameservers:
        addresses:
          - 10.0.0.1
          - 8.8.8.8
```

**In the Packer template:**
```hcl
ssh_host = "10.0.0.50"
```

**Benefits:**

- Packer connects directly — no guest agent needed for IP discovery
- Static IP provides DNS resolution (8.8.8.8 fallback) so apt works after first boot
- `qemu-guest-agent` installs during the provisioner phase when apt repos are reachable
- `10.0.0.50` must be reserved/unused on your network during builds

**Important:** The `driver: virtio*` match syntax uses a glob pattern to match the virtio network adapter regardless of the exact device name Proxmox assigns.

### 6. Package Installation — Provisioners, Not Autoinstall

**Problem:** Packages like `qemu-guest-agent`, `docker-ce`, and `nvidia-driver-550` failed to install during the autoinstall phase.

**Root Cause:** The VM may not have full internet access during the Ubuntu installer's package installation phase, even with a static IP configured. Only packages available on the ISO itself (like `openssh-server`) are guaranteed to install.

**Solution:** Keep the autoinstall `packages:` list minimal — only `openssh-server` (required for Packer SSH). Install everything else via Packer shell provisioners after first boot:

```yaml
# In user-data — MINIMAL
packages:
  - openssh-server
```

```hcl
# In Packer template — EVERYTHING ELSE
provisioner "shell" {
  inline = [
    "sudo apt-get update -y",
    "sudo apt-get install -y qemu-guest-agent cloud-init wget git jq htop net-tools",
    "sudo systemctl enable qemu-guest-agent"
  ]
}
```

**Rule of thumb:** If a package isn't on the ISO, install it with a Packer provisioner.

---

## Key Configuration Values

| Setting | Value | Notes |
|---------|-------|-------|
| Template VM ID | 9010 | Proxmox template ID |
| Build static IP | 10.0.0.50 | Must be unused during build |
| Gateway | 10.0.0.1 | Network gateway |
| Proxmox host | 10.0.0.2 | Proxmox API endpoint |
| Proxmox node | pia-dev | Node name in cluster |
| SSH user | packer | Temporary — removed by cleanup.sh |
| SSH password | packer | Plain text for Packer, hashed in user-data |
| Disk | 60G raw on local-lvm | LVM layout, full disk |
| CPU | 4 cores, host type | host passthrough for NVIDIA |
| RAM | 16384 MB (16 GB) | Sized for Ollama workloads |
| Machine | q35 + SeaBIOS | PCIe support for GPU passthrough |
| ISO | ubuntu-24.04.4-live-server-amd64.iso | On local:iso storage |
| Boot wait | 10s | Time before boot command starts |
| Autoinstall prompt wait | 3 minutes | Hardware-dependent |
| SSH timeout | 30 minutes | Allows for full install + reboot |

## Autoinstall user-data Highlights

- **Password hash:** Generated with `mkpasswd --method=sha-512` for the string "packer"
- **Storage:** LVM with `sizing-policy: all` — uses entire disk
- **Late commands:** Creates passwordless sudo for packer user (provisioners need root)
- **Shutdown:** `reboot` — installer reboots into the installed system, then Packer connects
- **Updates:** `security` — don't prompt for update source selection during install

## Provisioner Execution Order

1. **Wait for cloud-init** — `cloud-init status --wait` ensures first-boot config is done
2. **System update + base packages** — apt update/upgrade, qemu-guest-agent, cloud-init, utilities
3. **Docker CE** — `setup-docker.sh` (Docker's official apt repo + compose plugin)
4. **NVIDIA** — `setup-nvidia.sh` (driver 550 + container toolkit from NVIDIA repo)
5. **Hardening** — `harden.sh` (SSH config, unattended-upgrades, UFW)
6. **Cleanup** — `cleanup.sh` (remove packer user, truncate machine-id, clear SSH keys, apt cache, logs)

---

## Troubleshooting Quick Reference

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| VM boots into installer loop | `boot = "order=ide2"` only | Change to `boot = "order=scsi0;ide2"` |
| "Continue with autoinstall?" hangs | Missing `yes<enter>` in boot_command | Add `<wait3m>` then `yes<enter>` |
| `yes` typed before prompt appears | Wait too short | Increase `<wait3m>` to `<wait4m>` or more |
| `yes` typed after Packer times out | Wait too long or prompt appeared early | Decrease wait or add multiple attempts |
| "Waiting for SSH..." indefinitely | Guest agent can't report IP | Add `ssh_host = "10.0.0.50"` + static IP in user-data |
| Packages fail during autoinstall | No internet during install phase | Move packages to Packer provisioners |
| GRUB editor text in wrong position | `<end>` key not working | Use `<esc>` entry method, not spacebar |
| `autoinstall` not detected | Parameter after `---` separator | Must be before `---` on kernel line |
| Boot command does nothing | `boot_wait` too short | Increase `boot_wait` to 10s or more |
| cidata ISO not detected | Wrong `cd_label` | Must be exactly `cidata` (lowercase) |
| Template won't clone cleanly | machine-id not truncated | Verify cleanup.sh runs `truncate -s 0 /etc/machine-id` |

---

## External References

- **TeKanAid article** — Ubuntu 24.04 autoinstall on Proxmox with Packer (tekanaid.com). Provided the proven boot command sequence and confirmed the confirmation prompt is by design.
- **Packer Proxmox builder docs** — https://developer.hashicorp.com/packer/integrations/hashicorp/proxmox
- **Ubuntu autoinstall reference** — https://canonical-subiquity.readthedocs-hosted.com/en/latest/reference/autoinstall-reference.html
- **Cloud-init datasources** — https://cloudinit.readthedocs.io/en/latest/reference/datasources/nocloud.html

---

## Revision History

| Date | Change |
|------|--------|
| 2026-02-18 | Initial working build — template 9010 created in 10m52s |
