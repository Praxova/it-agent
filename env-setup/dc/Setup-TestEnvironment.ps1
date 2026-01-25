<#
.SYNOPSIS
    Sets up a test environment for Lucid IT Agent development.

.DESCRIPTION
    This script creates test users, groups, and file shares in Active Directory
    for use in developing and testing the Lucid IT Agent.
    
    All objects are created in a dedicated OU (LucidTest) to keep them isolated
    from production objects.

.PARAMETER Cleanup
    If specified, removes all test objects instead of creating them.

.PARAMETER TestUserCount
    Number of test users to create (default: 10)

.PARAMETER TestPassword
    Password for test users (default: TempPass123!)

.PARAMETER OUPath
    Base OU for test objects (default: auto-detected based on domain)

.EXAMPLE
    .\Setup-TestEnvironment.ps1
    Creates the default test environment.

.EXAMPLE
    .\Setup-TestEnvironment.ps1 -Cleanup
    Removes all test objects.

.EXAMPLE
    .\Setup-TestEnvironment.ps1 -TestUserCount 20 -TestPassword "MyTestPass1!"
    Creates 20 test users with custom password.

.NOTES
    Requires:
    - Windows Server with AD DS role
    - Run as Domain Admin
    - PowerShell 5.1 or later
#>

[CmdletBinding()]
param(
    [switch]$Cleanup,
    [int]$TestUserCount = 10,
    [string]$TestPassword = "TempPass123!",
    [string]$OUPath = ""
)

# Ensure running as admin
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script must be run as Administrator"
    exit 1
}

# Import AD module
try {
    Import-Module ActiveDirectory -ErrorAction Stop
} catch {
    Write-Error "Active Directory module not found. Is AD DS role installed?"
    exit 1
}

# Get domain info
$domain = Get-ADDomain
$domainDN = $domain.DistinguishedName
$domainNetBIOS = $domain.NetBIOSName

# Set OU path if not specified
if ([string]::IsNullOrEmpty($OUPath)) {
    $OUPath = "OU=LucidTest,$domainDN"
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Lucid IT Agent - Test Environment Setup" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Domain: $($domain.DNSRoot)" -ForegroundColor Gray
Write-Host "Base OU: $OUPath" -ForegroundColor Gray
Write-Host ""

# --------------------------------------------------------------------------
# Cleanup Mode
# --------------------------------------------------------------------------
if ($Cleanup) {
    Write-Host "CLEANUP MODE - Removing test objects..." -ForegroundColor Yellow
    Write-Host ""
    
    # Confirm
    $confirm = Read-Host "Are you sure you want to remove all Lucid test objects? (yes/no)"
    if ($confirm -ne "yes") {
        Write-Host "Cleanup cancelled." -ForegroundColor Yellow
        exit 0
    }
    
    # Remove users
    Write-Host "Removing test users..." -ForegroundColor Yellow
    Get-ADUser -Filter * -SearchBase "OU=Users,$OUPath" -ErrorAction SilentlyContinue | 
        ForEach-Object {
            Write-Host "  Removing user: $($_.SamAccountName)" -ForegroundColor Gray
            Remove-ADUser -Identity $_ -Confirm:$false
        }
    
    # Remove groups
    Write-Host "Removing test groups..." -ForegroundColor Yellow
    Get-ADGroup -Filter * -SearchBase "OU=Groups,$OUPath" -ErrorAction SilentlyContinue | 
        ForEach-Object {
            Write-Host "  Removing group: $($_.Name)" -ForegroundColor Gray
            Remove-ADGroup -Identity $_ -Confirm:$false
        }
    
    # Remove service account
    Write-Host "Removing service account..." -ForegroundColor Yellow
    Get-ADUser -Filter "SamAccountName -eq 'svc-lucid-agent'" -ErrorAction SilentlyContinue | 
        ForEach-Object {
            Write-Host "  Removing: $($_.SamAccountName)" -ForegroundColor Gray
            Remove-ADUser -Identity $_ -Confirm:$false
        }
    
    # Remove OUs (must be empty)
    Write-Host "Removing OUs..." -ForegroundColor Yellow
    @("OU=Users,$OUPath", "OU=Groups,$OUPath", "OU=ServiceAccounts,$OUPath") | ForEach-Object {
        if (Get-ADOrganizationalUnit -Filter "DistinguishedName -eq '$_'" -ErrorAction SilentlyContinue) {
            Set-ADOrganizationalUnit -Identity $_ -ProtectedFromAccidentalDeletion $false
            Remove-ADOrganizationalUnit -Identity $_ -Confirm:$false
            Write-Host "  Removed: $_" -ForegroundColor Gray
        }
    }
    
    # Remove base OU
    if (Get-ADOrganizationalUnit -Filter "DistinguishedName -eq '$OUPath'" -ErrorAction SilentlyContinue) {
        Set-ADOrganizationalUnit -Identity $OUPath -ProtectedFromAccidentalDeletion $false
        Remove-ADOrganizationalUnit -Identity $OUPath -Confirm:$false
        Write-Host "  Removed: $OUPath" -ForegroundColor Gray
    }
    
    # Remove file share
    Write-Host "Removing file share..." -ForegroundColor Yellow
    $shareName = "LucidTestShare"
    if (Get-SmbShare -Name $shareName -ErrorAction SilentlyContinue) {
        Remove-SmbShare -Name $shareName -Force
        Write-Host "  Removed share: $shareName" -ForegroundColor Gray
    }
    
    $sharePath = "C:\LucidTestShare"
    if (Test-Path $sharePath) {
        Remove-Item -Path $sharePath -Recurse -Force
        Write-Host "  Removed folder: $sharePath" -ForegroundColor Gray
    }
    
    Write-Host ""
    Write-Host "Cleanup complete!" -ForegroundColor Green
    exit 0
}

# --------------------------------------------------------------------------
# Setup Mode
# --------------------------------------------------------------------------

Write-Host "Creating test environment..." -ForegroundColor Green
Write-Host ""

# Create OUs
Write-Host "[1/5] Creating Organizational Units..." -ForegroundColor Cyan

$ousToCreate = @(
    @{ Path = $OUPath; Name = "LucidTest" },
    @{ Path = "OU=Users,$OUPath"; Name = "Users" },
    @{ Path = "OU=Groups,$OUPath"; Name = "Groups" },
    @{ Path = "OU=ServiceAccounts,$OUPath"; Name = "ServiceAccounts" }
)

foreach ($ou in $ousToCreate) {
    if (-not (Get-ADOrganizationalUnit -Filter "DistinguishedName -eq '$($ou.Path)'" -ErrorAction SilentlyContinue)) {
        $parentPath = ($ou.Path -replace "^OU=[^,]+,", "")
        if ($parentPath -eq $ou.Path) { $parentPath = $domainDN }
        
        New-ADOrganizationalUnit -Name $ou.Name -Path $parentPath -ProtectedFromAccidentalDeletion $true
        Write-Host "  Created OU: $($ou.Path)" -ForegroundColor Gray
    } else {
        Write-Host "  OU exists: $($ou.Path)" -ForegroundColor DarkGray
    }
}

# Create test users
Write-Host ""
Write-Host "[2/5] Creating test users..." -ForegroundColor Cyan

$securePassword = ConvertTo-SecureString $TestPassword -AsPlainText -Force

for ($i = 1; $i -le $TestUserCount; $i++) {
    $userName = "TestUser{0:D2}" -f $i
    $displayName = "Test User $i"
    $email = "$userName@$($domain.DNSRoot)"
    
    if (-not (Get-ADUser -Filter "SamAccountName -eq '$userName'" -ErrorAction SilentlyContinue)) {
        New-ADUser -Name $displayName `
            -SamAccountName $userName `
            -UserPrincipalName $email `
            -GivenName "Test" `
            -Surname "User $i" `
            -DisplayName $displayName `
            -EmailAddress $email `
            -Path "OU=Users,$OUPath" `
            -AccountPassword $securePassword `
            -Enabled $true `
            -PasswordNeverExpires $false `
            -ChangePasswordAtLogon $false `
            -Description "Lucid IT Agent test user"
        
        Write-Host "  Created user: $userName" -ForegroundColor Gray
    } else {
        Write-Host "  User exists: $userName" -ForegroundColor DarkGray
    }
}

# Create service account
$svcAccountName = "svc-lucid-agent"
if (-not (Get-ADUser -Filter "SamAccountName -eq '$svcAccountName'" -ErrorAction SilentlyContinue)) {
    New-ADUser -Name "Lucid Agent Service" `
        -SamAccountName $svcAccountName `
        -UserPrincipalName "$svcAccountName@$($domain.DNSRoot)" `
        -Path "OU=ServiceAccounts,$OUPath" `
        -AccountPassword $securePassword `
        -Enabled $true `
        -PasswordNeverExpires $true `
        -CannotChangePassword $true `
        -Description "Service account for Lucid IT Agent tool server"
    
    Write-Host "  Created service account: $svcAccountName" -ForegroundColor Gray
} else {
    Write-Host "  Service account exists: $svcAccountName" -ForegroundColor DarkGray
}

# Create test groups
Write-Host ""
Write-Host "[3/5] Creating test groups..." -ForegroundColor Cyan

$groupsToCreate = @(
    @{ Name = "LucidTest-ReadOnly"; Description = "Test group - Read Only access" },
    @{ Name = "LucidTest-Contributors"; Description = "Test group - Contributor access" },
    @{ Name = "LucidTest-Managers"; Description = "Test group - Manager access" },
    @{ Name = "LucidTest-VPNUsers"; Description = "Test group - VPN access" },
    @{ Name = "LucidTest-RemoteDesktop"; Description = "Test group - Remote Desktop access" }
)

foreach ($group in $groupsToCreate) {
    if (-not (Get-ADGroup -Filter "Name -eq '$($group.Name)'" -ErrorAction SilentlyContinue)) {
        New-ADGroup -Name $group.Name `
            -SamAccountName $group.Name `
            -GroupCategory Security `
            -GroupScope Global `
            -Path "OU=Groups,$OUPath" `
            -Description $group.Description
        
        Write-Host "  Created group: $($group.Name)" -ForegroundColor Gray
    } else {
        Write-Host "  Group exists: $($group.Name)" -ForegroundColor DarkGray
    }
}

# Add some users to groups for testing
Write-Host "  Adding sample memberships..." -ForegroundColor Gray
Add-ADGroupMember -Identity "LucidTest-ReadOnly" -Members "TestUser01", "TestUser02", "TestUser03" -ErrorAction SilentlyContinue
Add-ADGroupMember -Identity "LucidTest-Contributors" -Members "TestUser04", "TestUser05" -ErrorAction SilentlyContinue
Add-ADGroupMember -Identity "LucidTest-Managers" -Members "TestUser06" -ErrorAction SilentlyContinue

# Create file share
Write-Host ""
Write-Host "[4/5] Creating test file share..." -ForegroundColor Cyan

$sharePath = "C:\LucidTestShare"
$shareName = "LucidTestShare"

if (-not (Test-Path $sharePath)) {
    New-Item -Path $sharePath -ItemType Directory | Out-Null
    Write-Host "  Created folder: $sharePath" -ForegroundColor Gray
    
    # Create subfolders
    @("Public", "Department1", "Department2", "Restricted") | ForEach-Object {
        New-Item -Path "$sharePath\$_" -ItemType Directory | Out-Null
        Write-Host "  Created subfolder: $_" -ForegroundColor Gray
    }
} else {
    Write-Host "  Folder exists: $sharePath" -ForegroundColor DarkGray
}

if (-not (Get-SmbShare -Name $shareName -ErrorAction SilentlyContinue)) {
    New-SmbShare -Name $shareName -Path $sharePath -FullAccess "Everyone"
    Write-Host "  Created share: \\$env:COMPUTERNAME\$shareName" -ForegroundColor Gray
} else {
    Write-Host "  Share exists: \\$env:COMPUTERNAME\$shareName" -ForegroundColor DarkGray
}

# Grant service account permissions
Write-Host ""
Write-Host "[5/5] Configuring service account permissions..." -ForegroundColor Cyan

# Grant password reset permissions on test OU
$svcAccount = Get-ADUser -Identity $svcAccountName
$testUsersOU = Get-ADOrganizationalUnit -Identity "OU=Users,$OUPath"

# This is a simplified permission grant - in production you'd use more specific delegations
Write-Host "  Note: Manual delegation may be required for production" -ForegroundColor Yellow
Write-Host "  Service account: $domainNetBIOS\$svcAccountName" -ForegroundColor Gray

# --------------------------------------------------------------------------
# Summary
# --------------------------------------------------------------------------
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Setup Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Created:" -ForegroundColor Cyan
Write-Host "  - $TestUserCount test users (TestUser01-TestUser$($TestUserCount.ToString('D2')))" -ForegroundColor Gray
Write-Host "  - 5 test groups" -ForegroundColor Gray
Write-Host "  - 1 service account ($svcAccountName)" -ForegroundColor Gray
Write-Host "  - 1 file share (\\$env:COMPUTERNAME\$shareName)" -ForegroundColor Gray
Write-Host ""
Write-Host "Test User Password: $TestPassword" -ForegroundColor Yellow
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Delegate password reset permissions to $svcAccountName" -ForegroundColor Gray
Write-Host "  2. Configure tools.yaml with service account credentials" -ForegroundColor Gray
Write-Host "  3. Test connectivity from agent machine" -ForegroundColor Gray
Write-Host ""
