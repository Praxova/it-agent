<#
.SYNOPSIS
    Builds the Praxova Tool Server MSI installer and EXE bootstrapper.

.DESCRIPTION
    Publishes the tool server, builds the MSI, and optionally builds
    the EXE bootstrapper (with prerequisites).

.PARAMETER Configuration
    Build configuration. Default: Release

.PARAMETER BootstrapperOnly
    Skip the MSI build and only build the EXE bootstrapper.

.EXAMPLE
    .\build-installer.ps1

.EXAMPLE
    .\build-installer.ps1 -Configuration Debug
#>

param(
    [string]$Configuration = "Release",
    [switch]$BootstrapperOnly
)

$ErrorActionPreference = "Stop"
$SolutionDir = Join-Path $PSScriptRoot ".."
$PublishDir = Join-Path $SolutionDir "publish"

Write-Host "Building Praxova Tool Server Installer" -ForegroundColor Cyan
Write-Host "=======================================" -ForegroundColor Cyan

# Step 1: Publish the tool server
Write-Host ""
Write-Host "[1/3] Publishing Tool Server..." -ForegroundColor Yellow
dotnet publish "$SolutionDir\src\LucidToolServer\LucidToolServer.csproj" `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -o "$PublishDir\app"

# Step 2: Build the MSI
Write-Host ""
Write-Host "[2/3] Building MSI installer..." -ForegroundColor Yellow
dotnet build "$SolutionDir\src\LucidToolServer.Installer\LucidToolServer.Installer.wixproj" `
    -c $Configuration `
    -o "$PublishDir\msi"

# Step 3: Build the EXE bootstrapper (optional)
if (-not $BootstrapperOnly) {
    Write-Host ""
    Write-Host "[3/3] Building EXE bootstrapper..." -ForegroundColor Yellow
    # Build the bundle project if it exists
    $bundleProj = "$SolutionDir\src\LucidToolServer.Installer\Prerequisites\Bundle.wixproj"
    if (Test-Path $bundleProj) {
        dotnet build $bundleProj -c $Configuration -o "$PublishDir\setup"
    }
    else {
        Write-Host "  Skipping bootstrapper (Bundle.wixproj not found)" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "Build complete!" -ForegroundColor Green
Write-Host "  MSI: $PublishDir\msi\" -ForegroundColor Cyan
Write-Host "  EXE: $PublishDir\setup\" -ForegroundColor Cyan
