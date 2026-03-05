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

            var normalizeWarnings = new List<string>();
            var injectedCompatibilityKeys = themeDef.EnsureCompatibilityColorOverrides(normalizeWarnings);
            if (injectedCompatibilityKeys > 0)
            {
                LogEngine($"Theme '{themeDef.DisplayName}' compatibility normalization injected {injectedCompatibilityKeys} color keys");
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
        /// Normalize on-disk custom/imported theme files by injecting compatibility color keys when missing.
        /// This upgrades older .zttheme files to work with newer list/contact selectors.
        /// </summary>
        public (int Scanned, int Updated, int Failed) NormalizeCustomThemesOnDisk()
        {
            var scanned = 0;
            var updated = 0;
            var failed = 0;

            var directories = GetThemeSearchDirectories();
            foreach (var themesDir in directories)
            {
                if (!System.IO.Directory.Exists(themesDir))
                {
                    continue;
                }

                string[] themeFiles;
                try
                {
                    themeFiles = System.IO.Directory.GetFiles(themesDir, "*.zttheme", System.IO.SearchOption.TopDirectoryOnly);
                }
                catch (Exception ex)
                {
                    LogEngine($"NormalizeCustomThemesOnDisk: failed to enumerate {themesDir}: {ex.Message}");
                    continue;
                }

                foreach (var filePath in themeFiles)
                {
                    scanned++;
                    try
                    {
                        var json = System.IO.File.ReadAllText(filePath);
                        if (!NeedsCompatibilityNormalization(json, out var missingKeys))
                        {
                            continue;
                        }

                        var theme = ThemeDefinition.LoadFromFile(filePath, out var warnings);
                        var injected = theme.EnsureCompatibilityColorOverrides(warnings);
                        theme.SaveToFile(filePath);
                        updated++;

                        LogEngine($"Normalized theme file {System.IO.Path.GetFileName(filePath)} (missing keys: {missingKeys}, injected: {injected})");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        LogEngine($"NormalizeCustomThemesOnDisk: failed for {System.IO.Path.GetFileName(filePath)}: {ex.Message}");
                    }
                }
            }

            LogEngine($"NormalizeCustomThemesOnDisk complete: scanned={scanned}, updated={updated}, failed={failed}");
            return (scanned, updated, failed);
        }

        private static bool NeedsCompatibilityNormalization(string json, out int missingKeys)
        {
            missingKeys = CompatibilityColorKeys.Length;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("colorOverrides", out var colorOverrides) ||
                    colorOverrides.ValueKind != System.Text.Json.JsonValueKind.Object)
                {
                    return true;
                }

                missingKeys = 0;
                foreach (var key in CompatibilityColorKeys)
                {
                    if (!colorOverrides.TryGetProperty(key, out var value) ||
                        value.ValueKind != System.Text.Json.JsonValueKind.String ||
                        string.IsNullOrWhiteSpace(value.GetString()))
                    {
                        missingKeys++;
                    }
                }

                return missingKeys > 0;
            }
            catch
            {
                // If JSON cannot be parsed, attempt migration path through ThemeDefinition loader.
                return true;
            }
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
            
            LogEngine("Starting theme search...");
            progressCallback?.Invoke("Starting theme search...");

            await System.Threading.Tasks.Task.Run(() =>
            {
                // First, search the primary AppData theme folder
                try
                {
                    var appDataThemes = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Zer0Talk",
                        "Themes");

                    if (System.IO.Directory.Exists(appDataThemes))
                    {
                        progressCallback?.Invoke($"Searching {appDataThemes}...");
                        LogEngine($"Searching primary theme folder: {appDataThemes}");

                        var themes = SearchDirectoryRecursive(appDataThemes, cancellationToken, progressCallback);
                        foundThemes.AddRange(themes);
                        
                        LogEngine($"Found {themes.Count} themes in AppData folder");
                    }
                }
                catch (Exception ex)
                {
                    LogEngine($"Error searching AppData themes folder: {ex.Message}");
                }

                if (cancellationToken.IsCancellationRequested)
                    return;

                // Next, search user's Documents folder
                try
                {
                    var documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    if (!string.IsNullOrEmpty(documentsFolder) && System.IO.Directory.Exists(documentsFolder))
                    {
                        progressCallback?.Invoke($"Searching Documents folder...");
                        LogEngine($"Searching Documents folder: {documentsFolder}");

                        var themes = SearchDirectoryRecursive(documentsFolder, cancellationToken, progressCallback, maxDepth: 3);
                        foundThemes.AddRange(themes);
                        
                        LogEngine($"Found {themes.Count} themes in Documents folder");
                    }
                }
                catch (Exception ex)
                {
                    LogEngine($"Error searching Documents folder: {ex.Message}");
                }

                if (cancellationToken.IsCancellationRequested)
                    return;

                // Finally, search other user-accessible drives
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

                        var themes = SearchDirectoryRecursive(drive.RootDirectory.FullName, cancellationToken, progressCallback, maxDepth: 4);
                        foundThemes.AddRange(themes);
                    }
                    catch (Exception ex)
                    {
                        LogEngine($"Error scanning drive {drive.Name}: {ex.Message}");
                    }
                }
            }, cancellationToken);

            LogEngine($"Theme search complete. Found {foundThemes.Count} unique theme files.");
            progressCallback?.Invoke($"Search complete. Found {foundThemes.Count} themes.");
            
            return foundThemes;
        }

        /// <summary>
        /// Recursively search a directory for .zttheme files.
        /// </summary>
        private List<string> SearchDirectoryRecursive(
            string path, 
            System.Threading.CancellationToken cancellationToken, 
            Action<string>? progressCallback,
            int maxDepth = 10,
            int currentDepth = 0)
        {
            var results = new List<string>();

            // Stop if max depth reached
            if (currentDepth >= maxDepth)
                return results;

            try
            {
                var dirInfo = new System.IO.DirectoryInfo(path);
                
                // Skip protected system folders
                if (IsProtectedFolder(path, dirInfo))
                    return results;

                // Search current directory for .zttheme files
                try
                {
                    var themeFiles = System.IO.Directory.GetFiles(path, "*.zttheme", System.IO.SearchOption.TopDirectoryOnly);
                    results.AddRange(themeFiles);
                    
                    if (themeFiles.Length > 0)
                    {
                        progressCallback?.Invoke($"Found {themeFiles.Length} in {System.IO.Path.GetFileName(path)}");
                        LogEngine($"Found {themeFiles.Length} themes in: {path}");
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

                            results.AddRange(SearchDirectoryRecursive(subdir, cancellationToken, progressCallback, maxDepth, currentDepth + 1));
                        }
                    }
                    catch { /* Access denied to subdirectories, skip */ }
                }
            }
            catch { /* Access denied to directory, skip */ }

            return results;
        }

        /// <summary>
        /// Check if a folder should be skipped (protected system folders, etc.)
        /// </summary>
        private bool IsProtectedFolder(string path, System.IO.DirectoryInfo dirInfo)
        {
            try
            {
                // Skip system and hidden directories
                if (dirInfo.Attributes.HasFlag(System.IO.FileAttributes.System) || 
                    dirInfo.Attributes.HasFlag(System.IO.FileAttributes.Hidden))
                {
                    return true;
                }

                var folderName = dirInfo.Name.ToLowerInvariant();
                var fullPath = path.ToLowerInvariant();

                // Skip Windows system folders
                if (folderName == "windows" || 
                    folderName == "program files" || 
                    folderName == "program files (x86)" ||
                    folderName == "programdata" ||
                    folderName == "$recycle.bin" ||
                    folderName == "system volume information" ||
                    folderName == "recovery" ||
                    folderName == "perflogs" ||
                    folderName.StartsWith("$"))
                {
                    return true;
                }

                // Skip AppData cache/temp folders
                if (fullPath.Contains("\\appdata\\local\\temp") ||
                    fullPath.Contains("\\appdata\\local\\microsoft") ||
                    fullPath.Contains("\\appdata\\local\\packages") ||
                    fullPath.Contains("\\appdata\\locallow") ||
                    fullPath.Contains("\\cache") ||
                    fullPath.Contains("\\.git") ||
                    fullPath.Contains("\\node_modules") ||
                    fullPath.Contains("\\.vs") ||
                    fullPath.Contains("\\obj") ||
                    fullPath.Contains("\\bin"))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return true; // If we can't check, skip it
            }
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
