# Settings Dirty Tracking Fix

**Date:** October 6, 2025  
**Issue:** System tray settings were not triggering "You Have Unsaved Changes" warning

## Problem

The three system tray settings were recently added but not integrated into the dirty tracking system:
- `ShowInSystemTray`
- `MinimizeToTray`
- `RunOnStartup`

These settings were being persisted correctly but the UI didn't show the "You Have Unsaved Changes" banner when they were modified.

## Root Cause

The SettingsViewModel uses a comprehensive dirty tracking system that requires:
1. **Baseline tracking variables** (`_base*` fields) to store original values
2. **Property names** in the `DirtyTrackedProperties` HashSet
3. **Comparison logic** in `ComputeHasUnsavedChanges()` method
4. **Baseline capture** in `CaptureBaseline()` method
5. **Discard logic** in `DiscardChanges()` method

The system tray settings were missing from all five of these locations.

## Solution Implemented

### 1. Added Baseline Tracking Variables
```csharp
private bool _baseShowInSystemTray;
private bool _baseMinimizeToTray;
private bool _baseRunOnStartup;
```

### 2. Added to DirtyTrackedProperties HashSet
```csharp
nameof(ShowInSystemTray),
nameof(MinimizeToTray),
nameof(RunOnStartup),
```

### 3. Added Comparison Logic in ComputeHasUnsavedChanges()
```csharp
if (_baseShowInSystemTray != _showInSystemTray) return true;
if (_baseMinimizeToTray != _minimizeToTray) return true;
if (_baseRunOnStartup != _runOnStartup) return true;
```

### 4. Added Baseline Capture in CaptureBaseline()
```csharp
_baseShowInSystemTray = _showInSystemTray;
_baseMinimizeToTray = _minimizeToTray;
_baseRunOnStartup = _runOnStartup;
```

### 5. Added Discard Logic in DiscardChanges()
```csharp
ShowInSystemTray = s.ShowInSystemTray;
MinimizeToTray = s.MinimizeToTray;
RunOnStartup = s.RunOnStartup;
```

## Files Modified

- `ViewModels/SettingsViewModel.cs` - Added system tray dirty tracking

## Testing Checklist

- [ ] Open Settings > General
- [ ] Toggle "Show icon in system tray" → Should show "You Have Unsaved Changes"
- [ ] Click Cancel → Setting should revert, warning should disappear
- [ ] Toggle again → Warning should reappear
- [ ] Click Apply → Warning should change to "Changes Saved" toast
- [ ] Close and reopen settings → Should show saved value
- [ ] Repeat for "Minimize to tray on close"
- [ ] Repeat for "Run on Windows startup"

## Notes on Ephemeral Settings

Some settings are intentionally **NOT** tracked for dirty state because they are ephemeral (runtime-only or diagnostic):

### Network Window Settings
- `Port`, `MajorNode`, `EnableGeoBlocking` - Managed by NetworkViewModel
- These have their own Save/Cancel buttons in the Network window
- Saved immediately when clicking Apply in that window

### Monitoring Window Settings
- `MonitoringIntervalMs` - Refresh rate slider (applied immediately)
- `MonitoringLogFontSize` - Font size slider (applied immediately)
- These are "live preview" settings that don't need save confirmation

### Window State Settings
- Window positions, sizes, states (Normal/Maximized/Minimized)
- Saved automatically on window close
- No user confirmation needed for layout changes

### Log Viewer Settings
- Font size, line count display
- Applied immediately, no persistence needed

## Architecture Notes

The dirty tracking system uses a two-phase approach:

### Phase 1: Detect Changes
- Every tracked property setter calls `OnPropertyChanged()`
- `OnPropertyChanged()` checks if property is in `DirtyTrackedProperties`
- If yes, calls `UpdateUnsavedChangesState()` which calls `ComputeHasUnsavedChanges()`
- `ComputeHasUnsavedChanges()` compares current values to baseline (`_base*` fields)

### Phase 2: Show UI Feedback
- If changes detected: `HasUnsavedChanges = true`
- This triggers `UpdateUnsavedWarningVisual()` which shows the banner
- Banner says "You Have Unsaved Changes"
- Apply button becomes actionable

### Phase 3: Handle Save/Cancel
- **Save (Apply)**: 
  - Persists to `AppSettings`
  - Applies changes immediately (e.g., shows tray icon)
  - Calls `CaptureBaseline()` to reset baseline
  - Shows "Changes Saved" toast
  - Banner disappears
  
- **Cancel**:
  - Calls `DiscardChanges()` 
  - Reverts properties to baseline values
  - Calls `CaptureBaseline()` to confirm clean state
  - Banner disappears

### Suppression
- `_suppressDirtyCheck` flag prevents spurious dirty detection during:
  - Initial load from disk
  - Programmatic updates (e.g., theme sync)
  - Baseline capture
  - Discard operation

## Future Considerations

When adding new persistent settings to SettingsViewModel:

1. **Is it ephemeral?** If yes (like monitoring intervals), skip dirty tracking
2. **Is it in another ViewModel?** If yes (like NetworkViewModel), let that VM handle it
3. **Otherwise**, add to all 5 dirty tracking locations:
   - Baseline variable declaration
   - `DirtyTrackedProperties` HashSet
   - `ComputeHasUnsavedChanges()` comparison
   - `CaptureBaseline()` capture
   - `DiscardChanges()` revert

## Build Status

✅ **Build succeeded** - No compilation errors
- Changes verified with `dotnet build -c Debug`
- All dirty tracking infrastructure in place
- Ready for runtime testing
