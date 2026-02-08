<#
.SYNOPSIS
    Creates test computer objects in Active Directory for Lucid IT Agent testing.
    Run this on the Domain Controller after Setup-TestEnvironment.ps1.
.DESCRIPTION
    Creates computer objects in the IT Equipment OU with managedBy set to
    Star Wars test users. Enables testing of the user-to-computer lookup feature.
#>

param(
    [string]$Domain = "montanifarms.com"
)

$ErrorActionPreference = "Stop"
Import-Module ActiveDirectory

$domainDN = ($Domain -split "\." | ForEach-Object { "DC=$_" }) -join ","

# Ensure OU exists
$ouPath = "OU=IT Equipment,$domainDN"
if (-not (Get-ADOrganizationalUnit -Filter "DistinguishedName -eq '$ouPath'" -ErrorAction SilentlyContinue)) {
    New-ADOrganizationalUnit -Name "IT Equipment" -Path $domainDN -Description "Computer objects for Lucid testing"
    Write-Host "Created OU: $ouPath" -ForegroundColor Green
}

# Define test computers with user assignments
$computers = @(
    @{ Name = "DESK-LSKYWALKER"; Description = "Luke Skywalker's Workstation"; ManagedBy = "luke.skywalker"; OS = "Windows 11 Enterprise"; OSVersion = "10.0 (22631)" },
    @{ Name = "DESK-HSOLO";      Description = "Han Solo's Workstation";       ManagedBy = "hsolo";          OS = "Windows 11 Enterprise"; OSVersion = "10.0 (22631)" },
    @{ Name = "DESK-LORGANA";    Description = "Leia Organa's Workstation";    ManagedBy = "leia.organa";    OS = "Windows 10 Enterprise"; OSVersion = "10.0 (19045)" },
    @{ Name = "DESK-OKENOBI";    Description = "Obi-Wan Kenobi's Workstation"; ManagedBy = "obi-wan.kenobi"; OS = "Windows 11 Enterprise"; OSVersion = "10.0 (22631)" },
    @{ Name = "LAPTOP-LSKYWALKER"; Description = "Luke Skywalker's Laptop";    ManagedBy = "luke.skywalker"; OS = "Windows 11 Enterprise"; OSVersion = "10.0 (22631)" }
)

foreach ($comp in $computers) {
    $compName = $comp.Name

    if (Get-ADComputer -Filter "Name -eq '$compName'" -ErrorAction SilentlyContinue) {
        Write-Host "  Computer already exists: $compName" -ForegroundColor DarkGray
    } else {
        try {
            # Get the user's DN for managedBy
            $user = Get-ADUser -Identity $comp.ManagedBy -ErrorAction SilentlyContinue
            if (-not $user) {
                Write-Host "  WARNING: User $($comp.ManagedBy) not found, skipping $compName" -ForegroundColor Yellow
                continue
            }

            New-ADComputer -Name $compName `
                -Path $ouPath `
                -Description $comp.Description `
                -ManagedBy $user.DistinguishedName `
                -OtherAttributes @{
                    'operatingSystem' = $comp.OS
                    'operatingSystemVersion' = $comp.OSVersion
                } `
                -Enabled $true

            Write-Host "  Created computer: $compName (managed by $($comp.ManagedBy))" -ForegroundColor Green
        } catch {
            Write-Host "  ERROR creating $compName : $_" -ForegroundColor Red
        }
    }
}

Write-Host ""
Write-Host "Test computers created. Luke Skywalker has 2 devices (desktop + laptop) for multi-device testing." -ForegroundColor Cyan
Write-Host "Run the Lucid Tool Server endpoint: GET /api/v1/user/luke.skywalker/computers" -ForegroundColor Cyan
