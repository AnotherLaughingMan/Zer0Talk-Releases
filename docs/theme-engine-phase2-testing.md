# Theme Engine Phase 2 - Testing Guide

## Phase 2 Status: ✅ IMPLEMENTED

Phase 2 (Hybrid Mode) has been fully implemented and is ready for testing.

## What's New in Phase 2

### 1. **Built-in Theme Registration**
All 4 legacy themes are now automatically registered as `ThemeDefinition` objects on startup:
- `legacy-dark`
- `legacy-light`
- `legacy-sandy`
- `legacy-butter`

### 2. **Engine-Based Theme Application**
When Phase 2 is active, themes are applied through the new engine path using `IThemeResourceLoader`.

### 3. **Automatic Fallback**
If theme application fails, the system automatically falls back to legacy `ThemeService`.

### 4. **Color Override Support**
Themes can now define color overrides without needing new .axaml files.

### 5. **Theme Validation**
Themes are validated before application to catch issues early.

---

## How to Enable Phase 2

### Option 1: Programmatic Activation (In Code)

Add this to your startup code (e.g., in `StartupInit.cs` or `Program.cs`):

```csharp
// After AppServices is initialized
AppServices.ThemeEngine.AdvancePhase(); // Move from Phase 1 to Phase 2
```

### Option 2: Runtime Activation (Via Debug Console)

If you have access to a debug console or can add a test button:

```csharp
private void OnTestPhase2Button_Click(object sender, RoutedEventArgs e)
{
    // Advance to Phase 2
    if (AppServices.ThemeEngine.AdvancePhase())
    {
        // Re-apply current theme through engine
        var currentTheme = AppServices.ThemeEngine.GetCurrentTheme();
        AppServices.ThemeEngine.SetTheme(currentTheme);
        
        Console.WriteLine($"Phase 2 activated! Current phase: {AppServices.ThemeEngine.CurrentPhase}");
    }
}
```

---

## Testing Checklist

### Basic Functionality ✅

- [x] ✅ Build succeeds without errors
- [ ] Switch to Phase 2
- [ ] Apply Dark theme → verify it works
- [ ] Apply Light theme → verify it works
- [ ] Apply Sandy theme → verify it works
- [ ] Apply Butter theme → verify it works
- [ ] Check `logs/theme_engine.log` for proper logging
- [ ] Verify no fallback warnings in log

### API Testing

#### Test 1: SetTheme() with Legacy Enum
```csharp
// Should work in both Phase 1 and Phase 2
AppServices.ThemeEngine.SetTheme(ThemeOption.Dark);
AppServices.ThemeEngine.SetTheme(ThemeOption.Light);
AppServices.ThemeEngine.SetTheme(ThemeOption.Sandy);
AppServices.ThemeEngine.SetTheme(ThemeOption.Butter);
```

#### Test 2: SetThemeById() (New in Phase 2)
```csharp
// Only works in Phase 2+
bool success = AppServices.ThemeEngine.SetThemeById("legacy-dark");
Console.WriteLine($"SetThemeById success: {success}");

// Try all built-in themes
AppServices.ThemeEngine.SetThemeById("legacy-light");
AppServices.ThemeEngine.SetThemeById("legacy-sandy");
AppServices.ThemeEngine.SetThemeById("legacy-butter");
```

#### Test 3: Check Registered Themes
```csharp
var themes = AppServices.ThemeEngine.GetRegisteredThemes();
Console.WriteLine($"Registered themes: {themes.Count}");
foreach (var theme in themes)
{
    Console.WriteLine($"  - {theme.Key}: {theme.Value.DisplayName}");
}

// Expected output:
// Registered themes: 5
//   - template-blank: Blank
//   - legacy-dark: Dark
//   - legacy-light: Light
//   - legacy-sandy: Sandy
//   - legacy-butter: Butter
```

#### Test 4: Custom Theme with Color Overrides
```csharp
// Create a custom variant
var customTheme = ThemeDefinition.FromLegacyTheme(ThemeOption.Dark);
customTheme.Id = "dark-purple";
customTheme.DisplayName = "Dark Purple";
customTheme.Description = "Dark theme with purple accent";
customTheme.ColorOverrides = new Dictionary<string, string>
{
    ["App.Accent"] = "#9B59B6",
    ["App.AccentLight"] = "#B370CF",
    ["App.SelectionBackground"] = "#8E44AD"
};

// Register and apply
AppServices.ThemeEngine.RegisterTheme(customTheme);
AppServices.ThemeEngine.SetThemeById("dark-purple");

// Check if it applied
var registered = AppServices.ThemeEngine.GetRegisteredThemes();
Console.WriteLine($"Has dark-purple: {registered.ContainsKey("dark-purple")}");
```

#### Test 5: Blank Template with Custom Gradients (Phase 2+)
```csharp
// Start from Blank template
var blankTheme = ThemeDefinition.CreateBlankTemplate();
blankTheme.Id = "custom-ocean";
blankTheme.DisplayName = "Ocean Theme";
blankTheme.Description = "Cool ocean-inspired theme with gradient title bar";

// Customize colors
blankTheme.ColorOverrides["App.Accent"] = "#00A8E8";
blankTheme.ColorOverrides["App.AccentLight"] = "#00C9FF";

// Add gradient to title bar
blankTheme.Gradients["App.TitleBarBackground"] = new GradientDefinition
{
    StartColor = "#003459",
    EndColor = "#007EA7",
    Direction = GradientDirection.Horizontal
};

// Register and apply
AppServices.ThemeEngine.RegisterTheme(blankTheme);
AppServices.ThemeEngine.SetThemeById("custom-ocean");
```

#### Test 6: Diagonal Gradient Example
```csharp
var sunsetTheme = ThemeDefinition.FromLegacyTheme(ThemeOption.Dark);
sunsetTheme.Id = "sunset-gradient";
sunsetTheme.DisplayName = "Sunset";

// Diagonal gradient (top-left to bottom-right)
sunsetTheme.Gradients["App.TitleBarBackground"] = new GradientDefinition
{
    StartColor = "#FF6B35",
    EndColor = "#F7931E",
    Angle = 45.0 // or use Direction = GradientDirection.DiagonalDown
};

AppServices.ThemeEngine.RegisterTheme(sunsetTheme);
AppServices.ThemeEngine.SetThemeById("sunset-gradient");
```

#### Test 7: Fallback Behavior
```csharp
// Try to apply an invalid theme
bool success = AppServices.ThemeEngine.SetThemeById("nonexistent-theme");
Console.WriteLine($"Invalid theme success: {success}"); // Should be false

// Check if fallback is active
bool fallbackActive = AppServices.ThemeEngine.IsFallbackActive;
Console.WriteLine($"Fallback active: {fallbackActive}"); // Should be false (no fallback needed for invalid request)
```

---

## Log File Analysis

### Phase 2 Log Entries

Look for these entries in `logs/theme_engine.log`:

```
2025-10-18T... [themeEngine] ThemeEngine initialized in Phase LegacyWrapper
2025-10-18T... [themeEngine] Registered built-in theme: legacy-dark
2025-10-18T... [themeEngine] Registered built-in theme: legacy-light
2025-10-18T... [themeEngine] Registered built-in theme: legacy-sandy
2025-10-18T... [themeEngine] Registered built-in theme: legacy-butter
2025-10-18T... [themeEngine] Advancing from Phase LegacyWrapper to Phase HybridMode
2025-10-18T... [themeEngine] SetTheme(Dark) via Phase HybridMode
2025-10-18T... [themeEngine] TrySetThemeHybrid: Mapping Dark to legacy-dark
2025-10-18T... [themeEngine] ApplyEngineTheme: Starting application of legacy-dark
2025-10-18T... [themeEngine] Set base variant to Dark
2025-10-18T... [themeEngine] Removed 1 old theme overrides
2025-10-18T... [themeEngine] Added 1 new theme styles
2025-10-18T... [themeEngine] No color overrides to apply
2025-10-18T... [themeEngine] Applied font: ..., scale: 1.0
2025-10-18T... [themeEngine] Refreshed 2 windows
2025-10-18T... [themeEngine] Successfully applied theme legacy-dark
```

### Success Indicators
✅ No "fallback" messages
✅ "Successfully applied theme" messages
✅ Correct number of windows refreshed
✅ Theme resources loaded without errors

### Warning Signs
⚠️ "Hybrid mode failed, falling back to legacy"
⚠️ "Failed to load resource dictionaries"
⚠️ "Theme validation failed"

---

## Performance Metrics

### Expected Performance

| Operation | Phase 1 (Legacy) | Phase 2 (Engine) | Acceptable Range |
|-----------|------------------|------------------|------------------|
| Theme Switch | ~30-50ms | ~40-60ms | < 100ms |
| Startup Registration | N/A | ~5-10ms | < 50ms |
| Color Override | N/A | < 5ms | < 10ms |

### Measuring Performance

Add timing to your tests:

```csharp
var sw = System.Diagnostics.Stopwatch.StartNew();
AppServices.ThemeEngine.SetTheme(ThemeOption.Dark);
sw.Stop();
Console.WriteLine($"Theme switch took: {sw.ElapsedMilliseconds}ms");
```

---

## Rollback Procedure

### If Phase 2 Has Issues

#### Option 1: Rollback to Phase 1
```csharp
AppServices.ThemeEngine.RollbackPhase();
```

#### Option 2: Restart and Don't Advance
Simply remove or comment out the `AdvancePhase()` call from your startup code.

---

## Known Limitations in Phase 2

1. **No Custom Theme Files Yet**
   - FileSystemThemeResourceLoader is not active
   - Can only use registered in-memory themes
   - Phase 3 will add file-based themes

2. **Font/Scale Still Via Legacy Service**
   - Font and scale settings use legacy ThemeService internally
   - This is intentional for stability
   - Will be unified in Phase 3

3. **No Hot Reload**
   - Theme changes require manual application
   - Theme file watching not implemented yet
   - Phase 3 feature

---

## Integration Testing

### Full Application Flow

1. **Start Application** → Phase 1 active by default
2. **Open Settings** → Theme selector shows 4 themes
3. **Advance to Phase 2** → `AppServices.ThemeEngine.AdvancePhase()`
4. **Change Theme** → Use existing UI, should work identically
5. **Check Logs** → Verify engine path is used
6. **Create Custom Theme** → Register and apply custom variant
7. **Restart App** → Verify theme persists (via AppSettings)

### Settings Integration

Your existing `SettingsViewModel.cs` should work without changes:

```csharp
// This code already works in both Phase 1 and Phase 2
public void ApplyTheme()
{
    var theme = IndexToTheme(_themeIndex);
    AppServices.ThemeEngine.SetTheme(theme); // Works in both phases
}
```

---

## Success Criteria

Phase 2 is considered successful when:

✅ All 4 legacy themes work identically to Phase 1
✅ SetThemeById() successfully loads registered themes  
✅ Custom theme with color overrides applies correctly
✅ No performance regression (< 100ms theme switch)
✅ No visual glitches or layout issues
✅ Fallback triggers correctly on errors
✅ Logs show proper engine activity
✅ Can rollback to Phase 1 if needed

---

## Next Steps After Phase 2 Validation

1. **Monitor in Production** (1-2 releases)
2. **Gather User Feedback** on theme system
3. **Plan Phase 3 Features** based on feedback
4. **Implement File-Based Themes** (Phase 3)
5. **Add Theme Builder UI** (Phase 3)

---

## Support & Troubleshooting

### Issue: Theme not applying in Phase 2
**Check:**
- Is Phase 2 actually active? Check `CurrentPhase` property
- Are there errors in `logs/theme_engine.log`?
- Try fallback: Does legacy `AppServices.Theme.SetTheme()` work?

### Issue: Fallback always triggering
**Check:**
- Theme validation errors in log
- Resource dictionary paths correct
- Theme ID matches registered ID

### Issue: Colors wrong after applying custom theme
**Check:**
- Color override format (must be hex like "#9B59B6")
- Resource keys match actual keys in .axaml files
- Check if color overrides were applied (in log)

---

## Code Examples Repository

See `docs/theme-engine-phased-migration.md` for more detailed code examples and migration patterns.
