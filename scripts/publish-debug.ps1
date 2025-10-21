Param(
    [string]$Configuration = 'Debug',
    [string]$Rid = 'win-x64',
    [switch]$SelfContained,
    [switch]$NoZip,
    [switch]$Single,
    [int]$KeepZips = 5
)

$ErrorActionPreference = 'Stop'

# Resolve project root based on this script's location
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $scriptDir
$proj = Join-Path $root 'Zer0Talk.csproj'
$publishDir = Join-Path $root 'publish'

# Extract version from project file for artifact naming
$versionLine = Get-Content $proj | Where-Object { $_ -match '<InformationalVersion>([^<]+)</InformationalVersion>' }
if ($versionLine -and $Matches[1]) {
  $version = $Matches[1]
} else {
  # Fallback: try Version property
  $versionLine = Get-Content $proj | Where-Object { $_ -match '<Version>([^<]+)</Version>' }
  $version = if ($versionLine -and $Matches[1]) { $Matches[1] } else { '0.0.1.unknown' }
}

$fdDir = Join-Path $publishDir "$Rid-$Configuration"           # framework-dependent
$scDir = Join-Path $publishDir "$Rid-$Configuration-sc"        # self-contained
$singleDir = Join-Path $publishDir "$Rid-$Configuration-single" # single-file (optional)
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$keepCount = $KeepZips

Write-Host "Project:" $proj
if (-not (Test-Path $proj)) { throw "Project file not found: $proj" }

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

# Clean prior outputs to ensure a fresh package
if (Test-Path $fdDir) { Remove-Item -Recurse -Force $fdDir }
if (Test-Path $scDir) { Remove-Item -Recurse -Force $scDir }

# Always publish framework-dependent first
Write-Host "\n=== Publishing framework-dependent ($Configuration, $Rid) ==="
dotnet publish $proj -c $Configuration -r $Rid -p:SelfContained=false -o $fdDir --nologo

# Publish self-contained unless explicitly disabled via -SelfContained:$false semantics
Write-Host "\n=== Publishing self-contained ($Configuration, $Rid) ==="
dotnet publish $proj -c $Configuration -r $Rid -p:SelfContained=true -o $scDir --nologo

if ($NoZip) {
    Write-Host "\nSkipping ZIP creation per -NoZip switch."
    exit 0
}

# Zip both outputs with versioned and timestamped filenames
$zip1 = Join-Path $publishDir "Zer0Talk-v$version-$Rid-$Configuration-$stamp.zip"
$zip2 = Join-Path $publishDir "Zer0Talk-v$version-$Rid-$Configuration-sc-$stamp.zip"
${legacy1} = Join-Path $publishDir "Zer0Talk-v$version-$Rid-$Configuration.zip"
${legacy2} = Join-Path $publishDir "Zer0Talk-v$version-$Rid-$Configuration-sc.zip"
${legacySingle} = Join-Path $publishDir "Zer0Talk-v$version-$Rid-$Configuration-single.zip"

# Remove existing zips with a brief retry in case another process has the file open
foreach ($zip in @($zip1, $zip2)) {
    if (Test-Path $zip) {
        $removed = $false
        try { Remove-Item -Force $zip -ErrorAction Stop; $removed = $true } catch { }
        if (-not $removed) {
            Start-Sleep -Milliseconds 750
            try { Remove-Item -Force $zip -ErrorAction Stop; $removed = $true } catch { }
        }
        if (-not $removed) { Write-Host "Warning: Could not remove existing zip (locked): $zip. Will try to overwrite or skip." }
    }
}

# Also remove legacy non-timestamped zip names if present
foreach ($legacy in @(${legacy1}, ${legacy2})) {
    if (Test-Path $legacy) {
        try { Remove-Item -Force $legacy -ErrorAction Stop } catch { Write-Host "Warning: Could not remove legacy zip: $legacy" }
    }
}
if (Test-Path ${legacySingle}) {
    try { Remove-Item -Force ${legacySingle} -ErrorAction Stop } catch { Write-Host "Warning: Could not remove legacy single zip: ${legacySingle}" }
}

Write-Host "\n=== Zipping artifacts ==="
function Invoke-Zip($src, $dst) {
    try {
        Compress-Archive -Path $src -DestinationPath $dst -Force -ErrorAction Stop
        return $true
    }
    catch {
        Start-Sleep -Milliseconds 750
        try {
            Compress-Archive -Path $src -DestinationPath $dst -Force -ErrorAction Stop
            return $true
        }
        catch {
            Write-Host "Warning: Skipping zip due to lock or error: $dst"
            return $false
        }
    }
}

Invoke-Zip (Join-Path $fdDir '*') $zip1 | Out-Null
Invoke-Zip (Join-Path $scDir '*') $zip2 | Out-Null

# Optionally build single-file artifact for this configuration
if ($Single) {
    Write-Host "\n=== Publishing single-file ($Configuration, $Rid) ==="
    if (Test-Path $singleDir) { Remove-Item -Recurse -Force $singleDir }
    dotnet publish $proj -c $Configuration -r $Rid -p:PublishSingleFile=true -p:SelfContained=true -p:EnableCompressionInSingleFile=true -o $singleDir --nologo

    $singleZip = Join-Path $publishDir "Zer0Talk-v$version-$Rid-$Configuration-single-$stamp.zip"
    if (Test-Path $singleZip) {
        $removed = $false
        try { Remove-Item -Force $singleZip -ErrorAction Stop; $removed = $true } catch { }
        if (-not $removed) { Start-Sleep -Milliseconds 750; try { Remove-Item -Force $singleZip -ErrorAction Stop; $removed = $true } catch { } }
    }
    Invoke-Zip (Join-Path $singleDir '*') $singleZip | Out-Null
}

Get-ChildItem $publishDir -Filter "Zer0Talk-v$version-$Rid-$Configuration*.zip" |
Select-Object Name, @{n = 'SizeMB'; e = { [math]::Round($_.Length / 1MB, 2) } } |
Format-Table -AutoSize

Write-Host "\nDone. Artifacts in: $publishDir"
Write-Host "Version: $version"

# Move older version builds to 'old' subfolder
function Move-OldVersionsToArchive([string]$CurrentVersion) {
  $oldDir = Join-Path $publishDir 'old'
  New-Item -ItemType Directory -Force -Path $oldDir | Out-Null
  
  # Find all zip files that don't match the current version
  $oldVersionFiles = Get-ChildItem $publishDir -Filter "Zer0Talk-*.zip" | Where-Object { $_.Name -notmatch "Zer0Talk-v$([regex]::Escape($CurrentVersion))-" }
  
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

# Archive older versions first
Move-OldVersionsToArchive $version

# Prune older zips for this RID/Configuration (keep latest $keepCount for each variant of current version only)
foreach ($pattern in @("Zer0Talk-v$version-$Rid-$Configuration-*.zip", "Zer0Talk-v$version-$Rid-$Configuration-sc-*.zip", "Zer0Talk-v$version-$Rid-$Configuration-single-*.zip")) {
    $files = Get-ChildItem $publishDir -Filter $pattern | Where-Object { $_.Name -match "Zer0Talk-v$([regex]::Escape($version))-" } | Sort-Object LastWriteTime -Descending
    if ($files.Count -gt $keepCount) {
        $toDelete = $files | Select-Object -Skip $keepCount
        foreach ($f in $toDelete) {
            try { Remove-Item -Force $f.FullName -ErrorAction Stop } catch { Write-Host "Warning: Could not delete old zip: $($f.Name)" }
        }
    }
}
