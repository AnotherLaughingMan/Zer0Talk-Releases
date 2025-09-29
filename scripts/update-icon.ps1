param(
    [string]$SourcePath = "../Assets/Icons/Icon.png",
    [string]$OutputPath = "../Assets/Icons/Icon.ico"
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class NativeIcon
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
"@

$sourceFullPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $SourcePath))
$outputFullPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $OutputPath))

if (-not (Test-Path $sourceFullPath)) {
    throw "Source icon PNG not found at $sourceFullPath"
}

[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($outputFullPath)) | Out-Null

$bitmap = New-Object System.Drawing.Bitmap -ArgumentList $sourceFullPath

try {
    $iconHandle = $bitmap.GetHicon()
    try {
        $icon = [System.Drawing.Icon]::FromHandle($iconHandle)
        $stream = [System.IO.File]::Open($outputFullPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
        try {
            $icon.Save($stream)
        }
        finally {
            $stream.Dispose()
            $icon.Dispose()
        }
    }
    finally {
        [NativeIcon]::DestroyIcon($iconHandle) | Out-Null
    }
}
finally {
    $bitmap.Dispose()
}

Write-Host "Regenerated $outputFullPath from $sourceFullPath"
