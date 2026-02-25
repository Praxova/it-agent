# ============================================================================
# Ubuntu Docker Host — Packer Template for Proxmox
# ============================================================================
# Builds a cloud-init-ready Ubuntu 24.04 LTS template with:
#   - Docker CE + Compose plugin
#   - NVIDIA driver (550) + Container Toolkit
#   - QEMU guest agent
#   - SSH hardening
#
# Autoinstall delivery: cidata CD-ROM (same pattern as Windows template)
#   - Packer creates a small ISO labeled "cidata" with user-data/meta-data
#   - Ubuntu's installer detects the cidata volume automatically
#   - Boot command only needs "autoinstall" keyword, no URLs
#
# Usage:
#   packer init .
#   packer validate -var-file="ubuntu-docker-host.pkrvars.hcl" .
#   packer build -var-file="ubuntu-docker-host.pkrvars.hcl" .
# ============================================================================

packer {
  required_plugins {
    proxmox = {
      version = ">= 1.2.2"
      source  = "github.com/hashicorp/proxmox"
    }
  }
}

# ============================================================================
# Variables
# ============================================================================

variable "proxmox_url" {
  type        = string
  description = "Proxmox API URL (e.g. https://10.0.0.2:8006/api2/json)"
}

variable "proxmox_token_id" {
  type        = string
  description = "Proxmox API token ID (e.g. packer@pam!packer-token)"
}

variable "proxmox_token_secret" {
  type        = string
  sensitive   = true
  description = "Proxmox API token secret"
}

variable "proxmox_node" {
  type        = string
  default     = "pve"
  description = "Proxmox node name"
}

variable "proxmox_skip_tls" {
  type        = bool
  default     = true
}

variable "vm_id" {
  type    = number
  default = 9010
}

variable "vm_name" {
  type    = string
  default = "ubuntu-docker-template"
}

variable "iso_file" {
  type    = string
  default = "local:iso/ubuntu-24.04.4-live-server-amd64.iso"
}

variable "vm_storage" {
  type    = string
  default = "local-lvm"
}

variable "disk_size" {
  type    = string
  default = "60G"
}

variable "cores" {
  type    = number
  default = 4
}

variable "memory" {
  type    = number
  default = 16384
}

variable "bridge" {
  type    = string
  default = "vmbr0"
}

variable "ssh_username" {
  type    = string
  default = "packer"
}

variable "ssh_password" {
  type      = string
  default   = "packer"
  sensitive = true
}

# ============================================================================
# Source — Proxmox ISO Builder
# ============================================================================

source "proxmox-iso" "ubuntu-docker" {
  # --- Proxmox connection ---
  proxmox_url              = var.proxmox_url
  username                 = var.proxmox_token_id
  token                    = var.proxmox_token_secret
  node                     = var.proxmox_node
  insecure_skip_tls_verify = var.proxmox_skip_tls

  # --- VM identity ---
  vm_id                = var.vm_id
  vm_name              = var.vm_name
  template_description = "Ubuntu 24.04 Docker host with NVIDIA toolkit. Packer ${timestamp()}"

  # --- Hardware ---
  os       = "l26"
  bios     = "seabios"
  cpu_type = "host"
  cores    = var.cores
  memory   = var.memory
  machine  = "q35"

  scsi_controller = "virtio-scsi-single"

  disks {
    storage_pool = var.vm_storage
    disk_size    = var.disk_size
    type         = "scsi"
    format       = "raw"
    discard      = true
    ssd          = true
  }

  network_adapters {
    model    = "virtio"
    bridge   = var.bridge
    firewall = false
  }

  # --- Cloud-init drive (Proxmox injects IP/hostname at clone time) ---
  cloud_init              = true
  cloud_init_storage_pool = var.vm_storage

  # --- Boot ISOs ---
  iso_file = var.iso_file

  # Autoinstall config as cidata CD-ROM
  # Matches proven TeKanAid approach for Ubuntu 24.04 on Proxmox
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

  # Boot order: try installed disk first, fall through to ISO for initial install
  # After install, scsi0 has the OS so it boots from disk automatically.
  # On first boot, scsi0 is empty so it falls through to ide2 (Ubuntu ISO).
  boot      = "order=scsi0;ide2"
  boot_wait = "10s"

  # --- Boot command ---
  # Proven approach from TeKanAid (tested with Ubuntu 24.04 on Proxmox).
  # Ubuntu 24.04 ALWAYS shows the "Continue with autoinstall?" prompt
  # even with autoinstall on the kernel line — this is by design.
  # The "yes<enter>" at the end answers it.
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

  # --- SSH communicator ---
  communicator           = "ssh"
  ssh_host               = "10.0.0.50"
  ssh_username           = var.ssh_username
  ssh_password           = var.ssh_password
  ssh_timeout            = "30m"
  ssh_handshake_attempts = 20
}

# ============================================================================
# Build
# ============================================================================

build {
  name    = "ubuntu-docker-host"
  sources = ["source.proxmox-iso.ubuntu-docker"]

  provisioner "shell" {
    inline = [
      "echo '=== Waiting for cloud-init ==='",
      "cloud-init status --wait",
      "echo '=== Cloud-init complete ==='"
    ]
  }

  provisioner "shell" {
    inline = [
      "echo '=== System update + base packages ==='",
      "sudo apt-get update -y",
      "sudo apt-get upgrade -y",
      "sudo apt-get install -y curl gnupg ca-certificates lsb-release software-properties-common",
      "sudo apt-get install -y qemu-guest-agent cloud-init wget git jq htop net-tools",
      "sudo systemctl enable qemu-guest-agent"
    ]
  }

  provisioner "shell" {
    script          = "scripts/setup-docker.sh"
    execute_command = "sudo bash '{{ .Path }}'"
  }

  provisioner "shell" {
    script          = "scripts/setup-nvidia.sh"
    execute_command = "sudo bash '{{ .Path }}'"
  }

  provisioner "shell" {
    script          = "scripts/harden.sh"
    execute_command = "sudo bash '{{ .Path }}'"
  }

  provisioner "shell" {
    script          = "scripts/cleanup.sh"
    execute_command = "sudo bash '{{ .Path }}'"
  }
}
