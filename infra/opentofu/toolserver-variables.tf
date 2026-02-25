# =============================================================================
# Tool Server Deployment Variables
# =============================================================================
#
# These variables map directly to the PraxovaToolServer.msi install properties.
# They all have sensible defaults so you can start simple and layer in complexity
# as your deployment matures.
#
# USAGE PATTERN:
#   - Basic install:     Just set toolserver_msi_path (everything else defaults)
#   - gMSA install:      Set toolserver_service_account = "MONTANIFARMS\\svc-toolserver$"
#   - Full domain + TLS: Set service account, password, cert paths, domain
#
# Override these in terraform.tfvars alongside your existing variables.
# =============================================================================

# --- MSI Artifact Location ---

variable "toolserver_msi_path" {
  description = "Local path to the PraxovaToolServer.msi artifact"
  type        = string
  default     = "/home/alton/Documents/lucid-it-agent/build/artifacts/PraxovaToolServer.msi"

  # 'validation' blocks let you fail fast with a clear message instead of
  # getting a cryptic error 10 minutes into a deploy. Always validate file
  # paths and critical inputs.
  validation {
    condition     = can(filemd5(var.toolserver_msi_path))
    error_message = "MSI file not found at the specified path. Run 'make build' first."
  }
}

# --- Service Identity ---
# These map to the MSI's SERVICE_ACCOUNT and SERVICE_ACCOUNT_PASSWORD properties.

variable "toolserver_service_account" {
  description = "Windows service identity. Use 'LocalSystem' for basic, 'DOMAIN\\user$' for gMSA, or 'DOMAIN\\user' for domain account."
  type        = string
  default     = "LocalSystem"

  # This validation demonstrates how to enforce business rules in your variables.
  # A gMSA account always ends with '$'. A domain account has a backslash.
  # LocalSystem is the Windows default. We accept all three patterns.
  validation {
    condition = (
      var.toolserver_service_account == "LocalSystem" ||
      can(regex("^[A-Za-z0-9]+\\\\[A-Za-z0-9._-]+\\$?$", var.toolserver_service_account))
    )
    error_message = "Service account must be 'LocalSystem' or in 'DOMAIN\\username' format (with optional '$' suffix for gMSA)."
  }
}

variable "toolserver_service_password" {
  description = "Password for domain service accounts. Leave empty for LocalSystem or gMSA (gMSA passwords are managed by AD automatically)."
  type        = string
  default     = ""
  sensitive   = true # <-- This is important! Marks the value so Tofu redacts it
                     #     from plan output, logs, and state display. The value
                     #     IS still stored in state, but won't show on screen.
                     #     For production, you'd use a secrets manager instead.
}

# --- TLS Certificate ---
# These map to the MSI's CERT_PATH and CERT_KEY_PATH properties.
# The paths are where the certs will live ON THE WINDOWS SERVER, not locally.

variable "toolserver_cert_path" {
  description = "Path to TLS certificate on the Windows server (e.g., C:\\certs\\toolserver.pfx). Empty = no TLS."
  type        = string
  default     = ""
}

variable "toolserver_cert_key_path" {
  description = "Path to TLS private key on the Windows server. Empty = no separate key file."
  type        = string
  default     = ""
}

# --- Network ---
# These map to the MSI's HTTPS_PORT and HTTP_PORT properties.

variable "toolserver_https_port" {
  description = "HTTPS listener port for the tool server"
  type        = number
  default     = 8443

  validation {
    condition     = var.toolserver_https_port > 0 && var.toolserver_https_port < 65536
    error_message = "Port must be between 1 and 65535."
  }
}

variable "toolserver_http_port" {
  description = "HTTP health check port for the tool server"
  type        = number
  default     = 8080

  validation {
    condition     = var.toolserver_http_port > 0 && var.toolserver_http_port < 65536
    error_message = "Port must be between 1 and 65535."
  }
}

# --- Domain ---

variable "toolserver_domain_name" {
  description = "AD domain name for the tool server. Empty = not set in MSI properties."
  type        = string
  default     = ""
}

# --- Behavior Flags ---

variable "toolserver_deploy_enabled" {
  description = "Set to false to skip tool server deployment (useful during infra-only iterations)"
  type        = bool
  default     = true
}
