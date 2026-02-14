# Theme Engine - Quick Reference

## Current Status: Phase 2 (Hybrid Mode) ✅

**What it does:** Applies themes through engine with automatic fallback to legacy
**Risk level:** Low - automatic fallback on any errors
**User impact:** None when in Phase 1, new features available in Phase 2
**Activation:** Call `AppServices.ThemeEngine.AdvancePhase()` to enable

---

## Quick Start

### Using Theme Engine (Phase 1)

```csharp
// All these work identically to before:
AppServices.ThemeEngine.SetTheme(ThemeOption.Dark);
AppServices.ThemeEngine.ApplyThemeEngine("Segoe UI", 1.2);
var currentTheme = AppServices.ThemeEngine.GetCurrentTheme();
```

### Or Continue Using Legacy

```csharp
// Old way still works (no migration required)
AppServices.Theme.SetTheme(ThemeOption.Dark);
```

---

## Phase Control

```csharp
// Check current phase
var phase = AppServices.ThemeEngine.CurrentPhase;
// Returns: EnginePhase.LegacyWrapper

// Advance to next phase (when Phase 2 is ready)
AppServices.ThemeEngine.AdvancePhase();

// Emergency rollback
AppServices.ThemeEngine.RollbackPhase();
```

---

## Monitoring

### Check if fallback is active (Phase 2+)
```csharp
if (AppServices.ThemeEngine.IsFallbackActive)
{
    // Custom theme failed, using legacy fallback
}
```

### Log files
- **Theme Engine:** `logs/theme_engine.log`
- **Legacy Theme:** `logs/theme.log` (still active)

---

## Phase Roadmap

| Phase | Status | Description |
|-------|--------|-------------|
| **Phase 1** | ✅ Complete | Legacy wrapper - zero risk |
| **Phase 2** | ✅ Implemented | Hybrid mode - new + legacy coexist |
| **Phase 3** | � Planned | Theme Builder - create custom themes in Settings |
| **Phase 4** | 🔴 Future | Advanced - P2P sharing, auto-switching, collections |

---

## When to Use What

| Scenario | Recommendation |
|----------|----------------|
| **New code now** | Use either - they're identical in Phase 1 |
| **Existing code** | No changes needed |
| **Phase 2 features** | Wait for Phase 2 announcement |
| **Production issues** | Can instantly rollback to legacy |

---

## Rollback Strategy

### Minor rollback (Phase to Phase)
```csharp
AppServices.ThemeEngine.RollbackPhase();
```

### Nuclear option (Disable Engine)
Comment out in `AppServices.cs`:
```csharp
// public static ThemeEngine ThemeEngine { get; } = new(Theme);
```
Change call sites back to `AppServices.Theme.*`

---

## Files Added

| File | Purpose |
|------|---------|
| `Services/ThemeEngine.cs` | Main engine coordinator |
| `Services/IThemeResourceLoader.cs` | Theme loading abstraction |
| `Models/ThemeDefinition.cs` | Theme metadata models |
| `docs/theme-engine-phased-migration.md` | Complete migration guide |
| `docs/theme-engine-quick-reference.md` | This file |

---

## Key Principles

1. **Never break what works** - Legacy always available
2. **Gradual migration** - Phases validated before advancing  
3. **Instant rollback** - Can revert at any time
4. **Zero forced migration** - Old APIs continue to work

---

## Next Steps

1. ✅ Phase 1 implemented and tested
2. ⏳ Monitor Phase 1 in production (1-2 releases)
3. ⏳ Design Phase 2 implementation
4. ⏳ Implement and test Phase 2
5. ⏳ Phase 3 planning

---

## Support

**Full documentation:** `docs/theme-engine-phased-migration.md`
**Issues:** Check `logs/theme_engine.log` and `logs/theme.log`
**Questions:** See FAQ in migration guide
