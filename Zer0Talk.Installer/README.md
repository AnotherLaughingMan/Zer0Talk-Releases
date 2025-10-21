# Zer0Talk WiX Installer

This directory contains the WiX Toolset installer project for Zer0Talk.

## Prerequisites

Install WiX Toolset v5:
```powershell
dotnet tool install --global wix
```

Or download from: https://wixtoolset.org/

## Building the Installer

### 1. Build the Main Application
First, publish the main Zer0Talk application:
```powershell
dotnet publish ..\Zer0Talk.csproj -c Release -r win-x64 --self-contained -o ..\publish\win-x64-Release
```

### 2. Build the Uninstaller
```powershell
dotnet publish ..\Uninstaller\Zer0Talk.Uninstaller.csproj -c Release -r win-x64 --self-contained
```

### 3. Build the MSI
```powershell
dotnet build Zer0Talk.Installer.wixproj -c Release
```

The MSI will be output to: `bin\Release\Zer0Talk-Setup.msi`

## Quick Build Script

Or use the provided build script:
```powershell
.\build-installer.ps1
```

## Installer Features

- ✅ Per-user installation (no admin required)
- ✅ Desktop shortcut
- ✅ Start Menu shortcuts
- ✅ Add/Remove Programs integration
- ✅ Automatic firewall exception
- ✅ Clean uninstallation
- ✅ Upgrade support (removes old versions)
- ✅ Professional Windows Installer (MSI) format

## Customization

Edit `Product.wxs` to:
- Add/remove files
- Modify shortcuts
- Change installation directory
- Add registry entries
- Configure custom actions

## Notes

- The installer uses a per-user installation scope (no admin required)
- Default install location: `%LocalAppData%\Zer0Talk`
- Firewall exceptions are created automatically
- Old versions are automatically removed during upgrade
