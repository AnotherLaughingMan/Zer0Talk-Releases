#Requires -RunAsAdministrator

<#
.SYNOPSIS
    Removes old ZTalk and P2PTalk firewall rules from Windows Firewall.

.DESCRIPTION
    This script removes legacy firewall rules for ZTalk and P2PTalk applications
    to clean up after the migration to Zer0Talk. Requires administrator privileges.

.EXAMPLE
    .\clear-old-firewall-rules.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host "   Zer0Talk - Legacy Firewall Rule Cleanup" -ForegroundColor Cyan
Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host ""

# Check if running as administrator
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ERROR: This script must be run as Administrator!" -ForegroundColor Red
    Write-Host "Right-click PowerShell and select 'Run as Administrator'" -ForegroundColor Yellow
    exit 1
}

# Define legacy application names to search for
$legacyNames = @(
    "ZTalk",
    "P2PTalk",
    "ztalk",
    "p2ptalk"
)

Write-Host "Searching for legacy firewall rules..." -ForegroundColor Yellow
Write-Host ""

$removedCount = 0
$totalFound = 0

foreach ($name in $legacyNames) {
    try {
        # Get all firewall rules matching the legacy name
        $rules = Get-NetFirewallRule -DisplayName "*$name*" -ErrorAction SilentlyContinue
        
        if ($rules) {
            foreach ($rule in $rules) {
                $totalFound++
                Write-Host "Found: $($rule.DisplayName)" -ForegroundColor Cyan
                Write-Host "  Direction: $($rule.Direction)" -ForegroundColor Gray
                Write-Host "  Action: $($rule.Action)" -ForegroundColor Gray
                Write-Host "  Enabled: $($rule.Enabled)" -ForegroundColor Gray
                
                try {
                    Remove-NetFirewallRule -Name $rule.Name -ErrorAction Stop
                    Write-Host "  [REMOVED]" -ForegroundColor Green
                    $removedCount++
                } catch {
                    Write-Host "  [FAILED] $($_.Exception.Message)" -ForegroundColor Red
                }
                Write-Host ""
            }
        }
    } catch {
        Write-Host "Error searching for '$name': $($_.Exception.Message)" -ForegroundColor Red
    }
}

# Also search by program path patterns
Write-Host "Searching for rules by program path..." -ForegroundColor Yellow
Write-Host ""

try {
    $allRules = Get-NetFirewallRule | Where-Object {
        $appFilter = $_ | Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue
        if ($appFilter -and $appFilter.Program) {
            $appFilter.Program -match "ZTalk|P2PTalk|ztalk|p2ptalk"
        }
    }
    
    if ($allRules) {
        foreach ($rule in $allRules) {
            $appFilter = $rule | Get-NetFirewallApplicationFilter
            $totalFound++
            
            Write-Host "Found: $($rule.DisplayName)" -ForegroundColor Cyan
            Write-Host "  Program: $($appFilter.Program)" -ForegroundColor Gray
            Write-Host "  Direction: $($rule.Direction)" -ForegroundColor Gray
            
            try {
                Remove-NetFirewallRule -Name $rule.Name -ErrorAction Stop
                Write-Host "  [REMOVED]" -ForegroundColor Green
                $removedCount++
            } catch {
                Write-Host "  [FAILED] $($_.Exception.Message)" -ForegroundColor Red
            }
            Write-Host ""
        }
    }
} catch {
    Write-Host "Error searching by program path: $($_.Exception.Message)" -ForegroundColor Yellow
}

# Summary
Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host "Summary:" -ForegroundColor White
Write-Host "  Total legacy rules found: $totalFound" -ForegroundColor White
Write-Host "  Successfully removed: $removedCount" -ForegroundColor Green

if ($removedCount -lt $totalFound) {
    Write-Host "  Failed to remove: $($totalFound - $removedCount)" -ForegroundColor Red
}

if ($totalFound -eq 0) {
    Write-Host "  No legacy firewall rules found. System is clean!" -ForegroundColor Green
}

Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Cleanup complete!" -ForegroundColor Green
