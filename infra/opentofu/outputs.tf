# ============================================================================
# Outputs
# ============================================================================

output "dc01_vm_id" {
  description = "DC01 VM ID"
  value       = proxmox_virtual_environment_vm.dc01.vm_id
}

output "tool01_vm_id" {
  description = "Tool01 VM ID"
  value       = proxmox_virtual_environment_vm.tool01.vm_id
}

output "environment_summary" {
  description = "Lab environment summary"
  value       = <<-EOT
    
    ═══════════════════════════════════════════════════════════════
    Praxova Lab Environment — Deployment Complete
    ═══════════════════════════════════════════════════════════════
    
    Domain:        ${var.ad_domain}
    NetBIOS:       ${var.ad_netbios}
    
    DC01:          ${var.dc01_ip}  (VM ${proxmox_virtual_environment_vm.dc01.vm_id})
                   AD DS + DNS
    
    TOOL01:        ${var.tool01_ip}  (VM ${proxmox_virtual_environment_vm.tool01.vm_id})
                   Domain member
    
    Containers:    (TBD)
      Ollama:      ${var.ollama_ip}
      Admin Portal:${var.admin_portal_ip}
      IT Agent:    ${var.it_agent_ip}
    
    ═══════════════════════════════════════════════════════════════
    Credentials:   Administrator / (see terraform.tfvars)
    Proxmox:       ${var.proxmox_url}
    ═══════════════════════════════════════════════════════════════
  EOT
}
