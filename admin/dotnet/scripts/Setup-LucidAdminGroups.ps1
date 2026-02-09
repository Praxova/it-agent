<#
.SYNOPSIS
    Creates Active Directory groups required for Lucid Admin Portal role mapping.
.DESCRIPTION
    Creates three security groups in AD that map to portal roles:
    - LucidAdmin-Admins    -> Admin role (full access)
    - LucidAdmin-Operators -> Operator role (can execute, cannot configure)
    - LucidAdmin-Viewers   -> Viewer role (read-only)

    Optionally adds specified users to each group for testing.
.PARAMETER OUPath
    The OU to create groups in. Default: searches for a Groups OU or uses Users container.
.PARAMETER TestUsers
    If specified, adds test user mappings. Expects hashtable like:
    @{ Admins = @("luke.skywalker"); Operators = @("han.solo"); Viewers = @("leia.organa") }
.EXAMPLE
    .\Setup-LucidAdminGroups.ps1
    .\Setup-LucidAdminGroups.ps1 -TestUsers @{ Admins = @("luke.skywalker"); Operators = @("han.solo") }
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

Write-Host "Creating Lucid Admin groups in: $OUPath" -ForegroundColor Cyan

$groups = @(
    @{ Name = "LucidAdmin-Admins";    Description = "Lucid Admin Portal - Administrator role (full access)" },
    @{ Name = "LucidAdmin-Operators"; Description = "Lucid Admin Portal - Operator role (execute operations)" },
    @{ Name = "LucidAdmin-Viewers";   Description = "Lucid Admin Portal - Viewer role (read-only access)" }
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
    $roleMap = @{ Admins = "LucidAdmin-Admins"; Operators = "LucidAdmin-Operators"; Viewers = "LucidAdmin-Viewers" }
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
Write-Host "  LucidAdmin-Admins    -> Admin role" -ForegroundColor Gray
Write-Host "  LucidAdmin-Operators -> Operator role" -ForegroundColor Gray
Write-Host "  LucidAdmin-Viewers   -> Viewer role" -ForegroundColor Gray
