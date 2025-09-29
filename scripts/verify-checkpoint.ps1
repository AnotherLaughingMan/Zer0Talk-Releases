param(
  [string]$ManifestPath = '',
  [switch]$Strict,
  [switch]$Prune,
  [int]$KeepCheckpoints = 4
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$PSNativeCommandUseErrorActionPreference = $true
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot 'publish'
$errorLog = Join-Path $repoRoot 'error.log'

function Write-Trace($msg) {
  $line = "$(Get-Date -Format o) [checkpoint] $msg"
  try { Add-Content -Path $errorLog -Value $line -Encoding UTF8 } catch {}
  Write-Host $line
}

if (-not $ManifestPath) { $ManifestPath = Join-Path $publishDir 'latest-checkpoint.json' }
if (-not (Test-Path $ManifestPath)) { throw "Checkpoint manifest not found: $ManifestPath" }

$manifest = Get-Content -Raw -Path $ManifestPath | ConvertFrom-Json
$regressions = @()

# Recompute style file hashes and compare
foreach ($kv in $manifest.styleHashes.PSObject.Properties) {
  $rel = $kv.Name
  $expected = ($kv.Value | Out-String).Trim()
  $path = Join-Path $repoRoot $rel
  if (-not (Test-Path $path)) { $regressions += "Missing style file: $rel"; continue }
  $actual = (Get-FileHash -Algorithm SHA256 -Path $path).Hash.ToLowerInvariant()
  if ($actual -ne $expected) { $regressions += "Style hash mismatch for $rel" }
}

if ($regressions.Count -gt 0) {
  foreach ($r in $regressions) { Write-Trace ("REGRESSION: " + $r) }
  if ($Strict) { exit 1 } else { exit 0 }
}

Write-Trace "Checkpoint verified: no regressions in tracked files"

# Optional pruning of older checkpoint manifests (keep latest N)
if ($Prune) {
  try {
    $manifests = Get-ChildItem -Path $publishDir -Filter 'checkpoint-*-*.json' -File | Sort-Object Name -Descending
    if ($manifests) {
      $toKeep = @($manifests | Select-Object -First $KeepCheckpoints)
      $toDelete = @($manifests | Select-Object -Skip $KeepCheckpoints | Sort-Object Name) # oldest first

      if ($toDelete.Length -gt 0) {
        Write-Host "[checkpoint] Pruning $($toDelete.Length) old checkpoint(s); keeping $($toKeep.Length)." -ForegroundColor Cyan
        foreach ($f in $toDelete) {
          try {
            Remove-Item -LiteralPath $f.FullName -Force -ErrorAction Stop
            Write-Host "[checkpoint] Deleted: $($f.Name)" -ForegroundColor DarkCyan
          }
          catch {
            Write-Host "[checkpoint] Skip delete (locked?): $($f.Name) -> $_" -ForegroundColor Yellow
          }
        }
      }
    }
  }
  catch {
    Write-Host "[checkpoint] Prune step encountered an error: $_" -ForegroundColor Yellow
    if ($Strict) { exit 1 }
  }
}

exit 0
