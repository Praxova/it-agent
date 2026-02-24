#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────
# Praxova — Build Tool Server on Windows Build VM
# Syncs source to build01, compiles, pulls artifacts back.
# ─────────────────────────────────────────────────────────────────
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
ARTIFACTS_DIR="${PROJECT_ROOT}/build/artifacts"
TOOL_SERVER_SRC="${PROJECT_ROOT}/tool-server/dotnet"

# Build VM settings
BUILD_HOST="${BUILD_HOST:-Administrator@build01}"
BUILD_DIR="C:/build/praxova-toolserver"

# Defaults
BUILD_MSI=false
CONFIGURATION="Release"

usage() {
    cat <<EOF
Usage: $(basename "$0") [OPTIONS]

Build Praxova Tool Server on Windows build VM (build01).

Options:
  --msi             Build MSI + Setup EXE bundle with prerequisites (requires WiX)
  --config CONFIG   Build configuration (default: Release)
  --host USER@HOST  Build VM SSH target (default: Administrator@build01)
  -h, --help        Show this help message

Prerequisites:
  - SSH key-based or password auth to build01
  - .NET 8 SDK installed on build01
  - WiX Toolset 5.0.1+ installed on build01 (for --msi)
EOF
    exit 0
}

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --msi) BUILD_MSI=true; shift ;;
        --config) CONFIGURATION="$2"; shift 2 ;;
        --host) BUILD_HOST="$2"; shift 2 ;;
        -h|--help) usage ;;
        -*) echo "ERROR: Unknown option: $1" >&2; exit 1 ;;
        *) echo "ERROR: Unexpected argument: $1" >&2; exit 1 ;;
    esac
done

echo "═══════════════════════════════════════════════════════════════"
echo "  Praxova Tool Server Build"
echo "  Host:    ${BUILD_HOST}"
echo "  Config:  ${CONFIGURATION}"
echo "  MSI+Bundle: ${BUILD_MSI}"
echo "═══════════════════════════════════════════════════════════════"

# Create artifacts directory
mkdir -p "$ARTIFACTS_DIR"

# ── Step 1: Sync source to build VM ──────────────────────────────
echo ""
echo "── [1/5] Syncing source to build VM ───────────────────────────"

# Create build directory on remote
ssh "$BUILD_HOST" "New-Item -Path 'C:\build\praxova-toolserver' -ItemType Directory -Force | Out-Null"

# Clean remote build dir and copy source via scp
ssh "$BUILD_HOST" "Remove-Item -Path '${BUILD_DIR}\*' -Recurse -Force -ErrorAction SilentlyContinue"
scp -r "${TOOL_SERVER_SRC}/src" "${TOOL_SERVER_SRC}/tests" "${TOOL_SERVER_SRC}/LucidToolServer.sln" "${BUILD_HOST}:${BUILD_DIR}/"

echo "  Source synced."

# ── Step 2: Restore + Publish (self-contained) ──────────────────
echo ""
echo "── [2/5] Building tool server (self-contained, win-x64) ──────"

ssh "$BUILD_HOST" "Set-Location '${BUILD_DIR}'; dotnet restore"

ssh "$BUILD_HOST" "Set-Location '${BUILD_DIR}'; dotnet publish src/LucidToolServer/LucidToolServer.csproj -c ${CONFIGURATION} -r win-x64 --self-contained true -o publish/win-service"

echo "  Publish complete."

# ── Step 3: Build MSI + Setup EXE bundle (optional) ──────────────
if [[ "$BUILD_MSI" == "true" ]]; then
    echo ""
    echo "── [3/5] Building MSI installer ───────────────────────────────"
    ssh "$BUILD_HOST" "Set-Location '${BUILD_DIR}'; dotnet build src/LucidToolServer.Installer/LucidToolServer.Installer.wixproj -c ${CONFIGURATION} -o publish/msi"
    echo "  MSI build complete."

    echo ""
    echo "── [3.5/5] Downloading prerequisites + building Setup EXE ─────"

    # Download VC++ Redistributable to bundle prereqs dir (cached across builds)
    ssh "$BUILD_HOST" "$(cat <<'PWSH'
$prereqDir = "C:\build\praxova-toolserver\src\LucidToolServer.Bundle\prereqs"
New-Item -Path $prereqDir -ItemType Directory -Force | Out-Null
$vcRedistPath = Join-Path $prereqDir "vc_redist.x64.exe"
if (-not (Test-Path $vcRedistPath)) {
    Write-Host "  Downloading VC++ Redistributable..."
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri "https://aka.ms/vs/17/release/vc_redist.x64.exe" -OutFile $vcRedistPath -UseBasicParsing
    $sizeMB = [math]::Round((Get-Item $vcRedistPath).Length / 1MB, 1)
    Write-Host "  Downloaded: ${sizeMB} MB"
} else {
    Write-Host "  VC++ Redistributable already cached."
}
PWSH
)"

    # Build the Bundle (bootstrapper EXE with embedded prereqs + MSI)
    ssh "$BUILD_HOST" "Set-Location '${BUILD_DIR}'; dotnet build src/LucidToolServer.Bundle/LucidToolServer.Bundle.wixproj -c ${CONFIGURATION} -o publish/setup"
    echo "  Setup EXE bundle complete."
else
    echo ""
    echo "── [3/5] Skipping MSI + bundle (use --msi to enable) ──────────"
fi

# ── Step 4: Bundle deployment scripts ────────────────────────────
echo ""
echo "── [4/5] Bundling deployment scripts ────────────────────────────"

ssh "$BUILD_HOST" "New-Item -Path '${BUILD_DIR}/publish/win-service/scripts' -ItemType Directory -Force | Out-Null"
scp "${PROJECT_ROOT}/scripts/provision-toolserver-certs.ps1" "${BUILD_HOST}:${BUILD_DIR}/publish/win-service/scripts/"
echo "  Bundled provision-toolserver-certs.ps1"

# ── Step 5: Pull artifacts back ──────────────────────────────────
echo ""
echo "── [5/5] Pulling artifacts to local machine ───────────────────"

# Create a zip of the published output for easy transfer
ssh "$BUILD_HOST" "Set-Location '${BUILD_DIR}/publish'; Remove-Item -Path win-service.zip -ErrorAction SilentlyContinue; Compress-Archive -Path 'win-service\*' -DestinationPath win-service.zip -Force"

# Pull the zip
scp "${BUILD_HOST}:${BUILD_DIR}/publish/win-service.zip" "${ARTIFACTS_DIR}/praxova-toolserver.zip"

if [[ "$BUILD_MSI" == "true" ]]; then
    echo "  Pulling MSI..."
    scp "${BUILD_HOST}:${BUILD_DIR}/publish/msi/*.msi" "${ARTIFACTS_DIR}/" 2>/dev/null || echo "  No MSI found in output."
    echo "  Pulling Setup EXE..."
    scp "${BUILD_HOST}:${BUILD_DIR}/publish/setup/PraxovaToolServer-Setup.exe" "${ARTIFACTS_DIR}/" 2>/dev/null || echo "  No Setup EXE found in output."
fi

# ── Summary ──────────────────────────────────────────────────────
echo ""
echo "── Build Artifacts ────────────────────────────────────────────"
printf "%-45s %10s\n" "FILE" "SIZE"
echo "───────────────────────────────────────────────────────────────"
for f in "${ARTIFACTS_DIR}"/praxova-toolserver* "${ARTIFACTS_DIR}"/PraxovaToolServer-Setup*; do
    if [[ -f "$f" ]]; then
        SIZE=$(du -h "$f" | cut -f1)
        printf "%-45s %10s\n" "$(basename "$f")" "$SIZE"
    fi
done

echo ""
if [[ "$BUILD_MSI" == "true" ]]; then
    echo "Build complete. Primary artifact: ${ARTIFACTS_DIR}/PraxovaToolServer-Setup.exe"
else
    echo "Build complete. Artifact: ${ARTIFACTS_DIR}/praxova-toolserver.zip"
fi
