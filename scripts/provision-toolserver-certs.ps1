<#
.SYNOPSIS
    Provision TLS certificates for Praxova Tool Server from the Admin Portal's internal PKI.

.DESCRIPTION
    Calls the Admin Portal API to issue a certificate for the tool server,
    deploys cert+key to the tool server, imports the CA into Windows trusted
    root store, and restarts the service.

.PARAMETER PortalUrl
    Admin Portal URL (e.g., https://10.0.0.10:5001)

.PARAMETER ToolServerHost
    Tool server hostname or IP (e.g., tool01 or 10.0.0.201)

.PARAMETER ToolServerInstallPath
    Path where tool server is installed on the remote machine.
    Default: C:\Program Files\Praxova\ToolServer

.PARAMETER CertName
    Certificate name for PKI tracking. Default: tool-server-01

.PARAMETER AdminCredential
    PSCredential for portal admin login (username/password).
    If not provided, prompts interactively.

.EXAMPLE
    .\provision-toolserver-certs.ps1 -PortalUrl https://10.0.0.10:5001 -ToolServerHost tool01
#>

param(
    [Parameter(Mandatory)]
    [string]$PortalUrl,

    [Parameter(Mandatory)]
    [string]$ToolServerHost,

    [string]$ToolServerInstallPath = "C:\Program Files\Praxova\ToolServer",

    [string]$CertName = "tool-server-01",

    [PSCredential]$AdminCredential
)

$ErrorActionPreference = "Stop"

# Resolve tool server IP for SAN
$toolServerIp = $null
try {
    $resolved = [System.Net.Dns]::GetHostAddresses($ToolServerHost) | Where-Object { $_.AddressFamily -eq 'InterNetwork' } | Select-Object -First 1
    $toolServerIp = $resolved.IPAddressToString
    Write-Host "Resolved $ToolServerHost -> $toolServerIp" -ForegroundColor Cyan
} catch {
    Write-Warning "Could not resolve $ToolServerHost — using hostname only for SANs"
}

# Build SAN lists
$dnsNames = @($ToolServerHost, "localhost")
$ipAddresses = @("127.0.0.1", "::1")
if ($toolServerIp -and $toolServerIp -ne "127.0.0.1") {
    $ipAddresses += $toolServerIp
}

Write-Host "`n=== Provisioning TLS Certificate ===" -ForegroundColor Green
Write-Host "Portal:      $PortalUrl"
Write-Host "Tool Server: $ToolServerHost ($toolServerIp)"
Write-Host "Cert Name:   $CertName"
Write-Host "DNS SANs:    $($dnsNames -join ', ')"
Write-Host "IP SANs:     $($ipAddresses -join ', ')"

# --- Step 1: Login to portal to get JWT ---
if (-not $AdminCredential) {
    $AdminCredential = Get-Credential -Message "Enter Admin Portal credentials"
}

Write-Host "`n--- Authenticating to Admin Portal ---" -ForegroundColor Yellow

# Trust the portal's self-signed cert for this session
# (Portal uses Praxova internal CA which Windows doesn't trust yet)
$trustAllCerts = @"
using System.Net;
using System.Security.Cryptography.X509Certificates;
public class TrustAllCertsPolicy : ICertificatePolicy {
    public bool CheckValidationResult(ServicePoint sp, X509Certificate cert, WebRequest req, int problem) { return true; }
}
"@

try { Add-Type -TypeDefinition $trustAllCerts } catch {}
[System.Net.ServicePointManager]::CertificatePolicy = New-Object TrustAllCertsPolicy
# Also for newer .NET HttpClient
if ([System.Net.ServicePointManager]::SecurityProtocol -notmatch 'Tls12') {
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
}

# Use -SkipCertificateCheck if available (PS 7+)
$skipCertParam = @{}
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $skipCertParam = @{ SkipCertificateCheck = $true }
}

$loginBody = @{
    username = $AdminCredential.UserName
    password = $AdminCredential.GetNetworkCredential().Password
} | ConvertTo-Json

$loginResponse = Invoke-RestMethod -Uri "$PortalUrl/api/auth/login" -Method POST -Body $loginBody -ContentType "application/json" @skipCertParam
$token = $loginResponse.token
if (-not $token) {
    throw "Login failed — no token returned"
}
Write-Host "Authenticated successfully" -ForegroundColor Green

$authHeaders = @{
    Authorization = "Bearer $token"
    "Content-Type" = "application/json"
}

# --- Step 2: Issue certificate via PKI API ---
Write-Host "`n--- Issuing TLS certificate ---" -ForegroundColor Yellow

$issueBody = @{
    name = $CertName
    commonName = $ToolServerHost
    dnsNames = $dnsNames
    ipAddresses = $ipAddresses
    lifetimeDays = 365
} | ConvertTo-Json

$certResponse = Invoke-RestMethod -Uri "$PortalUrl/api/pki/certificates/issue" -Method POST -Headers $authHeaders -Body $issueBody @skipCertParam

if (-not $certResponse.certificatePem) {
    throw "Certificate issuance failed — no PEM in response"
}

Write-Host "Certificate issued successfully" -ForegroundColor Green

# --- Step 3: Deploy certs to tool server ---
Write-Host "`n--- Deploying certificates to $ToolServerHost ---" -ForegroundColor Yellow

$remoteCertDir = "\\$ToolServerHost\$($ToolServerInstallPath.Replace(':', '$'))\certs"

# Create certs directory
if (!(Test-Path $remoteCertDir)) {
    New-Item -Path $remoteCertDir -ItemType Directory -Force | Out-Null
    Write-Host "Created $remoteCertDir"
}

# Write cert files
$certResponse.certificatePem | Out-File -FilePath "$remoteCertDir\toolserver-cert.pem" -Encoding UTF8 -NoNewline
$certResponse.privateKeyPem | Out-File -FilePath "$remoteCertDir\toolserver-key.pem" -Encoding UTF8 -NoNewline
$certResponse.caCertificatePem | Out-File -FilePath "$remoteCertDir\ca.pem" -Encoding UTF8 -NoNewline

Write-Host "Deployed cert, key, and CA to $remoteCertDir" -ForegroundColor Green

# --- Step 4: Import CA into Windows trusted root on tool server ---
Write-Host "`n--- Importing Praxova CA into trusted roots on $ToolServerHost ---" -ForegroundColor Yellow

$importScript = {
    param($CertDir)
    $caPath = Join-Path $CertDir "ca.pem"
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($caPath)
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "LocalMachine")
    $store.Open("ReadWrite")

    # Check if already imported
    $existing = $store.Certificates | Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
    if ($existing) {
        Write-Host "CA already trusted (thumbprint: $($cert.Thumbprint))"
    } else {
        $store.Add($cert)
        Write-Host "CA imported into Trusted Root (thumbprint: $($cert.Thumbprint))"
    }
    $store.Close()
}

Invoke-Command -ComputerName $ToolServerHost -ScriptBlock $importScript -ArgumentList "$ToolServerInstallPath\certs"
Write-Host "CA trusted on $ToolServerHost" -ForegroundColor Green

# --- Step 5: Restrict private key file permissions ---
Write-Host "`n--- Setting key file permissions ---" -ForegroundColor Yellow

$permScript = {
    param($KeyPath)
    $acl = Get-Acl $KeyPath
    $acl.SetAccessRuleProtection($true, $false) # Disable inheritance
    # Remove all existing rules
    $acl.Access | ForEach-Object { $acl.RemoveAccessRule($_) | Out-Null }
    # Add SYSTEM and Administrators only
    $systemRule = New-Object System.Security.AccessControl.FileSystemAccessRule("SYSTEM", "FullControl", "Allow")
    $adminRule = New-Object System.Security.AccessControl.FileSystemAccessRule("BUILTIN\Administrators", "FullControl", "Allow")
    $acl.AddAccessRule($systemRule)
    $acl.AddAccessRule($adminRule)
    Set-Acl $KeyPath $acl
    Write-Host "Key file permissions restricted to SYSTEM and Administrators"
}

Invoke-Command -ComputerName $ToolServerHost -ScriptBlock $permScript -ArgumentList "$ToolServerInstallPath\certs\toolserver-key.pem"

# --- Step 6: Restart tool server service ---
Write-Host "`n--- Restarting PraxovaToolServer service ---" -ForegroundColor Yellow

Invoke-Command -ComputerName $ToolServerHost -ScriptBlock {
    Restart-Service -Name "PraxovaToolServer" -Force
    Start-Sleep -Seconds 3
    $svc = Get-Service -Name "PraxovaToolServer"
    Write-Host "Service status: $($svc.Status)"
}

# --- Step 7: Verify HTTPS ---
Write-Host "`n--- Verifying HTTPS connectivity ---" -ForegroundColor Yellow
Start-Sleep -Seconds 5

try {
    $health = Invoke-RestMethod -Uri "https://${ToolServerHost}:8443/api/v1/health" @skipCertParam
    Write-Host "HTTPS health check: $($health.status)" -ForegroundColor Green
    Write-Host "AD connected: $($health.adConnected)"
} catch {
    Write-Warning "HTTPS health check failed: $_"
    Write-Host "Trying HTTP fallback..."
    try {
        $health = Invoke-RestMethod -Uri "http://${ToolServerHost}:8080/api/v1/health"
        Write-Host "HTTP health check: $($health.status) (HTTPS may need a moment to start)"
    } catch {
        Write-Warning "Both HTTPS and HTTP health checks failed"
    }
}

Write-Host "`n=== Certificate provisioning complete ===" -ForegroundColor Green
Write-Host @"

Next steps:
1. Update tool server URL in Admin Portal to: https://${ToolServerHost}:8443
2. Verify agent can reach tool server over HTTPS
3. Run end-to-end ticket test
"@
