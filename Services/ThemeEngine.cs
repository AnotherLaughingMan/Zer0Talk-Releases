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
using Zer0Talk.Models;

namespace Zer0Talk.Services
{
    public class ThemeEngine
    {
        // Event fired when custom themes are reloaded (e.g., after saving a new theme)
        public static event EventHandler? ThemesReloaded;

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
                    _legacyService.SetTheme(legacyTheme);
                    break;

                case EnginePhase.HybridMode:
                    // Phase 2: Try new system, fallback to legacy
                    if (!TrySetThemeHybrid(legacyTheme))
                    {
                        LogEngine($"Hybrid mode failed, falling back to legacy for {legacyTheme}");
                        _fallbackActive = true;
                        _legacyService.SetTheme(legacyTheme);
                    }
                    break;

                case EnginePhase.FullEngine:
                    // Phase 3: Use new system exclusively (not implemented)
                    LogEngine("FullEngine phase not yet implemented");
                    _legacyService.SetTheme(legacyTheme);
                    break;
            }
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
            return ApplyEngineTheme(themeId);
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
                    "legacy-butter"
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

                // 3. Apply color overrides
                ApplyColorOverrides(app, themeDef);

                // 4. Apply gradient definitions
                ApplyGradients(app, themeDef);

                // 5. Apply font and scale (use current settings from legacy service)
                ApplyFontAndScale(app);

                // 6. Refresh windows
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

        /// <summary>
        /// Apply color overrides from theme definition.
        /// </summary>
        private void ApplyColorOverrides(Application app, ThemeDefinition themeDef)
        {
            try
            {
                var overrides = themeDef.ColorOverrides ?? new Dictionary<string, string>();
                var desired = new HashSet<string>(overrides.Keys, StringComparer.Ordinal);

                foreach (var staleKey in _managedColorKeys.Where(k => !desired.Contains(k)).ToList())
                {
                    if (app.Resources.ContainsKey(staleKey))
                    {
                        app.Resources.Remove(staleKey);
                        LogEngine($"Removed stale color override: {staleKey}");
                    }
                }

                if (overrides.Count == 0)
                {
                    _managedColorKeys.Clear();
                    LogEngine("No color overrides provided; cleared previous overrides");
                    return;
                }

                var appliedKeys = new HashSet<string>(StringComparer.Ordinal);

                foreach (var kvp in overrides)
                {
                    try
                    {
                        var color = Avalonia.Media.Color.Parse(kvp.Value);
                        var brush = new Avalonia.Media.SolidColorBrush(color);

                        if (app.Resources.ContainsKey(kvp.Key))
                        {
                            app.Resources[kvp.Key] = brush;
                        }
                        else
                        {
                            app.Resources.Add(kvp.Key, brush);
                        }

                        appliedKeys.Add(kvp.Key);
                        LogEngine($"Applied color override: {kvp.Key} = {kvp.Value}");
                    }
                    catch (Exception ex)
                    {
                        LogEngine($"Failed to apply color override {kvp.Key}: {ex.Message}");
                    }
                }

                _managedColorKeys = appliedKeys;
            }
            catch (Exception ex)
            {
                LogEngine($"ApplyColorOverrides failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Apply gradient definitions from theme.
        /// </summary>
        private void ApplyGradients(Application app, ThemeDefinition themeDef)
        {
            try
            {
                var gradients = themeDef.Gradients ?? new Dictionary<string, GradientDefinition>();
                var desired = new HashSet<string>(gradients.Keys, StringComparer.Ordinal);

                foreach (var staleKey in _managedGradientKeys.Where(k => !desired.Contains(k)).ToList())
                {
                    if (app.Resources.ContainsKey(staleKey))
                    {
                        app.Resources.Remove(staleKey);
                        LogEngine($"Removed stale gradient override: {staleKey}");
                    }
                }

                if (gradients.Count == 0)
                {
                    _managedGradientKeys.Clear();
                    LogEngine("No gradients provided; cleared previous gradient overrides");
                    return;
                }

                var appliedKeys = new HashSet<string>(StringComparer.Ordinal);

                foreach (var kvp in gradients)
                {
                    try
                    {
                        var def = kvp.Value;
                        var gradient = new Avalonia.Media.LinearGradientBrush();
                        var (startX, startY, endX, endY) = def.GetGradientPoints();

                        gradient.StartPoint = new Avalonia.RelativePoint(startX, startY, Avalonia.RelativeUnit.Relative);
                        gradient.EndPoint = new Avalonia.RelativePoint(endX, endY, Avalonia.RelativeUnit.Relative);
                        gradient.SpreadMethod = Avalonia.Media.GradientSpreadMethod.Pad;

                        var startColor = Avalonia.Media.Color.Parse(def.StartColor);
                        gradient.GradientStops.Add(new Avalonia.Media.GradientStop(startColor, 0));

                        foreach (var stop in def.ColorStops.OrderBy(s => s.Key))
                        {
                            var position = Math.Clamp(stop.Key, 0.0, 1.0);
                            var stopColor = Avalonia.Media.Color.Parse(stop.Value);
                            gradient.GradientStops.Add(new Avalonia.Media.GradientStop(stopColor, position));
                        }

                        var endColor = Avalonia.Media.Color.Parse(def.EndColor);
                        gradient.GradientStops.Add(new Avalonia.Media.GradientStop(endColor, 1));

                        // Always remove first to ensure we override any style-based definitions
                        if (app.Resources.ContainsKey(kvp.Key))
                        {
                            app.Resources.Remove(kvp.Key);
                            LogEngine($"Removed existing resource: {kvp.Key}");
                        }
                        
                        // Add to application resources with high priority
                        app.Resources.Add(kvp.Key, gradient);
                        
                        // Also try to set it directly in Application.Current.Resources to ensure highest priority
                        try
                        {
                            if (Application.Current?.Resources != null)
                            {
                                if (Application.Current.Resources.ContainsKey(kvp.Key))
                                {
                                    Application.Current.Resources[kvp.Key] = gradient;
                                }
                                else
                                {
                                    Application.Current.Resources.Add(kvp.Key, gradient);
                                }
                                LogEngine($"Also applied to Application.Current.Resources: {kvp.Key}");
                            }
                        }
                        catch (Exception appEx)
                        {
                            LogEngine($"Failed to apply to Application.Current.Resources: {appEx.Message}");
                        }

                        appliedKeys.Add(kvp.Key);
                        LogEngine($"Applied gradient: {kvp.Key} ({def.StartColor} → {def.EndColor} @ {def.Angle}°)");
                    }
                    catch (Exception ex)
                    {
                        LogEngine($"Failed to apply gradient {kvp.Key}: {ex.Message}");
                    }
                }

                _managedGradientKeys = appliedKeys;
            }
            catch (Exception ex)
            {
                LogEngine($"ApplyGradients failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Apply current font and scale settings.
        /// </summary>
        private void ApplyFontAndScale(Application app)
        {
            try
            {
                var fontFamily = _legacyService.CurrentUiFontFamily ?? Avalonia.Media.FontFamily.Default;
                var scale = _legacyService.CurrentUiScale;

                if (!app.Resources.ContainsKey("App.Theme.FontFamily"))
                    app.Resources.Add("App.Theme.FontFamily", fontFamily);
                else
                    app.Resources["App.Theme.FontFamily"] = fontFamily;

                if (!app.Resources.ContainsKey("App.Theme.Scale"))
                    app.Resources.Add("App.Theme.Scale", scale);
                else
                    app.Resources["App.Theme.Scale"] = scale;

                LogEngine($"Applied font: {fontFamily}, scale: {scale:0.##}");
            }
            catch (Exception ex)
            {
                LogEngine($"ApplyFontAndScale failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Refresh all open windows to apply theme changes.
        /// </summary>
        private void RefreshWindows(Application app)
        {
            try
            {
                if (app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    foreach (var window in desktop.Windows)
                    {
                        try
                        {
                            // Force theme variant change to trigger resource re-evaluation
                            var currentVariant = window.RequestedThemeVariant;
                            window.RequestedThemeVariant = app.RequestedThemeVariant;
                            
                            // Force visual refresh by invalidating the entire visual tree
                            if (window.Content is Avalonia.Controls.Control content)
                            {
                                content.InvalidateVisual();
                                content.InvalidateMeasure();
                                content.InvalidateArrange();
                                
                                // Try to trigger a complete layout pass
                                window.InvalidateVisual();
                                window.InvalidateMeasure();
                                window.InvalidateArrange();
                            }
                            
                            // Additional force refresh techniques
                            try
                            {
                                // Force the window to recalculate all styling
                                if (window is Avalonia.Controls.Window win)
                                {
                                    var oldWidth = win.Width;
                                    var oldHeight = win.Height;
                                    
                                    // Tiny resize to force complete re-layout (most reliable method)
                                    win.Width = oldWidth + 0.1;
                                    win.Height = oldHeight + 0.1;
                                    
                                    // Immediately restore size
                                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                    {
                                        win.Width = oldWidth;
                                        win.Height = oldHeight;
                                    }, Avalonia.Threading.DispatcherPriority.Background);
                                }
                            }
                            catch (Exception resizeEx)
                            {
                                LogEngine($"Window resize refresh failed: {resizeEx.Message}");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogEngine($"Failed to refresh window: {ex.Message}");
                        }
                    }
                    LogEngine($"Refreshed {desktop.Windows.Count} windows with aggressive visual invalidation");
                }
            }
            catch (Exception ex)
            {
                LogEngine($"RefreshWindows failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Register a new theme definition (Phase 2+).
        /// </summary>
        public bool RegisterTheme(ThemeDefinition themeDef)
        {
            if (CurrentPhase == EnginePhase.LegacyWrapper)
            {
                LogEngine("RegisterTheme not available in Phase 1");
                return false;
            }

            if (string.IsNullOrWhiteSpace(themeDef.Id))
            {
                LogEngine("Cannot register theme with null/empty ID");
                return false;
            }

            _engineThemes[themeDef.Id] = themeDef;
            LogEngine($"Registered theme: {themeDef.Id} (Name: {themeDef.DisplayName})");
            return true;
        }

        /// <summary>
        /// Get all registered engine themes (Phase 2+).
        /// </summary>
        public IReadOnlyDictionary<string, ThemeDefinition> GetRegisteredThemes()
        {
            return _engineThemes;
        }

        /// <summary>
        /// Get all theme search directories (AppData, Documents, custom paths).
        /// </summary>
        public List<string> GetThemeSearchDirectories()
        {
            var directories = new List<string>();

            // 1. AppData\Zer0Talk\Themes (primary location for user themes)
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appDataThemes = System.IO.Path.Combine(appDataPath, "Zer0Talk", "Themes");
            directories.Add(appDataThemes);

            // 2. Documents\Zer0Talk Theme Templates (shared/template location)
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var documentsThemes = System.IO.Path.Combine(documentsPath, "Zer0Talk Theme Templates");
            directories.Add(documentsThemes);

            // Ensure directories exist
            foreach (var dir in directories)
            {
                if (!System.IO.Directory.Exists(dir))
                {
                    try
                    {
                        System.IO.Directory.CreateDirectory(dir);
                        LogEngine($"Created theme directory: {dir}");
                    }
                    catch (Exception ex)
                    {
                        LogEngine($"Failed to create directory {dir}: {ex.Message}");
                    }
                }
            }

            return directories;
        }

        /// <summary>
        /// Get custom themes directory path (AppData location).
        /// </summary>
        public string GetCustomThemesDirectory()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var themesPath = System.IO.Path.Combine(appDataPath, "Zer0Talk", "Themes");
            
            // Ensure directory exists
            if (!System.IO.Directory.Exists(themesPath))
            {
                try
                {
                    System.IO.Directory.CreateDirectory(themesPath);
                    LogEngine($"Created custom themes directory: {themesPath}");
                }
                catch (Exception ex)
                {
                    LogEngine($"Failed to create themes directory: {ex.Message}");
                }
            }
            
            return themesPath;
        }

        /// <summary>
        /// Get Documents theme templates directory path.
        /// </summary>
        public string GetDocumentsThemesDirectory()
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var themesPath = System.IO.Path.Combine(documentsPath, "Zer0Talk Theme Templates");
            
            // Ensure directory exists
            if (!System.IO.Directory.Exists(themesPath))
            {
                try
                {
                    System.IO.Directory.CreateDirectory(themesPath);
                    LogEngine($"Created documents themes directory: {themesPath}");
                }
                catch (Exception ex)
                {
                    LogEngine($"Failed to create documents themes directory: {ex.Message}");
                }
            }
            
            return themesPath;
        }

        /// <summary>
        /// Scan and load custom themes from all configured directories.
        /// </summary>
        public int LoadCustomThemes()
        {
            if (CurrentPhase == EnginePhase.LegacyWrapper)
            {
                LogEngine("LoadCustomThemes not available in Phase 1");
                return 0;
            }

            var existingCustomIds = _engineThemes.Values
                .Where(t => t != null && (t.ThemeType == ThemeType.Custom || t.ThemeType == ThemeType.Imported) && !t.IsLegacyTheme)
                .Select(t => t.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var loadedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var activeThemeReloaded = false;

            var totalLoaded = 0;
            var directories = GetThemeSearchDirectories();

            foreach (var themesDir in directories)
            {
                if (!System.IO.Directory.Exists(themesDir))
                {
                    LogEngine($"Skipping non-existent directory: {themesDir}");
                    continue;
                }

                var loaded = LoadThemesFromDirectory(themesDir, loadedIds, ref activeThemeReloaded);
                totalLoaded += loaded;
            }

            LogEngine($"Total custom themes loaded: {totalLoaded} from {directories.Count} directories");
            
            // Notify subscribers that themes have been reloaded
            try
            {
                ThemesReloaded?.Invoke(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                LogEngine($"Error firing ThemesReloaded event: {ex.Message}");
            }

            // Determine which themes were removed (files deleted or moved)
            foreach (var removedId in existingCustomIds.Except(loadedIds, StringComparer.OrdinalIgnoreCase).ToList())
            {
                if (_engineThemes.Remove(removedId))
                {
                    LogEngine($"Removed missing custom theme: {removedId}");
                }
            }

            if (activeThemeReloaded)
            {
                LogEngine($"Active theme '{_activeThemeId}' reloaded; reapplying to refresh resources");
                try
                {
                    ApplyEngineTheme(_activeThemeId!);
                }
                catch (Exception ex)
                {
                    LogEngine($"Failed to reapply active theme '{_activeThemeId}': {ex.Message}");
                }
            }
            else if (!string.IsNullOrWhiteSpace(_activeThemeId) && !_engineThemes.ContainsKey(_activeThemeId))
            {
                LogEngine($"Active theme '{_activeThemeId}' no longer available; reverting to fallback");
                var fallback = DetermineFallbackThemeId();
                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    SetThemeById(fallback);
                }
            }

            return totalLoaded;
        }

        /// <summary>
        /// Load themes from a specific directory.
        /// </summary>
        private int LoadThemesFromDirectory(string directory, ISet<string> newlyLoadedIds, ref bool activeThemeReloaded)
        {
            var loaded = 0;
            
            try
            {
                var themeFiles = System.IO.Directory.GetFiles(directory, "*.zttheme", System.IO.SearchOption.TopDirectoryOnly);
                LogEngine($"Found {themeFiles.Length} .zttheme files in {directory}");

                foreach (var filePath in themeFiles)
                {
                    try
                    {
                        var themeDef = ThemeDefinition.LoadFromFile(filePath, out var warnings);
                        
                        if (warnings.Count > 0)
                        {
                            LogEngine($"Theme '{themeDef.DisplayName}' loaded with {warnings.Count} warnings:");
                            foreach (var warning in warnings.Take(5))
                            {
                                LogEngine($"  - {warning}");
                            }
                        }

                        // Mark as custom/imported type
                        if (themeDef.ThemeType == ThemeType.BuiltInLegacy || themeDef.ThemeType == ThemeType.BuiltInTemplate)
                        {
                            themeDef.ThemeType = ThemeType.Imported;
                        }

                        // Register the theme
                        if (RegisterTheme(themeDef))
                        {
                            loaded++;
                            newlyLoadedIds.Add(themeDef.Id);
                            if (!string.IsNullOrWhiteSpace(_activeThemeId) && string.Equals(_activeThemeId, themeDef.Id, StringComparison.OrdinalIgnoreCase))
                            {
                                activeThemeReloaded = true;
                            }
                            LogEngine($"Loaded custom theme: {themeDef.DisplayName} (ID: {themeDef.Id}) from {System.IO.Path.GetFileName(filePath)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogEngine($"Failed to load theme from {System.IO.Path.GetFileName(filePath)}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogEngine($"Error scanning directory {directory}: {ex.Message}");
            }

            return loaded;
        }

        private string DetermineFallbackThemeId()
        {
            var preferred = new[] { "legacy-dark", "legacy-light", "legacy-sandy", "legacy-butter" };
            foreach (var candidate in preferred)
            {
                if (_engineThemes.ContainsKey(candidate))
                {
                    return candidate;
                }
            }

            return _engineThemes.Keys.FirstOrDefault() ?? string.Empty;
        }

        /// <summary>
        /// Search for theme files across all drives (expensive operation).
        /// </summary>
        /// <param name="progressCallback">Optional callback for progress updates</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>List of found theme file paths</returns>
        public async System.Threading.Tasks.Task<List<string>> SearchDrivesForThemesAsync(
            Action<string>? progressCallback = null, 
            System.Threading.CancellationToken cancellationToken = default)
        {
            var foundThemes = new List<string>();
            
            LogEngine("Starting drive-wide theme search...");
            progressCallback?.Invoke("Starting drive search...");

            await System.Threading.Tasks.Task.Run(() =>
            {
                var drives = System.IO.DriveInfo.GetDrives()
                    .Where(d => d.IsReady && (d.DriveType == System.IO.DriveType.Fixed || d.DriveType == System.IO.DriveType.Removable))
                    .ToList();

                LogEngine($"Scanning {drives.Count} drives for .zttheme files");

                foreach (var drive in drives)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    try
                    {
                        progressCallback?.Invoke($"Searching {drive.Name}...");
                        LogEngine($"Scanning drive: {drive.Name}");

                        var themes = SearchDirectoryRecursive(drive.RootDirectory.FullName, cancellationToken, progressCallback);
                        foundThemes.AddRange(themes);
                    }
                    catch (Exception ex)
                    {
                        LogEngine($"Error scanning drive {drive.Name}: {ex.Message}");
                    }
                }
            }, cancellationToken);

            LogEngine($"Drive search complete. Found {foundThemes.Count} theme files.");
            progressCallback?.Invoke($"Search complete. Found {foundThemes.Count} themes.");
            
            return foundThemes;
        }

        /// <summary>
        /// Recursively search a directory for .zttheme files.
        /// </summary>
        private List<string> SearchDirectoryRecursive(string path, System.Threading.CancellationToken cancellationToken, Action<string>? progressCallback)
        {
            var results = new List<string>();

            try
            {
                // Skip system and hidden directories
                var dirInfo = new System.IO.DirectoryInfo(path);
                if (dirInfo.Attributes.HasFlag(System.IO.FileAttributes.System) || 
                    dirInfo.Attributes.HasFlag(System.IO.FileAttributes.Hidden))
                {
                    return results;
                }

                // Search current directory
                try
                {
                    var themeFiles = System.IO.Directory.GetFiles(path, "*.zttheme", System.IO.SearchOption.TopDirectoryOnly);
                    results.AddRange(themeFiles);
                    
                    if (themeFiles.Length > 0)
                    {
                        progressCallback?.Invoke($"Found {themeFiles.Length} in {path}");
                    }
                }
                catch { /* Access denied to files, skip */ }

                // Search subdirectories
                if (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var subdirs = System.IO.Directory.GetDirectories(path);
                        foreach (var subdir in subdirs)
                        {
                            if (cancellationToken.IsCancellationRequested)
                                break;

                            results.AddRange(SearchDirectoryRecursive(subdir, cancellationToken, progressCallback));
                        }
                    }
                    catch { /* Access denied to subdirectories, skip */ }
                }
            }
            catch { /* Access denied to directory, skip */ }

            return results;
        }

        /// <summary>
        /// Load themes from search results.
        /// </summary>
        public int LoadThemesFromPaths(IEnumerable<string> themePaths)
        {
            if (CurrentPhase == EnginePhase.LegacyWrapper)
            {
                LogEngine("LoadThemesFromPaths not available in Phase 1");
                return 0;
            }

            var loaded = 0;

            foreach (var filePath in themePaths)
            {
                try
                {
                    var themeDef = ThemeDefinition.LoadFromFile(filePath, out var warnings);
                    
                    if (warnings.Count > 0)
                    {
                        LogEngine($"Theme '{themeDef.DisplayName}' loaded with {warnings.Count} warnings");
                    }

                    if (themeDef.ThemeType == ThemeType.BuiltInLegacy || themeDef.ThemeType == ThemeType.BuiltInTemplate)
                    {
                        themeDef.ThemeType = ThemeType.Imported;
                    }

                    if (RegisterTheme(themeDef))
                    {
                        loaded++;
                        LogEngine($"Loaded theme: {themeDef.DisplayName} from {filePath}");
                    }
                }
                catch (Exception ex)
                {
                    LogEngine($"Failed to load theme from {filePath}: {ex.Message}");
                }
            }

            LogEngine($"Loaded {loaded} themes from search results");
            return loaded;
        }

        // LOGGING

        private static void LogEngine(string msg)
        {
            try
            {
                Utilities.Logger.Info(msg, source: "ThemeEngine", categoryOverride: "theme");
            }
            catch { /* Swallow logging errors */ }
        }
    }
}

