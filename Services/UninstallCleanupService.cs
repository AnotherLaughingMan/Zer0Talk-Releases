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
    public static class UninstallCleanupService
    {
        /// <summary>
        /// Performs system cleanup for Zer0Talk uninstall
        /// User data in %APPDATA%\Zer0Talk is preserved
        /// </summary>
        /// <param name="results">List to collect operation results</param>
        /// <returns>True if all operations succeeded</returns>
        public static bool PerformUninstallCleanup(List<string> results)
        {
            results?.Clear();
            bool success = true;
            
            try
            {
                results.Add("=== Zer0Talk Uninstaller ===");
                results.Add("Cleaning up system components only");
                results.Add("");
                
                // 1. Remove Windows startup registry entry
                success &= CleanupWindowsStartup(results);
                
                // 2. Clean up application logs
                success &= CleanupApplicationLogs(results);
                
                // 3. Clean up temporary files
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
                var appNames = new[] { "Zer0Talk", "P2PTalk", "Zer0Talk.exe", "P2PTalk.exe" };
                
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
        /// Remove application logs (beside executable)
        /// </summary>
        private static bool CleanupApplicationLogs(List<string> results)
        {
            try
            {
                if (!Utilities.LoggingPaths.Enabled)
                {
                    results.Add("✓ Application logs: Logging disabled (nothing to clean)");
                    return true;
                }
                
                var logsDir = Utilities.LoggingPaths.LogsDirectory;
                return CleanupDirectory(logsDir, "Application logs directory", results);
            }
            catch (Exception ex)
            {
                results.Add($"✗ Application logs: Failed to determine logs directory - {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Remove temporary and cache files
        /// </summary>
        private static bool CleanupTemporaryFiles(List<string> results)
        {
            bool success = true;
            
            // Clean cache directory
            try
            {
                var cacheDir = Path.Combine(Utilities.AppDataPaths.Root, ".cache");
                if (Directory.Exists(cacheDir))
                {
                    success &= CleanupDirectory(cacheDir, "Cache directory", results);
                }
                else
                {
                    results.Add("✓ Cache directory: Not found (nothing to clean)");
                }
            }
            catch (Exception ex)
            {
                results.Add($"✗ Cache directory: Failed to clean - {ex.Message}");
                success = false;
            }
            
            return success;
        }
        
        /// <summary>
        /// Helper method to safely remove a directory and all its contents
        /// </summary>
        private static bool CleanupDirectory(string directoryPath, string displayName, List<string> results)
        {
            try
            {
                if (!Directory.Exists(directoryPath))
                {
                    results.Add($"✓ {displayName}: Not found (nothing to clean)");
                    return true;
                }
                
                var fileCount = 0;
                var dirCount = 0;
                
                // Count files and directories for reporting
                try
                {
                    fileCount = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories).Length;
                    dirCount = Directory.GetDirectories(directoryPath, "*", SearchOption.AllDirectories).Length;
                }
                catch { }
                
                // Remove the directory
                Directory.Delete(directoryPath, recursive: true);
                
                results.Add($"✓ {displayName}: Removed ({fileCount} files, {dirCount} subdirectories)");
                results.Add($"  Path: {directoryPath}");
                
                Utilities.Logger.Log($"UninstallCleanup: Removed {displayName} at {directoryPath}");
                return true;
            }
            catch (Exception ex)
            {
                results.Add($"✗ {displayName}: Failed to remove - {ex.Message}");
                results.Add($"  Path: {directoryPath}");
                
                Utilities.Logger.Log($"UninstallCleanup: Failed to remove {displayName} at {directoryPath}: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Get a preview of what would be cleaned up without actually performing the cleanup
        /// </summary>
        /// <summary>
        /// Generates preview of what would be cleaned up during uninstall
        /// </summary>
        /// <returns>List of preview lines</returns>
        public static List<string> GetCleanupPreview()
        {
            var preview = new List<string>
            {
                "=== Zer0Talk Uninstall Preview ===",
                "",
                "The following system components will be removed:",
                "",
                "REGISTRY ENTRIES:",
                "• Windows startup entry (if exists)",
                "  HKEY_CURRENT_USER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run\\Zer0Talk",
                "",
                "APPLICATION LOGS:",
                "• Debug logs (if any exist next to executable)",
                "• Error logs (if any exist next to executable)", 
                "• Performance logs (if any exist next to executable)",
                "",
                "TEMPORARY FILES:",
                "• Any temporary files created by Zer0Talk",
                "• Cache files (if any)",
                "",
                "PRESERVED (NOT REMOVED):",
                "• User data in %APPDATA%\\Zer0Talk\\",
                "  - Your messages and conversations",
                "  - Contact list and verification keys", 
                "  - Personal settings and preferences",
                "  - Custom themes (if any)",
                "",
                "This is a preview only - no changes will be made until you confirm."
            };
            
            return preview;
        }
        
        /// <summary>
        /// Helper to add directory information to preview
        /// </summary>
        private static void AddDirectoryPreview(List<string> preview, string directoryPath, string displayName)
        {
            try
            {
                if (Directory.Exists(directoryPath))
                {
                    var fileCount = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories).Length;
                    var dirCount = Directory.GetDirectories(directoryPath, "*", SearchOption.AllDirectories).Length;
                    var size = GetDirectorySize(directoryPath);
                    
                    preview.Add($"  {displayName}: {fileCount} files, {dirCount} folders, {FormatBytes(size)}");
                    preview.Add($"    Path: {directoryPath}");
                }
                else
                {
                    preview.Add($"  {displayName}: Not found");
                }
            }
            catch
            {
                preview.Add($"  {displayName}: Unable to analyze");
                preview.Add($"    Path: {directoryPath}");
            }
        }
        
        /// <summary>
        /// Calculate total size of directory in bytes
        /// </summary>
        private static long GetDirectorySize(string directoryPath)
        {
            try
            {
                var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
                long totalSize = 0;
                
                foreach (var file in files)
                {
                    try
                    {
                        totalSize += new FileInfo(file).Length;
                    }
                    catch { }
                }
                
                return totalSize;
            }
            catch
            {
                return 0;
            }
        }
        
        /// <summary>
        /// Format bytes in human-readable format
        /// </summary>
        private static string FormatBytes(long bytes)
        {
            if (bytes == 0) return "0 B";
            
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
