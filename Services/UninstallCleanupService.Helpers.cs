using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace Zer0Talk.Services
{
    public static partial class UninstallCleanupService
    {
        private static bool IsUninstallSubkeyMatch(string subkeyName, RegistryKey? subkey)
        {
            if (ContainsAppKeyword(subkeyName)) return true;
            if (subkey == null) return false;

            var displayName = subkey.GetValue("DisplayName")?.ToString() ?? string.Empty;
            var uninstallString = subkey.GetValue("UninstallString")?.ToString() ?? string.Empty;
            var installLocation = subkey.GetValue("InstallLocation")?.ToString() ?? string.Empty;
            var displayIcon = subkey.GetValue("DisplayIcon")?.ToString() ?? string.Empty;

            return ContainsAppKeyword(displayName) || ContainsAppKeyword(uninstallString) ||
                   ContainsAppKeyword(installLocation) || ContainsAppKeyword(displayIcon);
        }

        private static bool ContainsAppKeyword(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            foreach (var keyword in AppNameKeywords)
            {
                if (value.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
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
                "• Legacy uninstall/app paths entries (Zer0Talk/ZTalk/P2PTalk)",
                "  HKEY_CURRENT_USER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
                "  HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
                "  HKEY_LOCAL_MACHINE\\SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
                "  HKEY_CURRENT_USER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths",
                "  HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths",
                "  HKEY_CURRENT_USER\\SOFTWARE\\Classes\\Applications",
                "  HKEY_LOCAL_MACHINE\\SOFTWARE\\Classes\\Applications",
                "  HKEY_CURRENT_USER\\SOFTWARE\\RegisteredApplications",
                "  HKEY_LOCAL_MACHINE\\SOFTWARE\\RegisteredApplications",
                "",
                "TASKBAR PINS:",
                "• Pinned taskbar/start menu shortcuts (Zer0Talk/ZTalk/P2PTalk)",
                "  %APPDATA%\\Microsoft\\Internet Explorer\\Quick Launch\\User Pinned\\TaskBar",
                "  %APPDATA%\\Microsoft\\Internet Explorer\\Quick Launch\\User Pinned\\StartMenu",
                "  %APPDATA%\\Microsoft\\Windows\\Start Menu\\Programs",
                "  %PROGRAMDATA%\\Microsoft\\Windows\\Start Menu\\Programs",
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
