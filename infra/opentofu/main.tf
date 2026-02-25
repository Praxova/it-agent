# ============================================================================
# Praxova Lab Infrastructure — OpenTofu + Proxmox
# ============================================================================
# Full rebuild:  tofu destroy -auto-approve && tofu apply -auto-approve
#
# Architecture:
#   Template 9000 (Packer Win2022) → dc01   (10.0.0.200) — AD DS + DNS
#   Template 9000 (Packer Win2022) → tool01 (10.0.0.201) — Domain member
#   LXC containers for Ollama, Admin Portal, IT Agent (TBD)
#
# Build order: dc01 → (verify AD) → tool01 → (verify join)
# Only one Windows clone builds at a time (they share .90 on first boot)
# ============================================================================

terraform {
  required_version = ">= 1.11.0"

  required_providers {
    proxmox = {
      source  = "bpg/proxmox"
      version = "~> 0.74"
    }
    time = {
  source  = "hashicorp/time"
  version = "~> 0.12"
}
  }
}

provider "proxmox" {
  endpoint  = var.proxmox_url
  api_token = var.proxmox_api_token

  # Self-signed cert on fresh Proxmox install
  insecure = true

  ssh {
    agent = true
  }
}


