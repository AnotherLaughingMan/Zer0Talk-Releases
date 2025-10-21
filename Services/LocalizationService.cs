/*
    LocalizationService: Manages UI language translations from JSON files.
    - Loads language files from Resources/Localization
    - Provides string lookup by key path (e.g., "Settings.Language")
    - Falls back to English if translation missing
    - Notifies UI when language changes
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Zer0Talk.Services
{
    public class LocalizationService
    {
        private Dictionary<string, string> _currentStrings = new();
        private string _currentLanguage = "en";
        private readonly string _localizationPath;

        public event Action? LanguageChanged;

        public LocalizationService()
        {
            // Path to Resources/Localization folder in app directory
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            _localizationPath = Path.Combine(appDir, "Resources", "Localization");
            
            // Ensure directory exists
            try
            {
                if (!Directory.Exists(_localizationPath))
                {
                    Directory.CreateDirectory(_localizationPath);
                }
            }
            catch { }

            // Load default English
            LoadLanguage("en");
        }

        public string CurrentLanguage => _currentLanguage;

        public bool LoadLanguage(string languageCode)
        {
            try
            {
                var filePath = Path.Combine(_localizationPath, $"{languageCode}.json");
                
                if (!File.Exists(filePath))
                {
                    // If requested language not found, try English as fallback
                    if (languageCode != "en")
                    {
                        return LoadLanguage("en");
                    }
                    return false;
                }

                var json = File.ReadAllText(filePath);
                var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                
                if (parsed == null) return false;

                // Flatten the nested JSON structure into dot-notation keys
                var flatStrings = new Dictionary<string, string>();
                FlattenJson(parsed, string.Empty, flatStrings);

                _currentStrings = flatStrings;
                _currentLanguage = languageCode;
                
                LanguageChanged?.Invoke();
                return true;
            }
            catch
            {
                // If load fails and it wasn't English, try English as fallback
                if (languageCode != "en")
                {
                    return LoadLanguage("en");
                }
                return false;
            }
        }

        private void FlattenJson(Dictionary<string, JsonElement> obj, string prefix, Dictionary<string, string> result)
        {
            foreach (var kvp in obj)
            {
                var key = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}.{kvp.Key}";
                
                if (kvp.Value.ValueKind == JsonValueKind.Object)
                {
                    var nested = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(kvp.Value.GetRawText());
                    if (nested != null)
                    {
                        FlattenJson(nested, key, result);
                    }
                }
                else if (kvp.Value.ValueKind == JsonValueKind.String)
                {
                    result[key] = kvp.Value.GetString() ?? string.Empty;
                }
            }
        }

        public string GetString(string key, string? fallback = null)
        {
            if (_currentStrings.TryGetValue(key, out var value))
            {
                return value;
            }
            
            // Return fallback if provided, otherwise return the key itself
            return fallback ?? key;
        }

        public List<string> GetAvailableLanguages()
        {
            var languages = new List<string>();
            
            try
            {
                if (Directory.Exists(_localizationPath))
                {
                    var files = Directory.GetFiles(_localizationPath, "*.json");
                    foreach (var file in files)
                    {
                        var code = Path.GetFileNameWithoutExtension(file);
                        if (!string.IsNullOrWhiteSpace(code))
                        {
                            languages.Add(code);
                        }
                    }
                }
            }
            catch { }

            // Ensure English is always available
            if (!languages.Contains("en"))
            {
                languages.Insert(0, "en");
            }

            return languages;
        }
    }
}

