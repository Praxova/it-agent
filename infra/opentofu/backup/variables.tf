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
# Variables — Network
# ============================================================================

variable "network_bridge" {
  description = "Proxmox network bridge"
  type        = string
  default     = "vmbr0"
}

variable "network_gateway" {
  description = "Default gateway for VMs/CTs"
  type        = string
  default     = "10.0.0.1"
}

variable "network_dns" {
  description = "DNS server (gateway initially, dc01 after AD setup)"
  type        = string
  default     = "10.0.0.1"
}

# ============================================================================
# Variables — Windows ISO
# ============================================================================

variable "windows_iso" {
  description = "Windows Server 2022 ISO filename on Proxmox local storage"
  type        = string
  # Update this to match your actual ISO filename
  default     = "local:iso/SERVER_EVAL_x64FRE_en-us.iso"
}

# ============================================================================
# Variables — VM IP Assignments
# ============================================================================
# Using 10.0.0.x to match actual Proxmox network

variable "dc01_ip" {
  description = "Domain Controller IP"
  type        = string
  default     = "10.0.0.200"
}

variable "tool_server_ip" {
  description = "Tool Server IP"
  type        = string
  default     = "10.0.0.201"
}

# ============================================================================
# Variables — LXC Container IPs
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
