param(
  [string]$Rid = 'win-x64',
  [switch]$IncludeDebugSingle,
  [int]$KeepZips = 5
)

$PSNativeCommandUseErrorActionPreference = $true
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repoRoot 'ZTalk.csproj'
$publishDir = Join-Path $repoRoot 'publish'

# Extract version from project file for artifact naming
$versionLine = Get-Content $proj | Where-Object { $_ -match '<InformationalVersion>([^<]+)</InformationalVersion>' }
if ($versionLine -and $Matches[1]) {
  $version = $Matches[1]
} else {
  # Fallback: try Version property
  $versionLine = Get-Content $proj | Where-Object { $_ -match '<Version>([^<]+)</Version>' }
  $version = if ($versionLine -and $Matches[1]) { $Matches[1] } else { '0.0.1.unknown' }
}

$singleDir = Join-Path $publishDir "$Rid-Release-single"
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$singleZip = Join-Path $publishDir "ZTalk-v$version-$Rid-Release-single-$stamp.zip"
$legacySingleZip = Join-Path $publishDir "ZTalk-v$version-$Rid-Release-single.zip"
$keepCount = $KeepZips

Write-Host "Project: $proj"
Write-Host "Version: $version"
if (-not (Test-Path $proj)) { throw "Project file not found: $proj" }

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

function Write-Header($text) { Write-Host "`n=== $text ===" }

function Invoke-PublishScript([string]$Configuration) {
  Write-Header "Publish $Configuration (FD + SC)"
  pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'publish-debug.ps1') -Configuration $Configuration -Rid $Rid -KeepZips $KeepZips | Write-Host
}

function Move-OldVersionsToArchive([string]$CurrentVersion) {
  $oldDir = Join-Path $publishDir 'old'
  New-Item -ItemType Directory -Force -Path $oldDir | Out-Null
  
  # Find all zip files that don't match the current version
  $oldVersionFiles = Get-ChildItem $publishDir -Filter "ZTalk-*.zip" | Where-Object { $_.Name -notmatch "ZTalk-v$([regex]::Escape($CurrentVersion))-" }
  
  if ($oldVersionFiles) {
    Write-Host "Moving $($oldVersionFiles.Count) older version build(s) to 'old' subfolder..."
    foreach ($file in $oldVersionFiles) {
      try {
        $destPath = Join-Path $oldDir $file.Name
        Move-Item $file.FullName $destPath -Force -ErrorAction Stop
        Write-Host "  Moved: $($file.Name)"
      } catch {
        Write-Host "Warning: Could not move old archive: $($file.Name) - $($_.Exception.Message)"
      }
    }
  }
}

function Remove-OldZips([string]$Pattern) {
  # Only remove files of the current version that exceed the keep count
  $files = Get-ChildItem $publishDir -Filter $Pattern -ErrorAction SilentlyContinue | Where-Object { $_.Name -match "ZTalk-v$([regex]::Escape($version))-" } | Sort-Object LastWriteTime -Descending
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

  Write-Header $Header

  if (Test-Path $OutputDir) { 
    try {
      Remove-Item -Recurse -Force $OutputDir -ErrorAction Stop
      Start-Sleep -Milliseconds 200
    } catch {
      Write-Host "Warning: Could not fully remove output directory: $OutputDir"
    }
  }
  foreach ($artifact in @($ZipPath, $LegacyZipPath) | Where-Object { $_ }) {
    if (Test-Path $artifact) {
      try { Remove-Item -Force $artifact -ErrorAction Stop } catch { Write-Host "Warning: Could not remove legacy artifact: $artifact" }
    }
  }

  $publishResult = dotnet publish $proj -c $Configuration -r $Rid -p:PublishSingleFile=true -p:SelfContained=true -p:EnableCompressionInSingleFile=true -o $OutputDir --nologo 2>&1
  $publishResult | Write-Host
  
  if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: dotnet publish failed with exit code $LASTEXITCODE"
    return
  }
  
  if (Test-Path $OutputDir) {
    Compress-Archive -Path (Join-Path $OutputDir '*') -DestinationPath $ZipPath -Force
    Remove-OldZips $Pattern
  } else {
    Write-Host "Error: Output directory not found after publish: $OutputDir"
  }
}

# Move older version builds to 'old' subfolder before starting new builds
Write-Header "Archive older version builds"
Move-OldVersionsToArchive $version

# Proactively prune any legacy P2PTalk-named artifacts left from pre-rename runs
Write-Header "Prune legacy P2PTalk artifacts"
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
  try {
    $dbgSingleDir = Join-Path $publishDir "$Rid-Debug-single"
    $dbgSingleZip = Join-Path $publishDir "ZTalk-v$version-$Rid-Debug-single-$stamp.zip"
    $dbgLegacySingle = Join-Path $publishDir "ZTalk-v$version-$Rid-Debug-single.zip"
    Publish-SingleFile -Configuration 'Debug' -OutputDir $dbgSingleDir -ZipPath $dbgSingleZip -LegacyZipPath $dbgLegacySingle -Header "Publish Debug single-file (standalone)" -Pattern "ZTalk-v$version-$Rid-Debug-single-*.zip"
  } catch {
    Write-Host "Warning: Debug single-file build encountered an issue but continuing..."
  }
}

# 4) Publish Release (FD + SC)
Invoke-PublishScript -Configuration 'Release'

# 5) Publish Release single-file self-contained executable
Publish-SingleFile -Configuration 'Release' -OutputDir $singleDir -ZipPath $singleZip -LegacyZipPath $legacySingleZip -Header "Publish Release single-file (standalone)" -Pattern "ZTalk-v$version-$Rid-Release-single-*.zip"

# Summary table
Write-Header "Alpha Strike summary"
Get-ChildItem $publishDir -Filter "ZTalk-v$version-$Rid-*.zip" |
Sort-Object LastWriteTime -Descending |
Select-Object Name, @{n = 'SizeMB'; e = { [math]::Round($_.Length / 1MB, 2) } } |
Format-Table -AutoSize

Write-Host "`nDone. Artifacts in: $publishDir"
