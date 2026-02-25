# ============================================================================
# DC01 — Domain Controller (montanifarms.com)
# ============================================================================
# Phase 1: Clone template → connect to .90 → install AD DS → schedule
#          IP change + promotion → reboot
# Phase 2: Scheduled task runs on boot → sets IP to .200 → promotes DC
#          → reboots → DC is live
# Phase 3: Verification resource connects to .200 to confirm AD is up
# ============================================================================

# --- Clone from Packer template ---
resource "proxmox_virtual_environment_vm" "dc01" {
  name      = "dc01"
  node_name = var.proxmox_node
  vm_id     = 200

  description = "Domain Controller - ${var.ad_domain} - AD DS, DNS"
  tags        = ["windows", "domain-controller", "praxova"]

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

# Wait for clone OOBE to complete and WinRM to stabilize
resource "time_sleep" "dc01_boot_wait" {
  depends_on      = [proxmox_virtual_environment_vm.dc01]
  create_duration = "120s"
}

# --- Phase 1: Connect to .90 and set up AD promotion ---
resource "null_resource" "dc01_setup" {
  depends_on = [time_sleep.dc01_boot_wait]

  # Re-run if the VM is recreated
  triggers = {
    vm_id = proxmox_virtual_environment_vm.dc01.vm_id
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

  # Wait for WinRM to be ready after clone boots
  provisioner "remote-exec" {
    inline = [
      "powershell.exe -Command \"Write-Host 'WinRM connected to clone at ${var.template_ip}'\""
    ]
  }

  # Copy the promotion script
  provisioner "file" {
    source      = "${path.module}/scripts/setup-dc.ps1"
    destination = "C:\\setup\\setup-dc.ps1"
  }

  # Run Phase 1: install AD DS role + create scheduled task for Phase 2
  provisioner "remote-exec" {
    inline = [
      "powershell.exe -ExecutionPolicy Bypass -Command \"& { $params = @{ NewIP='${var.dc01_ip}'; Prefix='${var.network_prefix}'; Gateway='${var.network_gateway}'; Domain='${var.ad_domain}'; NetBIOS='${var.ad_netbios}'; DSRMPassword='${var.dsrm_password}' }; & C:\\setup\\setup-dc.ps1 @params }\""
    ]
  }
}

# Wait for rename reboot + DC promotion reboot + cleanup
resource "time_sleep" "dc01_promote_wait" {
  depends_on      = [null_resource.dc01_setup]
  create_duration = "300s"
}


# --- Phase 3: Verify DC is up on new IP ---
resource "null_resource" "dc01_verify" {
  depends_on = [time_sleep.dc01_promote_wait]

  triggers = {
    vm_id = proxmox_virtual_environment_vm.dc01.vm_id
  }

  connection {
    type     = "winrm"
    host     = var.dc01_ip
    user     = "Administrator"
    password = var.windows_admin_password
    https    = false
    insecure = true
    timeout  = "15m"
  }

  provisioner "remote-exec" {
    inline = [
      "powershell.exe -Command \"$attempts = 0; do { $attempts++; try { Get-ADDomainController -ErrorAction Stop | Out-Null; break } catch { Write-Host ('Waiting for AD DS... attempt ' + $attempts + '/30'); Start-Sleep -Seconds 20 } } while ($attempts -lt 30); Write-Host '=== DC01 Verification ==='; Write-Host ('Hostname: ' + $env:COMPUTERNAME); Write-Host ('IP: ' + (Get-NetIPAddress -AddressFamily IPv4 -InterfaceAlias 'Ethernet*' -ErrorAction SilentlyContinue).IPAddress); try { $dc = Get-ADDomainController; Write-Host ('Domain: ' + $dc.Domain); Write-Host ('DC: ' + $dc.Name); Write-Host '=== DC01 is healthy ===' } catch { Write-Host 'WARNING: AD not ready after 10 minutes'; exit 1 }\""
    ]
  }
  
}
