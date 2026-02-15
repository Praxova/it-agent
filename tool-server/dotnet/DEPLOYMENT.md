# Praxova IT Agent Tool Server — Deployment Guide

## Silent Installation (SCCM / GPO / Intune)

### Full options

```powershell
msiexec /i PraxovaToolServer.msi /qn ^
    SERVICE_ACCOUNT="MONTANIFARMS\svc-toolserver$" ^
    DOMAIN_NAME="montanifarms.com" ^
    HTTPS_PORT="8443" ^
    HTTP_PORT="8080" ^
    CERT_PATH="C:\certs\toolserver.pfx" ^
    INSTALLFOLDER="D:\Praxova\ToolServer"
```

### Minimal (LocalSystem, default ports)

```powershell
msiexec /i PraxovaToolServer.msi /qn DOMAIN_NAME="contoso.com"
```

### Silent uninstall

```powershell
msiexec /x PraxovaToolServer.msi /qn
```

## MSI Properties

| Property | Default | Description |
|----------|---------|-------------|
| `SERVICE_ACCOUNT` | `LocalSystem` | Service account. Use `DOMAIN\account$` for gMSA |
| `SERVICE_ACCOUNT_PASSWORD` | *(empty)* | Password for domain accounts (not needed for gMSA/LocalSystem) |
| `DOMAIN_NAME` | *(empty)* | Active Directory domain name |
| `HTTPS_PORT` | `8443` | HTTPS port for API traffic |
| `HTTP_PORT` | `8080` | HTTP port for health checks |
| `CERT_PATH` | *(empty)* | Path to TLS certificate (PEM or PFX) |
| `CERT_KEY_PATH` | *(empty)* | Path to private key (PEM only, not needed for PFX) |
| `INSTALLFOLDER` | `C:\Program Files\Praxova\ToolServer` | Installation directory |

## Post-Install Configuration

After installation, run the interactive configuration script:

```powershell
cd "C:\Program Files\Praxova\ToolServer"
.\configure-toolserver.ps1
```

This configures:
- AD domain connection and connectivity test
- TLS certificate paths
- Port settings
- Service restart with health check

## Service Management

```powershell
# Start the service
Start-Service PraxovaToolServer

# Stop the service
Stop-Service PraxovaToolServer

# Check service status
Get-Service PraxovaToolServer

# View recent logs
Get-EventLog -LogName Application -Source PraxovaToolServer -Newest 20

# Health check
Invoke-RestMethod http://localhost:8080/api/v1/health
```

## Building the Installer

From the `tool-server/dotnet/` directory:

```powershell
.\scripts\build-installer.ps1
```

Output:
- `publish/msi/` — MSI installer (for SCCM/GPO deployment)
- `publish/setup/` — EXE bootstrapper (includes prerequisite detection)

## Prerequisites

The EXE bootstrapper (`PraxovaToolServer-Setup.exe`) automatically detects and installs:
- .NET 8 ASP.NET Core Runtime
- Visual C++ Redistributable 2015-2022

For MSI-only deployment (SCCM/GPO), ensure prerequisites are installed separately.
