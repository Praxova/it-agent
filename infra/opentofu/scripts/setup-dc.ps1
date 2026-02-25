# ============================================================================
# setup-dc.ps1 — Domain Controller Setup (Three-Phase)
# ============================================================================
# Phase 1 (runs via WinRM from Tofu):
#   - Install AD DS role
#   - Write Phase 2a, 2b, and cleanup scripts to disk
#   - Register Phase 2a scheduled task
#   - Reboot
#
# Phase 2a (scheduled task on first boot):
#   - Change IP address
#   - Rename computer to DC01
#   - Register Phase 2b scheduled task
#   - Reboot (required for rename to take effect)
#
# Phase 2b (scheduled task on second boot):
#   - Promote to Domain Controller
#   - Reboot (automatic after DC promotion)
#
# Cleanup (scheduled task on third boot):
#   - Wait for AD DS to fully start
#   - Disable firewall (DC promotion re-enables it)
#   - Remove all scheduled tasks
# ============================================================================

param(
    [Parameter(Mandatory)] [string] $NewIP,
    [Parameter(Mandatory)] [string] $Prefix,
    [Parameter(Mandatory)] [string] $Gateway,
    [Parameter(Mandatory)] [string] $Domain,
    [Parameter(Mandatory)] [string] $NetBIOS,
    [Parameter(Mandatory)] [string] $DSRMPassword
)

$ErrorActionPreference = "Stop"
$setupDir = "C:\setup"

Write-Host "=== DC01 Phase 1: Preparing Domain Controller ==="

# --- Install AD DS Role ---
Write-Host "Installing AD-Domain-Services role..."
Install-WindowsFeature -Name AD-Domain-Services -IncludeManagementTools -ErrorAction Stop
Write-Host "AD DS role installed."

# ============================================================================
# Phase 2a Script — Change IP + Rename
# ============================================================================

Write-Host "Creating Phase 2a script (IP + Rename)..."

$phase2aScript = @"

`$ErrorActionPreference = "Stop"
Start-Transcript -Path "C:\setup\phase2a.log"

Write-Host "=== DC01 Phase 2a: Setting IP and Renaming ==="

# --- Change IP Address ---
Write-Host "Setting IP to $NewIP/$Prefix..."
`$adapter = Get-NetAdapter | Where-Object { `$_.Status -eq 'Up' -and `$_.InterfaceDescription -notmatch 'Loopback' } | Select-Object -First 1

`$adapter | Remove-NetIPAddress -Confirm:`$false -ErrorAction SilentlyContinue
`$adapter | Remove-NetRoute -Confirm:`$false -ErrorAction SilentlyContinue

New-NetIPAddress -InterfaceIndex `$adapter.ifIndex -IPAddress "$NewIP" -PrefixLength $Prefix -DefaultGateway "$Gateway" -ErrorAction Stop
Set-DnsClientServerAddress -InterfaceIndex `$adapter.ifIndex -ServerAddresses @("127.0.0.1", "$Gateway")

Write-Host "IP configured."

# --- Rename Computer ---
Write-Host "Renaming computer to DC01..."
Rename-Computer -NewName "DC01" -Force

# --- Register Phase 2b task for next boot ---
Write-Host "Registering Phase 2b scheduled task..."

`$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-ExecutionPolicy Bypass -File C:\setup\phase2b-promote.ps1"
`$trigger = New-ScheduledTaskTrigger -AtStartup
`$trigger.Delay = "PT30S"
`$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -RunLevel Highest

Unregister-ScheduledTask -TaskName "DC-Phase2a" -Confirm:`$false -ErrorAction SilentlyContinue
Register-ScheduledTask -TaskName "DC-Phase2b" -Action `$action -Trigger `$trigger -Principal `$principal -Description "Phase 2b: Promote to Domain Controller"

Write-Host "=== Phase 2a complete. Rebooting for rename... ==="
Stop-Transcript

Restart-Computer -Force

"@

$phase2aScript | Out-File -FilePath "$setupDir\phase2a-rename.ps1" -Encoding UTF8

# ============================================================================
# Phase 2b Script — Promote to DC
# ============================================================================

Write-Host "Creating Phase 2b script (DC Promotion)..."

$phase2bScript = @"

`$ErrorActionPreference = "Stop"
Start-Transcript -Path "C:\setup\phase2b.log"

Write-Host "=== DC01 Phase 2b: Promoting to Domain Controller ==="
Write-Host "Hostname: `$env:COMPUTERNAME"

# --- Promote to Domain Controller ---
Write-Host "Promoting to Domain Controller for $Domain..."

`$dsrmSecure = ConvertTo-SecureString "$DSRMPassword" -AsPlainText -Force

Install-ADDSForest ``
    -DomainName "$Domain" ``
    -DomainNetbiosName "$NetBIOS" ``
    -SafeModeAdministratorPassword `$dsrmSecure ``
    -InstallDns ``
    -CreateDnsDelegation:`$false ``
    -NoRebootOnCompletion:`$false ``
    -Force ``
    -ErrorAction Stop

# Note: Install-ADDSForest triggers an automatic reboot

"@

$phase2bScript | Out-File -FilePath "$setupDir\phase2b-promote.ps1" -Encoding UTF8

# ============================================================================
# Cleanup Script — runs after DC promotion reboot
# ============================================================================

Write-Host "Creating cleanup script..."

$cleanupScript = @"

Start-Transcript -Path "C:\setup\cleanup.log"
Write-Host "=== DC01 Cleanup ==="

# Wait for AD DS to be fully started
`$attempts = 0
do {
    `$attempts++
    try {
        Get-ADDomainController -ErrorAction Stop | Out-Null
        Write-Host "AD DS is running."
        break
    } catch {
        Write-Host "Waiting for AD DS... (attempt `$attempts/30)"
        Start-Sleep -Seconds 10
    }
} while (`$attempts -lt 30)

# Disable firewall (DC promotion re-enables it)
Write-Host "Disabling firewall..."
Set-NetFirewallProfile -Profile Domain,Public,Private -Enabled False

# Re-enable WinRM (belt and suspenders)
Write-Host "Ensuring WinRM is configured..."
winrm quickconfig -q
winrm set winrm/config/service '@{AllowUnencrypted="true"}'
winrm set winrm/config/service/auth '@{Basic="true"}'
Restart-Service WinRM

# Remove all setup scheduled tasks
Write-Host "Removing scheduled tasks..."
Unregister-ScheduledTask -TaskName "DC-Phase2a" -Confirm:`$false -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName "DC-Phase2b" -Confirm:`$false -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName "DC-Cleanup" -Confirm:`$false -ErrorAction SilentlyContinue

Write-Host "=== DC01 setup complete ==="
Stop-Transcript

"@

$cleanupScript | Out-File -FilePath "$setupDir\phase2-cleanup.ps1" -Encoding UTF8

# ============================================================================
# Register Phase 2a scheduled task (runs on next boot)
# ============================================================================

Write-Host "Registering Phase 2a scheduled task..."

$action = New-ScheduledTaskAction `
    -Execute "powershell.exe" `
    -Argument "-ExecutionPolicy Bypass -File C:\setup\phase2a-rename.ps1"

$trigger = New-ScheduledTaskTrigger -AtStartup
$trigger.Delay = "PT30S"

$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -RunLevel Highest

Register-ScheduledTask `
    -TaskName "DC-Phase2a" `
    -Action $action `
    -Trigger $trigger `
    -Principal $principal `
    -Description "Phase 2a: Set IP and rename computer" `
    -ErrorAction Stop

# --- Register cleanup task (runs after promotion reboot) ---
$cleanupAction = New-ScheduledTaskAction `
    -Execute "powershell.exe" `
    -Argument "-ExecutionPolicy Bypass -File C:\setup\phase2-cleanup.ps1"

$cleanupTrigger = New-ScheduledTaskTrigger -AtStartup
$cleanupTrigger.Delay = "PT120S"

Register-ScheduledTask `
    -TaskName "DC-Cleanup" `
    -Action $cleanupAction `
    -Trigger $cleanupTrigger `
    -Principal $principal `
    -Description "Cleanup: Disable firewall, remove tasks after DC promotion" `
    -ErrorAction Stop

Write-Host "=== Phase 1 complete. Rebooting to start Phase 2a... ==="

Restart-Computer -Force
