# =============================================================================
# Tool Server MSI Deployment
# =============================================================================
#
# This file handles copying and installing the PraxovaToolServer MSI onto
# TOOL01 after it's domain-joined. It's intentionally separate from tool01.tf
# so the VM lifecycle and the application lifecycle are independent.
#
# KEY CONCEPT: Separation of concerns
#   tool01.tf        = "make me a domain-joined Windows server"
#   tool01-deploy.tf = "install the application on that server"
#
# This means you can rebuild the app (tofu apply) without rebuilding the VM,
# OR rebuild the VM and the app redeploys automatically via triggers.
# =============================================================================


# =============================================================================
# LOCALS — Computed Values
# =============================================================================
#
# 'locals' is where you put logic that computes values from your variables.
# Think of it as a scratchpad where you assemble complex strings, do
# conditional logic, or derive values — keeping your resources clean.
#
# WHY NOT just inline this in the resource?
#   1. Readability — the msiexec command is complex; building it in pieces
#      is easier to understand and debug
#   2. Reuse — if you had multiple resources that needed the same computed
#      value, locals avoids duplication
#   3. Testability — you can output locals to verify them: tofu plan shows
#      computed values in the resource attributes
# =============================================================================

locals {
  toolserver_msi_path = var.toolserver_msi_path

  # ---------------------------------------------------------------------------
  # Build MSI property string dynamically
  # ---------------------------------------------------------------------------
  # This is the core pattern: conditionally append properties to the msiexec
  # command based on which variables are set. Empty string means "don't include
  # this property," and the MSI will use its compiled-in default.
  #
  # Each line uses a ternary: condition ? "value if true" : "value if false"
  # Then we join them together and trim extra spaces.
  #
  # This approach means:
  #   - Basic install:  msiexec /i ... /qn
  #   - gMSA install:   msiexec /i ... /qn SERVICE_ACCOUNT="MONTANIFARMS\svc$"
  #   - Full install:   msiexec /i ... /qn SERVICE_ACCOUNT="..." SERVICE_ACCOUNT_PASSWORD="..." CERT_PATH="..." ...
  # ---------------------------------------------------------------------------

  msi_properties = join(" ", compact([
    # compact() removes empty strings from the list, so properties that aren't
    # set simply disappear from the command. This is cleaner than having a
    # bunch of empty quotes in the msiexec line.

    var.toolserver_service_account != "LocalSystem" ? "SERVICE_ACCOUNT=\"${var.toolserver_service_account}\"" : "",

    var.toolserver_service_password != "" ? "SERVICE_ACCOUNT_PASSWORD=\"${var.toolserver_service_password}\"" : "",

    var.toolserver_cert_path != "" ? "CERT_PATH=\"${var.toolserver_cert_path}\"" : "",

    var.toolserver_cert_key_path != "" ? "CERT_KEY_PATH=\"${var.toolserver_cert_key_path}\"" : "",

    var.toolserver_domain_name != "" ? "DOMAIN_NAME=\"${var.toolserver_domain_name}\"" : "",

    # Ports: only include if non-default, to keep the command clean
    var.toolserver_https_port != 8443 ? "HTTPS_PORT=${var.toolserver_https_port}" : "",

    var.toolserver_http_port != 8080 ? "HTTP_PORT=${var.toolserver_http_port}" : "",
  ]))

  # The full msiexec command, assembled from the pieces above.
  # /qn  = quiet, no UI
  # /l*v = verbose logging (invaluable for debugging silent install failures)
  msi_install_cmd = "msiexec /i C:\\setup\\PraxovaToolServer.msi /qn /l*v C:\\setup\\toolserver-install.log ${local.msi_properties}"
}


# =============================================================================
# NULL_RESOURCE — The Deployment Action
# =============================================================================
#
# WHAT IS A null_resource?
#   It's a resource that doesn't create any infrastructure. It exists solely
#   to run provisioners (file copies, remote commands). Think of it as a
#   "run this script" resource.
#
# WHY null_resource INSTEAD OF a provisioner block inside the VM resource?
#   Provisioners inside a resource (like your proxmox_virtual_environment_vm)
#   only run when that resource is CREATED. They won't re-run when you change
#   the MSI. A null_resource with triggers gives you independent control over
#   when the deployment runs.
#
# HOW DO TRIGGERS WORK?
#   The 'triggers' map stores key-value pairs in Tofu's state. On each
#   'tofu apply', Tofu compares the current trigger values to what's in
#   state. If ANY value changed, Tofu destroys and recreates the resource,
#   which re-runs all provisioners.
#
#   This is the mechanism that makes "just push the new MSI" work:
#     - You run 'make build' → new MSI with different file hash
#     - You run 'tofu apply' → filemd5() returns new hash → trigger changed
#     - Tofu re-runs the file copy + install → new version deployed
#     - Total time: ~60 seconds instead of 8 minutes for a full VM rebuild
# =============================================================================

resource "null_resource" "tool01_deploy_toolserver" {
  # 'count' is the standard Tofu way to conditionally create a resource.
  # count = 1 means "create it", count = 0 means "skip it entirely".
  # This is controlled by the toolserver_deploy_enabled variable, which
  # lets you do infra-only iterations without touching the app layer.
  count = var.toolserver_deploy_enabled ? 1 : 0

  triggers = {
    # --- Trigger 1: MSI content changed ---
    # filemd5() computes the MD5 hash of the local file. If you rebuild
    # the MSI and even one byte changes, this hash changes, and Tofu
    # knows to redeploy.
    msi_hash = filemd5(local.toolserver_msi_path)

    # --- Trigger 2: VM was rebuilt ---
    # If you nuked and recreated TOOL01, the VM resource gets a new ID.
    # This ensures the app gets installed on the fresh VM automatically,
    # without you having to remember to do it separately.
    vm_id = proxmox_virtual_environment_vm.tool01.vm_id

    # --- Trigger 3: Install parameters changed ---
    # If you change the service account, ports, cert paths, etc., the
    # MSI needs to be reinstalled with the new properties. Hashing the
    # computed properties string catches any parameter change.
    install_params = md5(local.msi_properties)
  }

  # --- Dependency chain ---
  # depends_on is explicit ordering. This resource won't even START until
  # tool01_verify has succeeded, which means:
  #   VM exists → domain joined → verified → THEN deploy the app
  #
  # This is critical because your MSI needs service accounts and AD
  # resources that only exist after domain join.
  depends_on = [null_resource.tool01_verify]

  # --- Connection block ---
  # Tells Tofu HOW to talk to the target machine. This is used by both
  # the 'file' and 'remote-exec' provisioners below.
  connection {
    type     = "winrm"
    host     = var.tool01_ip
    user     = "Administrator"
    password = var.windows_admin_password
    https    = false
    insecure = true
    timeout  = "5m"
  }

  # --- Provisioner 1: Ensure setup directory exists ---
  provisioner "remote-exec" {
    inline = [
      "powershell.exe -Command \"New-Item -Path C:\\setup -ItemType Directory -Force | Out-Null\""
    ]
  }

  # --- Provisioner 2: Copy the MSI ---
  # The 'file' provisioner copies a local file to the remote machine over
  # the WinRM connection. This is how the artifact gets from your Linux
  # workstation onto the Windows server.
  provisioner "file" {
    source      = local.toolserver_msi_path
    destination = "C:\\setup\\PraxovaToolServer.msi"
  }

  # --- Provisioner 3: Install the MSI ---
  # remote-exec runs commands on the target. We use PowerShell for better
  # error handling than raw cmd.
  provisioner "remote-exec" {
    inline = [
      # Run the install command (assembled by locals above)
      "powershell.exe -Command \"Write-Host 'Installing PraxovaToolServer...'\"",
      "powershell.exe -Command \"Write-Host 'Command: ${replace(local.msi_install_cmd, "\"", "'")}'\"",
      "powershell.exe -Command \"$proc = Start-Process msiexec.exe -ArgumentList '${replace(local.msi_install_cmd, "msiexec ", "")}' -Wait -PassThru -NoNewWindow; if ($proc.ExitCode -ne 0) { Write-Host 'INSTALL FAILED - Exit code:' $proc.ExitCode; Get-Content C:\\setup\\toolserver-install.log -Tail 30; exit 1 } else { Write-Host 'Install succeeded.' }\"",
    ]
  }

  # --- Provisioner 4: Verify the service is running ---
  provisioner "remote-exec" {
    inline = [
      "powershell.exe -Command \"$svc = Get-Service -Name 'PraxovaToolServer' -ErrorAction SilentlyContinue; if (-not $svc) { Write-Host 'ERROR: Service not found after install'; exit 1 }; if ($svc.Status -ne 'Running') { Write-Host 'WARNING: Service installed but not running. Status:' $svc.Status; Start-Service 'PraxovaToolServer' -ErrorAction Stop; Start-Sleep -Seconds 5; Write-Host 'Service started.' } else { Write-Host 'Service is running.' }\"",
    ]
  }
}
