/*
    Zer0Talk Uninstaller System Cleanup Service
    - Removes registry entries (Windows startup)
    - Removes application logs and temporary files  
    - Does NOT touch user data in %APPDATA%\Zer0Talk
    - User data preservation is intentional - let users decide separately
*/
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace Zer0Talk.Services
{
    /// <summary>
    /// Service for cleaning up Zer0Talk system components during uninstall
    /// Focuses on registry entries, logs, and temp files only
    /// Intentionally preserves user data for user's own decision
    /// </summary>
    public static partial class UninstallCleanupService
    {
        private static readonly string[] AppNameKeywords = { "Zer0Talk", "ZTalk", "P2PTalk" };

        /// <summary>
        /// Performs system cleanup for Zer0Talk uninstall
        /// User data in %APPDATA%\Zer0Talk is preserved
        /// </summary>
        /// <param name="results">List to collect operation results</param>
        /// <returns>True if all operations succeeded</returns>
        public static bool PerformUninstallCleanup(List<string> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            bool success = true;
            
            try
            {
                results.Add("=== Zer0Talk Uninstaller ===");
                results.Add("Cleaning up system components only");
                results.Add("");
                
                // 1. Remove Windows startup registry entry
                success &= CleanupWindowsStartup(results);

                // 2. Remove legacy registry entries (uninstall/app paths/registered apps)
                success &= CleanupRegistryEntries(results);

                // 3. Remove taskbar pinned entries and shortcuts
                success &= CleanupTaskbarPins(results);
                
                // 4. Clean up application logs
                success &= CleanupApplicationLogs(results);
                
                // 5. Clean up temporary files
                success &= CleanupTemporaryFiles(results);
                
                results.Add("");
                results.Add(success ? "✓ System cleanup completed successfully" : "⚠ System cleanup completed with warnings");
                results.Add("");
                results.Add("NOTE: User data in %APPDATA%\\Zer0Talk has been preserved.");
                results.Add("This includes your messages, contacts, and settings.");
                results.Add("You can manually delete this folder if desired.");
                
                return success;
            }
            catch (Exception ex)
            {
                results.Add($"✗ Fatal error: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Remove Windows startup registry entries for Zer0Talk and legacy P2PTalk
        /// </summary>
        private static bool CleanupWindowsStartup(List<string> results)
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    results.Add("✓ Windows startup: Not applicable (non-Windows platform)");
                    return true;
                }
                
                const string runKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
                // Check for current and legacy application names
                var appNames = new[] { "Zer0Talk", "ZTalk", "P2PTalk", "Zer0Talk.exe", "ZTalk.exe", "P2PTalk.exe" };
                
                using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, true);
                if (key == null)
                {
                    results.Add("✓ Windows startup: Registry key not found (nothing to clean)");
                    return true;
                }
                
                int removedCount = 0;
                
                // Remove known app names
                foreach (var appName in appNames)
                {
                    try
                    {
                        var value = key.GetValue(appName) as string;
                        if (!string.IsNullOrEmpty(value))
                        {
                            key.DeleteValue(appName, false);
                            results.Add($"✓ Windows startup: Removed '{appName}' = '{value}'");
                            removedCount++;
                            Utilities.Logger.Log($"UninstallCleanup: Removed startup entry {appName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        results.Add($"⚠ Windows startup: Could not remove {appName} - {ex.Message}");
                    }
                }
                
                // Check for any other entries pointing to Zer0Talk or P2PTalk executables
                try
                {
                    var valueNames = key.GetValueNames();
                    foreach (var valueName in valueNames)
                    {
                        var value = key.GetValue(valueName)?.ToString() ?? "";
                        if (value.Contains("Zer0Talk", StringComparison.OrdinalIgnoreCase) || 
                            value.Contains("P2PTalk", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                key.DeleteValue(valueName, false);
                                results.Add($"✓ Windows startup: Removed legacy entry '{valueName}' = '{value}'");
                                removedCount++;
                                Utilities.Logger.Log($"UninstallCleanup: Removed legacy startup entry {valueName}");
                            }
                            catch (Exception ex)
                            {
                                results.Add($"⚠ Windows startup: Could not remove legacy entry {valueName} - {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    results.Add($"⚠ Windows startup: Error scanning for legacy entries - {ex.Message}");
                }
                
                if (removedCount > 0)
                {
                    results.Add($"✓ Windows startup: Total entries removed: {removedCount}");
                }
                else
                {
                    results.Add("✓ Windows startup: No registry entries found to remove");
                }
                
                return true;
            }
            catch (Exception ex)
            {
                results.Add($"✗ Windows startup: Failed to clean registry - {ex.Message}");
                Utilities.Logger.Log($"UninstallCleanup: Failed to clean Windows startup registry: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Remove legacy registry entries for Zer0Talk/ZTalk/P2PTalk
        /// </summary>
        private static bool CleanupRegistryEntries(List<string> results)
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    results.Add("✓ Registry cleanup: Not applicable (non-Windows platform)");
                    return true;
                }

                int removedKeys = 0;
                int removedValues = 0;

                var registryPaths = new[]
                {
                    (Hive: Registry.CurrentUser, Path: @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", Mode: "uninstall"),
                    (Hive: Registry.LocalMachine, Path: @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", Mode: "uninstall"),
                    (Hive: Registry.LocalMachine, Path: @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", Mode: "uninstall"),
                    (Hive: Registry.CurrentUser, Path: @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths", Mode: "subkeys"),
                    (Hive: Registry.LocalMachine, Path: @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths", Mode: "subkeys"),
                    (Hive: Registry.CurrentUser, Path: @"SOFTWARE\Classes\Applications", Mode: "subkeys"),
                    (Hive: Registry.LocalMachine, Path: @"SOFTWARE\Classes\Applications", Mode: "subkeys"),
                    (Hive: Registry.CurrentUser, Path: @"SOFTWARE\RegisteredApplications", Mode: "values"),
                    (Hive: Registry.LocalMachine, Path: @"SOFTWARE\RegisteredApplications", Mode: "values")
                };

                foreach (var (hive, path, mode) in registryPaths)
                {
                    try
                    {
                        using var key = hive.OpenSubKey(path, true);
                        if (key == null) continue;

                        if (mode == "values")
                        {
                            foreach (var valueName in key.GetValueNames())
                            {
                                var value = key.GetValue(valueName)?.ToString() ?? string.Empty;
                                if (ContainsAppKeyword(valueName) || ContainsAppKeyword(value))
                                {
                                    try
                                    {
                                        key.DeleteValue(valueName, false);
                                        removedValues++;
                                    }
                                    catch { }
                                }
                            }
                        }
                        else
                        {
                            foreach (var subkeyName in key.GetSubKeyNames())
                            {
                                try
                                {
                                    using var subkey = key.OpenSubKey(subkeyName);
                                    if (mode == "uninstall")
                                    {
                                        if (!IsUninstallSubkeyMatch(subkeyName, subkey))
                                            continue;
                                    }
                                    else
                                    {
                                        if (!ContainsAppKeyword(subkeyName))
                                        {
                                            var defaultValue = subkey?.GetValue("")?.ToString() ?? string.Empty;
                                            if (!ContainsAppKeyword(defaultValue))
                                                continue;
                                        }
                                    }

                                    key.DeleteSubKeyTree(subkeyName, false);
                                    removedKeys++;
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }

                if (removedKeys == 0 && removedValues == 0)
                {
                    results.Add("✓ Registry cleanup: No legacy entries found");
                }
                else
                {
                    results.Add($"✓ Registry cleanup: Removed {removedKeys} keys and {removedValues} values");
                }

                return true;
            }
            catch (Exception ex)
            {
                results.Add($"✗ Registry cleanup: Failed - {ex.Message}");
                Utilities.Logger.Log($"UninstallCleanup: Registry cleanup failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Remove taskbar pinned entries and shortcuts
        /// </summary>
        private static bool CleanupTaskbarPins(List<string> results)
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    results.Add("✓ Taskbar pins: Not applicable (non-Windows platform)");
                    return true;
                }

                int removedCount = 0;
                var locations = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\StartMenu"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs")
                };

                foreach (var location in locations)
                {
                    try
                    {
                        if (!Directory.Exists(location)) continue;
                        foreach (var file in Directory.GetFiles(location, "*.lnk", SearchOption.TopDirectoryOnly))
                        {
                            var fileName = Path.GetFileNameWithoutExtension(file) ?? string.Empty;
                            if (ContainsAppKeyword(fileName))
                            {
                                try
                                {
                                    File.Delete(file);
                                    removedCount++;
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }

                if (removedCount > 0)
                {
                    results.Add($"✓ Taskbar pins: Removed {removedCount} pinned shortcut(s)");
                    results.Add("  Note: Windows may take a moment to refresh the taskbar list");
                }
                else
                {
                    results.Add("✓ Taskbar pins: No pinned shortcuts found");
                }

                return true;
            }
            catch (Exception ex)
            {
                results.Add($"✗ Taskbar pins: Failed to clean - {ex.Message}");
                Utilities.Logger.Log($"UninstallCleanup: Taskbar pin cleanup failed: {ex.Message}");
                return false;
            }
        }

    }
}
