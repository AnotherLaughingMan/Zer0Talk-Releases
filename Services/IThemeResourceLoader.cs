/*
    IThemeResourceLoader - Abstraction for loading theme resources
    
    Allows multiple implementations:
    - Embedded resource loader (for built-in themes)
    - File system loader (for custom user themes)
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Styling;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Zer0Talk.Models;

namespace Zer0Talk.Services
{
    /// <summary>
    /// Interface for loading theme resource dictionaries from various sources.
    /// </summary>
    public interface IThemeResourceLoader
    {
        /// <summary>
        /// Load a theme definition from its source.
        /// </summary>
        /// <param name="themeId">Unique identifier for the theme</param>
        /// <returns>ThemeDefinition if successful, null otherwise</returns>
        Task<ThemeDefinition?> LoadThemeDefinitionAsync(string themeId);

        /// <summary>
        /// Load resource dictionaries specified in a theme definition.
        /// </summary>
        /// <param name="theme">Theme definition with resource paths</param>
        /// <returns>List of loaded resource includes, or empty list on failure</returns>
        Task<List<IStyle>> LoadResourceDictionariesAsync(ThemeDefinition theme);

        /// <summary>
        /// Check if a theme is available from this loader.
        /// </summary>
        /// <param name="themeId">Theme identifier to check</param>
        /// <returns>True if theme can be loaded</returns>
        Task<bool> IsThemeAvailableAsync(string themeId);

        /// <summary>
        /// List all themes available from this loader.
        /// </summary>
        /// <returns>Collection of available theme IDs</returns>
        Task<IEnumerable<string>> ListAvailableThemesAsync();

        /// <summary>
        /// Validate a theme's resources without fully loading them.
        /// </summary>
        /// <param name="theme">Theme to validate</param>
        /// <returns>True if theme resources are valid and loadable</returns>
        Task<bool> ValidateThemeAsync(ThemeDefinition theme);
    }

    /// <summary>
    /// Loads themes from embedded application resources (avares:// URIs).
    /// This is the default loader for built-in themes.
    /// </summary>
    public class EmbeddedThemeResourceLoader : IThemeResourceLoader
    {
        private static readonly string[] _builtInThemeIds = new[]
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

        public async Task<ThemeDefinition?> LoadThemeDefinitionAsync(string themeId)
        {
            try
            {
                var assetUri = new Uri($"avares://Zer0Talk/Resources/Themes/{themeId}.zttheme");
                using var stream = AssetLoader.Open(assetUri);
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync().ConfigureAwait(false);
                return ThemeDefinition.FromJson(json);
            }
            catch
            {
                // Fall back to generating the legacy definition if the asset does not exist
            }

            try
            {
                var fallback = themeId switch
                {
                    "template-blank" => ThemeDefinition.CreateBlankTemplate(),
                    "legacy-dark" => ThemeDefinition.FromLegacyTheme(ThemeOption.Dark),
                    "legacy-light" => ThemeDefinition.FromLegacyTheme(ThemeOption.Light),
                    "legacy-sandy" => ThemeDefinition.FromLegacyTheme(ThemeOption.Sandy),
                    "legacy-butter" => ThemeDefinition.FromLegacyTheme(ThemeOption.Butter),
                    _ => null
                };

                return fallback;
            }
            catch
            {
                return null;
            }
        }

        public Task<List<IStyle>> LoadResourceDictionariesAsync(ThemeDefinition theme)
        {
            var styles = new List<IStyle>();

            // Try to load each resource dictionary individually, continuing on failures
            foreach (var resourcePath in theme.ResourceDictionaries)
            {
                if (string.IsNullOrWhiteSpace(resourcePath))
                    continue;

                try
                {
                    // Create StyleInclude for each resource dictionary
                    var baseUri = new Uri("avares://Zer0Talk/");
                    var styleInclude = new Avalonia.Markup.Xaml.Styling.StyleInclude(baseUri)
                    {
                        Source = new Uri(resourcePath)
                    };

                    styles.Add(styleInclude);
                }
                catch (Exception ex)
                {
                    // Log but continue - invalid paths should not break the entire theme
                    Zer0Talk.Utilities.Logger.Log($"[ThemeLoader] Failed to load resource dictionary '{resourcePath}': {ex.Message}", 
                        Zer0Talk.Utilities.LogLevel.Warning, source: "EmbeddedThemeResourceLoader", categoryOverride: "theme");
                }
            }

            // If theme does not specify external dictionaries OR all failed to load, 
            // synthesize resources from ColorOverrides and Gradients
            if (styles.Count == 0)
            {
                try
                {
                    var generated = BuildGeneratedStyle(theme);
                    if (generated != null)
                    {
                        styles.Add(generated);
                    }
                }
                catch (Exception ex)
                {
                    // Log failure to build generated style
                    Zer0Talk.Utilities.Logger.Log($"[ThemeLoader] Failed to build generated style for theme '{theme.Id}': {ex.Message}", 
                        Zer0Talk.Utilities.LogLevel.Error, source: "EmbeddedThemeResourceLoader", categoryOverride: "theme");
                }
            }

            return Task.FromResult(styles);
        }

        private static IStyle? BuildGeneratedStyle(ThemeDefinition theme)
        {
            var hasColorOverrides = theme.ColorOverrides != null && theme.ColorOverrides.Count > 0;
            var hasGradientOverrides = theme.Gradients != null && theme.Gradients.Count > 0;

            if (!hasColorOverrides && !hasGradientOverrides)
            {
                return null;
            }

            var styles = new Styles();
            var resources = styles.Resources;

            if (hasColorOverrides)
            {
                var colorOverrides = theme.ColorOverrides!;
                foreach (var kvp in colorOverrides)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key) || string.IsNullOrWhiteSpace(kvp.Value))
                    {
                        continue;
                    }

                    try
                    {
                        var color = Color.Parse(kvp.Value);
                        resources[kvp.Key] = new SolidColorBrush(color);
                    }
                    catch
                    {
                        // Ignore individual failures so a single bad color does not break the theme
                    }
                }
            }

            if (hasGradientOverrides)
            {
                var gradients = theme.Gradients!;
                foreach (var kvp in gradients)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key) || kvp.Value == null)
                    {
                        continue;
                    }

                    try
                    {
                        var def = kvp.Value;
                        var points = def.GetGradientPoints();
                        var gradient = new LinearGradientBrush
                        {
                            StartPoint = new RelativePoint(points.startX, points.startY, RelativeUnit.Relative),
                            EndPoint = new RelativePoint(points.endX, points.endY, RelativeUnit.Relative),
                            GradientStops = new GradientStops()
                        };

                        gradient.GradientStops.Add(new GradientStop(Color.Parse(def.StartColor), 0));

                        foreach (var stop in def.ColorStops.OrderBy(s => s.Key))
                        {
                            var position = Math.Clamp(stop.Key, 0.0, 1.0);
                            gradient.GradientStops.Add(new GradientStop(Color.Parse(stop.Value), position));
                        }

                        gradient.GradientStops.Add(new GradientStop(Color.Parse(def.EndColor), 1));

                        resources[kvp.Key] = gradient;
                    }
                    catch
                    {
                        // Ignore individual gradient failures
                    }
                }
            }

            return resources.Count > 0 ? styles : null;
        }

        public Task<bool> IsThemeAvailableAsync(string themeId)
        {
            var available = Array.IndexOf(_builtInThemeIds, themeId) >= 0;
            return Task.FromResult(available);
        }

        public Task<IEnumerable<string>> ListAvailableThemesAsync()
        {
            return Task.FromResult<IEnumerable<string>>(_builtInThemeIds);
        }

        public Task<bool> ValidateThemeAsync(ThemeDefinition theme)
        {
            try
            {
                // Basic validation
                if (!theme.IsValid(out var error))
                    return Task.FromResult(false);

                // Check that resource paths are well-formed
                foreach (var path in theme.ResourceDictionaries)
                {
                    if (string.IsNullOrWhiteSpace(path))
                        return Task.FromResult(false);

                    if (!path.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
                        return Task.FromResult(false);
                }

                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }
    }

    /// <summary>
    /// Loads themes from file system (for custom user themes).
    /// Phase 2+ feature - not active in Phase 1.
    /// </summary>
    public class FileSystemThemeResourceLoader : IThemeResourceLoader
    {
        private readonly string _themesDirectory;

        public FileSystemThemeResourceLoader(string themesDirectory)
        {
            _themesDirectory = themesDirectory ?? throw new ArgumentNullException(nameof(themesDirectory));
        }

        public Task<ThemeDefinition?> LoadThemeDefinitionAsync(string themeId)
        {
            // Phase 2+ implementation
            // Would read theme.json or similar metadata file
            return Task.FromResult<ThemeDefinition?>(null);
        }

        public Task<List<IStyle>> LoadResourceDictionariesAsync(ThemeDefinition theme)
        {
            // Phase 2+ implementation
            // Would load .axaml files from theme directory
            return Task.FromResult(new List<IStyle>());
        }

        public Task<bool> IsThemeAvailableAsync(string themeId)
        {
            // Phase 2+ implementation
            return Task.FromResult(false);
        }

        public Task<IEnumerable<string>> ListAvailableThemesAsync()
        {
            // Phase 2+ implementation
            return Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
        }

        public Task<bool> ValidateThemeAsync(ThemeDefinition theme)
        {
            // Phase 2+ implementation
            return Task.FromResult(false);
        }
    }
}

