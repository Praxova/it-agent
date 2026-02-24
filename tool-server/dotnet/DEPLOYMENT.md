# Praxova IT Agent Tool Server — Deployment Guide

## Recommended: Setup EXE (PraxovaToolServer-Setup.exe)

The Setup EXE is the preferred distribution artifact. It embeds the VC++
Redistributable and the MSI installer in a single file. Prerequisites are
detected automatically and only installed if missing.

> The tool server is published **self-contained** — the .NET runtime is
> bundled in the app. Only the VC++ Redistributable is a true prerequisite.

### Interactive install

Double-click `PraxovaToolServer-Setup.exe` or run from an elevated prompt.

### Silent install (SCCM / Intune / GPO)

```cmd
PraxovaToolServer-Setup.exe /quiet ServiceAccount="MONTANIFARMS\svc-toolserver$" DomainName="montanifarms.com"
```

### Full options (silent)

```cmd
PraxovaToolServer-Setup.exe /quiet ^
    ServiceAccount="MONTANIFARMS\svc-toolserver$" ^
    DomainName="montanifarms.com" ^
    HttpsPort="8443" ^
    HttpPort="8080" ^
    CertPath="C:\certs\toolserver.pfx" ^
    InstallFolder="D:\Praxova\ToolServer"
```

### Silent uninstall

```cmd
PraxovaToolServer-Setup.exe /uninstall /quiet
```

### Bundle Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `ServiceAccount` | `LocalSystem` | Service account. Use `DOMAIN\account$` for gMSA |
| `ServiceAccountPassword` | *(empty)* | Password for domain accounts (not needed for gMSA/LocalSystem) |
| `DomainName` | *(empty)* | Active Directory domain name |
| `HttpsPort` | `8443` | HTTPS port for API traffic |
| `HttpPort` | `8080` | HTTP port for health checks |
| `CertPath` | *(empty)* | Path to TLS certificate (PEM or PFX) |
| `CertKeyPath` | *(empty)* | Path to private key (PEM only, not needed for PFX) |
| `InstallFolder` | `C:\Program Files\Praxova\ToolServer` | Override installation directory |

> **Note**: Bundle variable names use camelCase (e.g., `ServiceAccount`),
> not the MSI property names (e.g., `SERVICE_ACCOUNT`). The bundle
> translates them automatically.

## MSI-Only Installation (Advanced)

For environments where IT manages prerequisites separately (e.g., SCCM
prerequisite chains), the raw MSI is also available.

### Full options

```cmd
msiexec /i PraxovaToolServer.msi /qn ^
    SERVICE_ACCOUNT="MONTANIFARMS\svc-toolserver$" ^
    DOMAIN_NAME="montanifarms.com" ^
    HTTPS_PORT="8443" ^
    HTTP_PORT="8080" ^
    CERT_PATH="C:\certs\toolserver.pfx" ^
    INSTALLFOLDER="D:\Praxova\ToolServer"
```

### Minimal (LocalSystem, default ports)

```cmd
msiexec /i PraxovaToolServer.msi /qn DOMAIN_NAME="contoso.com"
```

### Silent uninstall

```cmd
msiexec /x PraxovaToolServer.msi /qn
```

### MSI Properties

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

### From Linux (via SSH to build VM)

```bash
make build-toolserver-msi
```

Output in `build/artifacts/`:
- `PraxovaToolServer-Setup.exe` — Setup EXE with embedded prerequisites
- `*.msi` — Raw MSI installer
- `praxova-toolserver.zip` — Published binaries

### From Windows (directly on build VM)

```powershell
cd tool-server\dotnet
.\scripts\build-installer.ps1
```

Output:
- `publish/setup/PraxovaToolServer-Setup.exe` — Setup EXE bundle
- `publish/msi/` — MSI installer

## Prerequisites

The Setup EXE (`PraxovaToolServer-Setup.exe`) embeds and automatically installs:
- **Visual C++ Redistributable 2015-2022 (x64)** — skipped if already present

The .NET runtime is **not** required — the tool server is published self-contained.

For MSI-only deployment, ensure the VC++ Redistributable is installed separately.
