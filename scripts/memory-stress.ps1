param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release',
    [int]$Runs = 3,
    [int]$DurationSec = 15,
    [int]$DelayBetweenRuns = 5,
    [switch]$AutoInstallTools
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Info($msg) { Write-Host "[memstress] $msg" -ForegroundColor Cyan }
function Write-Warn($msg) { Write-Host "[memstress] $msg" -ForegroundColor Yellow }
function Write-Err($msg)  { Write-Host "[memstress] $msg" -ForegroundColor Red }

$profileScript = Join-Path $PSScriptRoot 'memory-profile.ps1'
if (-not (Test-Path $profileScript)) {
    Write-Err "memory-profile.ps1 not found next to this script."
    exit 1
}

Write-Info ("Starting stress profiling: Runs={0}, DurationSec={1}, DelayBetweenRuns={2}, Config={3}" -f $Runs, $DurationSec, $DelayBetweenRuns, $Configuration)

for ($i = 1; $i -le $Runs; $i++) {
    Write-Info ("Run {0}/{1}: capturing in {2}s (interact with the app now)" -f $i, $Runs, $DurationSec)
    try {
        & pwsh -NoProfile -ExecutionPolicy Bypass -File $profileScript -Configuration $Configuration -DurationSec $DurationSec -AutoInstallTools:$AutoInstallTools
    } catch {
        Write-Warn ("Run {0} failed: {1}" -f $i, $_)
    }
    Write-Info ("Completed run {0}/{1}" -f $i, $Runs)
    if ($i -lt $Runs) {
        Write-Info ("Sleeping {0}s before next run..." -f $DelayBetweenRuns)
        Start-Sleep -Seconds $DelayBetweenRuns
    }
}

# Summarize latest artifact folders
try {
    $root = Resolve-Path (Join-Path $PSScriptRoot '..') | Select-Object -ExpandProperty Path
    $profilesDir = Join-Path (Join-Path $root 'publish') 'profiles'
    if (Test-Path $profilesDir) {
        $latest = Get-ChildItem $profilesDir -Directory | Sort-Object Name -Descending | Select-Object -First $Runs
        if ($latest) {
            Write-Info "Recent artifact folders:"
            foreach ($d in $latest) { Write-Host " - $($d.FullName)" }
        }
    }
} catch { }

Write-Info "Stress profiling complete."
