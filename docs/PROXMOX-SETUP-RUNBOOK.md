# Proxmox Setup Runbook — Titan XP GPU + LXC Passthrough

> **Hardware**: Threadripper 1950X / X399 / 96GB / 1TB NVMe / Titan XP (Pascal GP102)
> **Target**: Proxmox VE 8.x with NVIDIA GPU accessible in LXC containers
> **Date**: 2026-02-16

---

## Phase 1: Proxmox Installation (20 minutes)

### 1.1 Create USB Installer
Download Proxmox VE 8.x ISO from https://www.proxmox.com/en/downloads
```bash
# From your workstation:
# Option A: Using dd
sudo dd if=proxmox-ve_8.*.iso of=/dev/sdX bs=1M status=progress

# Option B: Using Ventoy (if you have it)
# Just copy the ISO to the Ventoy USB drive
```

### 1.2 BIOS/UEFI Settings (X399 board)
Before booting the installer, verify these settings:
- **IOMMU**: Enabled (may be called AMD-Vi or SVM)
- **Secure Boot**: DISABLED (simplifies NVIDIA driver install significantly)
- **Boot Mode**: UEFI (not Legacy/CSM)
- **Above 4G Decoding**: Enabled (if available, helps with GPU passthrough)

### 1.3 Install Proxmox
- Boot from USB
- Select the 1TB NVMe as target disk
- Filesystem: **ext4 with LVM-thin** (default — just accept it)
- Country/timezone/keyboard as appropriate
- Set root password (strong, you'll use this for web UI)
- Management interface: select your NIC, set static IP
  - 10.0.0.2/24, gateway 1.0.0.1
  - Hostname: pia-lab.montanifarms.com
- Install, reboot, remove USB

### 1.4 Post-Install Web Access
From your workstation browser:
```
https://10.0.0.2:8006
Login: root / (your password)
```

### 1.5 Configure Package Repositories (no subscription)
SSH into pve-lab or use the web shell:
```bash
# Disable enterprise repo (requires subscription)
sed -i 's/^deb/#deb/' /etc/apt/sources.list.d/pve-enterprise.list

# Add no-subscription community repo
echo "deb http://download.proxmox.com/debian/pve bookworm pve-no-subscription" > \
  /etc/apt/sources.list.d/pve-no-subscription.list

# Disable Ceph enterprise repo if present
if [ -f /etc/apt/sources.list.d/ceph.list ]; then
  sed -i 's/^deb/#deb/' /etc/apt/sources.list.d/ceph.list
fi

# Update
apt update && apt dist-upgrade -y
```

---

## Phase 2: NVIDIA Driver Installation (30 minutes)

### CRITICAL: Titan XP (Pascal) Requirements
- **Must use PROPRIETARY kernel module** (not open-source)
- Pascal is NOT compatible with --open-kernel-module flag
- Recommended driver: **550.127.05** (LTS branch, confirmed Pascal support)
- Alternative: 570.x branch also works, but 550.x is more battle-tested

### 2.1 Blacklist Nouveau Driver
```bash
cat > /etc/modprobe.d/blacklist-nouveau.conf << 'EOF'
blacklist nouveau
options nouveau modeset=0
EOF

update-initramfs -u -k all
reboot
```

### 2.2 Verify Nouveau is Gone and GPU is Visible
```bash
# Should return nothing:
lsmod | grep nouveau

# Should show NVIDIA Titan XP:
lspci | grep -i nvidia
# Expected output like:
# XX:00.0 VGA compatible controller: NVIDIA Corporation GP102 [TITAN Xp] (rev a1)
# XX:00.1 Audio device: NVIDIA Corporation GP102 HDMI Audio Controller (rev a1)


# Note the bus ID (XX:00.0) — you'll need it later
```

### 2.3 Install Build Dependencies
```bash
apt update
apt install -y build-essential dkms pve-headers-$(uname -r)

# Verify headers installed:
ls /usr/src/linux-headers-$(uname -r)
```

### 2.4 Download and Install NVIDIA Driver
```bash
# Download 550.127.05 (LTS branch, proven with Pascal)
cd /tmp
wget https://us.download.nvidia.com/XFree86/Linux-x86_64/550.127.05/NVIDIA-Linux-x86_64-550.127.05.run
chmod +x NVIDIA-Linux-x86_64-550.127.05.run

# Install — NOTE: Do NOT use --open-kernel-module (Pascal requires proprietary)
./NVIDIA-Linux-x86_64-550.127.05.run --dkms

# During install:
#   - "Install NVIDIA's 32-bit compatibility libraries?" → Yes
#   - "Would you like to run nvidia-xconfig?" → No (headless server)
#   - If asked about open kernel module → Select PROPRIETARY (not open)
```

### 2.5 Verify Installation
```bash
nvidia-smi

# Expected output:
# +-------------------------------------------------------------------------+
# | NVIDIA-SMI 550.127.05   Driver Version: 550.127.05   CUDA Version: 12.4|
# |-----------------------------------------+--------------------------------+
# | GPU  Name         Persistence-M         |
# | Fan  Temp   Perf  Pwr:Usage/Cap         | Memory-Usage   | GPU-Util     |
# |   0  TITAN Xp     Off                   |
# |  23%  30C   P8    10W / 250W            |   0MiB / 12288MiB | 0%        |
# +-------------------------------------------------------------------------+
```

### 2.6 Configure NVIDIA Modules to Load at Boot
```bash
# Ensure modules load on boot
cat > /etc/modules-load.d/nvidia.conf << 'EOF'
nvidia
nvidia_uvm
EOF

# Create udev rules for device nodes
cat > /etc/udev/rules.d/70-nvidia.rules << 'EOF'
KERNEL=="nvidia", RUN+="/bin/bash -c '/usr/bin/nvidia-smi -L && /bin/chmod 0666 /dev/nvidia*'"
KERNEL=="nvidia_uvm", RUN+="/bin/bash -c '/usr/bin/nvidia-modprobe -c0 -u && /bin/chmod 0666 /dev/nvidia-uvm*'"
EOF

# Apply udev rules
udevadm control --reload-rules
udevadm trigger

# Verify device nodes exist
ls -la /dev/nvidia*
# Should show: nvidia0, nvidiactl, nvidia-uvm, nvidia-uvm-tools
```

### 2.7 Enable Persistence Mode (recommended for servers)
```bash
nvidia-smi -pm 1
# Makes the driver stay loaded even when no processes are using the GPU
# Faster first-use response time, avoids cold-start latency

# Make it persistent across reboots
cat > /etc/systemd/system/nvidia-persistenced.service << 'EOF'
[Unit]
Description=NVIDIA Persistence Daemon
After=syslog.target

[Service]
Type=forking
ExecStart=/usr/bin/nvidia-persistenced --user root
ExecStopPost=/bin/rm -rf /var/run/nvidia-persistenced

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable nvidia-persistenced
systemctl start nvidia-persistenced
```

### 2.8 Reboot and Final Verification
```bash
reboot

# After reboot:
nvidia-smi                    # Should show Titan XP
ls -la /dev/nvidia*           # Device nodes present
lsmod | grep nvidia           # nvidia, nvidia_uvm modules loaded
```

---

## Phase 3: Create Ollama LXC with GPU Passthrough (15 minutes)

### 3.1 Download Container Template
In Proxmox Web UI:
- Navigate to: pve-lab → local → CT Templates → Templates
- Download: **Ubuntu 22.04 Standard** (ubuntu-22.04-standard_22.04-1_amd64.tar.zst)

Or via CLI:
```bash
pveam update
pveam download local ubuntu-22.04-standard_22.04-1_amd64.tar.zst
```

### 3.2 Create LXC Container (CT 100 — ollama)
```bash
pct create 100 local:vztmpl/ubuntu-22.04-standard_22.04-1_amd64.tar.zst \
  --hostname ollama \
  --memory 16384 \
  --swap 2048 \
  --cores 8 \
  --rootfs local-lvm:20 \
  --net0 name=eth0,bridge=vmbr0,ip=192.168.1.100/24,gw=192.168.1.1 \
  --nameserver 192.168.1.1 \
  --features nesting=1 \
  --unprivileged 0 \
  --password <set-a-password>
```

**Note**: `--unprivileged 0` creates a PRIVILEGED container. This is required for
direct GPU device access. For production, consider using unprivileged with more
granular device allow rules, but privileged is simpler and fine for a lab.

### 3.3 Add GPU Passthrough to LXC Config
```bash
cat >> /etc/pve/lxc/100.conf << 'EOF'

# NVIDIA Titan XP GPU Passthrough
lxc.cdev.allow: c 195:* rwm
lxc.cdev.allow: c 509:* rwm
lxc.mount.entry: /dev/nvidia0 dev/nvidia0 none bind,optional,create=file
lxc.mount.entry: /dev/nvidiactl dev/nvidiactl none bind,optional,create=file
lxc.mount.entry: /dev/nvidia-uvm dev/nvidia-uvm none bind,optional,create=file
lxc.mount.entry: /dev/nvidia-uvm-tools dev/nvidia-uvm-tools none bind,optional,create=file
EOF
```

### 3.4 Start Container and Install NVIDIA Userspace
```bash
pct start 100
pct enter 100

# Inside the container:
apt update && apt upgrade -y
apt install -y curl wget

# Install NVIDIA driver userspace ONLY (no kernel module — host handles that)
cd /tmp
wget https://us.download.nvidia.com/XFree86/Linux-x86_64/550.127.05/NVIDIA-Linux-x86_64-550.127.05.run
chmod +x NVIDIA-Linux-x86_64-550.127.05.run

# CRITICAL: --no-kernel-module flag — the host already has the kernel module
./NVIDIA-Linux-x86_64-550.127.05.run --no-kernel-module

# Verify GPU is accessible from inside the container:
nvidia-smi
# Should show the Titan XP with 12GB VRAM — same output as host
```

### 3.5 Install Ollama
```bash
# Still inside CT 100:
curl -fsSL https://ollama.com/install.sh | sh

# Configure Ollama to listen on all interfaces (so other containers can reach it)
mkdir -p /etc/systemd/system/ollama.service.d
cat > /etc/systemd/system/ollama.service.d/override.conf << 'EOF'
[Service]
Environment="OLLAMA_HOST=0.0.0.0:11434"
EOF

systemctl daemon-reload
systemctl restart ollama

# Pull a model and test:
ollama pull llama3.1
ollama run llama3.1 "Hello from Proxmox LXC with Titan XP!"

# Verify it's using GPU:
nvidia-smi
# Should show ollama process using VRAM
```

### 3.6 Test from Host
```bash
# From the Proxmox host (or any machine on the network):
curl http://192.168.1.100:11434/api/tags
# Should return JSON with llama3.1 model info
```

---

## Phase 4: Create Remaining LXC Containers (30 minutes)

### 4.1 Admin Portal (CT 101)
```bash
pct create 101 local:vztmpl/ubuntu-22.04-standard_22.04-1_amd64.tar.zst \
  --hostname admin-portal \
  --memory 2048 \
  --swap 512 \
  --cores 2 \
  --rootfs local-lvm:10 \
  --net0 name=eth0,bridge=vmbr0,ip=192.168.1.101/24,gw=192.168.1.1 \
  --nameserver 192.168.1.1 \
  --features nesting=1 \
  --unprivileged 1

pct start 101
pct enter 101

# Install .NET 8 Runtime
apt update && apt install -y wget apt-transport-https
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb
apt update
apt install -y dotnet-sdk-8.0    # SDK for building; use dotnet-runtime-8.0 for runtime-only

dotnet --version  # Verify: should show 8.0.x
```

### 4.2 IT Agent (CT 102)
```bash
pct create 102 local:vztmpl/ubuntu-22.04-standard_22.04-1_amd64.tar.zst \
  --hostname it-agent \
  --memory 2048 \
  --swap 512 \
  --cores 2 \
  --rootfs local-lvm:10 \
  --net0 name=eth0,bridge=vmbr0,ip=192.168.1.102/24,gw=192.168.1.1 \
  --nameserver 192.168.1.1 \
  --features nesting=1 \
  --unprivileged 1

pct start 102
pct enter 102

# Install Python
apt update && apt install -y python3 python3-pip python3-venv git curl
python3 --version  # Verify: should show 3.10+
```

### 4.3 Monitoring (CT 104) — Optional, do later
```bash
pct create 104 local:vztmpl/ubuntu-22.04-standard_22.04-1_amd64.tar.zst \
  --hostname monitoring \
  --memory 1024 \
  --swap 256 \
  --cores 1 \
  --rootfs local-lvm:20 \
  --net0 name=eth0,bridge=vmbr0,ip=192.168.1.104/24,gw=192.168.1.1 \
  --nameserver 192.168.1.1 \
  --features nesting=1 \
  --unprivileged 1
```

### 4.4 Docker Dev (CT 105) — Optional, do later
```bash
pct create 105 local:vztmpl/ubuntu-22.04-standard_22.04-1_amd64.tar.zst \
  --hostname docker-dev \
  --memory 4096 \
  --swap 1024 \
  --cores 2 \
  --rootfs local-lvm:30 \
  --net0 name=eth0,bridge=vmbr0,ip=192.168.1.105/24,gw=192.168.1.1 \
  --nameserver 192.168.1.1 \
  --features nesting=1,keyctl=1 \
  --unprivileged 1

# Note: nesting=1 and keyctl=1 required for Docker-in-LXC
# After starting, install Docker inside:
# curl -fsSL https://get.docker.com | sh
```

---

## Phase 5: Windows VMs (2-3 hours)

### 5.1 Upload Windows Server 2022 ISO
Upload via Proxmox UI: pve-lab → local → ISO Images → Upload
Or via CLI:
```bash
# If ISO is on your workstation, SCP it:
scp /path/to/windows_server_2022.iso root@192.168.1.10:/var/lib/vz/template/iso/
```

### 5.2 Download VirtIO Drivers ISO
```bash
cd /var/lib/vz/template/iso/
wget https://fedorapeople.org/groups/virt/virtio-win/direct-downloads/stable-virtio/virtio-win.iso
```

### 5.3 Create Domain Controller VM (VM 200)
In Proxmox UI → Create VM:
- **General**: VMID 200, Name: dc01
- **OS**: Windows Server 2022 ISO, Type: Microsoft Windows, Version: 11/2022
- **System**: BIOS: OVMF (UEFI), EFI Storage: local-lvm, TPM: none (or add if needed)
- **Disk**: 60GB on local-lvm, VirtIO Block (fastest), Write Back cache
- **CPU**: 4 cores, Type: host
- **Memory**: 8192 MB
- **Network**: vmbr0, VirtIO (paravirtualized)
- **Add second CD/DVD**: VirtIO drivers ISO

During Windows install:
- Load VirtIO SCSI driver when disk isn't visible (browse virtio ISO → vioscsi → w11 → amd64)
- After install, install all VirtIO drivers from the ISO (run virtio-win-guest-tools.exe)
- Set static IP: 192.168.1.200/24, gateway 192.168.1.1
- Install AD DS, DNS roles
- Promote to DC: montanifarms.com domain
- Run Setup-TestEnvironment.ps1 for Star Wars test accounts

### 5.4 Create Tool Server VM (VM 201)
Same process as DC, but:
- VMID: 201, Name: tool-server
- 40GB disk, 8GB RAM, 4 cores
- Static IP: 192.168.1.201/24
- Domain join to montanifarms.com
- Deploy .NET Tool Server

---

## Phase 6: Validation Checklist

After all containers and VMs are up:

```
[ ] Proxmox web UI accessible at https://192.168.1.10:8006
[ ] nvidia-smi works on host
[ ] CT 100 (ollama): nvidia-smi works inside container
[ ] CT 100 (ollama): ollama serve running, model loaded
[ ] CT 100 (ollama): curl http://192.168.1.100:11434/api/tags returns model list
[ ] CT 101 (admin-portal): dotnet --version returns 8.0.x
[ ] CT 102 (it-agent): python3 --version returns 3.10+
[ ] VM 200 (dc01): AD DS running, montanifarms.com domain functional
[ ] VM 200 (dc01): Star Wars test accounts created
[ ] VM 201 (tool-server): Domain joined, .NET tool server responding
[ ] Cross-container: CT 102 can reach CT 100 on port 11434
[ ] Cross-container: CT 102 can reach CT 101 on port 5000
[ ] Cross-container: CT 102 can reach VM 201 on port 8080
[ ] From workstation: all IPs reachable
```

---

## Second Titan XP (When Ready)

When the water block is swapped for the fan:

1. Install second card in available PCIe slot
2. Reboot Proxmox host
3. Verify both GPUs visible: `nvidia-smi` should show GPU 0 and GPU 1
4. New device node `/dev/nvidia1` will appear automatically

**Option A: Both GPUs to Ollama (multi-GPU inference)**
Add to `/etc/pve/lxc/100.conf`:
```
lxc.mount.entry: /dev/nvidia1 dev/nvidia1 none bind,optional,create=file
```
Ollama will automatically detect both GPUs (24GB total VRAM).

**Option B: Second GPU to separate container**
Create CT 106 with its own GPU mount pointing to `/dev/nvidia1`.
Useful for: second Ollama instance, CUDA dev environment, or other GPU workload.

---

## Troubleshooting

### nvidia-smi fails on host
```bash
# Check if module loaded:
lsmod | grep nvidia
# If empty, try loading manually:
modprobe nvidia
# Check dmesg for errors:
dmesg | tail -50
```

### nvidia-smi fails inside LXC but works on host
```bash
# Verify device nodes exist in container:
ls -la /dev/nvidia*
# If missing, check /etc/pve/lxc/100.conf for mount entries
# Ensure container is PRIVILEGED (unprivileged=0 for GPU access)

# Verify driver version match:
# Host: nvidia-smi → note Driver Version
# Container: must have same version installed with --no-kernel-module
```

### Container can't reach other containers
```bash
# From inside container, test:
ping 192.168.1.101
# If fails, check bridge config:
# On host: cat /etc/network/interfaces (vmbr0 should be bridged to physical NIC)
# In container: ip addr show (should have IP on eth0)
```

### GPU not in its own IOMMU group (for future VM passthrough)
```bash
# Check IOMMU groups:
for d in /sys/kernel/iommu_groups/*/devices/*; do
  n=${d#*/iommu_groups/*}; n=${n%%/*}
  printf 'IOMMU Group %s ' "$n"
  lspci -nns "${d##*/}"
done | grep -i nvidia
# X399/Threadripper usually puts GPUs in clean groups — should be fine
```
