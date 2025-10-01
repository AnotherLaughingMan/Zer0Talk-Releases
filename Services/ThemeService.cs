/*
    Dynamic theming: swaps resource dictionaries to apply light/dark themes at runtime.
    - Reads persisted theme from settings after unlock.
*/
// TODO[ANCHOR]: ThemeService - Switch and apply app themes at runtime
// TODO[REVIEW]: Consider persisting per-window theme overrides if requested
using System;
using System.Linq;
using System.IO;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;

using ZTalk.Models;

namespace ZTalk.Services
{
    public class ThemeService
    {
        public ThemeOption CurrentTheme { get; private set; } = ThemeOption.Dark;
    // Theme Engine runtime state
    public string? CurrentUiFontFamily { get; private set; }
    public double CurrentUiScale { get; private set; } = 1.0;

        private bool _suppressionHookInitialized;
        private bool _reapplyingFromSuppression;
    private static readonly object _themeLogLock = new();
    private static string ThemeLogPath => Utilities.LoggingPaths.Theme;

        private void EnsurePlatformColorSuppressionHook()
        {
            if (_suppressionHookInitialized) return;
            _suppressionHookInitialized = true;
            try
            {
                var app = Application.Current;
                var ps = app?.PlatformSettings;
                if (ps != null)
                {
                    ps.ColorValuesChanged += (_, __) =>
                    {
                        // Suppress any platform-driven color/accent/theme mutation.
                        try { LogTheme("PlatformColorChange.Suppressed"); } catch { }
                        try
                        {
                            // Reassert current theme & sovereignty layer to overwrite any transient OS accent propagation.
                            _reapplyingFromSuppression = true;
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                try { InternalApplyTheme(CurrentTheme, reassertOnly: true); } catch { }
                                finally { _reapplyingFromSuppression = false; }
                            });
                        }
                        catch { _reapplyingFromSuppression = false; }
                    };
                }
            }
            catch { }
        }

    private static void LogTheme(string msg)
        {
            try
            {
        var line = System.DateTime.Now.ToString("O") + " [theme] " + msg + System.Environment.NewLine;
        lock (_themeLogLock)
                {
                if (Utilities.LoggingPaths.Enabled) File.AppendAllText(ThemeLogPath, line);
                }
            }
            catch { }
        }

        private void InternalApplyTheme(ThemeOption theme, bool reassertOnly = false)
        {
            if (Application.Current is not { } app) return;

            if (!reassertOnly)
            {
                // 1) Switch the Avalonia ThemeVariant (explicitly set so OS changes cannot auto-insert)
                app.RequestedThemeVariant = theme switch
                {
                    ThemeOption.Dark => ThemeVariant.Dark,
                    ThemeOption.Light => ThemeVariant.Light,
                    ThemeOption.Butter => ThemeVariant.Dark,
                    _ => ThemeVariant.Dark
                };
            }

            // 2) (Re)ensure our resource dictionaries. When reassertOnly, avoid removing & re-adding to minimize churn.
            var styles = app.Styles;
            if (!reassertOnly)
            {
                var toRemove = styles.OfType<StyleInclude>()
                    .Where(s => s.Source != null && (
                        s.Source.OriginalString.Contains("DarkThemeOverrides.axaml") ||
                        s.Source.OriginalString.Contains("LightThemeOverrides.axaml") ||
                        s.Source.OriginalString.Contains("SandyThemeOverrides.axaml") ||
                        s.Source.OriginalString.Contains("ButterThemeOverride.axaml")
                    ))
                    .ToList();
                foreach (var s in toRemove) styles.Remove(s);

                var baseUri = new Uri("avares://ZTalk/");
                var source = theme switch
                {
                    ThemeOption.Dark => new Uri("avares://ZTalk/Styles/DarkThemeOverrides.axaml"),
                    ThemeOption.Light => new Uri("avares://ZTalk/Styles/LightThemeOverrides.axaml"),
                    ThemeOption.Sandy => new Uri("avares://ZTalk/Styles/SandyThemeOverrides.axaml"),
                    ThemeOption.Butter => new Uri("avares://ZTalk/Styles/ButterThemeOverride.axaml"),
                    _ => new Uri("avares://ZTalk/Styles/DarkThemeOverrides.axaml")
                };
                styles.Add(new StyleInclude(baseUri) { Source = source });
            }

            ApplyGlobalPaletteOverrides(app, theme);

            // Sovereignty layer: ThemeSovereignty.axaml is intentionally not included directly.
            // Core/Base layers are loaded via App.axaml and managed statically.

            // Apply dynamic font & scale
            try
            {
                if (!app.Resources.ContainsKey("App.Theme.FontFamily")) app.Resources.Add("App.Theme.FontFamily", CurrentUiFontFamily ?? Avalonia.Media.FontFamily.Default);
                else app.Resources["App.Theme.FontFamily"] = CurrentUiFontFamily ?? Avalonia.Media.FontFamily.Default;
                if (!app.Resources.ContainsKey("App.Theme.Scale")) app.Resources.Add("App.Theme.Scale", CurrentUiScale);
                else app.Resources["App.Theme.Scale"] = CurrentUiScale;
            }
            catch { }

            // 3) Force refresh of open windows so template-level brushes recalc against our resources
            if (app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                foreach (var window in desktop.Windows)
                {
                    try { window.RequestedThemeVariant = app.RequestedThemeVariant; } catch { }
                }
            }
        }

        public void SetTheme(ThemeOption theme)
        {
            EnsurePlatformColorSuppressionHook();
            CurrentTheme = theme;
            if (_reapplyingFromSuppression)
            {
                // During suppression reapply we call InternalApplyTheme separately.
                return;
            }
            InternalApplyTheme(theme, reassertOnly: false);
        }

        private static void ApplyGlobalPaletteOverrides(Application app, ThemeOption theme)
        {
            static void SetResource(Application app, string key, object value)
            {
                if (app.Resources.ContainsKey(key))
                {
                    app.Resources[key] = value;
                }
                else
                {
                    app.Resources.Add(key, value);
                }
            }

            static void RemoveResource(Application app, string key)
            {
                if (app.Resources.ContainsKey(key))
                {
                    app.Resources.Remove(key);
                }
            }

            if (theme == ThemeOption.Light)
            {
                var gradient = new LinearGradientBrush
                {
                    StartPoint = new Avalonia.RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new Avalonia.RelativePoint(1, 0, RelativeUnit.Relative),
                    SpreadMethod = GradientSpreadMethod.Pad
                };
                gradient.GradientStops.Add(new GradientStop(Color.Parse("#0A246A"), 0));
                gradient.GradientStops.Add(new GradientStop(Color.Parse("#2F5DAD"), 0.45));
                gradient.GradientStops.Add(new GradientStop(Color.Parse("#A6CAF0"), 1));

                SetResource(app, "App.TitleBarBackground", gradient);
                SetResource(app, "App.ChatSenderName", new SolidColorBrush(Color.Parse("#1F4EA3")));
                SetResource(app, "App.ChatReceiverName", new SolidColorBrush(Color.Parse("#8C4B0F")));
            }
            else
            {
                RemoveResource(app, "App.TitleBarBackground");
                RemoveResource(app, "App.ChatSenderName");
                RemoveResource(app, "App.ChatReceiverName");
            }
        }

        // Theme Engine: update font family & scale; clamp scale range and reassert current dictionaries.
        public void ApplyThemeEngine(string? fontFamily, double uiScale)
        {
            try
            {
                CurrentUiFontFamily = fontFamily;
                if (uiScale < 0.5) uiScale = 0.5; else if (uiScale > 3.0) uiScale = 3.0;
                CurrentUiScale = uiScale;
                InternalApplyTheme(CurrentTheme, reassertOnly: true);
                LogTheme($"ThemeEngine.Apply font='{fontFamily ?? "<default>"}' scale={uiScale:0.##}");
            }
            catch { }
        }

        // Debug/manual trigger: simulate a platform color change suppression for validation.
        public void TriggerSuppressionTest()
        {
            try
            {
                EnsurePlatformColorSuppressionHook();
                LogTheme("PlatformColorChange.DebugTriggered");
                InternalApplyTheme(CurrentTheme, reassertOnly: true);
            }
            catch { }
        }
    }
}
