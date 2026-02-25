# ============================================================================
# Praxova Lab Infrastructure — OpenTofu + Proxmox
# ============================================================================
# Nuke and rebuild:  tofu destroy -auto-approve && tofu apply -auto-approve
# ============================================================================

terraform {
  required_version = ">= 1.11.0"

  required_providers {
    proxmox = {
      source  = "bpg/proxmox"
      version = "~> 0.74"
    }
  }
}

provider "proxmox" {
  endpoint = var.proxmox_url
  api_token = var.proxmox_api_token

  # Self-signed cert on fresh Proxmox install
  insecure = true

  ssh {
    agent = true
  }
}
