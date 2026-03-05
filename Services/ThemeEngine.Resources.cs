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

                        // Remove first to ensure clean override
                        if (app.Resources.ContainsKey(kvp.Key))
                        {
                            app.Resources.Remove(kvp.Key);
                        }
                        
                        // Add the new brush
                        app.Resources.Add(kvp.Key, brush);

                        // Log the actual resource type and color to help debugging live UI updates
                        try
                        {
                            var res = app.Resources[kvp.Key];
                            var typeName = res?.GetType().FullName ?? "(null)";
                            string details = typeName;
                            if (res is Avalonia.Media.SolidColorBrush scb)
                            {
                                details += $" Color={scb.Color.ToString()}";
                            }
                            LogEngine($"Applied color override: {kvp.Key} = {kvp.Value} (resource: {details})");
                        }
                        catch (Exception logEx)
                        {
                            LogEngine($"Applied color override: {kvp.Key} = {kvp.Value} (resource logging failed: {logEx.Message})");
                        }

                        appliedKeys.Add(kvp.Key);
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
        /// Apply system accent color overrides to prevent OS colors from showing through.
        /// These override SystemAccentColor, SystemAccentColor2, SystemAccentColor3, SystemAccentColor4.
        /// </summary>
        private void ApplySystemAccentColors(Application app, ThemeDefinition themeDef)
        {
            try
            {
                var systemColors = new[]
                {
                    ("SystemAccentColor", themeDef.SystemAccentColor),
                    ("SystemAccentColor2", themeDef.SystemAccentColor2),
                    ("SystemAccentColor3", themeDef.SystemAccentColor3),
                    ("SystemAccentColor4", themeDef.SystemAccentColor4),
                    ("SystemAccentColorLight", themeDef.SystemAccentColorLight),
                    ("SystemListLowColor", themeDef.SystemListLowColor),
                    ("SystemListMediumColor", themeDef.SystemListMediumColor),
                    ("SystemAltHighColor", themeDef.SystemAltHighColor),
                    ("SystemAltMediumHighColor", themeDef.SystemAltMediumHighColor),
                    ("SystemAltMediumColor", themeDef.SystemAltMediumColor),
                    ("SystemAltMediumLowColor", themeDef.SystemAltMediumLowColor),
                    ("SystemAltLowColor", themeDef.SystemAltLowColor),
                    ("SystemBaseHighColor", themeDef.SystemBaseHighColor),
                    ("SystemBaseMediumHighColor", themeDef.SystemBaseMediumHighColor),
                    ("SystemBaseMediumColor", themeDef.SystemBaseMediumColor),
                    ("SystemBaseMediumLowColor", themeDef.SystemBaseMediumLowColor),
                    ("SystemBaseLowColor", themeDef.SystemBaseLowColor),
                    ("SystemChromeAltLowColor", themeDef.SystemChromeAltLowColor),
                    ("SystemChromeBlackHighColor", themeDef.SystemChromeBlackHighColor),
                    ("SystemChromeBlackLowColor", themeDef.SystemChromeBlackLowColor),
                    ("SystemChromeBlackMediumColor", themeDef.SystemChromeBlackMediumColor),
                    ("SystemChromeBlackMediumLowColor", themeDef.SystemChromeBlackMediumLowColor),
                    ("SystemChromeDisabledHighColor", themeDef.SystemChromeDisabledHighColor),
                    ("SystemChromeDisabledLowColor", themeDef.SystemChromeDisabledLowColor),
                    ("SystemChromeGrayColor", themeDef.SystemChromeGrayColor),
                    ("SystemChromeHighColor", themeDef.SystemChromeHighColor),
                    ("SystemChromeLowColor", themeDef.SystemChromeLowColor),
                    ("SystemChromeMediumColor", themeDef.SystemChromeMediumColor),
                    ("SystemChromeMediumLowColor", themeDef.SystemChromeMediumLowColor),
                    ("SystemChromeWhiteColor", themeDef.SystemChromeWhiteColor)
                };

                var appliedCount = 0;
                var removedCount = 0;
                
                foreach (var (key, value) in systemColors)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        // Theme defines this color - apply it
                        try
                        {
                            var color = Avalonia.Media.Color.Parse(value);
                            
                            // Remove first to force override
                            if (app.Resources.ContainsKey(key))
                            {
                                app.Resources.Remove(key);
                            }
                            app.Resources.Add(key, color);

                            appliedCount++;
                            LogEngine($"Applied system accent override: {key} = {value}");
                        }
                        catch (Exception ex)
                        {
                            LogEngine($"Failed to apply system accent {key}: {ex.Message}");
                        }
                    }
                    else
                    {
                        // Theme doesn't define this color - remove override to restore OS default
                        if (app.Resources.ContainsKey(key))
                        {
                            app.Resources.Remove(key);
                            removedCount++;
                            LogEngine($"Removed system accent override: {key} (restoring OS default)");
                        }
                    }
                }

                if (appliedCount > 0 || removedCount > 0)
                {
                    LogEngine($"Applied {appliedCount} system color overrides, removed {removedCount} overrides");
                }
            }
            catch (Exception ex)
            {
                LogEngine($"ApplySystemAccentColors failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear all system color overrides to restore OS defaults.
        /// Called when switching to legacy themes that don't define system colors.
        /// </summary>
        private void ClearSystemColorOverrides()
        {
            try
            {
                var app = Application.Current;
                if (app == null)
                {
                    LogEngine("ClearSystemColorOverrides: Application.Current is null");
                    return;
                }

                var systemColorKeys = new[]
                {
                    "SystemAccentColor", "SystemAccentColor2", "SystemAccentColor3", "SystemAccentColor4",
                    "SystemAccentColorLight", "SystemListLowColor", "SystemListMediumColor",
                    "SystemAltHighColor", "SystemAltMediumHighColor", "SystemAltMediumColor",
                    "SystemAltMediumLowColor", "SystemAltLowColor",
                    "SystemBaseHighColor", "SystemBaseMediumHighColor", "SystemBaseMediumColor",
                    "SystemBaseMediumLowColor", "SystemBaseLowColor",
                    "SystemChromeAltLowColor", "SystemChromeBlackHighColor", "SystemChromeBlackLowColor",
                    "SystemChromeBlackMediumColor", "SystemChromeBlackMediumLowColor",
                    "SystemChromeDisabledHighColor", "SystemChromeDisabledLowColor",
                    "SystemChromeGrayColor", "SystemChromeHighColor", "SystemChromeLowColor",
                    "SystemChromeMediumColor", "SystemChromeMediumLowColor", "SystemChromeWhiteColor"
                };

                var removedCount = 0;
                foreach (var key in systemColorKeys)
                {
                    if (app.Resources.ContainsKey(key))
                    {
                        app.Resources.Remove(key);
                        removedCount++;
                    }
                }

                if (removedCount > 0)
                {
                    LogEngine($"Cleared {removedCount} system color overrides (restoring OS defaults)");
                }
            }
            catch (Exception ex)
            {
                LogEngine($"ClearSystemColorOverrides failed: {ex.Message}");
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

    }
}
