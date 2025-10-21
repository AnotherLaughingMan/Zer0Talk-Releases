param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release',
    [switch]$AutoInstallTools,
    [int]$DurationSec = 15
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Info($msg) { Write-Host "[memprof] $msg" -ForegroundColor Cyan }
function Write-Warn($msg) { Write-Host "[memprof] $msg" -ForegroundColor Yellow }
function Write-Err($msg)  { Write-Host "[memprof] $msg" -ForegroundColor Red }

function Ensure-UserDotnetToolsPath() {
    try {
        $toolsDir = Join-Path $env:USERPROFILE ".dotnet\tools"
        if (Test-Path $toolsDir) {
            $pathParts = $env:PATH.Split([IO.Path]::PathSeparator)
            if (-not ($pathParts -contains $toolsDir)) {
                $env:PATH = "$toolsDir" + [IO.Path]::PathSeparator + $env:PATH
                Write-Info "Added .NET tools to PATH: $toolsDir"
            }
        }
    } catch { }
}

function Ensure-DotnetTool($name) {
    Ensure-UserDotnetToolsPath
    if (Get-Command $name -ErrorAction SilentlyContinue) { return $true }
    if (-not $AutoInstallTools) { return $false }
    try {
        Write-Info ("Installing dotnet tool: {0}" -f $name)
        dotnet tool install -g $name | Out-Null
        Ensure-UserDotnetToolsPath
        return $true
    } catch {
        Write-Warn ("Failed to install {0}: {1}" -f $name, $_)
        return $false
    }
}

if (-not (Ensure-DotnetTool 'dotnet-gcdump')) {
    Write-Err "dotnet-gcdump is required. Install with: dotnet tool install -g dotnet-gcdump"
    exit 2
}

$root = Resolve-Path "${PSScriptRoot}\.." | Select-Object -ExpandProperty Path
$publish = Join-Path $root 'publish'
New-Item -ItemType Directory -Force -Path $publish | Out-Null
$profilesDir = Join-Path $publish 'profiles'
New-Item -ItemType Directory -Force -Path $profilesDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outDir = Join-Path $profilesDir "mem-$stamp"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# Try to find an existing process
$proc = Get-Process -Name 'Zer0Talk' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) {
    # Try to launch
    $exeCandidates = @()
    $exeCandidates += Join-Path $publish "win-x64-$Configuration-sc\Zer0Talk.exe"
    $exeCandidates += Join-Path $publish "win-x64-$Configuration\Zer0Talk.exe"
    $exeCandidates += Join-Path $root "bin\$Configuration\net9.0\win-x64\Zer0Talk.exe"
    $exe = $exeCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $exe) {
    Write-Err "Unable to find Zer0Talk executable. Build/publish the app first."
        exit 3
    }
    Write-Info "Launching: $exe"
    Start-Process -FilePath $exe | Out-Null
    Start-Sleep -Seconds 3
    $proc = Get-Process -Name 'Zer0Talk' -ErrorAction SilentlyContinue | Select-Object -First 1
}

if (-not $proc) { Write-Err "Zer0Talk process not found."; exit 4 }
Write-Info ("Profiling PID {0} (waiting {1}s before collect)" -f $proc.Id, $DurationSec)
Start-Sleep -Seconds $DurationSec

$gcdumpPath = Join-Path $outDir 'heap.gcdump'
& dotnet-gcdump collect -p $proc.Id -o $gcdumpPath 2>&1 | Tee-Object -FilePath (Join-Path $outDir 'gcdump-collect.log') | Out-Null

# Verify dump was created
if (-not (Test-Path $gcdumpPath)) {
    Write-Err "GC dump was not created. See gcdump-collect.log for details."
    Get-Content (Join-Path $outDir 'gcdump-collect.log') -TotalCount 50 | ForEach-Object { Write-Warn $_ }
    exit 5
}

Write-Info "Analyzing GC dump"
$analysisOut = Join-Path $outDir 'heap-analysis.txt'
$analysisErr = Join-Path $outDir 'heap-analysis.err.txt'
# Capture tool version and help for diagnostics
try { & dotnet-gcdump --version 1> (Join-Path $outDir 'dotnet-gcdump.version.txt') 2>&1 } catch { }
try { & dotnet-gcdump report -h 1> (Join-Path $outDir 'dotnet-gcdump.report.help.txt') 2>&1 } catch { }
# Preferred syntax first: explicit -i/--input
Remove-Item -ErrorAction SilentlyContinue $analysisOut, $analysisErr
& dotnet-gcdump report -i $gcdumpPath 1> $analysisOut 2> $analysisErr
$exit = $LASTEXITCODE

# Fallback 1: positional input (older/newer variants)
if ($exit -ne 0 -or -not (Get-Item $analysisOut -ErrorAction SilentlyContinue) -or (Get-Content $analysisErr -ErrorAction SilentlyContinue | Where-Object { $_ }) ) {
    Write-Warn "Primary report attempt failed (exit=$exit). Trying positional arg..."
    & dotnet-gcdump report $gcdumpPath 1> $analysisOut 2> $analysisErr
    $exit = $LASTEXITCODE
}

# Fallback 2: explicit --input long form
if ($exit -ne 0 -or -not (Test-Path $analysisOut)) {
    Write-Warn "Secondary report attempt failed (exit=$exit). Trying --input..."
    & dotnet-gcdump report --input $gcdumpPath 1> $analysisOut 2> $analysisErr
    $exit = $LASTEXITCODE
}

# Fallback 3: add explicit report type and end-of-options
if ($exit -ne 0 -or -not (Test-Path $analysisOut)) {
    Write-Warn "Tertiary attempt (exit=$exit). Trying explicit -t and -- separator..."
    & dotnet-gcdump report -t heapstat -- $gcdumpPath 1> $analysisOut 2> $analysisErr
    $exit = $LASTEXITCODE
}

# Fallback 4: report directly from process id
if ($exit -ne 0 -or -not (Test-Path $analysisOut)) {
    Write-Warn "Quaternary attempt (exit=$exit). Trying -p <PID> (live report)..."
    & dotnet-gcdump report -p $proc.Id -t heapstat 1> $analysisOut 2> $analysisErr
    $exit = $LASTEXITCODE
}

if ($exit -ne 0) {
    Write-Err "dotnet-gcdump report failed with exit code $exit"
}

Write-Info "Summary (top of report):"
if (Test-Path $analysisOut) {
    Get-Content $analysisOut -TotalCount 120 | ForEach-Object { Write-Host $_ }
}
if (Test-Path $analysisErr) {
    $err = Get-Content $analysisErr | Select-Object -First 5
    if ($err -and $err.Length -gt 0) { Write-Warn ("Errors: {0}" -f ($err -join ' ')) }
}

if ($exit -ne 0) { exit 6 }

Write-Info "Artifacts: $outDir"