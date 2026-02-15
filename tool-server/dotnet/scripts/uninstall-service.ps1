#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Uninstalls the Praxova IT Agent Tool Server Windows Service.
#>

$ServiceName = "PraxovaToolServer"

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $existingService) {
    Write-Warning "Service '$ServiceName' is not installed."
    exit 0
}

Write-Host "Stopping service '$ServiceName'..." -ForegroundColor Cyan
Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

Write-Host "Removing service '$ServiceName'..." -ForegroundColor Cyan
sc.exe delete $ServiceName

Write-Host "Service removed successfully." -ForegroundColor Green
