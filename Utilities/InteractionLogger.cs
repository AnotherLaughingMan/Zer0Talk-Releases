using System;
using System.IO;

namespace P2PTalk.Utilities
{
    public static class InteractionLogger
    {
        private static readonly object _lock = new object();

        private static string GetLogPath()
        {
            try
            {
                return LoggingPaths.Interaction;
            }
            catch
            {
                // Fallback: beside executable
                try { return Path.Combine(AppContext.BaseDirectory, "interaction.log"); } catch { return "interaction.log"; }
            }
        }

        public static void Log(string message)
        {
            try
            {
                var path = GetLogPath();
                var line = $"{DateTime.Now:O} {message}{Environment.NewLine}";
                lock (_lock)
                {
                    File.AppendAllText(path, line);
                }
            }
            catch { }
        }
    }
}
