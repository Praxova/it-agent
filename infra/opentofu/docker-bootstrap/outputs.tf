output "bootstrap_summary" {
  description = "Bootstrap run summary"
  value = <<-EOT

    ═══════════════════════════════════════════════════════════════
      Praxova Bootstrap Complete
    ═══════════════════════════════════════════════════════════════
      Docker Host : ${var.docker_host_user}@${var.docker_host_ip}
      Stack Dir   : ${var.remote_stack_dir}
      Agent       : ${var.agent_name}
      Ollama Model: ${var.ollama_model}
      Deploy Tag  : ${var.deployment_tag}

      The LUCID_API_KEY has been written to ${var.remote_stack_dir}/.env
      on the remote host and the agent has been restarted.

      To verify:
        ssh ${var.docker_host_user}@${var.docker_host_ip} \\
          "cd ${var.remote_stack_dir} && docker compose ps"

      Portal UI:
        http://${var.docker_host_ip}:5000
        https://${var.docker_host_ip}:5001
    ═══════════════════════════════════════════════════════════════
  EOT
}
