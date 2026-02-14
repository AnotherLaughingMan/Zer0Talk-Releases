// Centralized logging path management and enable/disable policy.
// Debug builds: logging enabled by default and all logs go to %APPDATA%/Zer0Talk/Logs.
// Release builds: logging disabled unless explicitly enabled via env var ZER0TALK_ENABLE_LOGS=1
// or presence of an enable-logs.flag file next to the executable.
using System;
using System.IO;

namespace Zer0Talk.Utilities
{
    internal static class LoggingPaths
    {
        private static readonly string[] LegacyTxtLogNames =
        {
            "error.txt",
            "startup.txt",
            "app.txt",
            "debug.txt",
            "network.txt",
            "network_heartbeat.txt",
            "performance.txt",
            "ui.txt",
            "interaction.txt",
            "maintenance.txt",
            "audio.txt",
            "crypto.txt",
            "enc_chat.txt",
            "theme.txt",
            "theme_engine.txt",
            "monitoring.txt",
            "retention.txt",
            "account_creation.txt"
        };

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
            string currentDir;
            try { currentDir = Environment.CurrentDirectory; } catch { currentDir = baseDir; }

            _enabled = DefaultEnabled;
            try
            {
                var env = Environment.GetEnvironmentVariable("ZER0TALK_ENABLE_LOGS");
                if (!string.IsNullOrWhiteSpace(env) && (env == "1" || env.Equals("true", StringComparison.OrdinalIgnoreCase)))
                    _enabled = true;
                var flag = Path.Combine(baseDir, "enable-logs.flag");
                if (File.Exists(flag)) _enabled = true;
            }
            catch { }

            var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _logsDir = Path.Combine(appDataRoot, "Zer0Talk", "Logs");
            try { Directory.CreateDirectory(_logsDir); } catch { }

            try
            {
                MigrateLegacyLogs(baseDir, currentDir, _logsDir);
            }
            catch { }

            if (_enabled)
            {
                try { Directory.CreateDirectory(_logsDir); } catch { }
            }
        }

        private static void MigrateLegacyLogs(string baseDir, string currentDir, string targetDir)
        {
            if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir)) return;

            MoveLogFilesFromDirectory(baseDir, targetDir);

            if (!string.Equals(currentDir, baseDir, StringComparison.OrdinalIgnoreCase))
            {
                MoveLogFilesFromDirectory(currentDir, targetDir);
            }

            var baseLegacyLogsDir = Path.Combine(baseDir, "logs");
            MoveLogFilesFromDirectory(baseLegacyLogsDir, targetDir);

            if (!string.Equals(currentDir, baseDir, StringComparison.OrdinalIgnoreCase))
            {
                var currentLegacyLogsDir = Path.Combine(currentDir, "logs");
                MoveLogFilesFromDirectory(currentLegacyLogsDir, targetDir);
            }
        }

        private static void MoveLogFilesFromDirectory(string sourceDir, string targetDir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir)) return;
                if (string.Equals(Path.GetFullPath(sourceDir), Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase)) return;

                foreach (var sourcePath in Directory.GetFiles(sourceDir, "*.log", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var fileName = Path.GetFileName(sourcePath);
                        var targetPath = Path.Combine(targetDir, fileName);

                        if (File.Exists(targetPath))
                        {
                            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                            var name = Path.GetFileNameWithoutExtension(fileName);
                            targetPath = Path.Combine(targetDir, $"{name}-{stamp}.log");
                        }

                        File.Move(sourcePath, targetPath);
                    }
                    catch { }
                }

                foreach (var legacyName in LegacyTxtLogNames)
                {
                    try
                    {
                        var sourcePath = Path.Combine(sourceDir, legacyName);
                        if (!File.Exists(sourcePath)) continue;

                        var targetPath = Path.Combine(targetDir, legacyName);
                        if (File.Exists(targetPath))
                        {
                            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                            var name = Path.GetFileNameWithoutExtension(legacyName);
                            targetPath = Path.Combine(targetDir, $"{name}-{stamp}.txt");
                        }

                        File.Move(sourcePath, targetPath);
                    }
                    catch { }
                }
            }
            catch { }
        }
        
        // Called by SettingsService after settings are loaded to sync logging state with user preference
        public static void SyncWithSettings()
        {
#if DEBUG
            try
            {
                var settings = Zer0Talk.Services.AppServices.Settings?.Settings;
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
        public static string Crypto => PathFor("crypto.log");
        public static string EncryptedChat => PathFor("enc_chat.log");
        public static string ThemeEngine => PathFor("theme_engine.log");
        public static string AccountCreation => PathFor("account_creation.log");

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
