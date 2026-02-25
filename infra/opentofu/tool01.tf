# ============================================================================
# TOOL01 — Member Server (montanifarms.com)
# ============================================================================
# Same two-phase approach as DC01.
# depends_on dc01_verify so we know AD is ready before attempting join.
# ============================================================================

# --- Clone from Packer template ---
resource "proxmox_virtual_environment_vm" "tool01" {
  depends_on = [null_resource.dc01_verify]

  name      = "tool01"
  node_name = var.proxmox_node
  vm_id     = 201

  description = "Tool Server - ${var.ad_domain} domain member"
  tags        = ["windows", "member-server", "praxova"]

  clone {
    vm_id = var.template_vm_id
    full  = true
  }

  # --- CPU ---
  cpu {
    cores   = 4
    sockets = 1
    type    = "host"
  }

  # --- Memory ---
  memory {
    dedicated = 8192
  }

  # --- Agent ---
  agent {
    enabled = false
  }

  on_boot = true
  started = true

  lifecycle {
    ignore_changes = [
      description,
      tags,
    ]
  }
}

resource "time_sleep" "tool01_boot_wait" {
  depends_on      = [proxmox_virtual_environment_vm.tool01]
  create_duration = "180s"
}

# --- Phase 1: Connect to .90 and set up domain join ---
resource "null_resource" "tool01_setup" {
  depends_on = [time_sleep.tool01_boot_wait]

  triggers = {
    vm_id = proxmox_virtual_environment_vm.tool01.vm_id
  }

  connection {
    type     = "winrm"
    host     = var.template_ip
    user     = "Administrator"
    password = var.windows_admin_password
    https    = false
    insecure = true
    timeout  = "10m"
  }

  # Wait for WinRM to be ready
  provisioner "remote-exec" {
    inline = [
      "powershell.exe -Command \"Write-Host 'WinRM connected to clone at ${var.template_ip}'\""
    ]
  }

  # Copy the domain join script
  provisioner "file" {
    source      = "${path.module}/scripts/setup-member.ps1"
    destination = "C:\\setup\\setup-member.ps1"
  }

  # Run Phase 1: create scheduled task for IP change + domain join
  provisioner "remote-exec" {
    inline = [
      "powershell.exe -ExecutionPolicy Bypass -Command \"& { $params = @{ NewIP='${var.tool01_ip}'; Prefix='${var.network_prefix}'; Gateway='${var.network_gateway}'; DNSIP='${var.dc01_ip}'; Domain='${var.ad_domain}'; AdminPassword='${var.windows_admin_password}' }; & C:\\setup\\setup-member.ps1 @params }\""
    ]
  }
}

resource "time_sleep" "tool01_join_wait" {
  depends_on      = [null_resource.tool01_setup]
  create_duration = "180s"
}

# --- Phase 3: Verify tool01 is domain-joined on new IP ---
resource "null_resource" "tool01_verify" {
  depends_on = [time_sleep.tool01_join_wait]

  triggers = {
    vm_id = proxmox_virtual_environment_vm.tool01.vm_id
  }

  connection {
    type     = "winrm"
    host     = var.tool01_ip
    user     = "Administrator"
    password = var.windows_admin_password
    https    = false
    insecure = true
    timeout  = "15m"
  }

  provisioner "remote-exec" {
    inline = [
      "powershell.exe -Command \"Write-Host '=== TOOL01 Verification ==='; Write-Host ('Hostname: ' + $env:COMPUTERNAME); Write-Host ('IP: ' + (Get-NetIPAddress -AddressFamily IPv4 -InterfaceAlias 'Ethernet*' -ErrorAction SilentlyContinue).IPAddress); Write-Host ('Domain: ' + (Get-WmiObject Win32_ComputerSystem).Domain); Write-Host '=== TOOL01 is healthy ==='\""
    ]
  }
  
}
