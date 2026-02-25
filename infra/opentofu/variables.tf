# ============================================================================
# Variables — Proxmox Connection
# ============================================================================

variable "proxmox_url" {
  description = "Proxmox VE API URL"
  type        = string
  default     = "https://10.0.0.2:8006"
}

variable "proxmox_api_token" {
  description = "Proxmox API token in format: user@realm!tokenid=secret"
  type        = string
  sensitive   = true
}

variable "proxmox_node" {
  description = "Proxmox node name"
  type        = string
  default     = "pia-dev"
}

# ============================================================================
# Variables — Packer Template
# ============================================================================

variable "template_vm_id" {
  description = "VM ID of the Packer-built Server 2022 template"
  type        = number
  default     = 9000
}

variable "template_ip" {
  description = "IP the template boots with (used for initial WinRM connection)"
  type        = string
  default     = "10.0.0.90"
}

# ============================================================================
# Variables — Windows Credentials
# ============================================================================

variable "windows_admin_password" {
  description = "Administrator password (must match what Packer baked into the template)"
  type        = string
  sensitive   = true
  default     = "P@ssw0rd123!"
}

variable "dsrm_password" {
  description = "Directory Services Restore Mode password for AD DS"
  type        = string
  sensitive   = true
  default     = "P@ssw0rd123!"
}

# ============================================================================
# Variables — Network
# ============================================================================

variable "network_bridge" {
  description = "Proxmox network bridge"
  type        = string
  default     = "vmbr0"
}

variable "network_gateway" {
  description = "Default gateway for VMs"
  type        = string
  default     = "10.0.0.1"
}

variable "network_prefix" {
  description = "Network prefix length (e.g. 24 for /24)"
  type        = number
  default     = 24
}

# ============================================================================
# Variables — VM IP Assignments
# ============================================================================

variable "dc01_ip" {
  description = "Domain Controller IP"
  type        = string
  default     = "10.0.0.200"
}

variable "tool01_ip" {
  description = "Tool Server IP"
  type        = string
  default     = "10.0.0.201"
}

# ============================================================================
# Variables — AD Domain
# ============================================================================

variable "ad_domain" {
  description = "Active Directory domain name"
  type        = string
  default     = "montanifarms.com"
}

variable "ad_netbios" {
  description = "Active Directory NetBIOS name"
  type        = string
  default     = "MONTANIFARMS"
}

# ============================================================================
# Variables — LXC Container IPs (for future use)
# ============================================================================

variable "ollama_ip" {
  description = "Ollama LLM server IP"
  type        = string
  default     = "10.0.0.100"
}

variable "admin_portal_ip" {
  description = "Admin Portal IP"
  type        = string
  default     = "10.0.0.101"
}

variable "it_agent_ip" {
  description = "IT Agent IP"
  type        = string
  default     = "10.0.0.102"
}
