# ─────────────────────────────────────────────────────────────────
# Docker Bootstrap — Variables
# ─────────────────────────────────────────────────────────────────

# ── SSH Connection ──────────────────────────────────────────────
variable "docker_host_ip" {
  description = "IP address of the Ubuntu Docker host VM"
  type        = string
  default     = "10.0.0.51"
}

variable "docker_host_user" {
  description = "SSH username for the Docker host"
  type        = string
  default     = "packer"
}

variable "docker_host_password" {
  description = "SSH password for the Docker host"
  type        = string
  sensitive   = true
  default     = "packer"
}

# ── Portal Authentication ───────────────────────────────────────
variable "portal_admin_user" {
  description = "Admin portal username"
  type        = string
  default     = "admin"
}

variable "portal_admin_password" {
  description = "Admin portal password"
  type        = string
  sensitive   = true
}

# ── Bootstrap Targets ───────────────────────────────────────────
variable "agent_name" {
  description = "Name of the Praxova agent to create/bootstrap"
  type        = string
  default     = "test-agent"
}

variable "agent_display_name" {
  description = "Display name for the agent in the portal UI"
  type        = string
  default     = "Helpdesk Agent"
}

variable "ollama_model" {
  description = "Ollama model to pre-pull during bootstrap"
  type        = string
  default     = "llama3.1"
}

# ── Stack Paths ─────────────────────────────────────────────────
variable "remote_stack_dir" {
  description = "Directory on the remote host where the Docker stack lives"
  type        = string
  default     = "/opt/praxova"
}

variable "portal_http_url" {
  description = "HTTP URL for the portal, as seen from inside the Docker host"
  type        = string
  default     = "http://localhost:5000"
}

# ── Trigger ─────────────────────────────────────────────────────
variable "deployment_tag" {
  description = <<-EOD
    Release tag of the currently deployed stack.
    Changing this value triggers a re-run of the bootstrap provisioner.
    Set to the same tag you passed to deploy-containers.sh.
  EOD
  type    = string
  default = "dev"
}
