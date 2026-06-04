using System;
using System.IO;

namespace Zer0Talk.RelayServer.Services;

public static class RelayDiagnostics
{
    private static readonly object Gate = new();
    private static string? _startupAlert;

    public static string DiagnosticsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Zer0TalkRelay",
        "Logs");

    public static string DiagnosticsLogPath => Path.Combine(DiagnosticsDirectory, "relay-diagnostics.log");
    public static string CrashMarkerPath => Path.Combine(DiagnosticsDirectory, "relay-last-crash.txt");

    public static void LogInfo(string source, string message)
    {
        AppendLine($"[{DateTime.Now:O}] [INF] [{source}] {message}");
    }

    public static void LogException(string source, Exception? ex, bool markCrash)
    {
        try
        {
            var header = ex == null
                ? $"[{DateTime.Now:O}] [ERR] [{source}] <null exception>"
                : $"[{DateTime.Now:O}] [ERR] [{source}] {ex.GetType().FullName}: {ex.Message}";

            AppendLine(header);
            if (!string.IsNullOrWhiteSpace(ex?.StackTrace))
            {
                AppendLine(ex.StackTrace!);
            }

            if (!markCrash)
            {
                return;
            }

            var summary = ex == null
                ? $"[{DateTime.Now:O}] {source}: <null exception>"
                : $"[{DateTime.Now:O}] {source}: {ex.GetType().Name}: {ex.Message}";

            lock (Gate)
            {
                EnsureDirectory();
                File.WriteAllText(CrashMarkerPath, summary);
            }
        }
        catch
        {
            // Never throw from diagnostics path.
        }
    }

    public static bool TryConsumeCrashMarker(out string summary)
    {
        summary = string.Empty;

        try
        {
            lock (Gate)
            {
                if (!File.Exists(CrashMarkerPath))
                {
                    return false;
                }

                summary = File.ReadAllText(CrashMarkerPath).Trim();
                File.Delete(CrashMarkerPath);
                return !string.IsNullOrWhiteSpace(summary);
            }
        }
        catch
        {
            return false;
        }
    }

    public static void SetStartupAlert(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return;
        }

        lock (Gate)
        {
            _startupAlert = summary;
        }
    }

    public static bool TryConsumeStartupAlert(out string summary)
    {
        summary = string.Empty;

        lock (Gate)
        {
            if (string.IsNullOrWhiteSpace(_startupAlert))
            {
                return false;
            }

            summary = _startupAlert;
            _startupAlert = null;
            return true;
        }
    }

    private static void AppendLine(string line)
    {
        lock (Gate)
        {
            EnsureDirectory();
            File.AppendAllText(DiagnosticsLogPath, line + Environment.NewLine);
        }
    }

    private static void EnsureDirectory()
    {
        Directory.CreateDirectory(DiagnosticsDirectory);
    }
}