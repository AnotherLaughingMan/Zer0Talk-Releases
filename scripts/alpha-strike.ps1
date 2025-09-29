param(
  [string]$Rid = 'win-x64',
  [switch]$IncludeDebugSingle,
  [int]$KeepZips = 5
)

$PSNativeCommandUseErrorActionPreference = $true
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repoRoot 'P2PTalk.csproj'
$publishDir = Join-Path $repoRoot 'publish'
$singleDir = Join-Path $publishDir "$Rid-Release-single"
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$singleZip = Join-Path $publishDir "ZTalk-$Rid-Release-single-$stamp.zip"
$legacySingleZip = Join-Path $publishDir "ZTalk-$Rid-Release-single.zip"
$keepCount = $KeepZips

Write-Host "Project: $proj"
if (-not (Test-Path $proj)) { throw "Project file not found: $proj" }

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

function Show-Header($text) { Write-Host "`n=== $text ===" }

function Invoke-PublishScript([string]$Configuration) {
  Show-Header "Publish $Configuration (FD + SC)"
  pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'publish-debug.ps1') -Configuration $Configuration -Rid $Rid -KeepZips $KeepZips | Write-Host
}

function Remove-OldZips([string]$Pattern) {
  $files = Get-ChildItem $publishDir -Filter $Pattern -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending
  if ($files.Count -gt $keepCount) {
    $files | Select-Object -Skip $keepCount | ForEach-Object {
      try { Remove-Item -Force $_.FullName -ErrorAction Stop } catch { Write-Host "Warning: Could not delete old archive: $($_.Name)" }
    }
  }
}

function Publish-SingleFile {
  param(
    [string]$Configuration,
    [string]$OutputDir,
    [string]$ZipPath,
    [string]$Header,
    [string]$Pattern,
    [string]$LegacyZipPath
  )

  Show-Header $Header

  if (Test-Path $OutputDir) { Remove-Item -Recurse -Force $OutputDir }
  foreach ($artifact in @($ZipPath, $LegacyZipPath) | Where-Object { $_ }) {
    if (Test-Path $artifact) {
      try { Remove-Item -Force $artifact -ErrorAction Stop } catch { Write-Host "Warning: Could not remove legacy artifact: $artifact" }
    }
  }

  dotnet publish $proj -c $Configuration -r $Rid -p:PublishSingleFile=true -p:SelfContained=true -p:EnableCompressionInSingleFile=true -o $OutputDir --nologo
  Compress-Archive -Path (Join-Path $OutputDir '*') -DestinationPath $ZipPath -Force

  Remove-OldZips $Pattern
}

# Proactively prune any legacy P2PTalk-named artifacts left from pre-rename runs
Show-Header "Prune legacy P2PTalk artifacts"
$legacyFiles = Get-ChildItem -Path $publishDir -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'P2PTalk' }
if ($legacyFiles) {
  foreach ($f in $legacyFiles) {
    try { Remove-Item -Force -LiteralPath $f.FullName -ErrorAction Stop } catch { Write-Host "Warning: Could not delete legacy artifact: $($f.FullName)" }
  }
}

# Build steps removed: dotnet publish performs builds per configuration/target.

# 3) Publish Debug (FD + SC)
Invoke-PublishScript -Configuration 'Debug'

# 3b) Optionally publish Debug single-file (timestamped)
if ($IncludeDebugSingle) {
  $dbgSingleDir = Join-Path $publishDir "$Rid-Debug-single"
  $dbgSingleZip = Join-Path $publishDir "ZTalk-$Rid-Debug-single-$stamp.zip"
  $dbgLegacySingle = Join-Path $publishDir "ZTalk-$Rid-Debug-single.zip"
  Publish-SingleFile -Configuration 'Debug' -OutputDir $dbgSingleDir -ZipPath $dbgSingleZip -LegacyZipPath $dbgLegacySingle -Header "Publish Debug single-file (standalone)" -Pattern "ZTalk-$Rid-Debug-single-*.zip"
}

# 4) Publish Release (FD + SC)
Invoke-PublishScript -Configuration 'Release'

# 5) Publish Release single-file self-contained executable
Publish-SingleFile -Configuration 'Release' -OutputDir $singleDir -ZipPath $singleZip -LegacyZipPath $legacySingleZip -Header "Publish Release single-file (standalone)" -Pattern "ZTalk-$Rid-Release-single-*.zip"

# Summary table
Show-Header "Alpha Strike summary"
Get-ChildItem $publishDir -Filter "ZTalk-$Rid-*.zip" |
Sort-Object LastWriteTime -Descending |
Select-Object Name, @{n = 'SizeMB'; e = { [math]::Round($_.Length / 1MB, 2) } } |
Format-Table -AutoSize

Write-Host "`nDone. Artifacts in: $publishDir"
