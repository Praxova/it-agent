# ============================================================================
# DC01 — Windows Server 2022 Domain Controller
# ============================================================================
# montanifarms.com domain, AD DS + DNS
# After first boot: manual Windows install, then run AD setup script
# ============================================================================

resource "proxmox_virtual_environment_vm" "dc01" {
  name      = "dc01"
  node_name = var.proxmox_node
  vm_id     = 200

  description = "Domain Controller - montanifarms.com - AD DS, DNS"
  tags        = ["windows", "domain-controller", "praxova"]

  # --- Boot / BIOS ---
  bios       = "seabios"
  boot_order = ["scsi0", "ide2"]

  # --- CPU ---
  cpu {
    cores   = 4
    sockets = 1
    type    = "host"
  }

  # --- Memory ---
  memory {
    dedicated = 8192
  }

  # --- OS Disk (60GB on LVM-thin) ---
  disk {
    datastore_id = "local-lvm"
    interface    = "scsi0"
    size         = 60
    file_format  = "raw"
  }

  # --- SCSI Controller ---
  scsi_hardware = "virtio-scsi-single"

  # --- Windows ISO (CD-ROM) ---
  cdrom {
    file_id   = var.windows_iso
    interface = "ide2"
  }

  # --- Network (E1000 for Windows out-of-box compatibility) ---
  network_device {
    bridge = var.network_bridge
    model  = "e1000"
  }

  # --- VGA ---
  vga {
    type   = "std"
    memory = 64
  }

  # --- Agent ---
  agent {
    enabled = false  # Enable after installing QEMU guest agent
  }

  # --- Lifecycle ---
  on_boot  = true
  started  = true

  # Don't destroy and recreate if only tags or description change
  lifecycle {
    ignore_changes = [
      description,
      tags,
    ]
  }
}
