using System;
using System.IO;
using Microsoft.Win32;

namespace Zer0Talk.RelayServer.Services;

public static class RelayStartupManager
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Zer0TalkRelay";

    public static bool IsRunOnStartupEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            var value = key?.GetValue(AppName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch
        {
            return false;
        }
    }

    public static bool SetRunOnStartup(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key == null) return false;

            if (enabled)
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;

                if (string.IsNullOrEmpty(exePath) || exePath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                        if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            var exeFile = Path.ChangeExtension(exePath, ".exe");
                            if (File.Exists(exeFile))
                                exePath = exeFile;
                            else
                                return false;
                        }
                    }
                    catch
                    {
                        return false;
                    }
                }

                if (string.IsNullOrEmpty(exePath)) return false;

                key.SetValue(AppName, $"\"{exePath}\"", RegistryValueKind.String);
                return true;
            }
            else
            {
                if (key.GetValue(AppName) != null)
                    key.DeleteValue(AppName, false);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    public static void ApplyStartupSetting(bool shouldRun)
    {
        try
        {
            if (IsRunOnStartupEnabled() != shouldRun)
                SetRunOnStartup(shouldRun);
        }
        catch { }
    }
}
