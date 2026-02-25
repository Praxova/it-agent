# ============================================================================
# setup-member.ps1 — Member Server Setup (Three-Phase)
# ============================================================================
# Phase 1 (runs via WinRM from Tofu):
#   - Write Phase 2a, 2b scripts to disk
#   - Register Phase 2a scheduled task
#   - Reboot
#
# Phase 2a (scheduled task on first boot):
#   - Change IP address, set DNS to DC
#   - Rename computer to TOOL01
#   - Register Phase 2b scheduled task
#   - Reboot (required for rename to take effect)
#
# Phase 2b (scheduled task on second boot):
#   - Join domain (hostname is already TOOL01)
#   - Clean up scheduled tasks
#   - Reboot
# ============================================================================

param(
    [Parameter(Mandatory)] [string] $NewIP,
    [Parameter(Mandatory)] [string] $Prefix,
    [Parameter(Mandatory)] [string] $Gateway,
    [Parameter(Mandatory)] [string] $DNSIP,
    [Parameter(Mandatory)] [string] $Domain,
    [Parameter(Mandatory)] [string] $AdminPassword
)

$ErrorActionPreference = "Stop"
$setupDir = "C:\setup"

if (-not (Test-Path $setupDir)) { New-Item -Path $setupDir -ItemType Directory -Force }

Write-Host "=== TOOL01 Phase 1: Preparing Member Server ==="

# ============================================================================
# Phase 2a Script — Change IP + Rename
# ============================================================================

Write-Host "Creating Phase 2a script (IP + Rename)..."

$phase2aScript = @"

`$ErrorActionPreference = "Stop"
Start-Transcript -Path "C:\setup\phase2a.log"

Write-Host "=== TOOL01 Phase 2a: Setting IP and Renaming ==="

# --- Change IP Address ---
Write-Host "Setting IP to $NewIP/$Prefix..."
`$adapter = Get-NetAdapter | Where-Object { `$_.Status -eq 'Up' -and `$_.InterfaceDescription -notmatch 'Loopback' } | Select-Object -First 1

`$adapter | Remove-NetIPAddress -Confirm:`$false -ErrorAction SilentlyContinue
`$adapter | Remove-NetRoute -Confirm:`$false -ErrorAction SilentlyContinue

New-NetIPAddress -InterfaceIndex `$adapter.ifIndex -IPAddress "$NewIP" -PrefixLength $Prefix -DefaultGateway "$Gateway" -ErrorAction Stop
Set-DnsClientServerAddress -InterfaceIndex `$adapter.ifIndex -ServerAddresses @("$DNSIP", "$Gateway")

Write-Host "IP configured."

# --- Rename Computer ---
Write-Host "Renaming computer to TOOL01..."
Rename-Computer -NewName "TOOL01" -Force

# --- Register Phase 2b task for next boot ---
Write-Host "Registering Phase 2b scheduled task..."

`$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-ExecutionPolicy Bypass -File C:\setup\phase2b-join.ps1"
`$trigger = New-ScheduledTaskTrigger -AtStartup
`$trigger.Delay = "PT30S"
`$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -RunLevel Highest

Unregister-ScheduledTask -TaskName "Member-Phase2a" -Confirm:`$false -ErrorAction SilentlyContinue
Register-ScheduledTask -TaskName "Member-Phase2b" -Action `$action -Trigger `$trigger -Principal `$principal -Description "Phase 2b: Join domain"

Write-Host "=== Phase 2a complete. Rebooting for rename... ==="
Stop-Transcript

Restart-Computer -Force

"@

$phase2aScript | Out-File -FilePath "$setupDir\phase2a-rename.ps1" -Encoding UTF8

# ============================================================================
# Phase 2b Script — Join Domain
# ============================================================================

Write-Host "Creating Phase 2b script (Domain Join)..."

$phase2bScript = @"

`$ErrorActionPreference = "Stop"
Start-Transcript -Path "C:\setup\phase2b.log"

Write-Host "=== TOOL01 Phase 2b: Joining Domain ==="
Write-Host "Hostname: `$env:COMPUTERNAME"

# --- Wait for DNS resolution to DC ---
Write-Host "Waiting for DNS resolution to $Domain..."
`$attempts = 0
do {
    `$attempts++
    try {
        Resolve-DnsName "$Domain" -Server "$DNSIP" -ErrorAction Stop | Out-Null
        Write-Host "DNS resolution to $Domain successful."
        break
    } catch {
        Write-Host "Waiting for DNS... (attempt `$attempts/30)"
        Start-Sleep -Seconds 10
    }
} while (`$attempts -lt 30)

if (`$attempts -ge 30) {
    Write-Host "ERROR: Cannot resolve $Domain after 30 attempts. Aborting."
    Stop-Transcript
    exit 1
}

# --- Join Domain ---
Write-Host "Joining domain $Domain..."

`$securePassword = ConvertTo-SecureString "$AdminPassword" -AsPlainText -Force
`$credential = New-Object System.Management.Automation.PSCredential("$Domain\Administrator", `$securePassword)

Add-Computer ``
    -DomainName "$Domain" ``
    -Credential `$credential ``
    -ErrorAction Stop

Write-Host "Domain join successful."

# --- Disable firewall (domain join may re-enable Domain profile) ---
Set-NetFirewallProfile -Profile Domain,Public,Private -Enabled False

# --- Ensure WinRM is configured ---
winrm quickconfig -q
winrm set winrm/config/service '@{AllowUnencrypted="true"}'
winrm set winrm/config/service/auth '@{Basic="true"}'
Restart-Service WinRM

# --- Clean up all scheduled tasks ---
Write-Host "Removing scheduled tasks..."
Unregister-ScheduledTask -TaskName "Member-Phase2a" -Confirm:`$false -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName "Member-Phase2b" -Confirm:`$false -ErrorAction SilentlyContinue

Write-Host "=== Phase 2b complete. Rebooting... ==="
Stop-Transcript

Restart-Computer -Force

"@

$phase2bScript | Out-File -FilePath "$setupDir\phase2b-join.ps1" -Encoding UTF8

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
    -TaskName "Member-Phase2a" `
    -Action $action `
    -Trigger $trigger `
    -Principal $principal `
    -Description "Phase 2a: Set IP and rename computer" `
    -ErrorAction Stop

Write-Host "=== Phase 1 complete. Rebooting to start Phase 2a... ==="

Restart-Computer -Force
