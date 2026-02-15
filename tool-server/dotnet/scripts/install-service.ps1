#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs the Praxova IT Agent Tool Server as a Windows Service.

.DESCRIPTION
    Creates a Windows Service for the Praxova Tool Server. Supports running
    under a gMSA (recommended) or a standard domain service account.

.PARAMETER InstallPath
    Path where the tool server binaries are located.
    Default: C:\Program Files\Praxova\ToolServer

.PARAMETER ServiceAccount
    The service account to run under.
    For gMSA: DOMAIN\AccountName$ (note the trailing $)
    For domain account: DOMAIN\AccountName
    Default: LocalSystem

.PARAMETER ServiceAccountPassword
    Password for domain service accounts. Not needed for gMSA or LocalSystem.

.EXAMPLE
    .\install-service.ps1 -ServiceAccount "MONTANIFARMS\svc-toolserver$"

.EXAMPLE
    .\install-service.ps1 -InstallPath "D:\Praxova\ToolServer" -ServiceAccount "MONTANIFARMS\svc-praxova"
#>

param(
    [string]$InstallPath = "C:\Program Files\Praxova\ToolServer",
    [string]$ServiceAccount = "LocalSystem",
    [string]$ServiceAccountPassword = ""
)

$ServiceName = "PraxovaToolServer"
$DisplayName = "Praxova IT Agent Tool Server"
$Description = "Praxova IT Agent Tool Server - Provides Active Directory, file permission, and remote management operations for the IT Agent."
$ExePath = Join-Path $InstallPath "LucidToolServer.exe"

# Validate install path
if (-not (Test-Path $ExePath)) {
    Write-Error "Tool Server executable not found at: $ExePath"
    Write-Error "Ensure the application is published to: $InstallPath"
    exit 1
}

# Check if service already exists
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Warning "Service '$ServiceName' already exists. Stopping and removing..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName
    Start-Sleep -Seconds 2
}

# Create the service
Write-Host "Creating service '$DisplayName'..." -ForegroundColor Cyan

if ($ServiceAccount -eq "LocalSystem") {
    New-Service -Name $ServiceName `
        -BinaryPathName $ExePath `
        -DisplayName $DisplayName `
        -Description $Description `
        -StartupType Automatic
}
elseif ($ServiceAccount.EndsWith('$')) {
    # gMSA account — no password needed
    Write-Host "Using gMSA account: $ServiceAccount" -ForegroundColor Green
    sc.exe create $ServiceName binPath= $ExePath DisplayName= $DisplayName start= auto obj= $ServiceAccount
    sc.exe description $ServiceName $Description
}
else {
    # Domain service account — password required
    if ([string]::IsNullOrEmpty($ServiceAccountPassword)) {
        $securePassword = Read-Host "Enter password for $ServiceAccount" -AsSecureString
        $ServiceAccountPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
            [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword))
    }
    New-Service -Name $ServiceName `
        -BinaryPathName $ExePath `
        -DisplayName $DisplayName `
        -Description $Description `
        -StartupType Automatic `
        -Credential (New-Object System.Management.Automation.PSCredential($ServiceAccount,
            (ConvertTo-SecureString $ServiceAccountPassword -AsPlainText -Force)))
}

# Configure recovery options (restart on failure)
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000

# Create EventLog source if it doesn't exist
if (-not [System.Diagnostics.EventLog]::SourceExists("PraxovaToolServer")) {
    [System.Diagnostics.EventLog]::CreateEventSource("PraxovaToolServer", "Application")
    Write-Host "Created EventLog source: PraxovaToolServer" -ForegroundColor Green
}

Write-Host ""
Write-Host "Service '$DisplayName' installed successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Configure appsettings.json in $InstallPath"
Write-Host "  2. Place TLS certificates in $InstallPath\certs\"
Write-Host "  3. Start the service: Start-Service $ServiceName"
Write-Host "  4. Verify health: Invoke-RestMethod http://localhost:8080/api/v1/health"
