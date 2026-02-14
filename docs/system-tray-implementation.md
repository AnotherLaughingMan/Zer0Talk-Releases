# System Tray Implementation

**Date:** October 6, 2025  
**Feature:** System tray icon with minimize to tray and run on startup functionality

## Overview

Zer0Talk now supports running in the system tray with the following capabilities:
1. **Show in System Tray** - Display app icon in Windows system tray
2. **Minimize to Tray** - Close button minimizes to tray instead of exiting
3. **Run on Startup** - Automatically start Zer0Talk when Windows starts

## User-Facing Features

### Settings Location
All system tray settings are located in **Settings > General > System Tray** section.

### Settings Options

#### 1. Show icon in system tray
- **Default:** OFF
- **Description:** Display Zer0Talk icon in the Windows system tray for quick access
- **Behavior:** When enabled, tray icon appears with context menu

#### 2. Minimize to tray on close
- **Default:** OFF
- **Description:** Close button will minimize to system tray instead of exiting the application
- **Behavior:** 
  - Window close button (X) hides window instead of closing app
  - App continues running in background
  - Click tray icon or "Show Zer0Talk" to restore window
- **Requirement:** "Show icon in system tray" must also be enabled

#### 3. Run on Windows startup
- **Default:** OFF
- **Description:** Automatically start Zer0Talk when Windows starts (minimized to tray)
- **Behavior:**
  - Adds registry entry to `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`
  - App starts automatically with Windows
  - No admin rights required (uses current user registry)

## Technical Implementation

### Files Created

#### 1. `Services/TrayIconService.cs`
- Manages tray icon lifecycle
- Creates native menu with Show/Exit options
- Handles tray icon click events
- Loads icon from `Assets/Icons/Icon.ico`

**Key Methods:**
- `Initialize()` - Creates tray icon and menu
- `SetVisible(bool)` - Shows/hides tray icon
- `ShowMainWindow()` - Restores main window from tray
- `ExitApplication()` - Graceful shutdown and exit

#### 2. `Services/WindowsStartupManager.cs`
- Windows registry management for startup
- Uses `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`
- Static utility methods for registry operations

**Key Methods:**
- `IsRunOnStartupEnabled()` - Checks if registry entry exists
- `SetRunOnStartup(bool)` - Adds/removes registry entry
- `ApplyStartupSetting(bool)` - Syncs registry with settings

### Files Modified

#### 1. `Models/AppSettings.cs`
Added three new properties:
```csharp
public bool MinimizeToTray { get; set; } = false;
public bool RunOnStartup { get; set; } = false;
public bool ShowInSystemTray { get; set; } = false;
```

#### 2. `Views/Controls/SettingsView.axaml`
- Added "System Tray" section in General panel
- Three toggle switches with descriptions
- Positioned after "Key Visibility" section

#### 3. `ViewModels/SettingsViewModel.cs`
- Added backing fields for tray settings
- Load settings from `AppSettings`
- Save settings and apply immediately:
  - Initialize/hide tray icon based on `ShowInSystemTray`
  - Update Windows registry based on `RunOnStartup`
  - Verify registry matches settings on load

#### 4. `Views/MainWindow.axaml.cs`
- **`OnMainWindowClosing`**: Check if minimize to tray is enabled
  - If yes: Cancel close event, hide window instead
  - If no: Normal shutdown process
- **`Opened` event**: Initialize tray icon if enabled in settings

#### 5. `Services/AppServices.cs`
- Added `TrayIcon` service singleton
- Dispose tray icon in `Shutdown()` method

## Behavior Details

### Minimize to Tray Logic
```csharp
if (MinimizeToTray && ShowInSystemTray)
{
    e.Cancel = true;  // Cancel the close event
    this.Hide();      // Hide the window
    return;           // Don't shutdown services
}
// Otherwise normal close/shutdown
```

### Tray Icon Context Menu
- **Show Zer0Talk**: Restores window (Show + WindowState.Normal + Activate)
- **Separator**: Visual divider
- **Exit**: Graceful shutdown then close application

### Windows Startup Registry
- **Key**: `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`
- **Value Name**: `Zer0Talk`
- **Value Data**: `"C:\Path\To\Zer0Talk.exe"`
- **Type**: `REG_SZ` (string)

## Settings Persistence

All three settings are persisted in encrypted `settings.p2e`:
- Saved when user clicks Apply/OK in Settings
- Loaded on application startup
- Registry synced with `RunOnStartup` setting on load

## Platform Support

### Windows
✅ **Fully Supported**
- System tray icon
- Minimize to tray
- Run on startup (registry)

### Linux/macOS
⚠️ **Partial Support**
- System tray should work (depends on desktop environment)
- Run on startup NOT implemented (Windows-only registry approach)
- Code safely checks `OperatingSystem.IsWindows()` before registry operations

## User Workflows

### Scenario 1: Background Operation
1. User enables "Show icon in system tray"
2. User enables "Minimize to tray on close"
3. User clicks window close button (X)
4. Window disappears, app continues running
5. User sees Zer0Talk icon in system tray
6. User clicks tray icon → window reappears

### Scenario 2: Auto-Start
1. User enables "Show icon in system tray"
2. User enables "Run on Windows startup"
3. User restarts computer
4. Zer0Talk starts automatically with Windows
5. App runs minimized to tray (if minimize setting also enabled)

### Scenario 3: Complete Exit
- **With Minimize to Tray Enabled:**
  - Right-click tray icon → Exit
  - Or disable "Minimize to tray", then use close button

- **Without Minimize to Tray:**
  - Normal close button (X) exits app

## Error Handling

### Tray Icon Initialization Failures
- Logged to `logs/zer0talk-YYYYMMDD-HHMMSS.log`
- App continues to function normally
- User sees no tray icon (graceful degradation)

### Registry Access Failures
- Logged to `logs/zer0talk-YYYYMMDD-HHMMSS.log`
- Setting remains in app settings
- Registry not updated (no crash)

### Missing Icon File
- Falls back to default Windows icon
- Logged as warning
- Functionality preserved

## Testing Checklist

### Manual Tests
- [ ] Enable "Show in system tray" → Icon appears
- [ ] Disable "Show in system tray" → Icon disappears
- [ ] Enable "Minimize to tray" + "Show in tray" → Close button hides window
- [ ] Click tray icon → Window restores
- [ ] Right-click tray icon → Context menu appears
- [ ] Select "Show Zer0Talk" from menu → Window restores
- [ ] Select "Exit" from menu → App exits completely
- [ ] Enable "Run on startup" → Check registry entry created
- [ ] Restart Windows → App starts automatically
- [ ] Disable "Run on startup" → Check registry entry removed
- [ ] Restart Windows → App does NOT start

### Registry Verification
```powershell
# Check if entry exists
Get-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name "Zer0Talk"

# Verify path
$entry = Get-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name "Zer0Talk" -ErrorAction SilentlyContinue
if ($entry) { Write-Host "Startup enabled: $($entry.Zer0Talk)" } else { Write-Host "Startup disabled" }
```

### Edge Cases
- [ ] Enable minimize to tray WITHOUT enabling show in tray → Should close normally
- [ ] Close window while settings dialog open → Settings should handle properly
- [ ] Multiple rapid enable/disable of tray icon → No crashes
- [ ] Change startup setting while app is already in startup → Registry updates

## Known Limitations

1. **No Command-Line Args**: Currently app doesn't support `--minimized` or `--hidden` startup arg
   - Future: Add arg parsing to start minimized on auto-start

2. **Icon Hot-Reload**: Changing icon requires app restart
   - Icon loaded once on tray initialization

3. **No Tray Notifications**: No balloon tips/notifications implemented yet
   - Future: Add notifications for new messages when minimized

4. **Single Instance**: No single-instance enforcement
   - Multiple instances can run simultaneously (each with own tray icon)

5. **No Tray Menu Customization**: Context menu is fixed (Show/Exit)
   - Future: Add status indicator, quick actions

## Future Enhancements

### Planned Features
1. **Balloon Notifications**
   - New message notifications when minimized
   - Contact request notifications
   - Connection status alerts

2. **Start Minimized**
   - Add `--minimized` command-line argument
   - Modify startup registry entry to include arg
   - Skip window.Show() on startup if arg present

3. **Extended Tray Menu**
   - Show online status
   - Quick status change (Online/Away/DND)
   - Unread message count

4. **Smart Minimize**
   - Remember if window was minimized when closed
   - Restore to same state (normal/minimized)

5. **Platform Support**
   - Linux: XDG autostart desktop file
   - macOS: Login Items via LaunchAgents

6. **Tray Icon Badging**
   - Overlay unread count on icon
   - Different icon states (online/away/dnd)

## Logs to Monitor

**Tray Icon Operations:**
- `TrayIcon: Initialized successfully`
- `TrayIcon: Visibility set to true/false`
- `TrayIcon: Main window restored`
- `TrayIcon: Exit requested`
- `TrayIcon: Could not load custom icon: {error}`

**Startup Registry:**
- `WindowsStartup: Added to startup: {path}`
- `WindowsStartup: Removed from startup`
- `WindowsStartup: Failed to set startup: {error}`

**Window Behavior:**
- `MainWindow: Minimized to tray instead of closing`

## Migration Notes

**No database changes required** - only settings and registry modifications.

**Backward Compatibility:**
- New settings default to `false` (disabled)
- Existing users see no behavior change until they enable features
- No breaking changes to existing functionality

**Settings Migration:**
- Old settings files work without modification
- New properties added with default values on first save

## Security Considerations

1. **Registry Permissions**
   - Uses `HKCU` (current user) - no admin rights needed
   - Cannot affect other users on system
   - User can manually remove registry entry

2. **Executable Path**
   - Uses `Process.GetCurrentProcess().MainModule.FileName`
   - No hardcoded paths
   - Works with portable installations

3. **Settings Encryption**
   - Tray settings stored in encrypted `settings.p2e`
   - Uses existing DPAPI encryption
   - No new encryption keys required

## Troubleshooting

### Tray Icon Not Appearing
- Check Settings > General > "Show icon in system tray" is enabled
- Restart app after enabling
- Check logs for initialization errors
- Verify `Assets/Icons/Icon.ico` exists in app directory

### App Doesn't Start with Windows
- Verify "Run on startup" is enabled in settings
- Check registry entry exists (see PowerShell commands above)
- Ensure executable path in registry is correct
- Check Windows startup programs list (Task Manager > Startup tab)

### Close Button Still Exits Instead of Minimizing
- Verify both toggles enabled:
  - "Show icon in system tray" = ON
  - "Minimize to tray on close" = ON
- Restart app after changing settings
- Check logs for any errors

### Can't Exit Application
- Right-click tray icon → Exit
- Or disable "Minimize to tray" setting, then use close button
- Or use Task Manager to end process (last resort)
