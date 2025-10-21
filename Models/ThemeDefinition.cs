/*
    ThemeDefinition - Metadata for Theme Engine themes
    
    Represents a complete theme package including:
    - Identity (ID, name, description)
    - Resource dictionary paths
    - Color palette definitions
    - Typography settings
    - Compatibility flags
*/

using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace Zer0Talk.Models
{
    /// <summary>
    /// Defines a complete theme for the ThemeEngine.
    /// </summary>
    public class ThemeDefinition
    {
        /// <summary>
        /// Unique identifier for this theme (e.g., "dark-pro", "light-high-contrast").
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// User-facing display name (e.g., "Dark Professional", "Light High Contrast").
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Optional description of the theme.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Author/creator of the theme.
        /// </summary>
        public string? Author { get; set; }

        /// <summary>
        /// Theme version (for compatibility tracking).
        /// </summary>
        public string Version { get; set; } = "1.0.0";

        /// <summary>
        /// Base Avalonia theme variant (Dark, Light, or Default).
        /// </summary>
        public string BaseVariant { get; set; } = "Dark";

        /// <summary>
        /// Paths to resource dictionary files (avares:// URIs or file paths).
        /// Applied in order (later dictionaries can override earlier ones).
        /// </summary>
        public List<string> ResourceDictionaries { get; set; } = new();

        /// <summary>
        /// Direct color palette overrides (applied after resource dictionaries).
        /// Key = resource key (e.g., "App.Background"), Value = hex color.
        /// </summary>
        public Dictionary<string, string> ColorOverrides { get; set; } = new();

        /// <summary>
        /// Gradient definitions for title bars and other surfaces.
        /// Key = resource key (e.g., "App.TitleBarBackground"), Value = gradient config.
        /// </summary>
        public Dictionary<string, GradientDefinition> Gradients { get; set; } = new();

        /// <summary>
        /// Default font family for this theme (null = use system default).
        /// </summary>
        public string? DefaultFontFamily { get; set; }

        /// <summary>
        /// Default UI scale for this theme (1.0 = 100%).
        /// </summary>
        public double DefaultUiScale { get; set; } = 1.0;

        /// <summary>
        /// Minimum supported app version (for forward compatibility).
        /// </summary>
        public string? MinAppVersion { get; set; }

        /// <summary>
        /// Indicates if this is a legacy theme (maps to old ThemeOption).
        /// </summary>
        public bool IsLegacyTheme { get; set; }

        /// <summary>
        /// If IsLegacyTheme, which ThemeOption it corresponds to.
        /// </summary>
        public ThemeOption? LegacyThemeOption { get; set; }

        /// <summary>
        /// Whether this theme supports custom user modifications.
        /// </summary>
        public bool AllowsCustomization { get; set; } = false;

        /// <summary>
        /// Indicates if this theme is read-only (built-in themes cannot be overwritten, only exported as new themes).
        /// Built-in themes include: Dark, Light, Sandy, Butter, and Blank template.
        /// </summary>
        public bool IsReadOnly { get; set; } = false;

        /// <summary>
        /// Type of theme for classification and behavior control.
        /// </summary>
        public ThemeType ThemeType { get; set; } = ThemeType.Custom;

        /// <summary>
        /// Tags for categorization/search (e.g., "high-contrast", "colorful", "minimal").
        /// </summary>
        public List<string> Tags { get; set; } = new();

        /// <summary>
        /// Preview image path (for theme picker UI in future).
        /// </summary>
        public string? PreviewImagePath { get; set; }

        /// <summary>
        /// Custom metadata (extensibility for future features).
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new();

        /// <summary>
        /// Creation/modification timestamp.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Validate that this theme definition is complete and usable.
        /// </summary>
        public bool IsValid(out string? errorMessage)
        {
            if (string.IsNullOrWhiteSpace(Id))
            {
                errorMessage = "Theme ID cannot be empty";
                return false;
            }

            if (string.IsNullOrWhiteSpace(DisplayName))
            {
                errorMessage = "Theme DisplayName cannot be empty";
                return false;
            }

            if (DefaultUiScale < 0.5 || DefaultUiScale > 3.0)
            {
                errorMessage = $"DefaultUiScale ({DefaultUiScale}) must be between 0.5 and 3.0";
                return false;
            }

            errorMessage = null;
            return true;
        }

        /// <summary>
        /// Check if this theme can be modified in-place (saved/overwritten).
        /// Built-in themes (legacy + template) are read-only and require "Save As" to create new theme.
        /// </summary>
        public bool CanModifyInPlace()
        {
            return !IsReadOnly && (ThemeType == ThemeType.Custom || ThemeType == ThemeType.Imported);
        }

        /// <summary>
        /// Check if this theme is a built-in theme (legacy or template).
        /// </summary>
        public bool IsBuiltIn()
        {
            return ThemeType == ThemeType.BuiltInLegacy || ThemeType == ThemeType.BuiltInTemplate;
        }

        /// <summary>
        /// Serialize this theme definition to JSON format (.zttheme file).
        /// Phase 3 Step 2: Save only, no loading yet.
        /// </summary>
        public string ToJson()
        {
            var options = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            return System.Text.Json.JsonSerializer.Serialize(this, options);
        }

        /// <summary>
        /// Save this theme to a .zttheme file.
        /// Phase 3 Step 2: Export functionality only.
        /// </summary>
        public void SaveToFile(string filePath)
        {
            if (!IsValid(out var error))
            {
                throw new InvalidOperationException($"Cannot save invalid theme: {error}");
            }

            var json = ToJson();
            System.IO.File.WriteAllText(filePath, json);
            
            Zer0Talk.Utilities.Logger.Log($"[Theme Export] Saved theme '{DisplayName}' to {filePath}", Zer0Talk.Utilities.LogLevel.Info, source: "ThemeDefinition", categoryOverride: "theme");
        }

        /// <summary>
        /// Deserialize a theme definition from JSON.
        /// Phase 3 Step 3: Import with validation.
        /// </summary>
        public static ThemeDefinition FromJson(string json)
        {
            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };

            var theme = System.Text.Json.JsonSerializer.Deserialize<ThemeDefinition>(json, options);
            if (theme == null)
            {
                throw new InvalidOperationException("Failed to deserialize theme: result was null");
            }

            return theme;
        }

        /// <summary>
        /// Load a theme from a .zttheme file with comprehensive validation.
        /// Phase 3 Step 3: Import with safeguards.
        /// </summary>
        public static ThemeDefinition LoadFromFile(string filePath, out List<string> warnings)
        {
            warnings = new List<string>();

            // Validate file exists
            if (!System.IO.File.Exists(filePath))
            {
                throw new System.IO.FileNotFoundException($"Theme file not found: {filePath}");
            }

            // Validate file size (max 5MB to prevent abuse)
            var fileInfo = new System.IO.FileInfo(filePath);
            if (fileInfo.Length > 5 * 1024 * 1024)
            {
                throw new InvalidOperationException($"Theme file too large: {fileInfo.Length} bytes (max 5MB)");
            }

            // Read and parse JSON
            string json;
            try
            {
                json = System.IO.File.ReadAllText(filePath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to read theme file: {ex.Message}", ex);
            }

            ThemeDefinition theme;
            try
            {
                theme = FromJson(json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse theme JSON: {ex.Message}", ex);
            }

            // Comprehensive validation
            ValidateThemeContent(theme, warnings);

            // Log successful import
            Zer0Talk.Utilities.Logger.Log($"[Theme Import] Loaded theme '{theme.DisplayName}' from {filePath} ({warnings.Count} warnings)", 
                Zer0Talk.Utilities.LogLevel.Info, source: "ThemeDefinition", categoryOverride: "theme");

            return theme;
        }

        /// <summary>
        /// Comprehensive validation of theme content with warnings collection.
        /// Phase 3 Step 3: Safety validation.
        /// </summary>
        private static void ValidateThemeContent(ThemeDefinition theme, List<string> warnings)
        {
            // Basic validation (throws on critical errors)
            if (!theme.IsValid(out var error))
            {
                throw new InvalidOperationException($"Theme validation failed: {error}");
            }

            // ID validation
            if (theme.Id.Length > 100)
            {
                throw new InvalidOperationException("Theme ID too long (max 100 characters)");
            }
            if (!System.Text.RegularExpressions.Regex.IsMatch(theme.Id, @"^[a-zA-Z0-9\-_]+$"))
            {
                throw new InvalidOperationException("Theme ID contains invalid characters (use only letters, numbers, hyphens, underscores)");
            }

            // DisplayName validation
            if (theme.DisplayName.Length > 200)
            {
                throw new InvalidOperationException("Theme DisplayName too long (max 200 characters)");
            }

            // Description validation (warning only)
            if (!string.IsNullOrWhiteSpace(theme.Description) && theme.Description.Length > 1000)
            {
                warnings.Add("Description is very long (>1000 chars), may not display well");
            }

            // Validate color overrides
            foreach (var kvp in theme.ColorOverrides)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                {
                    throw new InvalidOperationException("Color override has empty resource key");
                }
                if (!IsValidColor(kvp.Value))
                {
                    throw new InvalidOperationException($"Invalid color format for '{kvp.Key}': {kvp.Value} (use hex format like #RRGGBB or #AARRGGBB)");
                }
            }

            // Validate gradients
            foreach (var kvp in theme.Gradients)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                {
                    throw new InvalidOperationException("Gradient has empty resource key");
                }
                
                var gradient = kvp.Value;
                if (gradient == null)
                {
                    throw new InvalidOperationException($"Gradient '{kvp.Key}' is null");
                }

                if (!IsValidColor(gradient.StartColor))
                {
                    throw new InvalidOperationException($"Gradient '{kvp.Key}' has invalid StartColor: {gradient.StartColor}");
                }
                if (!IsValidColor(gradient.EndColor))
                {
                    throw new InvalidOperationException($"Gradient '{kvp.Key}' has invalid EndColor: {gradient.EndColor}");
                }
                if (gradient.Angle < 0 || gradient.Angle > 360)
                {
                    throw new InvalidOperationException($"Gradient '{kvp.Key}' has invalid Angle: {gradient.Angle} (must be 0-360)");
                }

                // Validate color stops
                foreach (var stop in gradient.ColorStops)
                {
                    if (stop.Key < 0 || stop.Key > 1)
                    {
                        throw new InvalidOperationException($"Gradient '{kvp.Key}' has invalid color stop position: {stop.Key} (must be 0.0-1.0)");
                    }
                    if (!IsValidColor(stop.Value))
                    {
                        throw new InvalidOperationException($"Gradient '{kvp.Key}' has invalid color stop color: {stop.Value}");
                    }
                }
            }

            // Validate resource dictionaries (warning only - paths may be custom)
            foreach (var path in theme.ResourceDictionaries)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    warnings.Add("Resource dictionary list contains empty path");
                }
                else if (!path.StartsWith("avares://") && !System.IO.File.Exists(path))
                {
                    warnings.Add($"Resource dictionary path may not exist: {path}");
                }
            }

            // Validate tags (warning only)
            if (theme.Tags.Count > 20)
            {
                warnings.Add($"Theme has many tags ({theme.Tags.Count}), some may be ignored");
            }

            // Check for suspicious content
            if (theme.Id.Contains("..") || theme.DisplayName.Contains(".."))
            {
                throw new InvalidOperationException("Theme contains suspicious path traversal characters");
            }

            // Warn about legacy theme conflicts
            if (theme.IsLegacyTheme)
            {
                warnings.Add("Theme is marked as legacy - may conflict with built-in themes");
            }
        }

        /// <summary>
        /// Validate hex color format (#RGB, #ARGB, #RRGGBB, #AARRGGBB).
        /// </summary>
        private static bool IsValidColor(string color)
        {
            if (string.IsNullOrWhiteSpace(color))
                return false;

            if (!color.StartsWith("#"))
                return false;

            var hex = color.Substring(1);
            if (hex.Length != 3 && hex.Length != 4 && hex.Length != 6 && hex.Length != 8)
                return false;

            return System.Text.RegularExpressions.Regex.IsMatch(hex, @"^[0-9A-Fa-f]+$");
        }

        /// <summary>
        /// Public wrapper for color validation (ViewModel access).
        /// </summary>
        public static bool IsValidColorPublic(string color) => IsValidColor(color);

        /// <summary>
        /// Create a legacy theme definition that wraps an old ThemeOption.
        /// </summary>
        public static ThemeDefinition FromLegacyTheme(ThemeOption legacyTheme)
        {
            var resourcePath = legacyTheme switch
            {
                ThemeOption.Dark => "avares://Zer0Talk/Styles/DarkThemeOverrides.axaml",
                ThemeOption.Light => "avares://Zer0Talk/Styles/LightThemeOverrides.axaml",
                ThemeOption.Sandy => "avares://Zer0Talk/Styles/SandyThemeOverrides.axaml",
                ThemeOption.Butter => "avares://Zer0Talk/Styles/ButterThemeOverride.axaml",
                _ => "avares://Zer0Talk/Styles/DarkThemeOverrides.axaml"
            };

            return new ThemeDefinition
            {
                Id = $"legacy-{legacyTheme.ToString().ToLowerInvariant()}",
                DisplayName = legacyTheme.ToString(),
                Description = $"Legacy {legacyTheme} theme (compatibility mode)",
                Author = "Zer0Talk",
                BaseVariant = legacyTheme == ThemeOption.Light ? "Light" : "Dark",
                ResourceDictionaries = new List<string> { resourcePath },
                IsLegacyTheme = true,
                LegacyThemeOption = legacyTheme,
                IsReadOnly = true,
                ThemeType = ThemeType.BuiltInLegacy,
                Tags = new List<string> { "legacy", "built-in" }
            };
        }

        /// <summary>
        /// Create a blank template theme with neutral grey palette - ideal starting point for customization.
        /// </summary>
        public static ThemeDefinition CreateBlankTemplate()
        {
            var blank = new ThemeDefinition
            {
                Id = "template-blank",
                DisplayName = "Blank",
                Description = "Neutral grey template - perfect starting point for custom themes",
                Author = "Zer0Talk",
                BaseVariant = "Dark",
                ResourceDictionaries = new List<string> { "avares://Zer0Talk/Styles/DarkThemeOverrides.axaml" },
                IsReadOnly = true,
                ThemeType = ThemeType.BuiltInTemplate,
                Tags = new List<string> { "template", "built-in" },
                // Subdued grey palette - neutral and customizable
                ColorOverrides = new Dictionary<string, string>
                {
                    // Backgrounds - neutral greys
                    ["App.Background"] = "#1C1C1C",
                    ["App.Surface"] = "#242424",
                    ["App.CardBackground"] = "#2A2A2A",
                    
                    // Foreground - soft white
                    ["App.ForegroundPrimary"] = "#E0E0E0",
                    ["App.ForegroundSecondary"] = "#A0A0A0",
                    ["App.TitleBarForeground"] = "#FFFFFF",
                    
                    // Borders - subtle
                    ["App.Border"] = "#3A3A3A",
                    ["App.InputBorder"] = "#3A3A3A",
                    
                    // Accent - neutral blue-grey (easy to customize)
                    ["App.Accent"] = "#6B7B8C",
                    ["App.AccentLight"] = "#8A97A5",
                    
                    // Selection
                    ["App.SelectionBackground"] = "#4A4A4A",
                    ["App.SelectionForeground"] = "#FFFFFF",
                    
                    // Buttons
                    ["App.ButtonBackground"] = "#2A2A2A",
                    ["App.ButtonHover"] = "#323232",
                    ["App.ButtonPressed"] = "#3A3A3A",
                    
                    // Chat colors - neutral tones
                    ["App.ChatSenderName"] = "#9CA5AF",
                    ["App.ChatReceiverName"] = "#AFA89C",
                    
                    // Contact list
                    ["App.ContactName"] = "#A0A0A0",
                    ["App.ContactNameActive"] = "#E0E0E0",
                    
                    // List items
                    ["App.ItemHover"] = "#303030",
                    ["App.ItemSelected"] = "#383838"
                },
                // Default gradient for title bar - neutral grey gradient that users can customize
                Gradients = new Dictionary<string, GradientDefinition>
                {
                    ["App.TitleBarBackground"] = new GradientDefinition
                    {
                        StartColor = "#3A3A3A",
                        EndColor = "#1C1C1C",
                        Angle = 90.0
                    }
                }
            };

            return blank;
        }
    }

    /// <summary>
    /// Defines a linear gradient for backgrounds, title bars, etc.
    /// </summary>
    public class GradientDefinition
    {
        /// <summary>
        /// Starting color (hex format, e.g., "#FF0000").
        /// </summary>
        public string StartColor { get; set; } = "#000000";

        /// <summary>
        /// Ending color (hex format, e.g., "#0000FF").
        /// </summary>
        public string EndColor { get; set; } = "#FFFFFF";

        /// <summary>
        /// Gradient angle in degrees (0-360).
        /// 0° = horizontal (left to right)
        /// 90° = vertical (top to bottom)
        /// 45° = diagonal (top-left to bottom-right)
        /// 135° = diagonal (top-right to bottom-left)
        /// </summary>
        public double Angle { get; set; } = 0.0;

        /// <summary>
        /// Predefined direction for convenience (overrides Angle if set).
        /// </summary>
        public GradientDirection? Direction { get; set; }

        /// <summary>
        /// Optional intermediate color stops.
        /// Format: Dictionary of position (0.0-1.0) to color hex.
        /// </summary>
        public Dictionary<double, string> ColorStops { get; set; } = new();

        /// <summary>
        /// Convert this definition to Avalonia LinearGradientBrush parameters.
        /// </summary>
        public (double startX, double startY, double endX, double endY) GetGradientPoints()
        {
            var angle = Direction.HasValue ? (double)Direction.Value : Angle;
            var radians = angle * Math.PI / 180.0;
            
            // Calculate start and end points based on angle
            // 0° = (0,0.5) to (1,0.5) - horizontal
            // 90° = (0.5,0) to (0.5,1) - vertical
            var startX = 0.5 - Math.Cos(radians) * 0.5;
            var startY = 0.5 - Math.Sin(radians) * 0.5;
            var endX = 0.5 + Math.Cos(radians) * 0.5;
            var endY = 0.5 + Math.Sin(radians) * 0.5;

            return (startX, startY, endX, endY);
        }
    }

    /// <summary>
    /// Common gradient directions for convenience.
    /// </summary>
    public enum GradientDirection
    {
        Horizontal = 0,          // Left to right
        Vertical = 90,           // Top to bottom
        DiagonalDown = 45,       // Top-left to bottom-right
        DiagonalUp = 135,        // Top-right to bottom-left
        HorizontalReverse = 180, // Right to left
        VerticalReverse = 270    // Bottom to top
    }

    /// <summary>
    /// Classification of theme types for behavior control.
    /// </summary>
    public enum ThemeType
    {
        /// <summary>
        /// Built-in legacy theme (Dark, Light, Sandy, Butter) - Read-only, cannot be modified in-place.
        /// </summary>
        BuiltInLegacy,

        /// <summary>
        /// Built-in template (Blank) - Read-only, used as starting point for new themes.
        /// </summary>
        BuiltInTemplate,

        /// <summary>
        /// User-created custom theme - Can be edited and overwritten.
        /// </summary>
        Custom,

        /// <summary>
        /// Imported theme from .zttheme file - Can be edited and overwritten.
        /// </summary>
        Imported
    }

    /// <summary>
    /// Configuration for the Theme Engine system.
    /// </summary>
    public class ThemeEngineConfig
    {
        /// <summary>
        /// Enable or disable the theme engine (master switch for rollback).
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Current phase of engine deployment.
        /// </summary>
        public int Phase { get; set; } = 1; // Start at Phase 1 (LegacyWrapper)

        /// <summary>
        /// Currently active theme ID.
        /// </summary>
        public string? ActiveThemeId { get; set; }

        /// <summary>
        /// Fallback theme ID if active theme fails to load.
        /// </summary>
        public string FallbackThemeId { get; set; } = "legacy-dark";

        /// <summary>
        /// Allow user-created custom themes (Phase 3+).
        /// </summary>
        public bool AllowCustomThemes { get; set; } = false;

        /// <summary>
        /// Directory path for user custom themes.
        /// </summary>
        public string? CustomThemesDirectory { get; set; }

        /// <summary>
        /// Log detailed theme loading/application info.
        /// </summary>
        public bool EnableDetailedLogging { get; set; } = true;

        /// <summary>
        /// Maximum number of custom themes to load (safety limit).
        /// </summary>
        public int MaxCustomThemes { get; set; } = 50;

        /// <summary>
        /// Automatically reload themes when files change (dev mode).
        /// </summary>
        public bool EnableHotReload { get; set; } = false;
    }
}
