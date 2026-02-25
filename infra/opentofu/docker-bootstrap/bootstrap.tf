# ─────────────────────────────────────────────────────────────────
# Docker Bootstrap — Main Provisioner
# ─────────────────────────────────────────────────────────────────
#
# Bootstraps the Praxova stack after containers are deployed.
# Runs on every apply when deployment_tag changes.
#
# Sequence:
#   1. Write bootstrap config to remote (env vars for the script)
#   2. Upload bootstrap script
#   3. Execute: wait → pull model → create agent → create key → restart

locals {
  ssh_host = var.docker_host_ip
  ssh_user = var.docker_host_user
}

resource "null_resource" "docker_bootstrap" {
  # Re-run bootstrap whenever the deployment tag changes.
  # To force a re-run without changing the tag, use:
  #   tofu taint null_resource.docker_bootstrap
  triggers = {
    deployment_tag = var.deployment_tag
  }

  connection {
    type     = "ssh"
    host     = local.ssh_host
    user     = local.ssh_user
    password = var.docker_host_password
    timeout  = "10m"
  }

  # ── Step 1: Write config for bootstrap script ────────────────
  # Writes a sourceable env file with all Tofu variables.
  # Sensitive values go here — cleaned up at end of bootstrap.sh.
  provisioner "file" {
    content = <<-EOT
      PORTAL_HTTP_URL="${var.portal_http_url}"
      PORTAL_ADMIN_USER="${var.portal_admin_user}"
      PORTAL_ADMIN_PASS="${var.portal_admin_password}"
      AGENT_NAME="${var.agent_name}"
      AGENT_DISPLAY_NAME="${var.agent_display_name}"
      OLLAMA_MODEL="${var.ollama_model}"
      STACK_DIR="${var.remote_stack_dir}"
    EOT
    destination = "/tmp/praxova-bootstrap.env"
  }

  # ── Step 2: Upload bootstrap script ─────────────────────────
  provisioner "file" {
    source      = "${path.module}/scripts/bootstrap.sh"
    destination = "/tmp/praxova-bootstrap.sh"
  }

  # ── Step 3: Execute ─────────────────────────────────────────
  provisioner "remote-exec" {
    inline = [
      "chmod +x /tmp/praxova-bootstrap.sh",
      "bash /tmp/praxova-bootstrap.sh"
    ]
  }
}
