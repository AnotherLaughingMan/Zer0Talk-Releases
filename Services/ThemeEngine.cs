/*
    ThemeEngine - Next-generation theming system with phased rollout
    
    ARCHITECTURE:
    - Wraps legacy ThemeService to preserve existing functionality
    - Operates in phases to enable gradual migration
    - Maintains fallback to old themes if new system fails
    
    PHASES:
    Phase 1: Legacy Wrapper (CURRENT)
        - All calls route to ThemeService
        - No new functionality, just infrastructure
        - Zero risk migration
    
    Phase 2: Hybrid Mode (PLANNED)
        - New theme definitions coexist with legacy
        - Legacy themes remain as fallback
        - Gradual feature addition
    
    Phase 3: Full Engine (PLANNED)
        - Complete theme customization
        - User themes, dynamic palettes
        - Legacy themes deprecated but retained
*/

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Zer0Talk.Models;

namespace Zer0Talk.Services
{
    public partial class ThemeEngine
    {
        // Event fired when custom themes are reloaded (e.g., after saving a new theme)
        public static event EventHandler? ThemesReloaded;
        // Event fired whenever a theme is applied so open views can repaint cached visuals.
        public static event EventHandler? ThemeApplied;

        // Phase control flag - determines engine behavior
        public enum EnginePhase
        {
            LegacyWrapper = 1,    // Phase 1: Route everything to ThemeService
            HybridMode = 2,       // Phase 2: New + Legacy coexist
            FullEngine = 3        // Phase 3: Full new system
        }

        // Diagnostic helper: log resource type and value for a given key
        private void LogResourceDiagnostics(Application app, string key)
        {
            try
            {
                if (app.Resources.ContainsKey(key))
                {
                    var res = app.Resources[key];
                    LogEngine($"[Diagnostics] Resource '{key}' present in app.Resources: Type={res?.GetType().FullName}");
                    try { LogEngine($"[Diagnostics] Value: {res}"); } catch { }
                }
                else if (Application.Current?.Resources != null && Application.Current.Resources.ContainsKey(key))
                {
                    var res = Application.Current.Resources[key];
                    LogEngine($"[Diagnostics] Resource '{key}' present in Application.Current.Resources: Type={res?.GetType().FullName}");
                    try { LogEngine($"[Diagnostics] Value: {res}"); } catch { }
                }
                else
                {
                    LogEngine($"[Diagnostics] Resource '{key}' not found in app resources");
                }
            }
            catch (Exception ex)
            {
                LogEngine($"[Diagnostics] LogResourceDiagnostics failed: {ex.Message}");
            }
        }

        // Current active phase
        public EnginePhase CurrentPhase { get; private set; } = EnginePhase.LegacyWrapper;

        // Legacy service reference for fallback
        private readonly ThemeService _legacyService;
        
        // Phase 2+ infrastructure
        private readonly Dictionary<string, ThemeDefinition> _engineThemes = new();
        private HashSet<string> _managedColorKeys = new(StringComparer.Ordinal);
        private HashSet<string> _managedGradientKeys = new(StringComparer.Ordinal);
        private readonly IThemeResourceLoader _embeddedLoader;
        private string? _activeThemeId;
        private bool _fallbackActive;
        private static readonly string[] CompatibilityColorKeys =
        {
            "App.ItemHover",
            "App.ItemSelected",
            "App.AccentLight",
            "App.Border",
            "SystemControlHighlightListLowBrush",
            "SystemControlHighlightListAccentLowBrush"
        };

        public ThemeEngine(ThemeService legacyService)
        {
            _legacyService = legacyService ?? throw new ArgumentNullException(nameof(legacyService));
            _embeddedLoader = new EmbeddedThemeResourceLoader();
            
            LogEngine($"ThemeEngine initialized in Phase {CurrentPhase}");
            
            // Auto-register built-in legacy themes as engine themes
            RegisterBuiltInThemes();
        }

        // PUBLIC API - Phase-aware methods

        /// <summary>
        /// Set the theme. Routes to appropriate handler based on current phase.
        /// </summary>
        public void SetTheme(ThemeOption legacyTheme)
        {
            LogEngine($"SetTheme({legacyTheme}) via Phase {CurrentPhase}");
            
            switch (CurrentPhase)
            {
                case EnginePhase.LegacyWrapper:
                    // Phase 1: Direct passthrough to legacy service
                    // Clear any system color overrides when switching to legacy themes
                    ClearSystemColorOverrides();
                    _legacyService.SetTheme(legacyTheme);
                    break;

                case EnginePhase.HybridMode:
                    // Phase 2: Try new system, fallback to legacy
                    if (!TrySetThemeHybrid(legacyTheme))
                    {
                        LogEngine($"Hybrid mode failed, falling back to legacy for {legacyTheme}");
                        // Clear system color overrides when falling back to pure legacy
                        ClearSystemColorOverrides();
                        _legacyService.SetTheme(legacyTheme);
                    }
                    break;

                case EnginePhase.FullEngine:
                    // Phase 3: Use new system exclusively (not implemented)
                    LogEngine("FullEngine phase not yet implemented");
                    ClearSystemColorOverrides();
                    _legacyService.SetTheme(legacyTheme);
                    break;
            }

                    RaiseThemeApplied();
        }

        private static void RaiseThemeApplied()
        {
            try
            {
                ThemeApplied?.Invoke(null, EventArgs.Empty);
            }
            catch { }
        }
        /// <summary>
        /// Set theme by ID (engine theme name). Only works in Phase 2+.
        /// </summary>
        public bool SetThemeById(string themeId)
        {
            if (CurrentPhase == EnginePhase.LegacyWrapper)
            {
                LogEngine($"SetThemeById({themeId}) not available in Phase 1");
                return false;
            }

            LogEngine($"SetThemeById({themeId}) via Phase {CurrentPhase}");

            if (!_engineThemes.ContainsKey(themeId))
            {
                LogEngine($"Theme '{themeId}' not found in engine registry");
                return false;
            }

            _activeThemeId = themeId;
            var applied = ApplyEngineTheme(themeId);
            if (applied)
            {
                RaiseThemeApplied();
            }
            return applied;
        }

        /// <summary>
        /// Apply a theme definition for preview/testing without registering it.
        /// Useful for live preview in theme editor.
        /// </summary>
        public bool ApplyThemePreview(ThemeDefinition themeDef)
        {
            if (CurrentPhase == EnginePhase.LegacyWrapper)
            {
                LogEngine("ApplyThemePreview not available in Phase 1");
                return false;
            }

            try
            {
                LogEngine($"ApplyThemePreview: Previewing theme '{themeDef.DisplayName}'");

                if (!themeDef.IsValid(out var validationError))
                {
                    LogEngine($"Theme validation failed: {validationError}");
                    return false;
                }

                var app = Application.Current;
                if (app == null)
                {
                    LogEngine("Application.Current is null");
                    return false;
                }

                // Apply system accent colors first
                ApplySystemAccentColors(app, themeDef);

                // Apply gradients (most common use case for preview)
                ApplyGradients(app, themeDef);

                // Apply color overrides
                ApplyColorOverrides(app, themeDef);

                // Refresh windows
                RefreshWindows(app);

                LogEngine($"Successfully applied preview for theme '{themeDef.DisplayName}'");
                return true;
            }
            catch (Exception ex)
            {
                LogEngine($"ApplyThemePreview failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Apply theme engine settings (font family, scale). Always available.
        /// </summary>
        public void ApplyThemeEngine(string? fontFamily, double uiScale)
        {
            LogEngine($"ApplyThemeEngine(font='{fontFamily ?? "<default>"}', scale={uiScale:0.##})");
            
            // All phases support font/scale through legacy service
            _legacyService.ApplyThemeEngine(fontFamily, uiScale);
        }

        /// <summary>
        /// Get current theme option (legacy enum).
        /// </summary>
        public ThemeOption GetCurrentTheme()
        {
            return _legacyService.CurrentTheme;
        }

        /// <summary>
        /// Get current UI font family.
        /// </summary>
        public string? GetCurrentFontFamily()
        {
            return _legacyService.CurrentUiFontFamily;
        }

        /// <summary>
        /// Get current UI scale.
        /// </summary>
        public double GetCurrentScale()
        {
            return _legacyService.CurrentUiScale;
        }

        // PHASE CONTROL METHODS

        /// <summary>
        /// Advance to the next phase (use with caution - for controlled rollout).
        /// </summary>
        public bool AdvancePhase()
        {
            var nextPhase = CurrentPhase + 1;
            if (!Enum.IsDefined(typeof(EnginePhase), nextPhase))
            {
                LogEngine($"Cannot advance beyond Phase {CurrentPhase}");
                return false;
            }

            LogEngine($"Advancing from Phase {CurrentPhase} to Phase {nextPhase}");
            CurrentPhase = (EnginePhase)nextPhase;
            return true;
        }

        /// <summary>
        /// Rollback to previous phase (emergency fallback).
        /// </summary>
        public bool RollbackPhase()
        {
            var prevPhase = CurrentPhase - 1;
            if (!Enum.IsDefined(typeof(EnginePhase), prevPhase))
            {
                LogEngine($"Cannot rollback below Phase {CurrentPhase}");
                return false;
            }

            LogEngine($"Rolling back from Phase {CurrentPhase} to Phase {prevPhase}");
            CurrentPhase = (EnginePhase)prevPhase;
            _fallbackActive = false;
            return true;
        }

        /// <summary>
        /// Check if fallback is currently active (hybrid mode safety indicator).
        /// </summary>
        public bool IsFallbackActive => _fallbackActive;

        // PHASE 2+ IMPLEMENTATION

        /// <summary>
        /// Register all built-in legacy themes as engine themes.
        /// </summary>
        private void RegisterBuiltInThemes()
        {
            try
            {
                var builtInIds = new[]
                {
                    "template-blank",
                    "legacy-dark",
                    "legacy-light",
                    "legacy-sandy",
                    "legacy-butter",
                    "vscode-dark-plus",
                    "vscode-light-plus",
                    "vscode-monokai"
                };

                foreach (var themeId in builtInIds)
                {
                    ThemeDefinition? theme = null;

                    try
                    {
                        theme = _embeddedLoader.LoadThemeDefinitionAsync(themeId).GetAwaiter().GetResult();
                    }
                    catch
                    {
                        // Ignore loader exceptions; we will fall back below
                    }

                    if (theme == null)
                    {
                        theme = themeId switch
                        {
                            "template-blank" => ThemeDefinition.CreateBlankTemplate(),
                            "legacy-dark" => ThemeDefinition.FromLegacyTheme(ThemeOption.Dark),
                            "legacy-light" => ThemeDefinition.FromLegacyTheme(ThemeOption.Light),
                            "legacy-sandy" => ThemeDefinition.FromLegacyTheme(ThemeOption.Sandy),
                            "legacy-butter" => ThemeDefinition.FromLegacyTheme(ThemeOption.Butter),
                            _ => null
                        };
                    }

                    if (theme == null)
                    {
                        LogEngine($"Failed to register built-in theme: {themeId}");
                        continue;
                    }

                    _engineThemes[theme.Id] = theme;
                    LogEngine($"Registered built-in theme: {theme.Id}");
                }
            }
            catch (Exception ex)
            {
                LogEngine($"RegisterBuiltInThemes failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Try to apply a legacy theme using the engine (Phase 2 hybrid mode).
        /// </summary>
        private bool TrySetThemeHybrid(ThemeOption legacyTheme)
        {
            try
            {
                // Map legacy enum to engine theme ID
                var themeId = $"legacy-{legacyTheme.ToString().ToLowerInvariant()}";
                
                LogEngine($"TrySetThemeHybrid: Mapping {legacyTheme} to {themeId}");
                
                if (!_engineThemes.ContainsKey(themeId))
                {
                    LogEngine($"Theme {themeId} not found in registry");
                    return false;
                }
                
                _activeThemeId = themeId;
                _fallbackActive = false;
                
                return ApplyEngineTheme(themeId);
            }
            catch (Exception ex)
            {
                LogEngine($"TrySetThemeHybrid failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Apply an engine theme by loading its resources and color overrides.
        /// </summary>
        private bool ApplyEngineTheme(string themeId)
        {
            try
            {
                if (!_engineThemes.TryGetValue(themeId, out var themeDef))
                {
                    LogEngine($"Theme definition not found: {themeId}");
                    return false;
                }

                LogEngine($"ApplyEngineTheme: Starting application of {themeId}");

                // Validate theme before applying
                if (!themeDef.IsValid(out var validationError))
                {
                    LogEngine($"Theme validation failed: {validationError}");
                    return false;
                }

                var app = Application.Current;
                if (app == null)
                {
                    LogEngine("Application.Current is null");
                    return false;
                }

                // 1. Set base Avalonia theme variant
                var variant = themeDef.BaseVariant.ToLowerInvariant() switch
                {
                    "light" => Avalonia.Styling.ThemeVariant.Light,
                    "dark" => Avalonia.Styling.ThemeVariant.Dark,
                    _ => Avalonia.Styling.ThemeVariant.Dark
                };
                app.RequestedThemeVariant = variant;
                LogEngine($"Set base variant to {themeDef.BaseVariant}");

                // 2. Load and apply resource dictionaries
                var loadTask = _embeddedLoader.LoadResourceDictionariesAsync(themeDef);
                loadTask.Wait(); // Synchronous wait since we're in a sync method
                var styles = loadTask.Result;

                if (styles.Count == 0)
                {
                    LogEngine("Theme supplied no resource dictionaries; skipping style replacement");
                }
                else
                {
                    // Remove old theme overrides only when we have replacements lined up
                    var toRemove = new List<Avalonia.Styling.IStyle>();
                    foreach (var style in app.Styles)
                    {
                        if (style is Avalonia.Markup.Xaml.Styling.StyleInclude si && si.Source != null)
                        {
                            var sourceStr = si.Source.OriginalString;
                            if (sourceStr.Contains("DarkThemeOverrides.axaml") ||
                                sourceStr.Contains("LightThemeOverrides.axaml") ||
                                sourceStr.Contains("SandyThemeOverrides.axaml") ||
                                sourceStr.Contains("ButterThemeOverride.axaml"))
                            {
                                toRemove.Add(style);
                            }
                        }
                    }

                    foreach (var style in toRemove)
                    {
                        app.Styles.Remove(style);
                    }
                    LogEngine($"Removed {toRemove.Count} old theme overrides");

                    // Add new theme styles
                    foreach (var style in styles)
                    {
                        app.Styles.Add(style);
                    }
                    LogEngine($"Added {styles.Count} new theme styles");
                    // Diagnostic: Log the current resource for the title bar after loading styles
                    // Keep this diagnostic in DEBUG builds only to avoid noisy logs in release.
                    #if DEBUG
                    try
                    {
                        LogEngine("[Diagnostics] After loading styles:");
                        LogResourceDiagnostics(app, "App.TitleBarBackground");
                    }
                    catch { }
                    #endif
                }

                // 3. Apply system accent color overrides (must be before regular color overrides)
                ApplySystemAccentColors(app, themeDef);

                // 4. Apply color overrides
                ApplyColorOverrides(app, themeDef);

                // 5. Apply gradient definitions
                ApplyGradients(app, themeDef);

                // 6. Apply font and scale (use current settings from legacy service)
                ApplyFontAndScale(app);

                // 7. Refresh windows
                RefreshWindows(app);

                // Diagnostic: Log the current resource for the title bar after applying gradients and refreshing
                // Enabled only in DEBUG builds.
                #if DEBUG
                try
                {
                    LogEngine("[Diagnostics] After applying gradients and refresh:");
                    LogResourceDiagnostics(app, "App.TitleBarBackground");
                }
                catch { }
                #endif

                LogEngine($"Successfully applied theme {themeId}");
                return true;
            }
            catch (Exception ex)
            {
                LogEngine($"ApplyEngineTheme failed: {ex.Message}");
                return false;
            }
        }
    }
}
