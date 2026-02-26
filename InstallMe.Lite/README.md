# InstallMe.Lite

InstallMe.Lite is a local GUI installer shell for Windows desktop apps. By default it is configured for Zer0Talk, but app identity, executable name, resource package, paths, and uninstall metadata can be overridden with configuration so the installer can be reused in other projects.

Features
- GUI-based install and uninstall
- Creates Desktop and Start Menu shortcuts using native Windows shortcuts
- Registers under Add/Remove Programs (HKCU) with an UninstallString
- Safer uninstall: attempts to stop running processes, retries deletions, schedules cleanup on reboot if needed

Usage
- Run the executable. The installer extracts the embedded package resource (default `zer0talk_release.zip`) and installs it to the selected target folder.
- Click Install to copy files and register the app.
- Click Uninstall to remove installed files and registry keys.

Configuration
- This repo now includes a ready-to-use `installer-config.json` preconfigured for Zer0Talk.
- It is copied next to `InstallMe.Lite.exe` on build/publish and auto-loaded at runtime.
- Place `installer-config.json` next to `InstallMe.Lite.exe`, or pass a path explicitly: `InstallMe.Lite.exe --config C:\path\installer-config.json`.
- Supported keys:

```json
{
	"appDisplayName": "MyApp",
	"executableName": "MyApp.exe",
	"defaultInstallPath": "C:\\Apps\\MyApp",
	"appDataFolderName": "MyApp",
	"shortcutName": "MyApp",
	"appUserModelId": "MyCompany.MyApp",
	"uninstallKeyName": "MyApp_InstallMeLite",
	"packageResourceName": "myapp_release.zip",
	"publisher": "MyCompany",
	"displayVersion": "1.0.0",
	"matchKeywords": ["MyApp", "MyOldApp"],
	"legacyCleanupNames": ["MyOldApp"]
}
```

- Silent uninstall remains available: `InstallMe.Lite.exe /uninstall`.

Building a self-contained single EXE for testing

```powershell
# From repository root
cd InstallMe.Lite
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeAllContentForSelfExtract=true -o ..\publish\InstallMe.Lite
```

Embedding a different package file
- The project now uses an MSBuild property for the embedded package.
- Override at publish time for other apps:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeAllContentForSelfExtract=true /p:InstallerPackageFile=..\myapp_release.zip -o ..\publish\InstallMe.Lite
```

Notes
- This project is intentionally kept local and excluded from git by default during development.
- The uninstaller schedules deletion on reboot if files are locked and cannot be removed immediately.

Authorship: AnotherLaughingMan
