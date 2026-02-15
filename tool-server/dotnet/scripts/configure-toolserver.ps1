#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Configures the Praxova IT Agent Tool Server after installation.

.DESCRIPTION
    Interactive configuration wizard that sets up:
    - Active Directory domain connection
    - TLS certificate paths
    - Port configuration
    - Protected groups and accounts

.PARAMETER InstallPath
    Path where the tool server is installed.
    Default: C:\Program Files\Praxova\ToolServer

.EXAMPLE
    .\configure-toolserver.ps1

.EXAMPLE
    .\configure-toolserver.ps1 -InstallPath "D:\Praxova\ToolServer"
#>

param(
    [string]$InstallPath = "C:\Program Files\Praxova\ToolServer"
)

$settingsPath = Join-Path $InstallPath "appsettings.json"

if (-not (Test-Path $settingsPath)) {
    Write-Error "appsettings.json not found at: $settingsPath"
    exit 1
}

Write-Host ""
Write-Host "======================================================" -ForegroundColor Cyan
Write-Host "  Praxova IT Agent Tool Server Configuration" -ForegroundColor Cyan
Write-Host "======================================================" -ForegroundColor Cyan
Write-Host ""

# Load current settings
$settings = Get-Content $settingsPath -Raw | ConvertFrom-Json

# Domain configuration
Write-Host "-- Active Directory Configuration --" -ForegroundColor Yellow
$domain = Read-Host "AD Domain Name (e.g., contoso.com) [$($settings.ToolServer.DomainName)]"
if (-not [string]::IsNullOrEmpty($domain)) {
    $settings.ToolServer.DomainName = $domain
}

# Test AD connectivity
Write-Host "Testing AD connectivity to $($settings.ToolServer.DomainName)..." -ForegroundColor Cyan
try {
    $context = New-Object System.DirectoryServices.ActiveDirectory.DirectoryContext("Domain", $settings.ToolServer.DomainName)
    $domainObj = [System.DirectoryServices.ActiveDirectory.Domain]::GetDomain($context)
    Write-Host "  [OK] Connected to domain: $($domainObj.Name)" -ForegroundColor Green
    Write-Host "  [OK] Domain Controller: $($domainObj.PdcRoleOwner.Name)" -ForegroundColor Green
}
catch {
    Write-Host "  [FAIL] Could not connect to domain: $_" -ForegroundColor Red
    Write-Host "  The tool server requires AD connectivity. Verify:" -ForegroundColor Yellow
    Write-Host "    - This server is domain-joined" -ForegroundColor Yellow
    Write-Host "    - The service account has AD read/write permissions" -ForegroundColor Yellow
}

Write-Host ""

# Certificate configuration
Write-Host "-- TLS Certificate Configuration --" -ForegroundColor Yellow
$configureTls = Read-Host "Configure TLS certificates now? (Y/n)"

if ($configureTls -ne 'n') {
    $certPath = Read-Host "Certificate file path (PEM or PFX)"
    $keyPath = Read-Host "Private key file path (leave empty for PFX)"

    if (-not [string]::IsNullOrEmpty($certPath)) {
        if (Test-Path $certPath) {
            Write-Host "  [OK] Certificate file found" -ForegroundColor Green

            # Update Kestrel HTTPS config
            $settings.Kestrel.Endpoints.Https.Certificate.Path = $certPath
            if (-not [string]::IsNullOrEmpty($keyPath)) {
                $settings.Kestrel.Endpoints.Https.Certificate.KeyPath = $keyPath
            }
        }
        else {
            Write-Host "  [WARN] Certificate file not found at: $certPath" -ForegroundColor Yellow
            Write-Host "  You can configure this later in appsettings.json" -ForegroundColor Yellow
        }
    }

    # CA trust
    $caPath = Read-Host "Internal CA root certificate path (leave empty to skip)"
    if (-not [string]::IsNullOrEmpty($caPath) -and (Test-Path $caPath)) {
        $caTrustDir = Join-Path $InstallPath "certs\ca-trust"
        Copy-Item $caPath -Destination $caTrustDir
        Write-Host "  [OK] CA certificate copied to trust directory" -ForegroundColor Green

        # Import to Windows certificate store
        certutil -addstore -f "Root" $caPath
        Write-Host "  [OK] CA certificate added to Windows trust store" -ForegroundColor Green
    }
}

Write-Host ""

# Port configuration
Write-Host "-- Port Configuration --" -ForegroundColor Yellow
$httpPort = Read-Host "HTTP port (health checks) [8080]"
$httpsPort = Read-Host "HTTPS port (API traffic) [8443]"

if (-not [string]::IsNullOrEmpty($httpPort)) {
    $settings.Kestrel.Endpoints.Http.Url = "http://0.0.0.0:$httpPort"
}
if (-not [string]::IsNullOrEmpty($httpsPort)) {
    $settings.Kestrel.Endpoints.Https.Url = "https://0.0.0.0:$httpsPort"
}

# Save updated settings
$settings | ConvertTo-Json -Depth 10 | Set-Content $settingsPath -Encoding UTF8
Write-Host ""
Write-Host "[OK] Configuration saved to: $settingsPath" -ForegroundColor Green

# Restart service if running
$service = Get-Service -Name "PraxovaToolServer" -ErrorAction SilentlyContinue
if ($service -and $service.Status -eq "Running") {
    $restart = Read-Host "Restart the Tool Server service to apply changes? (Y/n)"
    if ($restart -ne 'n') {
        Restart-Service -Name "PraxovaToolServer"
        Start-Sleep -Seconds 3
        $service = Get-Service -Name "PraxovaToolServer"
        if ($service.Status -eq "Running") {
            Write-Host "[OK] Service restarted successfully" -ForegroundColor Green

            # Quick health check
            $effectiveHttpPort = if ([string]::IsNullOrEmpty($httpPort)) { '8080' } else { $httpPort }
            try {
                $health = Invoke-RestMethod "http://localhost:$effectiveHttpPort/api/v1/health" -TimeoutSec 5
                Write-Host "[OK] Health check: $($health.status) - $($health.message)" -ForegroundColor Green
            }
            catch {
                Write-Host "[WARN] Health check failed: $_" -ForegroundColor Yellow
            }
        }
        else {
            Write-Host "[FAIL] Service failed to start. Check Event Viewer for details." -ForegroundColor Red
        }
    }
}

Write-Host ""
Write-Host "Configuration complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  - Start the service: Start-Service PraxovaToolServer"
Write-Host "  - Check health: Invoke-RestMethod http://localhost:8080/api/v1/health"
Write-Host "  - View logs: Get-EventLog -LogName Application -Source PraxovaToolServer -Newest 20"
Write-Host ""
