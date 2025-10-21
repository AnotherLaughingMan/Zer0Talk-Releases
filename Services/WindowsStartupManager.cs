/*
    Windows Startup Manager: Handles adding/removing Zer0Talk from Windows startup registry.
    - Uses HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run for current user
    - No admin rights required for HKCU
*/
using System;
using System.IO;
using Microsoft.Win32;

namespace Zer0Talk.Services
{
    public static class WindowsStartupManager
    {
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "Zer0Talk";

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
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
                if (key == null)
                {
                    Utilities.Logger.Log("WindowsStartup: Could not create/open registry key");
                    return false;
                }

                if (enabled)
                {
                    // Get executable path
                    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    
                    // Handle development scenario (dotnet run) - get the actual executable path
                    if (string.IsNullOrEmpty(exePath) || exePath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        // Try alternative methods to get executable path
                        try
                        {
                            exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                            if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                            {
                                // For published apps, look for .exe in same directory
                                var exeFile = Path.ChangeExtension(exePath, ".exe");
                                if (File.Exists(exeFile))
                                {
                                    exePath = exeFile;
                                }
                                else
                                {
                                    Utilities.Logger.Log("WindowsStartup: Running in development mode - startup entry will not work until published");
                                    return false;
                                }
                            }
                        }
                        catch
                        {
                            Utilities.Logger.Log("WindowsStartup: Could not determine executable path");
                            return false;
                        }
                    }
                    
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

