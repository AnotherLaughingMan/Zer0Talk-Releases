/*
    Lightweight logging helper: timestamps, levels, and optional file sinks.
*/
using System;
using System.IO;

namespace P2PTalk.Utilities
{
    public static class Logger
    {
        // Raised whenever a line is logged; UI can subscribe for live logs.
        public static event Action<string>? LineLogged;
        public static void Log(string message)
        {
            // TODO: Implement structured logging
            var line = $"[LOG] {DateTime.Now:O}: {message}";
            try { Console.WriteLine(line); } catch { /* ignore */ }
            try
            {
                if (LoggingPaths.Enabled)
                    File.AppendAllText(LoggingPaths.App, line + Environment.NewLine);
            }
            catch { /* ignore logging failures */ }
            try { LineLogged?.Invoke(line); } catch { /* best-effort */ }
        }
    }
}
