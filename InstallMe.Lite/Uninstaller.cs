using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

namespace InstallMe.Lite
{
    [SupportedOSPlatform("windows")]
    public static class Uninstaller
    {
        private const string StartupRegKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
        private static InstallerConfig _config = new();

        public static Action<string>? LogSink;

        public static void Configure(InstallerConfig config)
        {
            _config = config ?? new InstallerConfig();
        }

        private static string UninstallRegKey => $"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{_config.UninstallKeyName}";

        private static void Log(string message)
        {
            try { LogSink?.Invoke(message); } catch { }
        }

        public static int RunFromArgs()
        {
            try
            {
                var installPath = ReadInstallPathFromRegistry();
                if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath))
                {
                    return 1;
                }

                TryKillProcessesUsingPath(installPath);

                if (!TryDeleteDirectoryWithRetries(installPath, 5, TimeSpan.FromSeconds(1)))
                {
                    try { MoveToTempAndScheduleDelete(installPath); } catch { }
                }

                try { RemoveTaskbarPins(); } catch { }
                try { CleanupTrayIconSettings(); } catch { }
                try { RemoveAllRegistryEntries(); } catch { }
                try { RemoveRegistryKey(); } catch { }

                return 0;
            }
            catch
            {
                return 2;
            }
        }

        public static void WriteInstallPathToRegistry(string path)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(UninstallRegKey);
                if (key == null)
                {
                    return;
                }

                key.SetValue("DisplayName", _config.AppDisplayName + " (InstallMe Lite)");
                key.SetValue("DisplayVersion", _config.DisplayVersion);
                key.SetValue("Publisher", _config.Publisher);
                key.SetValue("InstallLocation", path);

                var exe = System.Reflection.Assembly.GetEntryAssembly()?.Location ?? string.Empty;
                key.SetValue("UninstallString", exe + " /uninstall");
                Log("Registry: Set uninstall info (HKCU) at " + UninstallRegKey);
            }
            catch { }
        }

        public static string? GetInstallPath() => ReadInstallPathFromRegistry();

        public static void StopProcesses(string path) => TryKillProcessesUsingPath(path);

        public static bool DeleteDirectoryWithRetries(string path, int attempts, TimeSpan delay) => TryDeleteDirectoryWithRetries(path, attempts, delay);

        public static void ScheduleDelete(string path) => MoveToTempAndScheduleDelete(path);

        public static void RemoveRegistryKey()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall", true);
                key?.DeleteSubKeyTree(_config.UninstallKeyName, false);
            }
            catch { }
        }

        public static void RemoveShortcutsNow()
        {
            try { RemoveShortcuts(); } catch { }
        }

        public static void CleanupRegistryHiveEntries()
        {
            try { RemoveAllRegistryEntries(); } catch { }
        }

        public static List<string> DetectAndRemoveLegacyInstalls()
        {
            var removedItems = new List<string>();
            var legacyNames = _config.LegacyCleanupNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!legacyNames.Any())
            {
                return removedItems;
            }

            RemoveLegacyUninstallEntries(Registry.CurrentUser, "HKCU", legacyNames, removedItems);
            RemoveLegacyUninstallEntries(Registry.LocalMachine, "HKLM", legacyNames, removedItems);

            foreach (var legacyName in legacyNames)
            {
                RemoveOldShortcuts(legacyName, removedItems);
            }

            foreach (var path in BuildLegacyCandidatePaths(legacyNames))
            {
                if (!Directory.Exists(path))
                {
                    continue;
                }

                try
                {
                    TryKillProcessesUsingPath(path);
                    if (TryDeleteDirectoryWithRetries(path, 3, TimeSpan.FromSeconds(1)))
                    {
                        removedItems.Add("Legacy folder: " + path);
                    }
                }
                catch { }
            }

            return removedItems;
        }

        // Backward-compatible wrapper retained for existing call sites.
        public static List<string> DetectAndRemoveOldZTalk() => DetectAndRemoveLegacyInstalls();

        public static void RemoveFromStartup()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(StartupRegKey, true);
                RemoveMatchingRunEntries(key, "HKCU");
            }
            catch { }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(StartupRegKey, true);
                RemoveMatchingRunEntries(key, "HKLM");
            }
            catch { }

            try
            {
                var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                foreach (var file in Directory.GetFiles(startupFolder, "*.lnk", SearchOption.TopDirectoryOnly))
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    if (!ContainsAppKeyword(fileName))
                    {
                        continue;
                    }

                    try
                    {
                        File.Delete(file);
                        Log("File: Removed startup shortcut " + file);
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static void CompleteUninstall(string installPath)
        {
            TryKillProcessesUsingPath(installPath);
            RemoveFromStartup();
            RemoveTaskbarPins();
            CleanupTrayIconSettings();
            RemoveShortcuts();

            if (!TryDeleteDirectoryWithRetries(installPath, 6, TimeSpan.FromSeconds(1)))
            {
                try { MoveToTempAndScheduleDelete(installPath); } catch { }
            }

            RemoveAllRegistryEntries();
        }

        public static void RemoveTaskbarPins()
        {
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
                    if (!Directory.Exists(location))
                    {
                        continue;
                    }

                    foreach (var file in Directory.GetFiles(location, "*.lnk", SearchOption.TopDirectoryOnly))
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file) ?? string.Empty;
                        if (!ContainsAppKeyword(fileName))
                        {
                            continue;
                        }

                        try
                        {
                            File.Delete(file);
                            Log("File: Removed pin/shortcut " + file);
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        public static void CleanupTrayIconSettings()
        {
            try { RemoveNotifyIconSettings(@"Control Panel\NotifyIconSettings"); } catch { }
            try { RemoveNotifyIconSettings(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\TrayNotify\NotifyIconSettings"); } catch { }
        }

        public static void ForceRefreshInstalledApps()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c rundll32.exe shell32.dll,Control_RunDLL appwiz.cpl",
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(1000);
            }
            catch { }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c ie4uinit.exe -ClearIconCache",
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(2000);
            }
            catch { }
        }

        private static string? ReadInstallPathFromRegistry()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(UninstallRegKey);
                return key?.GetValue("InstallLocation") as string;
            }
            catch
            {
                return null;
            }
        }

        private static void MoveToTempAndScheduleDelete(string path)
        {
            var tmp = Path.Combine(Path.GetTempPath(), _config.ShortcutName + "_Uninstall_" + Guid.NewGuid().ToString("N"));
            Directory.Move(path, tmp);

            var cmd = $"/C ping 127.0.0.1 -n 2 >nul & rmdir /s /q \"{tmp}\"";
            var psi = new ProcessStartInfo("cmd.exe", cmd)
            {
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);
        }

        private static void TryKillProcessesUsingPath(string path)
        {
            try
            {
                foreach (var process in Process.GetProcesses())
                {
                    try
                    {
                        var modulePath = process.MainModule?.FileName;
                        if (string.IsNullOrEmpty(modulePath))
                        {
                            continue;
                        }

                        if (modulePath.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                        {
                            try { process.Kill(); process.WaitForExit(2000); } catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static bool TryDeleteDirectoryWithRetries(string path, int attempts, TimeSpan delay)
        {
            for (var i = 0; i < attempts; i++)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                    }
                    return true;
                }
                catch
                {
                    System.Threading.Thread.Sleep(delay);
                }
            }

            return !Directory.Exists(path);
        }

        private static void RemoveAllRegistryEntries()
        {
            var registryPaths = new[]
            {
                ("HKCU", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall"),
                ("HKLM", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall"),
                ("HKLM", "SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall"),
                ("HKCU", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Installer\\UserData"),
                ("HKLM", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Installer\\UserData"),
                ("HKCU", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths"),
                ("HKLM", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths"),
                ("HKCU", "SOFTWARE\\Classes\\Applications"),
                ("HKLM", "SOFTWARE\\Classes\\Applications"),
                ("HKCU", "SOFTWARE\\RegisteredApplications"),
                ("HKLM", "SOFTWARE\\RegisteredApplications"),
                ("HKCU", "SOFTWARE\\Clients"),
                ("HKLM", "SOFTWARE\\Clients")
            };

            foreach (var (hive, path) in registryPaths)
            {
                try
                {
                    var baseKey = hive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                    using var key = baseKey.OpenSubKey(path, true);
                    if (key == null)
                    {
                        continue;
                    }

                    foreach (var subkey in key.GetSubKeyNames().ToList())
                    {
                        using var child = key.OpenSubKey(subkey);
                        if (!IsUninstallSubkeyMatch(subkey, child) && !ContainsAppKeyword(subkey))
                        {
                            continue;
                        }

                        try
                        {
                            key.DeleteSubKeyTree(subkey, false);
                            Log("Registry: Removed " + hive + "\\" + path + "\\" + subkey);
                        }
                        catch { }
                    }
                }
                catch { }
            }

            CleanupRegistryValues();
        }

        private static void CleanupRegistryValues()
        {
            var valuePaths = new[]
            {
                ("HKCU", "SOFTWARE\\RegisteredApplications"),
                ("HKLM", "SOFTWARE\\RegisteredApplications")
            };

            foreach (var (hive, path) in valuePaths)
            {
                try
                {
                    var baseKey = hive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                    using var key = baseKey.OpenSubKey(path, true);
                    if (key == null)
                    {
                        continue;
                    }

                    foreach (var valueName in key.GetValueNames().ToList())
                    {
                        var valueData = key.GetValue(valueName)?.ToString() ?? string.Empty;
                        if (!ContainsAppKeyword(valueName) && !ContainsAppKeyword(valueData))
                        {
                            continue;
                        }

                        try
                        {
                            key.DeleteValue(valueName, false);
                            Log("Registry: Removed value " + hive + "\\" + path + " : " + valueName);
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        private static void RemoveShortcuts()
        {
            var shortcutNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                _config.ShortcutName,
                _config.AppDisplayName
            };

            foreach (var legacy in _config.LegacyCleanupNames)
            {
                if (!string.IsNullOrWhiteSpace(legacy))
                {
                    shortcutNames.Add(legacy);
                }
            }

            foreach (var shortcutName in shortcutNames)
            {
                TryDeleteShortcut(Environment.SpecialFolder.DesktopDirectory, shortcutName);
                TryDeleteShortcut(Environment.SpecialFolder.CommonDesktopDirectory, shortcutName);
                TryDeleteShortcut(Environment.SpecialFolder.StartMenu, shortcutName);
                TryDeleteShortcut(Environment.SpecialFolder.CommonStartMenu, shortcutName);
            }
        }

        private static void TryDeleteShortcut(Environment.SpecialFolder rootFolder, string shortcutName)
        {
            try
            {
                var basePath = Environment.GetFolderPath(rootFolder);
                var shortcutPath = rootFolder == Environment.SpecialFolder.StartMenu || rootFolder == Environment.SpecialFolder.CommonStartMenu
                    ? Path.Combine(basePath, "Programs", shortcutName + ".lnk")
                    : Path.Combine(basePath, shortcutName + ".lnk");

                File.Delete(shortcutPath);
            }
            catch { }
        }

        private static void RemoveNotifyIconSettings(string subKeyPath)
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKeyPath, true);
            if (key == null)
            {
                return;
            }

            foreach (var sub in key.GetSubKeyNames())
            {
                try
                {
                    using var child = key.OpenSubKey(sub);
                    if (child == null)
                    {
                        continue;
                    }

                    var exePath = child.GetValue("ExecutablePath")?.ToString() ?? string.Empty;
                    var tooltip = child.GetValue("InitialTooltip")?.ToString() ?? string.Empty;
                    var appName = child.GetValue("ApplicationName")?.ToString() ?? string.Empty;
                    var appId = child.GetValue("AppUserModelID")?.ToString() ?? string.Empty;

                    if (!ContainsAppKeyword(sub) && !ContainsAppKeyword(exePath) && !ContainsAppKeyword(tooltip) &&
                        !ContainsAppKeyword(appName) && !ContainsAppKeyword(appId))
                    {
                        continue;
                    }

                    try
                    {
                        key.DeleteSubKeyTree(sub, false);
                        Log("Registry: Removed tray icon entry " + subKeyPath + "\\" + sub);
                    }
                    catch { }
                }
                catch { }
            }
        }

        private static bool IsUninstallSubkeyMatch(string subkeyName, RegistryKey? subkey)
        {
            if (ContainsAppKeyword(subkeyName))
            {
                return true;
            }

            if (subkey == null)
            {
                return false;
            }

            var displayName = subkey.GetValue("DisplayName")?.ToString() ?? string.Empty;
            var uninstallString = subkey.GetValue("UninstallString")?.ToString() ?? string.Empty;
            var installLocation = subkey.GetValue("InstallLocation")?.ToString() ?? string.Empty;
            var displayIcon = subkey.GetValue("DisplayIcon")?.ToString() ?? string.Empty;

            return ContainsAppKeyword(displayName) || ContainsAppKeyword(uninstallString) ||
                   ContainsAppKeyword(installLocation) || ContainsAppKeyword(displayIcon);
        }

        private static bool ContainsAppKeyword(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            foreach (var keyword in _config.MatchKeywords)
            {
                if (value.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RemoveMatchingRunEntries(RegistryKey? key, string hiveName)
        {
            if (key == null)
            {
                return;
            }

            foreach (var valueName in key.GetValueNames())
            {
                if (!ContainsAppKeyword(valueName))
                {
                    continue;
                }

                try
                {
                    key.DeleteValue(valueName, false);
                    Log("Registry: Removed " + hiveName + " Run value " + valueName);
                }
                catch { }
            }
        }

        private static void RemoveLegacyUninstallEntries(RegistryKey root, string hiveName, IReadOnlyCollection<string> legacyNames, List<string> removedItems)
        {
            try
            {
                using var key = root.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall", true);
                if (key == null)
                {
                    return;
                }

                foreach (var subkeyName in key.GetSubKeyNames())
                {
                    var isLegacy = legacyNames.Any(name => subkeyName.Contains(name, StringComparison.OrdinalIgnoreCase));
                    var isCurrent = subkeyName.Contains(_config.AppDisplayName, StringComparison.OrdinalIgnoreCase);
                    if (!isLegacy || isCurrent)
                    {
                        continue;
                    }

                    try
                    {
                        using var oldKey = key.OpenSubKey(subkeyName);
                        var installPath = oldKey?.GetValue("InstallLocation") as string;

                        if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                        {
                            TryKillProcessesUsingPath(installPath);
                            TryDeleteDirectoryWithRetries(installPath, 3, TimeSpan.FromSeconds(1));
                            removedItems.Add("Legacy installation: " + installPath);
                        }

                        key.DeleteSubKeyTree(subkeyName, false);
                        removedItems.Add("Registry entry (" + hiveName + "): " + subkeyName);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static IEnumerable<string> BuildLegacyCandidatePaths(IEnumerable<string> legacyNames)
        {
            foreach (var name in legacyNames)
            {
                yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), name);
                yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), name);
                yield return Path.Combine("C:\\Apps", name);
                yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), name);
            }
        }

        private static void RemoveOldShortcuts(string appName, List<string> removedItems)
        {
            var locations = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs")
            };

            foreach (var location in locations)
            {
                try
                {
                    if (!Directory.Exists(location))
                    {
                        continue;
                    }

                    foreach (var file in Directory.GetFiles(location, "*.lnk", SearchOption.TopDirectoryOnly))
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        if (!fileName.Equals(appName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        try
                        {
                            File.Delete(file);
                            removedItems.Add("Shortcut: " + file);
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }
    }
}
