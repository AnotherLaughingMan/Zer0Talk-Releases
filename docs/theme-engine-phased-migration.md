# Theme Engine - Phased Migration Guide

## Overview

The Theme Engine is Zer0Talk's next-generation theming system, designed to replace the legacy ThemeService with a more flexible, extensible architecture. To minimize risk and preserve stability, the migration is being executed in **three distinct phases**, each building upon the previous one.

## Critical Design Principle: Fallback Safety

**At every phase, the legacy ThemeService remains operational and can be used as a fallback.** If any phase encounters issues, we can instantly rollback to the previous phase or directly to legacy without data loss or user disruption.

---

## Architecture Overview

### Components

1. **ThemeEngine** (`Services/ThemeEngine.cs`)
   - Main orchestrator for theme management
   - Phase-aware: behavior changes based on `CurrentPhase`
   - Wraps `ThemeService` for backwards compatibility

2. **ThemeDefinition** (`Models/ThemeDefinition.cs`)
   - Metadata describing a complete theme
   - Includes resource paths, colors, fonts, compatibility info
   - Can represent both new engine themes and legacy themes

3. **ThemeEngineConfig** (`Models/ThemeDefinition.cs`)
   - Configuration for engine behavior
   - Controls phase level, custom theme support, logging

4. **IThemeResourceLoader** (`Services/IThemeResourceLoader.cs`)
   - Abstraction for loading theme resources
   - Implementations:
     - `EmbeddedThemeResourceLoader` - Built-in themes (Phase 1+)
     - `FileSystemThemeResourceLoader` - User custom themes (Phase 2+)

5. **ThemeService** (Legacy, `Services/ThemeService.cs`)
   - Existing theme service
   - **Remains untouched** - continues to work independently
   - Used as fallback at all phases

---

## Phase 1: Legacy Wrapper (CURRENT PHASE)

### Status: **✅ IMPLEMENTED**

### Goal
Establish the ThemeEngine infrastructure without changing any user-facing behavior. All theme operations route through ThemeEngine but immediately delegate to legacy ThemeService.

### What's Active
- ✅ `ThemeEngine` class exists and is initialized in `AppServices`
- ✅ All public APIs route to `ThemeService`
- ✅ Logging infrastructure for engine activity
- ✅ Theme models and loader interfaces defined but not actively used
- ✅ Zero risk: identical behavior to legacy system

### Implementation Details

```csharp
// Phase 1 behavior in ThemeEngine.SetTheme()
case EnginePhase.LegacyWrapper:
    // Direct passthrough - no new logic
    _legacyService.SetTheme(legacyTheme);
    break;
```

### APIs Available

All APIs are pass-through to legacy:

| Method | Behavior |
|--------|----------|
| `SetTheme(ThemeOption)` | Routes to `ThemeService.SetTheme()` |
| `ApplyThemeEngine(font, scale)` | Routes to `ThemeService.ApplyThemeEngine()` |
| `GetCurrentTheme()` | Returns `ThemeService.CurrentTheme` |
| `GetCurrentFontFamily()` | Returns `ThemeService.CurrentUiFontFamily` |
| `GetCurrentScale()` | Returns `ThemeService.CurrentUiScale` |

### Migration Path from Legacy Code

**No migration needed in Phase 1.** Existing code using `AppServices.Theme` continues to work unchanged.

Optional early adoption:
```csharp
// Old way (still works)
AppServices.Theme.SetTheme(ThemeOption.Dark);

// New way (Phase 1 - identical behavior)
AppServices.ThemeEngine.SetTheme(ThemeOption.Dark);
```

### Testing Phase 1
1. ✅ Build succeeds
2. ✅ All existing themes work identically
3. ✅ `logs/theme_engine.log` shows pass-through activity
4. ✅ No user-visible changes

### Rollback Strategy
**Not needed** - Phase 1 is essentially a no-op wrapper. If any issues arise, simply:
- Remove `ThemeEngine` initialization from `AppServices`
- Continue using `ThemeService` directly

---

## Phase 2: Hybrid Mode (IMPLEMENTED)

### Status: **✅ IMPLEMENTED**

### Goal
Introduce new theme engine capabilities while keeping legacy themes as functional fallbacks. New features become available, but system gracefully degrades if they fail.

### What Will Be Active
- 🔶 ThemeEngine registers built-in legacy themes as `ThemeDefinition` objects
- 🔶 `EmbeddedThemeResourceLoader` actively loads themes
- 🔶 `SetThemeById(string)` API becomes functional
- 🔶 Color overrides and dynamic palettes supported
- 🔶 Automatic fallback to legacy if engine theme fails

### Implementation Approach

```csharp
case EnginePhase.HybridMode:
    // Try new system first
    if (!TrySetThemeHybrid(legacyTheme))
    {
        LogEngine($"Hybrid mode failed, falling back to legacy");
        _fallbackActive = true;
        _legacyService.SetTheme(legacyTheme);
    }
    break;
```

### New Features in Phase 2
1. **Theme Registration System**
   ```csharp
   var themeDef = new ThemeDefinition
   {
       Id = "custom-dark-blue",
       DisplayName = "Dark Blue",
       ResourceDictionaries = new List<string> 
       { 
           "avares://Zer0Talk/Styles/DarkThemeOverrides.axaml" 
       },
       ColorOverrides = new Dictionary<string, string>
       {
           ["App.Accent"] = "#4A90E2",
           ["App.AccentLight"] = "#6AA3E8"
       }
   };
   
   AppServices.ThemeEngine.RegisterTheme(themeDef);
   AppServices.ThemeEngine.SetThemeById("custom-dark-blue");
   ```

2. **Dynamic Color Overrides**
   - Themes can override specific colors without new .axaml files
   - Useful for accent color variations

3. **Theme Metadata**
   - Descriptions, authors, versions
   - Foundation for theme marketplace in Phase 3

4. **Graceful Degradation**
   ```csharp
   // Check if fallback was triggered
   if (AppServices.ThemeEngine.IsFallbackActive)
   {
       // Show notification that custom theme failed
       // Offer to reset to default
   }
   ```

### Migration Path to Phase 2

When Phase 2 is ready:

1. **Update Phase Flag**
   ```csharp
   // In initialization code
   AppServices.ThemeEngine.AdvancePhase(); // Move to Phase 2
   ```

2. **Adopt New APIs Gradually**
   ```csharp
   // Still works - maps to engine theme
   AppServices.ThemeEngine.SetTheme(ThemeOption.Dark);
   
   // New capability - theme by ID
   AppServices.ThemeEngine.SetThemeById("legacy-dark");
   ```

3. **Register Custom Variations**
   ```csharp
   // Create theme variants without new files
   var warmDark = ThemeDefinition.FromLegacyTheme(ThemeOption.Dark);
   warmDark.Id = "dark-warm";
   warmDark.DisplayName = "Dark Warm";
   warmDark.ColorOverrides["App.Accent"] = "#FF8C42";
   
   AppServices.ThemeEngine.RegisterTheme(warmDark);
   ```

### Testing Phase 2
1. All legacy themes still work via `SetTheme(ThemeOption)`
2. New `SetThemeById()` API successfully loads registered themes
3. Color overrides apply correctly
4. Fallback triggers when theme loading fails
5. `IsFallbackActive` accurately reflects fallback state

### Rollback Strategy Phase 2
```csharp
// Emergency rollback to Phase 1
if (AppServices.ThemeEngine.IsFallbackActive || criticalIssue)
{
    AppServices.ThemeEngine.RollbackPhase(); // Back to Phase 1
    // System automatically uses pass-through behavior
}
```

---

## Phase 3: Theme Builder UI (PLANNED)

### Status: **� DESIGN PHASE**

### Goal
Enable users to create custom theme variants directly from the Settings → Appearance panel, using existing themes as templates.

### Planned Features

1. **Theme Creation Wizard in Settings**
   - "Create Custom Theme" button in Appearance panel
   - Select base theme as template:
     - **Blank** - Neutral grey palette, ideal starting point for full customization
     - Dark - Rich charcoal base
     - Light - Clean white base
     - Sandy - Warm beach tones
     - Butter - Soft yellow palette
   - Name your theme
   - Visual color picker for accent colors
   - Real-time preview in settings window
   - Save custom theme

2. **Theme Customization Panel**
   - Edit accent colors (primary, light, hover states)
   - Adjust background tones (background, surface, card)
   - Modify text colors (primary, secondary)
   - Configure border colors
   - **Window Title Bar Gradients** (optional):
     - Enable/disable gradient for title bars
     - Start color picker
     - End color picker
     - Gradient direction (horizontal, vertical, diagonal TL→BR, diagonal TR→BL)
     - Gradient angle (0-360° for full control)
     - Preview on sample title bar
   - Font family override
   - UI scale adjustment
   - All changes preview live

3. **Theme Management**
   - List of custom themes in Settings
   - Edit existing custom themes
   - Duplicate themes to create variants
   - Delete custom themes
   - Reset theme to default
   - Export theme as file (.zttheme)
   - Import theme from file

4. **Template System**
   - **Blank** template - Neutral starting point with subdued greys
   - All 4 legacy themes available as templates
   - Templates are read-only (clone to customize)
   - Custom themes stored separately from built-in
   - Easy "Create variant of..." workflow

**Gradient Support**
   - Linear gradients for window/dialog title bars
   - Configurable start/end colors
   - Multiple gradient directions supported
   - Optional: gradients can be disabled for solid colors
   - Stored in ThemeDefinition as gradient metadata

### Implementation Details

**New Settings Panel Structure:**
```
Settings → Appearance
├── Theme Selector (shows built-in + custom)
├── [Create Custom Theme] Button
├── Theme Details (name, author, description)
└── [Edit Theme] Button (for custom themes only)
```

**Theme Editor Window:**
- Left panel: Color pickers and controls
- Right panel: Live preview of UI elements
- Bottom: Save / Cancel / Export buttons

**Storage:**
- Custom themes saved to: `%AppData%/Zer0Talk/Themes/custom/`
- Format: JSON files with `.zttheme` extension
- Contains all ThemeDefinition properties
- Can reference existing .axaml files or override colors

---

## Phase 4: Advanced Features (FUTURE)

### Status: **🔴 NOT YET DESIGNED**

### Goal
Advanced theme capabilities and sharing features.

### Planned Features

1. **P2P Theme Sharing**
   - Share theme files with contacts
   - Theme preview before accepting
   - Verify theme source/signature
   - Install shared themes

2. **Advanced Customization**
   - Per-window theme overrides
   - Time-based theme switching (dark at night, light during day)
   - Accent color extraction from wallpaper
   - Custom font bundles
   - Animated theme transitions

3. **Theme Collections**
   - Group related theme variants
   - Theme presets (High Contrast, Colorblind-friendly, etc.)
   - Seasonal themes
   - Community theme packs

4. **Legacy Deprecation (But Not Removal)**
   - Legacy themes remain as built-in fallbacks
   - ThemeService kept as emergency rollback
   - "Classic Themes" section in UI

### Phase 3 Implementation Timeline
Phase 3 will not begin until:
- Phase 2 has been stable in production for at least 2 releases
- User feedback on Phase 2 features is positive
- No critical bugs in hybrid mode
- Design mockups for Theme Builder UI approved

### Key Phase 3 Deliverables
1. **Settings UI Updates**
   - New "Create Custom Theme" workflow
   - Theme management interface
   - Import/export functionality

2. **Theme Editor Component**
   - Reusable color picker controls
   - Live preview system
   - Validation before save

3. **File System Integration**
   - `.zttheme` file format specification
   - FileSystemThemeResourceLoader implementation
   - Theme validation and sandboxing

### Testing Phase 3
1. Create custom theme from each legacy template
2. Verify live preview updates correctly
3. Test theme save/load cycle
4. Export theme and reimport on different profile
5. Edit existing custom theme
6. Delete custom theme
7. Verify fallback if custom theme corrupted

### Rollback Strategy Phase 3
Built-in themes always work regardless of custom theme issues:
```csharp
// If custom themes cause issues, users can always select built-in
AppServices.ThemeEngine.SetTheme(ThemeOption.Dark); // Always works

// Or delete custom themes
var customThemesDir = Path.Combine(AppDataPaths.Root, "Themes", "custom");
if (Directory.Exists(customThemesDir))
    Directory.Delete(customThemesDir, true);
```

---

## Phase 4 Implementation Timeline
Phase 4 features depend on:
- Phase 3 Theme Builder stable and well-received
- User demand for advanced features
- P2P infrastructure ready for theme sharing

---

## Rollback Decision Matrix

| Issue Severity | Action | Timeline |
|----------------|--------|----------|
| **Critical Bug** (crashes, data loss) | Rollback to Phase 1 immediately | Within 1 hour of detection |
| **Major Bug** (themes broken, fallbacks failing) | Rollback to previous phase | Within 1 day |
| **Minor Issue** (cosmetic, edge case) | Fix forward, no rollback | Next patch release |
| **Enhancement Request** | Queue for next phase | Roadmap planning |

## Emergency Rollback Procedure

### Complete Rollback to Legacy (Nuclear Option)

If the entire ThemeEngine needs to be disabled:

1. **Code Change** (in `AppServices.cs`):
   ```csharp
   // Comment out ThemeEngine initialization
   // public static ThemeEngine ThemeEngine { get; } = new(Theme);
   ```

2. **Update all call sites**:
   ```csharp
   // Change from:
   AppServices.ThemeEngine.SetTheme(theme);
   
   // Back to:
   AppServices.Theme.SetTheme(theme);
   ```

3. **Build and deploy**

This rollback has **zero risk** because `ThemeService` is completely independent and untouched by the engine.

---

## Configuration File (Future)

Phase 2+ will support `theme-engine-config.json` in app data directory:

```json
{
  "enabled": true,
  "phase": 2,
  "activeThemeId": "legacy-dark",
  "fallbackThemeId": "legacy-dark",
  "allowCustomThemes": false,
  "customThemesDirectory": null,
  "enableDetailedLogging": true,
  "maxCustomThemes": 50,
  "enableHotReload": false
}
```

---

## Monitoring and Telemetry

### Log Files

All theme engine activity is logged to `logs/theme_engine.log`:

```
2025-10-18T14:23:45.123Z [themeEngine] ThemeEngine initialized in Phase LegacyWrapper
2025-10-18T14:23:47.456Z [themeEngine] SetTheme(Dark) via Phase LegacyWrapper
2025-10-18T14:25:12.789Z [themeEngine] ApplyThemeEngine(font='Segoe UI', scale=1.0)
```

Legacy theme operations continue logging to `logs/theme.log` (unchanged).

### Health Checks

Monitor these indicators:

| Metric | Healthy | Warning | Critical |
|--------|---------|---------|----------|
| Fallback Active | False | True (Phase 2+) | N/A |
| Theme Load Time | < 50ms | 50-200ms | > 200ms |
| Theme Validation Failures | 0 | 1-5 per session | > 5 per session |

---

## Code Migration Examples

### Example 1: Settings Window Theme Selector

**Current (Phase 1 - unchanged):**
```csharp
// SettingsViewModel.cs
public void ApplyTheme()
{
    var theme = IndexToTheme(_themeIndex);
    AppServices.Theme.SetTheme(theme);
}
```

**Phase 2 (optional migration):**
```csharp
public void ApplyTheme()
{
    var theme = IndexToTheme(_themeIndex);
    
    // Use engine API (auto-fallback to legacy)
    AppServices.ThemeEngine.SetTheme(theme);
    
    // Check if fallback was needed
    if (AppServices.ThemeEngine.IsFallbackActive)
    {
        // Log or notify user
        Console.WriteLine("Theme applied via fallback");
    }
}
```

**Phase 3 (new features):**
```csharp
public void ApplyTheme()
{
    // Use theme ID instead of enum
    var themeId = _selectedThemeId; // e.g., "custom-midnight-blue"
    
    if (!AppServices.ThemeEngine.SetThemeById(themeId))
    {
        // Fallback to default
        AppServices.ThemeEngine.SetTheme(ThemeOption.Dark);
        await Dialogs.ShowWarningAsync("Theme Failed", 
            "Custom theme could not be loaded. Using default.");
    }
}
```

### Example 2: Dynamic Accent Color

**Phase 2+ Feature:**
```csharp
// Create a variant of dark theme with custom accent
var customTheme = ThemeDefinition.FromLegacyTheme(ThemeOption.Dark);
customTheme.Id = "dark-purple-accent";
customTheme.DisplayName = "Dark with Purple Accent";
customTheme.ColorOverrides = new Dictionary<string, string>
{
    ["App.Accent"] = "#9B59B6",
    ["App.AccentLight"] = "#B370CF",
    ["App.SelectionBackground"] = "#8E44AD"
};

// Register and apply
AppServices.ThemeEngine.RegisterTheme(customTheme);
AppServices.ThemeEngine.SetThemeById("dark-purple-accent");
```

---

## FAQ

### Q: Do I need to change my code for Phase 1?
**A:** No. Phase 1 is a transparent wrapper. All existing code continues to work unchanged.

### Q: What if ThemeEngine breaks something?
**A:** Legacy `ThemeService` is completely independent and untouched. You can always fall back to it instantly.

### Q: When should I start using ThemeEngine APIs?
**A:** You can start now if you want, but there's no benefit until Phase 2. The safest approach is to wait until Phase 2 is stable in production.

### Q: Will my custom .axaml theme files still work?
**A:** Yes. In Phase 2, the engine will support loading existing .axaml resource dictionaries. In Phase 3, you'll have even more flexibility.

### Q: How do I know which phase is active?
**A:** Check `AppServices.ThemeEngine.CurrentPhase` or look in `logs/theme_engine.log`.

### Q: Can I skip Phase 1 and go straight to Phase 2?
**A:** Technically yes, but strongly discouraged. The phased approach is for safety. Each phase should be validated in production before advancing.

### Q: What happens to my selected theme when rolling back phases?
**A:** Theme selection persists via `AppSettings.Theme` (ThemeOption enum). When rolling back, the engine will map any custom theme back to the nearest legacy equivalent.

---

## Testing Checklist

### Phase 1 Validation ✅
- [x] Build succeeds without errors
- [x] All 4 legacy themes (Dark, Light, Sandy, Butter) work
- [x] Font family changes apply correctly
- [x] UI scale changes apply correctly
- [x] Theme changes persist across app restarts
- [x] Theme engine log created and contains entries
- [x] No regressions in theme switching performance
- [x] Settings window theme selector works unchanged

### Phase 2 Validation (When Implemented)
- [ ] RegisterTheme() successfully adds new themes
- [ ] SetThemeById() loads registered themes
- [ ] Color overrides apply correctly to UI
- [ ] Legacy theme mapping still works
- [ ] Fallback triggers on invalid theme ID
- [ ] IsFallbackActive accurately reflects state
- [ ] EmbeddedThemeResourceLoader loads .axaml files
- [ ] Theme validation catches malformed definitions
- [ ] Performance remains acceptable (<50ms theme switch)
- [ ] Settings window shows new themes (if UI updated)

### Phase 3 Validation (Future)
- TBD based on Phase 3 design

---

## Version History

| Version | Date | Phase | Changes |
|---------|------|-------|---------|
| 1.0.0 | 2025-10-18 | Phase 1 | Initial implementation: Legacy wrapper established |
| 2.0.0 | 2025-10-18 | Phase 2 | Hybrid mode implemented: Theme registration, engine loading, color overrides, fallback |
| 3.0.0 | TBD | Phase 3 | Theme Builder UI: Create/edit custom themes from Settings panel with live preview |
| 4.0.0 | TBD | Phase 4 | Advanced features: P2P theme sharing, time-based switching, theme collections |

---

## Summary

The Theme Engine is being built **incrementally and safely**, with the legacy system always available as a safety net. This document serves as the single source of truth for understanding the migration process.

**Current Status: Phase 2 Available ✅**
- ThemeEngine with full Phase 2 hybrid mode
- Built-in themes registered automatically
- Color overrides supported
- Automatic fallback to legacy on errors
- Ready for testing and validation

**Next Steps:**
1. Enable Phase 2 via `AdvancePhase()` when ready
2. Test Phase 2 using `docs/theme-engine-phase2-testing.md`
3. Validate hybrid mode in development (2-3 builds)
4. Monitor for any issues or performance impacts
5. Design Phase 3 when Phase 2 is stable

**Key Principle:** Never break what works. The phased approach ensures we can always rollback to a known-good state.
