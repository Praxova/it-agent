<#
.SYNOPSIS
    Provisions AD test users, groups, and file shares from CSV data files.

.DESCRIPTION
    Reads the same CSV files used by the Python test data generator to
    create matching Active Directory objects. This ensures usernames in
    generated tickets always exist in AD.

    Data sources (relative to this script's directory):
      ../../test_data_generator/data/test_users.csv
      ../../test_data_generator/data/test_groups.csv
      ../../test_data_generator/data/test_shares.csv

.PARAMETER Cleanup
    Remove all test objects instead of creating them.

.PARAMETER TestPassword
    Password for all test users (default: TempPass123!)

.PARAMETER OUName
    Name of the OU to create (default: LucidTest)

.PARAMETER DataDir
    Path to CSV data directory (default: auto-detected)

.EXAMPLE
    .\Setup-TestUsersFromCSV.ps1
    Creates all users, groups, and shares from CSV files.

.EXAMPLE
    .\Setup-TestUsersFromCSV.ps1 -Cleanup
    Removes all test objects.

.NOTES
    Requires:
    - Windows Server with AD DS role
    - Run as Domain Admin
    - PowerShell 5.1+
#>

[CmdletBinding()]
param(
    [switch]$Cleanup,
    [string]$TestPassword = "TempPass123!",
    [string]$OUName = "LucidTest",
    [string]$DataDir = ""
)

# ─────────────────────────────────────────────────────────────────────
# Boilerplate
# ─────────────────────────────────────────────────────────────────────

$ErrorActionPreference = "Stop"

$currentPrincipal = New-Object Security.Principal.WindowsPrincipal(
    [Security.Principal.WindowsIdentity]::GetCurrent()
)
if (-not $currentPrincipal.IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script must be run as Administrator"
    exit 1
}

Import-Module ActiveDirectory -ErrorAction Stop

$domain     = Get-ADDomain
$domainDN   = $domain.DistinguishedName
$domainDNS  = $domain.DNSRoot
$OUPath     = "OU=$OUName,$domainDN"

# Locate CSV data directory
if ([string]::IsNullOrEmpty($DataDir)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $DataDir = Join-Path $scriptDir "..\..\test_data_generator\data"
    $DataDir = (Resolve-Path $DataDir -ErrorAction SilentlyContinue).Path

    if (-not $DataDir) {
        # Fallback: try relative to project root
        $DataDir = Join-Path $scriptDir "..\..\test_data_generator\data"
    }
}

$usersCSV  = Join-Path $DataDir "test_users.csv"
$groupsCSV = Join-Path $DataDir "test_groups.csv"
$sharesCSV = Join-Path $DataDir "test_shares.csv"

Write-Host "═══════════════════════════════════════════" -ForegroundColor Cyan
Write-Host " Lucid IT Agent — AD Test Environment Setup" -ForegroundColor Cyan
Write-Host " Source: CSV data files (single source of truth)" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Domain:    $domainDNS" -ForegroundColor Gray
Write-Host "  Base OU:   $OUPath" -ForegroundColor Gray
Write-Host "  Data dir:  $DataDir" -ForegroundColor Gray
Write-Host ""

# Validate CSV files exist
foreach ($csv in @($usersCSV, $groupsCSV, $sharesCSV)) {
    if (-not (Test-Path $csv)) {
        Write-Error "CSV file not found: $csv"
        exit 1
    }
    Write-Host "  ✓ Found: $(Split-Path -Leaf $csv)" -ForegroundColor Green
}
Write-Host ""

# ─────────────────────────────────────────────────────────────────────
# Cleanup Mode
# ─────────────────────────────────────────────────────────────────────
if ($Cleanup) {
    Write-Host "CLEANUP MODE — Removing test objects..." -ForegroundColor Yellow
    $confirm = Read-Host "Remove all Lucid test objects? (yes/no)"
    if ($confirm -ne "yes") { Write-Host "Cancelled."; exit 0 }

    # Remove users
    Write-Host "`n  Removing users..." -ForegroundColor Yellow
    Get-ADUser -Filter * -SearchBase "OU=Users,$OUPath" -ErrorAction SilentlyContinue |
        ForEach-Object {
            Remove-ADUser -Identity $_ -Confirm:$false
            Write-Host "    Removed user: $($_.SamAccountName)" -ForegroundColor Gray
        }

    # Remove groups
    Write-Host "  Removing groups..." -ForegroundColor Yellow
    Get-ADGroup -Filter * -SearchBase "OU=Groups,$OUPath" -ErrorAction SilentlyContinue |
        ForEach-Object {
            Remove-ADGroup -Identity $_ -Confirm:$false
            Write-Host "    Removed group: $($_.Name)" -ForegroundColor Gray
        }

    # Remove shares
    Write-Host "  Removing file shares..." -ForegroundColor Yellow
    $shareName = "LucidTestShare"
    if (Get-SmbShare -Name $shareName -ErrorAction SilentlyContinue) {
        Remove-SmbShare -Name $shareName -Force
    }
    $sharePath = "C:\LucidTestShare"
    if (Test-Path $sharePath) {
        Remove-Item -Path $sharePath -Recurse -Force
    }

    # Remove OUs
    Write-Host "  Removing OUs..." -ForegroundColor Yellow
    @("OU=Users,$OUPath", "OU=Groups,$OUPath", "OU=ServiceAccounts,$OUPath") |
        ForEach-Object {
            $ou = Get-ADOrganizationalUnit -Filter "DistinguishedName -eq '$_'" -ErrorAction SilentlyContinue
            if ($ou) {
                Set-ADOrganizationalUnit -Identity $_ -ProtectedFromAccidentalDeletion $false
                Remove-ADOrganizationalUnit -Identity $_ -Confirm:$false
            }
        }
    $baseOU = Get-ADOrganizationalUnit -Filter "DistinguishedName -eq '$OUPath'" -ErrorAction SilentlyContinue
    if ($baseOU) {
        Set-ADOrganizationalUnit -Identity $OUPath -ProtectedFromAccidentalDeletion $false
        Remove-ADOrganizationalUnit -Identity $OUPath -Confirm:$false
    }

    Write-Host "`n  Cleanup complete!" -ForegroundColor Green
    exit 0
}

# ─────────────────────────────────────────────────────────────────────
# Setup Mode
# ─────────────────────────────────────────────────────────────────────

$securePassword = ConvertTo-SecureString $TestPassword -AsPlainText -Force
$userCount = 0
$groupCount = 0

# Step 1: Create OUs
Write-Host "[1/4] Creating OUs..." -ForegroundColor Cyan
$ous = @($OUPath, "OU=Users,$OUPath", "OU=Groups,$OUPath", "OU=ServiceAccounts,$OUPath")
foreach ($ouDN in $ous) {
    if (-not (Get-ADOrganizationalUnit -Filter "DistinguishedName -eq '$ouDN'" -ErrorAction SilentlyContinue)) {
        $ouName = ($ouDN -split ",")[0] -replace "^OU=", ""
        $parentDN = ($ouDN -replace "^OU=[^,]+,", "")
        if ($parentDN -eq $ouDN) { $parentDN = $domainDN }
        New-ADOrganizationalUnit -Name $ouName -Path $parentDN -ProtectedFromAccidentalDeletion $true
        Write-Host "  ✓ Created: $ouDN" -ForegroundColor Gray
    } else {
        Write-Host "  · Exists:  $ouDN" -ForegroundColor DarkGray
    }
}

# Step 2: Create users from CSV
Write-Host "`n[2/4] Creating users from test_users.csv..." -ForegroundColor Cyan
$users = Import-Csv $usersCSV

foreach ($u in $users) {
    $sam = $u.username
    if (-not (Get-ADUser -Filter "SamAccountName -eq '$sam'" -ErrorAction SilentlyContinue)) {
        New-ADUser `
            -Name $u.display_name `
            -SamAccountName $sam `
            -UserPrincipalName "$($u.email_prefix)@$domainDNS" `
            -GivenName $u.first_name `
            -Surname $u.last_name `
            -DisplayName $u.display_name `
            -EmailAddress "$($u.email_prefix)@$domainDNS" `
            -Department $u.department `
            -Title $u.role `
            -Path "OU=Users,$OUPath" `
            -AccountPassword $securePassword `
            -Enabled $true `
            -PasswordNeverExpires $false `
            -ChangePasswordAtLogon $false `
            -Description "Lucid IT Agent test user ($($u.department))"
        Write-Host "  ✓ Created: $sam ($($u.display_name)) - $($u.department)" -ForegroundColor Gray
        $userCount++
    } else {
        Write-Host "  · Exists:  $sam" -ForegroundColor DarkGray
    }
}

# Step 3: Create groups from CSV and assign members
Write-Host "`n[3/4] Creating groups from test_groups.csv..." -ForegroundColor Cyan
$groups = Import-Csv $groupsCSV

foreach ($g in $groups) {
    $gName = $g.group_name
    if (-not (Get-ADGroup -Filter "Name -eq '$gName'" -ErrorAction SilentlyContinue)) {
        New-ADGroup `
            -Name $gName `
            -SamAccountName $gName `
            -GroupCategory Security `
            -GroupScope Global `
            -Path "OU=Groups,$OUPath" `
            -Description $g.description
        Write-Host "  ✓ Created group: $gName ($($g.category))" -ForegroundColor Gray
        $groupCount++
    } else {
        Write-Host "  · Exists:  $gName" -ForegroundColor DarkGray
    }

    # Add initial members
    if ($g.initial_members) {
        $members = $g.initial_members -split "\|"
        foreach ($member in $members) {
            $member = $member.Trim()
            if ($member -and (Get-ADUser -Filter "SamAccountName -eq '$member'" -ErrorAction SilentlyContinue)) {
                try {
                    Add-ADGroupMember -Identity $gName -Members $member -ErrorAction SilentlyContinue
                } catch {
                    # Member may already be in group
                }
            }
        }
        Write-Host "    Members: $($members -join ', ')" -ForegroundColor DarkGray
    }
}

# Step 4: Create file share structure
Write-Host "`n[4/4] Creating file share structure..." -ForegroundColor Cyan
$sharePath = "C:\LucidTestShare"
$shareName = "LucidTestShare"

if (-not (Test-Path $sharePath)) {
    New-Item -Path $sharePath -ItemType Directory | Out-Null
    Write-Host "  ✓ Created: $sharePath" -ForegroundColor Gray
}

# Create subdirectories matching the share paths in CSV
$shares = Import-Csv $sharesCSV
foreach ($s in $shares) {
    # Extract relative path from UNC (e.g. \\fileserver\finance\Q4-Reports -> finance\Q4-Reports)
    $parts = $s.share_path -replace "^\\\\[^\\]+\\", ""
    $localPath = Join-Path $sharePath $parts
    if (-not (Test-Path $localPath)) {
        New-Item -Path $localPath -ItemType Directory -Force | Out-Null
        Write-Host "  ✓ Created: $parts" -ForegroundColor Gray
    }
}

if (-not (Get-SmbShare -Name $shareName -ErrorAction SilentlyContinue)) {
    New-SmbShare -Name $shareName -Path $sharePath -FullAccess "Everyone"
    Write-Host "  ✓ Shared as: \\$env:COMPUTERNAME\$shareName" -ForegroundColor Gray
}

# ─────────────────────────────────────────────────────────────────────
# Summary
# ─────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "═══════════════════════════════════════════" -ForegroundColor Green
Write-Host " Setup Complete!" -ForegroundColor Green
Write-Host "═══════════════════════════════════════════" -ForegroundColor Green
Write-Host ""
Write-Host "  Users:      $userCount created ($($users.Count) in CSV)" -ForegroundColor Cyan
Write-Host "  Groups:     $groupCount created ($($groups.Count) in CSV)" -ForegroundColor Cyan
Write-Host "  Shares:     $($shares.Count) paths configured" -ForegroundColor Cyan
Write-Host "  Password:   $TestPassword" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Data source (single source of truth):" -ForegroundColor Cyan
Write-Host "    $usersCSV" -ForegroundColor Gray
Write-Host "    $groupsCSV" -ForegroundColor Gray
Write-Host "    $sharesCSV" -ForegroundColor Gray
Write-Host ""
Write-Host "  Next: Run the test data generator:" -ForegroundColor Cyan
Write-Host "    python3 -m test_data_generator generate --preset regression --snow" -ForegroundColor White
Write-Host ""
