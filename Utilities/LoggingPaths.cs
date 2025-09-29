// Centralized logging path management and enable/disable policy.
// Debug builds: logging enabled by default and all logs go to <exe>/logs.
// Release builds: logging disabled unless explicitly enabled via env var P2PTALK_ENABLE_LOGS=1
// or presence of an enable-logs.flag file next to the executable.
using System;
using System.IO;

namespace P2PTalk.Utilities
{
    internal static class LoggingPaths
    {
#if DEBUG
        private const bool DefaultEnabled = true;
#else
        private const bool DefaultEnabled = false;
#endif
        private static bool _init;
        private static bool _enabled;
        private static string? _logsDir;

        public static bool Enabled
        {
            get
            {
                EnsureInitialized();
                return _enabled;
            }
        }

        public static string LogsDirectory
        {
            get
            {
                EnsureInitialized();
                return _logsDir!;
            }
        }

        public static void EnsureInitialized()
        {
            if (_init) return;
            _init = true;
            var baseDir = AppContext.BaseDirectory;
            _enabled = DefaultEnabled;
            try
            {
                var env = Environment.GetEnvironmentVariable("P2PTALK_ENABLE_LOGS");
                if (!string.IsNullOrWhiteSpace(env) && (env == "1" || env.Equals("true", StringComparison.OrdinalIgnoreCase)))
                    _enabled = true;
                var flag = Path.Combine(baseDir, "enable-logs.flag");
                if (File.Exists(flag)) _enabled = true;
            }
            catch { }
            _logsDir = Path.Combine(baseDir, "logs");
            if (_enabled)
            {
                try { Directory.CreateDirectory(_logsDir); } catch { }
            }
        }

        private static string PathFor(string fileName) => Path.Combine(LogsDirectory, fileName);

        public static string Error => PathFor("error.log");
        public static string Theme => PathFor("theme.log");
        public static string Performance => PathFor("performance.log");
        public static string Network => PathFor("network.log");
        public static string Startup => PathFor("startup.log");
        public static string Input => PathFor("input.log");
        public static string Render => PathFor("render.log");
        public static string Debug => PathFor("debug.log");
    public static string App => PathFor("app.log");
        public static string Retention => PathFor("retention.log");
        public static string Monitoring => PathFor("monitoring.log");
        public static string UI => PathFor("ui.log");
        public static string Interaction => PathFor("interaction.log");

        public static bool TryWrite(string path, string line)
        {
            if (!Enabled) return false;
            try { File.AppendAllText(path, line); return true; } catch { return false; }
        }
    }
}
