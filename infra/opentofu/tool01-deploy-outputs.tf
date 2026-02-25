# =============================================================================
# Tool Server Deployment Outputs
# =============================================================================
#
# Add these to your existing outputs.tf to include toolserver info in the
# environment summary that 'tofu apply' prints at the end.
#
# CONCEPT: Outputs serve two purposes:
#   1. Human-readable summary after apply (what you see on screen)
#   2. Machine-readable values other Tofu configs can reference
#      (if you ever use modules or workspaces)
# =============================================================================

output "toolserver_deployment" {
  description = "Tool server application deployment details"
  value = var.toolserver_deploy_enabled ? {
    status          = "deployed"
    msi_hash        = filemd5(var.toolserver_msi_path)
    service_account = var.toolserver_service_account
    https_port      = var.toolserver_https_port
    http_port       = var.toolserver_http_port
    domain          = var.toolserver_domain_name != "" ? var.toolserver_domain_name : "not configured"
    tls             = var.toolserver_cert_path != "" ? "enabled" : "disabled"
  } : {
    status          = "skipped"
    msi_hash        = "n/a"
    service_account = "n/a"
    https_port      = 0
    http_port       = 0
    domain          = "n/a"
    tls             = "n/a"
  }
}
