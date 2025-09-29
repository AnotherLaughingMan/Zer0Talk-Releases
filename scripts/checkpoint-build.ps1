param(
  [ValidateSet('Debug', 'Release')]
  [string]$Configuration = 'Release',
  [Parameter(Mandatory = $true)]
  [string]$Rid,
  [int]$KeepZips = 5,
  [int]$KeepCheckpoints = 4
)

$PSNativeCommandUseErrorActionPreference = $true
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot 'publish'
$binDll = Join-Path $repoRoot "bin/$Configuration/net9.0/ZTalk.dll"
$errorLog = Join-Path $repoRoot 'error.log'

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

function Write-Trace($msg) {
  $line = "$(Get-Date -Format o) [checkpoint] $msg"
  try { Add-Content -Path $errorLog -Value $line -Encoding UTF8 } catch {}
  Write-Host $line
}

Write-Trace "Begin publish Configuration=$Configuration Rid=$Rid"

# Publish using existing script with one retry on failure, then skip if still failing
$publishScript = Join-Path $repoRoot 'scripts/publish-debug.ps1'
$attempt = 0
$maxAttempts = 2  # initial + one retry
$published = $false
while ($attempt -lt $maxAttempts -and -not $published) {
  if ($attempt -gt 0) {
    Write-Trace "Previous publish attempt failed. Sleeping 2s then retrying ($attempt/$maxAttempts) ..."
    Start-Sleep -Seconds 2
  }
  & pwsh -NoProfile -ExecutionPolicy Bypass -File $publishScript -Configuration $Configuration -Rid $Rid -KeepZips $KeepZips
  if ($LASTEXITCODE -eq 0) {
    $published = $true
  }
  else {
    $attempt++
  }
}
if (-not $published) {
  Write-Trace "Publish step failed after one retry; continuing with existing artifacts."
}

# Find artifacts (timestamped zip names)
$artifacts = Get-ChildItem -Path $publishDir -Filter "ZTalk-$Rid-$Configuration*.zip" | Sort-Object LastWriteTime -Descending
if (-not $artifacts) { Write-Trace "No artifacts found for $Rid/$Configuration"; throw "No publish artifacts found" }

# Compute hashes for artifacts
$artifactInfo = @()
foreach ($a in $artifacts) {
  $sha = (Get-FileHash -Algorithm SHA256 -Path $a.FullName).Hash.ToLowerInvariant()
  $artifactInfo += [pscustomobject]@{ file = $a.Name; path = $a.FullName; sizeBytes = $a.Length; sha256 = $sha }
}

# Assembly version (best-effort)
$version = 'unknown'
try {
  if (Test-Path $binDll) { $version = ([System.Reflection.AssemblyName]::GetAssemblyName($binDll)).Version.ToString() }
}
catch { $version = 'unknown' }

# Git commit (best-effort)
$commit = 'unknown'
try {
  if (Test-Path (Join-Path $repoRoot '.git')) {
    $commit = (git -C $repoRoot rev-parse --short=10 HEAD).Trim()
  }
}
catch { $commit = 'unknown' }

# Hash critical styling resources to detect regressions
$styleFiles = @(
  'Styles/ThemeSovereignty.axaml',
  'Styles/DarkThemeOverrides.axaml',
  'Styles/LightThemeOverrides.axaml',
  'Styles/SandyThemeOverrides.axaml',
  'Styles/ButterThemeOverride.axaml'
)
$styleHashes = @{}
foreach ($rel in $styleFiles) {
  $p = Join-Path $repoRoot $rel
  if (Test-Path $p) {
    $styleHashes[$rel] = (Get-FileHash -Algorithm SHA256 -Path $p).Hash.ToLowerInvariant()
  }
}

$stamp = (Get-Date -Format 'yyyyMMdd-HHmmss')
$manifest = [pscustomobject]@{
  createdUtc    = (Get-Date).ToUniversalTime().ToString('o')
  configuration = $Configuration
  rid           = $Rid
  version       = $version
  commit        = $commit
  artifacts     = $artifactInfo
  styleHashes   = $styleHashes
}

$manifestPath = Join-Path $publishDir ("checkpoint-$Rid-$Configuration-$stamp.json")
$manifest | ConvertTo-Json -Depth 6 | Out-File -FilePath $manifestPath -Encoding UTF8 -Force
$latestPath = Join-Path $publishDir 'latest-checkpoint.json'
Copy-Item -Path $manifestPath -Destination $latestPath -Force

Write-Trace "Checkpoint created: $(Split-Path -Leaf $manifestPath) (version=$version commit=$commit)"
Write-Host "Manifest: $manifestPath"

# Prune old checkpoint manifests (keep latest 10)
$checkpoints = Get-ChildItem -Path $publishDir -Filter "checkpoint-$Rid-$Configuration-*.json" | Sort-Object LastWriteTime -Descending
if ($checkpoints.Count -gt $KeepCheckpoints) {
  $toDelete = $checkpoints | Select-Object -Skip $KeepCheckpoints
  foreach ($f in $toDelete) {
    try { Remove-Item -Force $f.FullName -ErrorAction Stop } catch { Write-Trace "Warning: Could not delete old checkpoint: $($f.Name)" }
  }
}

# Prune old zips for this RID/Configuration (keep latest 5 per variant)
$keepZipCount = $KeepZips
foreach ($pattern in @(
    "ZTalk-$Rid-$Configuration-*.zip",
    "ZTalk-$Rid-$Configuration-sc-*.zip",
    "ZTalk-$Rid-$Configuration-single-*.zip"
  )) {
  $zips = Get-ChildItem -Path $publishDir -Filter $pattern | Sort-Object LastWriteTime -Descending
  if ($zips.Count -gt $keepZipCount) {
    $toDelete = $zips | Select-Object -Skip $keepZipCount
    foreach ($f in $toDelete) {
      try { Remove-Item -Force $f.FullName -ErrorAction Stop } catch { Write-Trace "Warning: Could not delete old zip: $($f.Name)" }
    }
  }
}

# Remove legacy non-timestamp single zip name if present
$legacySingle = Join-Path $publishDir "ZTalk-$Rid-$Configuration-single.zip"
if (Test-Path $legacySingle) {
  try { Remove-Item -Force $legacySingle -ErrorAction Stop } catch { Write-Trace "Warning: Could not remove legacy single zip: $(Split-Path -Leaf $legacySingle)" }
}
exit 0
