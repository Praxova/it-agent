# Proxmox Lab Layout - Praxova Infrastructure

> **Status**: Planning (2026-02-15)  
> **Hardware**: Threadripper 1950X / X399 / 96GB RAM / Titan XP (×1, second pending)  
> **Replaces**: ESXi 8 (free license) on same hardware

## Design Philosophy

**LXC containers for all Linux services, VMs only where a separate kernel is required (Windows).**

LXC containers share the Proxmox host kernel, boot in ~1 second, and use 10-20× less RAM
overhead than full VMs. A base Ubuntu 22.04 LXC idles at ~30-50MB RAM versus 800MB+ for
an equivalent VM. This lets us run 15-20 workloads on 96GB instead of 6-8.

---

## Resource Budget

```
Total Available:
  CPU:    16 cores / 32 threads (TR 1950X)
  RAM:    96 GB (128 GB installed, B1/B2 channel not working)
  GPU:    Titan XP 12GB VRAM (Pascal) — second card pending refurb
  Storage: [TBD — existing ESXi datastore drives]

Proxmox Host Reservation:
  CPU:    Shared (Proxmox is lightweight, no dedicated cores needed)
  RAM:    ~4 GB (kernel, ZFS ARC cache if using ZFS, Proxmox services)
  
Available for Workloads: ~92 GB RAM, 32 threads, 1-2 GPUs
```

---

## Workload Layout

### Tier 1: Praxova Core Services (LXC Containers)

These are the production services for the IT Agent system.

```
┌─────────────────────────────────────────────────────────────────────┐
│                    PROXMOX HOST (pve-lab)                          │
│                Threadripper 1950X / 96GB / Titan XP               │
│                                                                    │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │  LXC CONTAINERS                                             │   │
│  │                                                             │   │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐        │   │
│  │  │ CT 100      │  │ CT 101      │  │ CT 102      │        │   │
│  │  │ ollama      │  │ admin-portal│  │ it-agent    │        │   │
│  │  │             │  │             │  │             │        │   │
│  │  │ Ubuntu 22.04│  │ Ubuntu 22.04│  │ Ubuntu 22.04│        │   │
│  │  │ 8 cores     │  │ 2 cores     │  │ 2 cores     │        │   │
│  │  │ 16 GB RAM   │  │ 2 GB RAM    │  │ 2 GB RAM    │        │   │
│  │  │ Titan XP GPU│  │ .NET 8 RT   │  │ Python 3.10+│        │   │
│  │  │ 20 GB disk  │  │ 10 GB disk  │  │ 10 GB disk  │        │   │
│  │  │             │  │             │  │             │        │   │
│  │  │ Port: 11434 │  │ Port: 5000  │  │ (internal)  │        │   │
│  │  └─────────────┘  └─────────────┘  └─────────────┘        │   │
│  │                                                             │   │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐        │   │
│  │  │ CT 103      │  │ CT 104      │  │ CT 105      │        │   │
│  │  │ servicenow- │  │ monitoring  │  │ docker-dev  │        │   │
│  │  │ connector   │  │             │  │             │        │   │
│  │  │ Ubuntu 22.04│  │ Ubuntu 22.04│  │ Ubuntu 22.04│        │   │
│  │  │ 1 core      │  │ 1 core      │  │ 2 cores     │        │   │
│  │  │ 1 GB RAM    │  │ 1 GB RAM    │  │ 4 GB RAM    │        │   │
│  │  │ Python 3.10+│  │ Grafana     │  │ Docker CE   │        │   │
│  │  │ 5 GB disk   │  │ Prometheus  │  │ Compose     │        │   │
│  │  │             │  │ 20 GB disk  │  │ 30 GB disk  │        │   │
│  │  │ (internal)  │  │ Port: 3000  │  │ (dev/test)  │        │   │
│  │  └─────────────┘  └─────────────┘  └─────────────┘        │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                                                                    │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │  QEMU/KVM VIRTUAL MACHINES (full VMs — Windows requires)   │   │
│  │                                                             │   │
│  │  ┌──────────────────┐  ┌──────────────────┐               │   │
│  │  │ VM 200           │  │ VM 201           │               │   │
│  │  │ dc01             │  │ tool-server-win  │               │   │
│  │  │                  │  │                  │               │   │
│  │  │ Win Server 2022  │  │ Win Server 2022  │               │   │
│  │  │ 4 cores          │  │ 4 cores          │               │   │
│  │  │ 8 GB RAM         │  │ 8 GB RAM         │               │   │
│  │  │ 60 GB disk       │  │ 40 GB disk       │               │   │
│  │  │                  │  │                  │               │   │
│  │  │ AD DS, DNS, DHCP │  │ .NET Tool Server │               │   │
│  │  │ montanifarms.com │  │ AD-joined member │               │   │
│  │  └──────────────────┘  └──────────────────┘               │   │
│  └─────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
```

### Container/VM Detail

| ID | Name | Type | OS | CPU | RAM | Disk | Purpose |
|----|------|------|-----|-----|-----|------|---------|
| 100 | ollama | LXC | Ubuntu 22.04 | 8c | 16 GB | 20 GB | LLM inference, Titan XP GPU passthrough |
| 101 | admin-portal | LXC | Ubuntu 22.04 | 2c | 2 GB | 10 GB | .NET 8 Blazor Server, Admin Portal + API |
| 102 | it-agent | LXC | Ubuntu 22.04 | 2c | 2 GB | 10 GB | Python Griptape agent, ticket processing |
| 103 | sn-connector | LXC | Ubuntu 22.04 | 1c | 1 GB | 5 GB | ServiceNow queue connector (can merge w/ 102) |
| 104 | monitoring | LXC | Ubuntu 22.04 | 1c | 1 GB | 20 GB | Grafana + Prometheus + node_exporter |
| 105 | docker-dev | LXC | Ubuntu 22.04 | 2c | 4 GB | 30 GB | Docker-in-LXC for dev/test, image builds |
| 200 | dc01 | VM | WS 2022 | 4c | 8 GB | 60 GB | Domain Controller, DNS, DHCP |
| 201 | tool-server | VM | WS 2022 | 4c | 8 GB | 40 GB | .NET Tool Server, AD-joined |

### Resource Summary

```
LXC Containers:  17 cores, 26 GB RAM, 95 GB disk
Windows VMs:      8 cores, 16 GB RAM, 100 GB disk
─────────────────────────────────────────────────
Total Allocated: 25 cores, 42 GB RAM, 195 GB disk
Available:       32 threads, 92 GB RAM

Headroom:  ~50 GB RAM unallocated (overcommit on CPU is fine)
           Room for 10+ additional LXC containers
           Room for 1-2 additional Windows VMs if needed
           Second Titan XP adds another GPU-enabled LXC slot
```

---

## Network Architecture

```
Physical Network: 192.168.10.0/24

Proxmox Bridge: vmbr0 (bridged to physical NIC)
  │
  ├── pve-lab (host)      → 192.168.10.10 (or DHCP)
  │
  ├── CT 100 ollama       → 192.168.10.100  (static)
  ├── CT 101 admin-portal → 192.168.10.101  (static)
  ├── CT 102 it-agent     → 192.168.10.102  (static)
  ├── CT 103 sn-connector → 192.168.10.103  (static)
  ├── CT 104 monitoring   → 192.168.10.104  (static)
  ├── CT 105 docker-dev   → 192.168.10.105  (static)
  │
  ├── VM 200 dc01         → 192.168.10.200  (static, also DNS server)
  └── VM 201 tool-server  → 192.168.10.201  (static)

Key network flows:
  it-agent → ollama:11434       (LLM inference)
  it-agent → admin-portal:5000  (configuration API)
  it-agent → tool-server:8080   (AD operations via REST)
  it-agent → dev341394.service-now.com (ServiceNow PDI, external)
  admin-portal → dc01:636       (LDAPS for credential validation)
  tool-server → dc01:389/636    (AD operations, Kerberos)
  monitoring → all containers   (Prometheus scraping)
```

All containers and VMs are on the same flat L2 network (bridged through vmbr0).
No NAT, no port forwarding, no Docker bridge networking complexity.
Every service is directly reachable by IP from your workstation.

---

## GPU Passthrough Configuration (Ollama LXC)

The Titan XP is Pascal architecture — proven stable for LXC GPU passthrough.

### Host-Level Setup (run once on Proxmox host)

```bash
# 1. Install NVIDIA driver on Proxmox host
apt update
apt install pve-headers-$(uname -r)
# Download NVIDIA driver .run file for Titan XP (Pascal)
# Example: NVIDIA-Linux-x86_64-550.xx.xx.run
chmod +x NVIDIA-Linux-x86_64-*.run
./NVIDIA-Linux-x86_64-*.run --dkms

# 2. Verify GPU is visible
nvidia-smi

# 3. Load nvidia-uvm module at boot
echo "nvidia-uvm" >> /etc/modules-load.d/nvidia.conf

# 4. Create udev rules for device nodes
cat > /etc/udev/rules.d/70-nvidia.rules << 'EOF'
KERNEL=="nvidia", RUN+="/bin/bash -c '/usr/bin/nvidia-smi -L && /bin/chmod 0666 /dev/nvidia*'"
KERNEL=="nvidia_uvm", RUN+="/bin/bash -c '/usr/bin/nvidia-modprobe -c0 -u && /bin/chmod 0666 /dev/nvidia-uvm*'"
EOF
```

### LXC Container Config (CT 100 - ollama)

Add to `/etc/pve/lxc/100.conf`:
```
# GPU passthrough
lxc.cdev.allow: c 195:* rwm
lxc.cdev.allow: c 509:* rwm
lxc.mount.entry: /dev/nvidia0 dev/nvidia0 none bind,optional,create=file
lxc.mount.entry: /dev/nvidiactl dev/nvidiactl none bind,optional,create=file
lxc.mount.entry: /dev/nvidia-uvm dev/nvidia-uvm none bind,optional,create=file
lxc.mount.entry: /dev/nvidia-uvm-tools dev/nvidia-uvm-tools none bind,optional,create=file

# NVIDIA driver libraries (match host driver version exactly)
lxc.mount.entry: /usr/lib/x86_64-linux-gnu/libnvidia-ml.so.1 usr/lib/x86_64-linux-gnu/libnvidia-ml.so.1 none bind,optional,create=file
# ... (additional library mounts as needed, or bind-mount entire nvidia dir)
```

### Inside the Ollama LXC

```bash
# Install NVIDIA driver userspace (MUST match host version exactly)
# Or bind-mount the host libraries (simpler, shown above)

# Install Ollama
curl -fsSL https://ollama.com/install.sh | sh

# Verify GPU access
ollama run llama3.1 "Hello from LXC with Titan XP!"
```

### Second Titan XP (when ready)

When the second card has its fan installed:
- Add `/dev/nvidia1` mount entries to same LXC (multi-GPU inference)
- OR create a second GPU-enabled LXC for a different workload
- Pascal cards don't need MMIO parameter tuning like newer GPUs

---

## MCP Integration Points

This layout is designed to be fully manageable by Agora DevOps agents via MCP.

```
┌────────────────────────────────────────────────────────────┐
│                   Agora DevOps Agent                       │
│              (runs on workstation or CT 102)               │
└────────┬──────────────┬──────────────┬─────────────────────┘
         │              │              │
         ▼              ▼              ▼
  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐
  │ Proxmox MCP  │ │ OpenTofu MCP │ │  WinRM MCP   │
  │              │ │              │ │  (custom)     │
  │ mjrestivo16/ │ │ mjrestivo16/ │ │              │
  │ mcp-proxmox  │ │ mcp-opentofu │ │              │
  │   (35 tools) │ │              │ │              │
  │              │ │ bpg/proxmox  │ │              │
  │ • List CTs   │ │ provider     │ │ • PS remoting│
  │ • Start/stop │ │              │ │ • Domain join│
  │ • Snapshots  │ │ • tofu plan  │ │ • AD config  │
  │ • Backups    │ │ • tofu apply │ │ • gMSA setup │
  │ • Console    │ │ • tofu state │ │              │
  │ • Create VM  │ │              │ │              │
  │ • GPU status │ │              │ │              │
  └──────────────┘ └──────────────┘ └──────────────┘
         │              │              │
         ▼              ▼              ▼
  ┌──────────────────────────────────────────────────┐
  │            Proxmox VE API (port 8006)            │
  │            Full R/W — no license limits           │
  └──────────────────────────────────────────────────┘
```

### MCP Server Selection

| MCP Server | Purpose | Auth Method |
|-----------|---------|-------------|
| **mjrestivo16/mcp-proxmox** (Node.js) | Proxmox VM/LXC/storage management | API token |
| **mjrestivo16/mcp-opentofu** | IaC provisioning | Local CLI |
| **Custom WinRM MCP** (Python) | Windows Server configuration | Kerberos/NTLM |

### Proxmox API Token Setup

```bash
# On Proxmox host — create dedicated automation user + token
pveum user add devops@pve --comment "Agora DevOps Agent"
pveum aclmod / -user devops@pve -role PVEAdmin
pveum user token add devops@pve agora --privsep=0

# Output: token ID and secret — store these securely
# Use in MCP config:
#   PROXMOX_URL=https://192.168.1.10:8006
#   PROXMOX_USER=devops@pve
#   PROXMOX_TOKEN_ID=agora
#   PROXMOX_TOKEN_SECRET=<secret>
```

---

## Migration Path from ESXi

### Phase 1: Install Proxmox (30 minutes)
- Back up any critical VMs from ESXi (or note they can be recreated)
- Boot Proxmox VE 8.x installer from USB
- Install to existing boot drive (wipes ESXi)
- Configure network (static IP, bridge)
- Access web UI at https://<ip>:8006

### Phase 2: Create LXC Containers (1-2 hours)
- Download Ubuntu 22.04 container template
- Create CT 100 (ollama) with GPU passthrough
- Create CT 101 (admin-portal) with .NET 8
- Create CT 102 (it-agent) with Python
- Test inter-container networking

### Phase 3: Create Windows VMs (2-3 hours)
- Upload Windows Server 2022 ISO
- Create VM 200 (dc01), install WS2022, configure AD DS
- Run Setup-TestEnvironment.ps1 for Star Wars test accounts
- Create VM 201 (tool-server), domain join, deploy .NET tool server

### Phase 4: Validate Praxova Stack (1-2 hours)
- Start Ollama, pull llama3.1 model
- Start Admin Portal, configure against new IPs
- Start IT Agent, verify ticket classification
- End-to-end test: create ServiceNow ticket → agent processes → AD action

### Phase 5: MCP + DevOps Agent Integration (future sprint)
- Install Proxmox MCP server
- Configure OpenTofu with bpg/proxmox provider
- Template the LXC containers for reproducible provisioning
- Connect Agora DevOps agent

### Total Migration Effort: ~1 day (Phases 1-4)

---

## Template Strategy

Once the base containers are working, convert them to templates for rapid cloning:

```bash
# Example: template the admin-portal container
pct shutdown 101
# In Proxmox UI: right-click CT 101 → Convert to Template
# Now clone new instances instantly:
pct clone 101 106 --hostname admin-portal-staging
```

Templates to create:
- **ubuntu-base** — Base Ubuntu 22.04 with standard packages (curl, git, htop, etc.)
- **ubuntu-dotnet** — ubuntu-base + .NET 8 runtime
- **ubuntu-python** — ubuntu-base + Python 3.10+ venv setup
- **ubuntu-docker** — ubuntu-base + Docker CE + Compose (nesting enabled)
- **ubuntu-gpu** — ubuntu-base + NVIDIA driver userspace (must match host version)

These templates are also what OpenTofu/bpg provider would reference when
provisioning new containers via IaC.

---

## Comparison: Before vs After

| Metric | ESXi (Current) | Proxmox (Planned) |
|--------|----------------|-------------------|
| License cost | $0 (but API locked) | $0 (full API) |
| API access | Read-only | Full R/W |
| GPU passthrough | Supported (VFIO) | Supported (LXC device or VFIO) |
| Container support | None (VM only) | Native LXC + Docker-in-LXC |
| IaC provider | vSphere (license-gated) | bpg/proxmox (30+ releases/yr) |
| MCP servers | 1 (limited) | 5+ (35-55 tools each) |
| RAM per Linux service | ~800 MB (VM overhead) | ~50 MB (LXC overhead) |
| Boot time per service | 30-60 seconds | 1-3 seconds |
| Max workloads (96GB) | ~8-10 VMs | ~20+ (mixed LXC/VM) |
| Backup/snapshot | Requires paid license | Built-in, free |
| Web UI | ESXi Host Client | Proxmox Web UI (more features) |
| Community/ecosystem | Shrinking (Broadcom) | Growing rapidly |

---

## Storage Configuration

**Single 1TB NVMe** — use Proxmox installer defaults (LVM-thin).

- `local` (ext4): ISOs, container templates, backups (~50 GB)
- `local-lvm` (LVM-thin): VM disks, CT rootfs (~950 GB usable)
- Thin provisioning means containers/VMs only consume actual used space
- Snapshots supported natively via LVM-thin
- No ZFS (single drive = no redundancy benefit, wastes RAM on ARC cache)

```
Disk Budget (actual usage, not allocated):
  Proxmox OS + local:     ~10 GB
  CT 100 ollama models:   ~15 GB (llama3.1 8B = ~4.7GB, room for more)
  CT 101-105 containers:  ~20 GB total (thin provisioned)
  VM 200 dc01:            ~25 GB (WS2022 actual usage)
  VM 201 tool-server:     ~20 GB (WS2022 actual usage)
  ────────────────────────────────
  Estimated actual usage: ~90 GB
  Free space:             ~900 GB
```

---

## Open Questions

2. **RAM troubleshooting**: Worth reseating B1/B2 DIMMs during Proxmox install.
   If channel is dead, 96GB is still plenty for this layout (50GB headroom).

3. **Second Titan XP timeline**: When fan swap is done, add to CT 100 for
   multi-GPU inference or create dedicated CT for second GPU workload.

4. **Firewall**: Proxmox has a built-in firewall (iptables/nftables) manageable
   per-container and per-VM from the UI. Enable as needed for security.

5. **Backups**: Proxmox Backup Server (PBS) is a free companion product that
   handles incremental, deduplicated backups. Can run as an LXC container itself
   if you have separate backup storage.
