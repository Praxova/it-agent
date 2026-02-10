<#
.SYNOPSIS
    Creates Active Directory groups required for Praxova Admin Portal role mapping.
.DESCRIPTION
    Creates three security groups in AD that map to portal roles:
    - PraxovaAdmin-Admins    -> Admin role (full access)
    - PraxovaAdmin-Operators -> Operator role (can execute, cannot configure)
    - PraxovaAdmin-Viewers   -> Viewer role (read-only)

    Optionally adds specified users to each group for testing.
.PARAMETER OUPath
    The OU to create groups in. Default: searches for a Groups OU or uses Users container.
.PARAMETER TestUsers
    If specified, adds test user mappings. Expects hashtable like:
    @{ Admins = @("luke.skywalker"); Operators = @("han.solo"); Viewers = @("leia.organa") }
.EXAMPLE
    .\Setup-PraxovaAdminGroups.ps1
    .\Setup-PraxovaAdminGroups.ps1 -TestUsers @{ Admins = @("luke.skywalker"); Operators = @("han.solo") }
#>
param(
    [string]$OUPath,
    [hashtable]$TestUsers
)

# Import AD module
Import-Module ActiveDirectory -ErrorAction Stop

# Determine OU
if (-not $OUPath) {
    $domain = Get-ADDomain
    # Try common OU names
    $candidates = @("OU=Groups", "OU=Security Groups", "CN=Users")
    foreach ($candidate in $candidates) {
        $testPath = "$candidate,$($domain.DistinguishedName)"
        if ([adsi]::Exists("LDAP://$testPath")) {
            $OUPath = $testPath
            break
        }
    }
    if (-not $OUPath) {
        $OUPath = $domain.UsersContainer
    }
}

Write-Host "Creating Praxova Admin groups in: $OUPath" -ForegroundColor Cyan

$groups = @(
    @{ Name = "PraxovaAdmin-Admins";    Description = "Praxova Admin Portal - Administrator role (full access)" },
    @{ Name = "PraxovaAdmin-Operators"; Description = "Praxova Admin Portal - Operator role (execute operations)" },
    @{ Name = "PraxovaAdmin-Viewers";   Description = "Praxova Admin Portal - Viewer role (read-only access)" }
)

foreach ($group in $groups) {
    $existing = Get-ADGroup -Filter "Name -eq '$($group.Name)'" -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "  [EXISTS] $($group.Name)" -ForegroundColor Yellow
    } else {
        New-ADGroup -Name $group.Name `
                    -GroupScope Global `
                    -GroupCategory Security `
                    -Path $OUPath `
                    -Description $group.Description
        Write-Host "  [CREATED] $($group.Name)" -ForegroundColor Green
    }
}

# Add test users if specified
if ($TestUsers) {
    Write-Host "`nAdding test user memberships:" -ForegroundColor Cyan
    $roleMap = @{ Admins = "PraxovaAdmin-Admins"; Operators = "PraxovaAdmin-Operators"; Viewers = "PraxovaAdmin-Viewers" }
    foreach ($role in $TestUsers.Keys) {
        $groupName = $roleMap[$role]
        if (-not $groupName) { Write-Warning "Unknown role: $role"; continue }
        foreach ($user in $TestUsers[$role]) {
            try {
                Add-ADGroupMember -Identity $groupName -Members $user -ErrorAction Stop
                Write-Host "  [ADDED] $user -> $groupName" -ForegroundColor Green
            } catch {
                Write-Warning "  [FAILED] $user -> $groupName : $_"
            }
        }
    }
}

Write-Host "`nDone. Configure these group names in the Admin Portal's ActiveDirectory settings." -ForegroundColor Cyan
Write-Host "Default mapping:" -ForegroundColor Gray
Write-Host "  PraxovaAdmin-Admins    -> Admin role" -ForegroundColor Gray
Write-Host "  PraxovaAdmin-Operators -> Operator role" -ForegroundColor Gray
Write-Host "  PraxovaAdmin-Viewers   -> Viewer role" -ForegroundColor Gray
