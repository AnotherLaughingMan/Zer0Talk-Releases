#!/usr/bin/env pwsh
#Requires -Version 7.0

<#
.SYNOPSIS
    Builds the Zer0Talk MSI installer package.

.DESCRIPTION
    This script builds the complete Zer0Talk installer by:
    1. Publishing the main application
    2. Publishing the uninstaller
    3. Building the WiX MSI package

.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Release

.PARAMETER SkipPublish
    Skip publishing the application and just build the MSI

.EXAMPLE
    .\build-installer.ps1
    
.EXAMPLE
    .\build-installer.ps1 -Configuration Debug
#>

[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

# Paths
$rootDir = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $rootDir "publish\win-x64-$Configuration"
$uninstallerProject = Join-Path $rootDir "Uninstaller\Zer0Talk.Uninstaller.csproj"
$mainProject = Join-Path $rootDir "Zer0Talk.csproj"
$installerProject = Join-Path $PSScriptRoot "Zer0Talk.Installer.wixproj"

Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host "   Zer0Talk Installer Build Script" -ForegroundColor Cyan
Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host ""

# Check for WiX toolset
Write-Host "Checking for WiX Toolset..." -ForegroundColor Yellow
try {
    $wixVersion = & dotnet tool list --global | Select-String "wix"
    if ($wixVersion) {
        Write-Host "✓ WiX Toolset found: $wixVersion" -ForegroundColor Green
    } else {
        throw "WiX not found"
    }
} catch {
    Write-Host "✗ WiX Toolset not found!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Install WiX Toolset with:" -ForegroundColor Yellow
    Write-Host "  dotnet tool install --global wix" -ForegroundColor White
    Write-Host ""
    exit 1
}

if (-not $SkipPublish) {
    # Step 1: Publish main application
    Write-Host ""
    Write-Host "Step 1: Publishing main application..." -ForegroundColor Cyan
    Write-Host "---------------------------------------" -ForegroundColor Gray
    
    & dotnet publish $mainProject `
        -c $Configuration `
        -r win-x64 `
        --self-contained `
        -p:PublishSingleFile=false `
        -o $publishDir
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Failed to publish main application" -ForegroundColor Red
        exit 1
    }
    Write-Host "✓ Main application published to: $publishDir" -ForegroundColor Green

    # Step 2: Publish uninstaller
    Write-Host ""
    Write-Host "Step 2: Publishing uninstaller..." -ForegroundColor Cyan
    Write-Host "---------------------------------------" -ForegroundColor Gray
    
    & dotnet publish $uninstallerProject `
        -c $Configuration `
        -r win-x64 `
        --self-contained `
        -p:PublishSingleFile=true
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Failed to publish uninstaller" -ForegroundColor Red
        exit 1
    }
    Write-Host "✓ Uninstaller published" -ForegroundColor Green
}

# Step 3: Build MSI
Write-Host ""
Write-Host "Step 3: Building MSI installer..." -ForegroundColor Cyan
Write-Host "---------------------------------------" -ForegroundColor Gray

& dotnet build $installerProject -c $Configuration

if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ Failed to build MSI" -ForegroundColor Red
    exit 1
}

$msiPath = Join-Path $PSScriptRoot "bin\$Configuration\Zer0Talk-Setup.msi"

Write-Host ""
Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host "✓ Build Complete!" -ForegroundColor Green
Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "MSI Installer: $msiPath" -ForegroundColor White
Write-Host ""

# Check MSI size
if (Test-Path $msiPath) {
    $msiSize = (Get-Item $msiPath).Length / 1MB
    Write-Host "Installer size: $([math]::Round($msiSize, 2)) MB" -ForegroundColor Gray
    Write-Host ""
}

Write-Host "To install, run:" -ForegroundColor Yellow
Write-Host "  msiexec /i `"$msiPath`"" -ForegroundColor White
Write-Host ""
Write-Host "To install silently:" -ForegroundColor Yellow
Write-Host "  msiexec /i `"$msiPath`" /quiet" -ForegroundColor White
Write-Host ""
