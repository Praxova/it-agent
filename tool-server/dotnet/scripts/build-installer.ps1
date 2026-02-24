<#
.SYNOPSIS
    Builds the Praxova Tool Server MSI installer and Setup EXE bootstrapper.

.DESCRIPTION
    Publishes the tool server, builds the MSI, downloads prerequisites,
    and builds the Setup EXE bundle (with embedded VC++ Redistributable).

.PARAMETER Configuration
    Build configuration. Default: Release

.PARAMETER SkipBundle
    Skip the Setup EXE bundle build (produce MSI only).

.EXAMPLE
    .\build-installer.ps1

.EXAMPLE
    .\build-installer.ps1 -Configuration Debug

.EXAMPLE
    .\build-installer.ps1 -SkipBundle
#>

param(
    [string]$Configuration = "Release",
    [switch]$SkipBundle
)

$ErrorActionPreference = "Stop"
$SolutionDir = Join-Path $PSScriptRoot ".."
$PublishDir = Join-Path $SolutionDir "publish"

Write-Host "Building Praxova Tool Server Installer" -ForegroundColor Cyan
Write-Host "=======================================" -ForegroundColor Cyan

# Step 1: Publish the tool server (self-contained)
Write-Host ""
Write-Host "[1/4] Publishing Tool Server (self-contained, win-x64)..." -ForegroundColor Yellow
dotnet publish "$SolutionDir\src\LucidToolServer\LucidToolServer.csproj" `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -o "$PublishDir\app"

# Step 2: Build the MSI
Write-Host ""
Write-Host "[2/4] Building MSI installer..." -ForegroundColor Yellow
dotnet build "$SolutionDir\src\LucidToolServer.Installer\LucidToolServer.Installer.wixproj" `
    -c $Configuration `
    -o "$PublishDir\msi"

# Step 3: Download prerequisites
if (-not $SkipBundle) {
    Write-Host ""
    Write-Host "[3/4] Downloading prerequisites..." -ForegroundColor Yellow

    $prereqDir = Join-Path $SolutionDir "src\LucidToolServer.Bundle\prereqs"
    New-Item -Path $prereqDir -ItemType Directory -Force | Out-Null
    $vcRedistPath = Join-Path $prereqDir "vc_redist.x64.exe"

    if (-not (Test-Path $vcRedistPath)) {
        Write-Host "  Downloading VC++ Redistributable 2015-2022 (x64)..." -ForegroundColor Cyan
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri "https://aka.ms/vs/17/release/vc_redist.x64.exe" `
            -OutFile $vcRedistPath -UseBasicParsing
        $sizeMB = [math]::Round((Get-Item $vcRedistPath).Length / 1MB, 1)
        Write-Host "  Downloaded: $sizeMB MB" -ForegroundColor Green
    }
    else {
        Write-Host "  VC++ Redistributable already cached." -ForegroundColor Green
    }

    # Step 4: Build the Setup EXE bundle
    Write-Host ""
    Write-Host "[4/4] Building Setup EXE bundle..." -ForegroundColor Yellow
    $bundleProj = "$SolutionDir\src\LucidToolServer.Bundle\LucidToolServer.Bundle.wixproj"
    if (Test-Path $bundleProj) {
        dotnet build $bundleProj -c $Configuration -o "$PublishDir\setup"
    }
    else {
        Write-Host "  ERROR: Bundle project not found at $bundleProj" -ForegroundColor Red
        exit 1
    }
}
else {
    Write-Host ""
    Write-Host "[3/4] Skipping prerequisites download (-SkipBundle)" -ForegroundColor Yellow
    Write-Host "[4/4] Skipping Setup EXE bundle (-SkipBundle)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Build complete!" -ForegroundColor Green
Write-Host "  MSI:   $PublishDir\msi\" -ForegroundColor Cyan
if (-not $SkipBundle) {
    Write-Host "  Setup: $PublishDir\setup\PraxovaToolServer-Setup.exe" -ForegroundColor Cyan
}
