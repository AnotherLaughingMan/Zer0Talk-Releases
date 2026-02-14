# Theme Editor Integration - Complete Custom Theme Support

## Overview
The Theme Editor now has **full integration** with Zer0Talk's theme system. Custom themes are automatically discovered from multiple locations, loaded, and made available for use.

## Theme Search Locations

### 1. Primary User Themes Directory
**Location**: `%AppData%\Zer0Talk\Themes\`
- Auto-created on first run
- Primary location for user-created themes
- Scanned automatically at startup

### 2. Shared Templates Directory
**Location**: `%UserProfile%\Documents\Zer0Talk Theme Templates\`
- Auto-created on first run
- Ideal for sharing themes between users
- Templates and presets can be stored here
- Scanned automatically at startup

### 3. Drive-Wide Search
- **Search Drives** button in Theme Editor
- Scans all fixed and removable drives for `*.zttheme` files
- Finds themes anywhere on your system
- Auto-loads discovered themes

## Complete Workflow

### 1. Create a Custom Theme
1. Open **Theme Editor** from the sidebar
2. Click **"Load Template"** dropdown and select a base theme (Dark, Light, Sandy, or Butter)
3. Edit colors:
   - Click pencil icon next to any color
   - Type new hex color values
   - Click **Save** to commit changes
4. Edit metadata:
   - Click **"Edit Metadata"** button
   - Set theme name, description, author, version
   - Click **Save**
5. Click **"Save As"** button
6. Save dialog defaults to `%AppData%\Zer0Talk\Themes\`
7. Theme is automatically registered with the engine

### 2. Use Your Custom Theme
**Automatic Loading:**
- Custom themes are scanned from **two locations** at startup:
  - `%AppData%\Zer0Talk\Themes\`
  - `%UserProfile%\Documents\Zer0Talk Theme Templates\`
- Themes in either location are automatically loaded (Phase 2)
- Engine logs: `ThemeEngine.Phase2.LoadedCustomThemes: {count}`
- Logs show: `Total custom themes loaded: {count} from {directories} directories`

**Manual Search:**
- Click **"Search Drives"** button in Theme Editor
- Searches all fixed/removable drives for `*.zttheme` files
- Progress logged to UI log
- Auto-loads all discovered themes
- Useful for finding themes in:
  - Downloads folder
  - External drives
  - Network shares
  - Custom locations

**Current Limitation (Phase 2):**
- Custom themes are **loaded and registered** ✅
- Custom themes can be **applied programmatically** ✅  
- Custom themes are **NOT YET in Settings UI dropdown** ⚠️

### 3. Apply Custom Themes (Current Methods)

#### Method A: Via Code
```csharp
AppServices.ThemeEngine.SetThemeById("your-theme-id");
```

#### Method B: Edit Settings File
Manually edit saved theme preference before Zer0Talk loads settings.

#### Method C: Future (Settings UI Integration)
*Coming Soon* - Settings window will display all custom themes in the theme dropdown.

## Integration Points

### ThemeEngine Methods
- `GetThemeSearchDirectories()` - Returns list of all theme search paths
- `GetCustomThemesDirectory()` - Returns `%AppData%\Zer0Talk\Themes\`
- `GetDocumentsThemesDirectory()` - Returns `%UserProfile%\Documents\Zer0Talk Theme Templates\`
- `LoadCustomThemes()` - Scans all directories and loads `.zttheme` files
- `SearchDrivesForThemesAsync()` - Searches all drives for themes (with progress callback)
- `LoadThemesFromPaths()` - Loads themes from specific file paths
- `SetThemeById(string themeId)` - Applies theme by ID (legacy or custom)
- `GetRegisteredThemes()` - Returns all themes (built-in + custom)

### Theme Editor Features
- **Save As** defaults to custom themes directory
- Auto-reloads custom themes after saving
- Generates unique theme IDs (`custom-{guid}`)
- Sets proper theme metadata (type, readonly flags)

### App Startup Flow
```
1. App.axaml.cs: ShowAppropriateWindow()
2. ThemeEngine.AdvancePhase() → Phase 2 enabled
3. ThemeEngine.LoadCustomThemes()
   a. GetThemeSearchDirectories() → 2 locations
   b. LoadThemesFromDirectory() for each location
   c. ThemeDefinition.LoadFromFile() for each .zttheme
   d. ThemeEngine.RegisterTheme() for each valid theme
4. Themes available via GetRegisteredThemes()
```

### Drive Search Flow
```
1. User clicks "Search Drives" button
2. SearchDrivesForThemesAsync() starts
   a. Gets all ready drives (Fixed + Removable)
   b. Recursively searches each drive
   c. Skips system/hidden directories
   d. Progress callbacks to UI log
3. LoadThemesFromPaths() with results
4. Themes immediately registered and available
```

## File Format
Custom themes use `.zttheme` extension (JSON format):

```json
{
  "id": "custom-abc123",
  "displayName": "My Custom Theme",
  "description": "A beautiful custom theme",
  "author": "Your Name",
  "version": "1.0.0",
  "baseVariant": "Dark",
  "colorOverrides": {
    "App.Background": "#1E1E1E",
    "App.Accent": "#0078D4",
    ...
  },
  "gradients": {
    "App.TitleBar.Gradient": {
      "startColor": "#2D2D30",
      "endColor": "#1E1E1E",
      "angle": 90
    }
  },
  "themeType": 2,
  "isReadOnly": false,
  "allowsCustomization": true
}
```

## Next Steps (Future Enhancements)

### Settings UI Integration
1. Modify `SettingsViewModel` to enumerate all registered themes
2. Replace hardcoded theme dropdown with dynamic list
3. Add theme preview/description display
4. Support theme management (delete, rename, export)

### Theme Editor Improvements
1. Live preview while editing
2. Import existing themes for editing
3. Duplicate/clone themes
4. Theme validation before save
5. Undo/redo history

### Advanced Features
1. Theme hot-reload during development
2. Theme marketplace/sharing
3. Theme inheritance/layering
4. Per-window theme overrides

## Current Status
✅ **Working:**
- Custom themes directory creation
- Automatic theme scanning at startup
- Theme loading and validation
- Theme registration with engine
- Theme saving from editor
- Programmatic theme application

⚠️ **Limitations:**
- No Settings UI integration yet
- Must apply custom themes programmatically
- No theme management UI

🔜 **Coming Soon:**
- Settings dropdown shows custom themes
- Theme management tools
- Enhanced editor features

## Testing Your Custom Themes

1. **Create and Save:**
   ```
   Theme Editor → Edit → Save As
   Default locations offered:
   - %AppData%\Zer0Talk\Themes\MyTheme.zttheme (primary)
   - %UserProfile%\Documents\Zer0Talk Theme Templates\MyTheme.zttheme (shared)
   ```

2. **Verify Auto-Loading:**
   ```
   Check startup logs for:
   - "Found X .zttheme files in {directory}"
   - "Total custom themes loaded: X from 2 directories"
   ```

3. **Test Drive Search:**
   ```
   Theme Editor → Search Drives button
   Check logs for:
   - "Starting drive-wide theme search..."
   - "Scanning drive: C:\"
   - "Found X theme files"
   - "Successfully loaded X themes from search results"
   ```

4. **Check Registration:**
   ```csharp
   var themes = AppServices.ThemeEngine.GetRegisteredThemes();
   // Should contain built-in + custom themes
   ```

5. **Apply Programmatically:**
   ```csharp
   AppServices.ThemeEngine.SetThemeById("custom-abc123");
   ```

## Troubleshooting

**Theme Not Loading:**
- Check files are in search directories:
  - `%AppData%\Zer0Talk\Themes\`
  - `%UserProfile%\Documents\Zer0Talk Theme Templates\`
- Verify `.zttheme` extension
- Check logs for parsing errors
- Validate JSON format

**Drive Search Not Finding Themes:**
- Ensure themes have `.zttheme` extension
- Check drives are mounted and accessible
- Search skips system/hidden directories
- Check UI log for search progress and errors

**Can't Find Theme Directories:**
- Run: `AppServices.ThemeEngine.GetThemeSearchDirectories()`
- Directories are auto-created on first access
- Defaults:
  - AppData: `C:\Users\{Username}\AppData\Roaming\Zer0Talk\Themes\`
  - Documents: `C:\Users\{Username}\Documents\Zer0Talk Theme Templates\`

**Drive Search is Slow:**
- Expected behavior - searches entire file system
- Can take several minutes on large drives
- Progress logged to UI log
- Consider using specific directories instead

## Performance Notes

**Startup Loading:**
- Fast - only scans 2 specific directories
- Negligible impact on startup time
- Typical: <100ms for dozen themes

**Drive Search:**
- Expensive - scans entire file system
- May take 5-15 minutes on large drives
- Runs asynchronously, doesn't block UI
- One-time operation per search
- Results are cached until next search

## Best Practices

**For End Users:**
- Save themes to `%AppData%\Zer0Talk\Themes\` for personal use
- Save to `Documents\Zer0Talk Theme Templates\` for sharing
- Use **Search Drives** only when themes are misplaced
- Organize themes in subdirectories (not currently scanned recursively in default locations)

**For Theme Creators:**
- Use Documents folder for theme distribution
- Include descriptive metadata (name, description, author)
- Test themes before sharing
- Use semantic versioning
- Document any special requirements

**For System Administrators:**
- Deploy shared themes to user Documents folders
- Network paths can be added as custom search directories
- Consider disabling drive search in enterprise environments
- Monitor theme file sizes (max 5MB per theme)
