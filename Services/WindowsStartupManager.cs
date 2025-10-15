/*
    Windows Startup Manager: Handles adding/removing ZTalk from Windows startup registry.
    - Uses HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run for current user
    - No admin rights required for HKCU
*/
using System;
using System.IO;
using Microsoft.Win32;

namespace ZTalk.Services
{
    public static class WindowsStartupManager
    {
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "ZTalk";

        public static bool IsRunOnStartupEnabled()
        {
            if (!OperatingSystem.IsWindows())
                return false;

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
                var value = key?.GetValue(AppName) as string;
                return !string.IsNullOrEmpty(value);
            }
            catch (Exception ex)
            {
                Utilities.Logger.Log($"WindowsStartup: Failed to check startup status: {ex.Message}");
                return false;
            }
        }

        public static bool SetRunOnStartup(bool enabled)
        {
            if (!OperatingSystem.IsWindows())
            {
                Utilities.Logger.Log("WindowsStartup: Not supported on non-Windows platforms");
                return false;
            }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
                if (key == null)
                {
                    Utilities.Logger.Log("WindowsStartup: Could not open registry key");
                    return false;
                }

                if (enabled)
                {
                    // Get executable path
                    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (string.IsNullOrEmpty(exePath))
                    {
                        Utilities.Logger.Log("WindowsStartup: Could not determine executable path");
                        return false;
                    }

                    // Add to startup with --minimized flag (if we add that arg later)
                    var command = $"\"{exePath}\"";
                    key.SetValue(AppName, command, RegistryValueKind.String);
                    Utilities.Logger.Log($"WindowsStartup: Added to startup: {command}");
                    return true;
                }
                else
                {
                    // Remove from startup
                    if (key.GetValue(AppName) != null)
                    {
                        key.DeleteValue(AppName, false);
                        Utilities.Logger.Log("WindowsStartup: Removed from startup");
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                Utilities.Logger.Log($"WindowsStartup: Failed to set startup: {ex.Message}");
                return false;
            }
        }

        public static void ApplyStartupSetting(bool shouldRun)
        {
            try
            {
                var currentlyEnabled = IsRunOnStartupEnabled();
                if (currentlyEnabled != shouldRun)
                {
                    SetRunOnStartup(shouldRun);
                }
            }
            catch (Exception ex)
            {
                Utilities.Logger.Log($"WindowsStartup: Failed to apply startup setting: {ex.Message}");
            }
        }
    }
}
