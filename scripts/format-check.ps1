param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Debug',
    [switch]$Fix
)

$PSNativeCommandUseErrorActionPreference = $true
$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $PSScriptRoot
$errorLog   = Join-Path $repoRoot 'error.log'

Write-Host "[format-check] Configuration=$Configuration Fix=$Fix"

# Per updated directive: do not perform any linting in any configuration. Always skip and log.
$msg = "$(Get-Date -Format o) [format-check] Skipped dotnet format by directive (Configuration=$Configuration)."
try { Add-Content -Path $errorLog -Value $msg -Encoding UTF8 } catch {}
Write-Host $msg
exit 0
