# Hotkey System Implementation

## Overview
Implemented a comprehensive, user-configurable hotkey system for ZTalk with centralized management and conflict detection.

## Files Modified/Created

### 1. **HotkeyManager Service** (`Services/HotkeyManager.cs`) - NEW
- Centralized hotkey registration and execution
- Thread-safe dictionary-based registration
- Conflict detection to prevent duplicate key bindings
- Support for updating key combinations at runtime
- Human-readable formatting for key combinations (e.g., "Ctrl+L")

### 2. **AppSettings Model Updates** (`Models/AppSettings.cs`)
Added properties for persisting hotkey configurations:
- `LockHotkeyKey` (int) - stores Key enum value
- `LockHotkeyModifiers` (int) - stores KeyModifiers flags
- Default: Ctrl+L (Key.L = 68, KeyModifiers.Control = 1)

### 3. **Settings UI** (`Views/Controls/SettingsView.axaml`)
New "Hotkeys" panel in the Settings overlay (accessed via Settings icon in MainWindow):
- Interactive hotkey capture box (click to record new key combination)
- Visual feedback during key capture
- Reset to default button
- Helpful guidance about avoiding OS hotkeys

### 4. **SettingsViewModel Updates** (`ViewModels/SettingsViewModel.cs`)
Added hotkey configuration logic:
- `LockHotkeyDisplay` - formatted string for UI
- `IsCapturingLockHotkey` - capture mode indicator
- `StartCapturingLockHotkey()` - initiates key recording
- `OnCaptureKeyDown()` - captures user's key press
- `ResetLockHotkeyCommand` - restores default (Ctrl+L)
- Conflict validation during capture
- Baseline tracking for dirty state detection
- Persistence in Save/Load methods

### 5. **Window and Control Updates**
Updated all windows and the SettingsView control to use HotkeyManager:
- **MainWindow** - Registers lock hotkey on startup, uses HotkeyManager in key handler
- **SettingsView** (Settings panel) - Handles hotkey capture UI and interaction
- **SettingsWindow** (deprecated) - Uses HotkeyManager for legacy compatibility
- **NetworkWindow** - Uses HotkeyManager for consistent hotkey handling

## Key Features

### ✅ User-Configurable Hotkeys
- Click hotkey field in Settings > Hotkeys
- Press desired key combination
- Automatic conflict detection
- Changes saved with other settings

### ✅ Safe Defaults
- Default lock hotkey: **Ctrl+L** (simple, non-conflicting)
- Previous hardcoded: Ctrl+Alt+Shift+L (too complex)
- Avoids common OS shortcuts (Alt+F4, Win+L, etc.)

### ✅ Conflict Prevention
- Validates against existing hotkeys before accepting
- Shows error toast if conflict detected
- Ignores modifier-only keys (Ctrl, Alt, Shift alone)

### ✅ Runtime Updates
- Hotkey changes apply immediately on Save
- No restart required
- HotkeyManager automatically updates all windows

### ✅ Persistence
- Hotkeys saved to encrypted settings.p2e
- Restored on application startup
- Baseline tracking for unsaved changes warning

## Usage

### For Users:
1. Click the Settings icon (⚙️) in MainWindow to open the Settings overlay
2. Select "Hotkeys" from the left menu
3. Click the hotkey field under "Lock Application"
4. Press your desired key combination (e.g., Ctrl+Shift+L)
5. Changes are automatically saved

### For Developers:
```csharp
// Register a new hotkey
HotkeyManager.Instance.Register(
    "unique.id",
    Key.F5,
    KeyModifiers.Control,
    () => { /* callback */ },
    "Refresh View");

// Update existing hotkey
HotkeyManager.Instance.UpdateKeyBinding("unique.id", Key.F6, KeyModifiers.Control);

// Handle key events in window
private void OnKeyDown(object? sender, KeyEventArgs e)
{
    if (HotkeyManager.Instance.HandleKeyEvent(e))
        return; // Hotkey was handled
}
```

## Settings Panel Organization
Settings menu items are now logically ordered in the MainWindow Settings overlay:
1. **Appearance** - Theme, fonts, UI scaling
2. **General** - Default presence, auto-lock settings
3. **Hotkeys** - ⭐ NEW: Keyboard shortcut configuration
4. **Profile** - User identity and display settings
5. **Network** - Connection, peers, adapters
6. **Performance** - CPU, GPU, framerate
7. **Accessibility** - Display and navigation options
8. **About** - Application and framework information
9. **Danger Zone** - Account deletion, data purging
10. **Log Out** - Sign out of session

## Security Considerations
- Lock hotkey uses same encryption as other settings
- No hardcoded backdoors
- User has full control over key combinations
- Locked state properly protects sensitive data

## Testing Checklist
✅ Project builds successfully
✅ No compilation errors
✅ HotkeyManager properly initialized
✅ Settings load/save hotkey configuration
✅ UI shows current hotkey binding
✅ Key capture works in Settings window
✅ Conflict detection prevents duplicates
✅ Reset to default works
✅ All windows use HotkeyManager
✅ Lock hotkey registered on startup

## Future Enhancements
- Add more global hotkeys (e.g., "New Message", "Toggle DND")
- Export/import hotkey profiles
- Hotkey hint overlay (like VS Code)
- Per-window hotkey scopes
- Global system hotkey registration (Windows/macOS APIs)
