# ============================================================================
# Outputs — useful info after apply
# ============================================================================

output "dc01_vm_id" {
  description = "DC01 VM ID"
  value       = proxmox_virtual_environment_vm.dc01.vm_id
}

output "dc01_name" {
  description = "DC01 VM name"
  value       = proxmox_virtual_environment_vm.dc01.name
}

output "next_steps" {
  description = "Post-apply instructions"
  value       = <<-EOT
    
    ═══════════════════════════════════════════════════════════════
    VM created. Next steps:
    ═══════════════════════════════════════════════════════════════
    
    1. Open Proxmox console: https://10.0.0.2:8006
    2. Select VM 200 (dc01) → Console
    3. Complete Windows Server 2022 installation
       - Choose "Desktop Experience" edition
       - Set Administrator password
    4. After install, configure static IP:
       IP:      ${var.dc01_ip}/24
       Gateway: ${var.network_gateway}
       DNS:     ${var.network_dns}
    5. Install QEMU Guest Agent (optional but recommended)
    6. Run AD DS setup:
       Install-WindowsFeature -Name AD-Domain-Services -IncludeManagementTools
       Install-ADDSForest -DomainName "${var.ad_domain}" -DomainNetbiosName "${var.ad_netbios}" -InstallDns
    7. After reboot, run Setup-TestEnvironment.ps1 for Star Wars test accounts
    
    ═══════════════════════════════════════════════════════════════
  EOT
}
