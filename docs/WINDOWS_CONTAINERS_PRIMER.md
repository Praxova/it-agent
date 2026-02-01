# Windows Containers Primer for Lucid IT Agent

This guide covers Windows container concepts for someone familiar with Linux containers.

## Key Differences from Linux Containers

### 1. Two Isolation Modes

| Mode | Description | Use Case |
|------|-------------|----------|
| **Process Isolation** | Container shares host kernel | Production (what we'll use) |
| **Hyper-V Isolation** | Container has its own kernel | Untrusted workloads, different OS versions |

For Lucid Tool Server, **Process Isolation** is correct because:
- Lower overhead (no hypervisor)
- gMSA works seamlessly (container uses host's domain membership)
- Requires OS version match (container ≤ host), which is fine for controlled deployments

### 2. OS Version Compatibility

Unlike Linux (kernel ABI stable), Windows containers require version matching:

| Host OS | Can Run Container Base Image |
|---------|------------------------------|
| Server 2022 (LTSC) | 2022, 2019, 1809 |
| Server 2019 (LTSC) | 2019, 1809 |

**Our choice**: `windowsservercore-ltsc2022` base image
- Most customers have Server 2022
- Server 2019 hosts can use Hyper-V isolation as fallback

### 3. Image Size

```
Linux:  aspnet:8.0 (~200MB)
Windows: aspnet:8.0-windowsservercore-ltsc2022 (~5GB)
```

First pull is slow, but layers are cached. Subsequent builds are fast.

### 4. Networking

Windows containers support the same concepts:
- `--publish 8080:80` works exactly like Linux
- Container gets its own IP (NAT by default)
- `host` network mode available but rarely needed

---

## Setup: Container Host Prerequisites

### On Your Windows Server (Member Server, Not DC)

```powershell
# 1. Install Windows Containers feature
Install-WindowsFeature -Name Containers

# 2. Install Docker
# Option A: Docker Desktop (has GUI, good for dev)
# Download from: https://www.docker.com/products/docker-desktop/

# Option B: Docker Engine (CLI only, lighter)
Invoke-WebRequest -UseBasicParsing "https://raw.githubusercontent.com/microsoft/Windows-Containers/Main/helpful_tools/Install-DockerCE/install-docker-ce.ps1" -o install-docker-ce.ps1
.\install-docker-ce.ps1

# 3. Reboot required
Restart-Computer -Force

# 4. After reboot, verify Docker
docker version
docker run hello-world:nanoserver-ltsc2022
```

### For gMSA Support (Later - Can Skip for Initial Dev)

```powershell
# On Domain Controller
# 1. Create KDS root key (only once per domain, may already exist)
Add-KdsRootKey -EffectiveImmediately  # Dev only; production: -EffectiveTime ((Get-Date).AddHours(-10))

# 2. Create gMSA
New-ADServiceAccount -Name svc-lucid-tools `
    -DNSHostName svc-lucid-tools.montanifarms.com `
    -PrincipalsAllowedToRetrieveManagedPassword "Domain Computers" `
    -ServicePrincipalNames "HTTP/svc-lucid-tools.montanifarms.com"

# On Container Host (Member Server)
# 3. Install AD module and test gMSA
Install-WindowsFeature RSAT-AD-PowerShell
Install-ADServiceAccount svc-lucid-tools
Test-ADServiceAccount svc-lucid-tools  # Should return True
```

---

## Development Workflow

### Your Development Machine (Linux - VS Code)

```bash
# Install .NET 8 SDK on Linux
wget https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 8.0

# Or via package manager (Ubuntu)
sudo apt-get install -y dotnet-sdk-8.0

# Verify
dotnet --version  # Should show 8.x.x
```

### Project Structure

```
tool-server/
├── dotnet/
│   ├── LucidToolServer.sln
│   ├── src/
│   │   └── LucidToolServer/
│   │       ├── LucidToolServer.csproj
│   │       ├── Program.cs                 # Minimal API entry point
│   │       ├── Services/
│   │       │   ├── IActiveDirectoryService.cs
│   │       │   ├── ActiveDirectoryService.cs
│   │       │   ├── IFilePermissionService.cs
│   │       │   └── FilePermissionService.cs
│   │       ├── Models/
│   │       │   ├── PasswordResetRequest.cs
│   │       │   ├── PasswordResetResponse.cs
│   │       │   └── ...
│   │       ├── appsettings.json
│   │       └── Dockerfile
│   └── tests/
│       └── LucidToolServer.Tests/
│           ├── LucidToolServer.Tests.csproj
│           └── Services/
│               ├── ActiveDirectoryServiceTests.cs
│               └── FilePermissionServiceTests.cs
```

### Build & Test Locally (Linux)

```bash
cd tool-server/dotnet

# Restore packages
dotnet restore

# Build
dotnet build

# Run unit tests (mocked AD, works on Linux)
dotnet test

# Run locally (will fail AD calls, but API endpoints work)
cd src/LucidToolServer
dotnet run
# API available at http://localhost:5000
```

### Build Container & Test on Windows

On your Windows Server:

```powershell
# Clone/copy the code to Windows
cd C:\Projects\lucid-it-agent\tool-server\dotnet

# Build the container image
docker build -t lucid-tool-server:dev -f src/LucidToolServer/Dockerfile .

# Run WITHOUT gMSA (uses current user context - good for initial testing)
docker run -d -p 8080:8080 --name lucid-tools lucid-tool-server:dev

# Test health endpoint
Invoke-RestMethod http://localhost:8080/api/v1/health

# View logs
docker logs lucid-tools

# Stop and remove
docker stop lucid-tools
docker rm lucid-tools
```

### Run WITH gMSA (Production-like)

```powershell
# First, create credential spec (one-time setup)
# Requires CredentialSpec PowerShell module
Install-Module CredentialSpec -Force
New-CredentialSpec -AccountName svc-lucid-tools

# This creates: C:\ProgramData\Docker\credentialspecs\svc-lucid-tools.json

# Run container with gMSA
docker run -d -p 8080:8080 `
    --name lucid-tools `
    --security-opt "credentialspec=file://svc-lucid-tools.json" `
    --hostname svc-lucid-tools `
    lucid-tool-server:dev

# Now AD operations use gMSA identity automatically
```

---

## Dockerfile Example

```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0-windowsservercore-ltsc2022 AS build
WORKDIR /src

# Copy csproj and restore (layer caching)
COPY ["src/LucidToolServer/LucidToolServer.csproj", "LucidToolServer/"]
RUN dotnet restore "LucidToolServer/LucidToolServer.csproj"

# Copy everything and build
COPY src/ .
WORKDIR /src/LucidToolServer
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0-windowsservercore-ltsc2022
WORKDIR /app

# Create non-admin user (security best practice)
# Note: With gMSA, the container runs as the gMSA identity
USER ContainerUser

COPY --from=build /app/publish .

# Health check
HEALTHCHECK --interval=30s --timeout=10s --retries=3 \
    CMD curl -f http://localhost:8080/api/v1/health || exit 1

EXPOSE 8080
ENTRYPOINT ["dotnet", "LucidToolServer.dll"]
```

---

## Common Docker Commands (Windows vs Linux)

| Task | Linux | Windows |
|------|-------|---------|
| List containers | `docker ps` | `docker ps` |
| View logs | `docker logs <name>` | `docker logs <name>` |
| Shell into container | `docker exec -it <name> /bin/bash` | `docker exec -it <name> powershell` |
| Build image | `docker build -t name .` | `docker build -t name .` |
| Pull base image | `docker pull mcr.microsoft.com/dotnet/aspnet:8.0` | `docker pull mcr.microsoft.com/dotnet/aspnet:8.0-windowsservercore-ltsc2022` |

Most commands are identical. The main differences:
- Base image tags include Windows version
- Shell is `powershell` or `cmd` instead of `bash`
- gMSA requires `--security-opt credentialspec=...`

---

## Troubleshooting

### "no matching manifest for windows/amd64"
You're trying to pull a Linux image on Windows. Use a Windows-specific tag:
```powershell
# Wrong
docker pull mcr.microsoft.com/dotnet/aspnet:8.0

# Right
docker pull mcr.microsoft.com/dotnet/aspnet:8.0-windowsservercore-ltsc2022
```

### "The container operating system does not match the host operating system"
Host/container version mismatch. Either:
- Use a container base image matching your host
- Switch to Hyper-V isolation: `docker run --isolation=hyperv ...`

### gMSA: "The specified account does not exist"
```powershell
# On container host, verify gMSA is installed
Test-ADServiceAccount svc-lucid-tools

# If False, install it
Install-ADServiceAccount svc-lucid-tools
```

### Container can't reach AD
- Ensure container host is domain-joined
- Check DNS: container should use domain DNS servers
- Test from host first: `Test-ComputerSecureChannel`

---

## Next Steps

1. **Set up Container Host**: Install Docker on your member server
2. **Basic Container Test**: `docker run hello-world:nanoserver-ltsc2022`
3. **Scaffold .NET Project**: Create solution structure on your Linux dev machine
4. **Build First Image**: Get a "hello world" API running in container
5. **Add AD Service**: Implement password reset
6. **Configure gMSA**: For production-like testing

I recommend doing steps 1-4 before worrying about gMSA. You can test the API structure and most code paths without it.
