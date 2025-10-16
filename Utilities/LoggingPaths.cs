// Centralized logging path management and enable/disable policy.
// Debug builds: logging enabled by default and all logs go to <exe>/logs.
// Release builds: logging disabled unless explicitly enabled via env var ZTALK_ENABLE_LOGS=1
// or presence of an enable-logs.flag file next to the executable.
using System;
using System.IO;

namespace ZTalk.Utilities
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
                var env = Environment.GetEnvironmentVariable("ZTALK_ENABLE_LOGS");
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
        
        // Called by SettingsService after settings are loaded to sync logging state with user preference
        public static void SyncWithSettings()
        {
#if DEBUG
            try
            {
                var settings = ZTalk.Services.AppServices.Settings?.Settings;
                if (settings != null)
                {
                    _enabled = settings.EnableLogging;
                    if (_enabled && !string.IsNullOrEmpty(_logsDir))
                    {
                        try { Directory.CreateDirectory(_logsDir); } catch { }
                    }
                }
            }
            catch { }
#endif
        }

        // Allow runtime toggling of logging (Debug builds only)
        public static void SetEnabled(bool enabled)
        {
#if DEBUG
            _enabled = enabled;
            if (_enabled && !string.IsNullOrEmpty(_logsDir))
            {
                try { Directory.CreateDirectory(_logsDir); } catch { }
            }
#endif
        }

        private static string PathFor(string fileName) => Path.Combine(LogsDirectory, fileName);

        public static string Error => PathFor("error.log");
        public static string Theme => PathFor("theme.log");
        public static string Performance => PathFor("performance.log");
        public static string Network => PathFor("network.log");
        public static string NetworkHeartbeat => PathFor("network_heartbeat.log");
        public static string Startup => PathFor("startup.log");
        public static string Input => PathFor("input.log");
        public static string Render => PathFor("render.log");
        public static string Debug => PathFor("debug.log");
        public static string App => PathFor("app.log");
        public static string Retention => PathFor("retention.log");
        public static string Monitoring => PathFor("monitoring.log");
        public static string UI => PathFor("ui.log");
        public static string Interaction => PathFor("interaction.log");
        public static string Maintenance => PathFor("maintenance.log");
        public static string Audio => PathFor("audio.log");

        public static bool TryWrite(string path, string line)
        {
            if (!Enabled) return false;
            try
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                    using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    using var sw = new StreamWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false);
                    sw.Write(line);
                    sw.Flush();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            catch { return false; }
        }
    }
}
