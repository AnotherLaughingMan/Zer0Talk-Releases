// Centralized logging path management and enable/disable policy.
// Debug builds: logging enabled by default and all logs go to %APPDATA%/Zer0Talk/Logs.
// Release builds: logging disabled unless explicitly enabled via env var ZER0TALK_ENABLE_LOGS=1
// or presence of an enable-logs.flag file next to the executable.
using System;
using System.IO;
using System.Linq;

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

            var movedFiles = 0;
            var touchedDirs = 0;

            movedFiles += MoveLogFilesFromDirectory(baseDir, targetDir, ref touchedDirs);

            if (!string.Equals(currentDir, baseDir, StringComparison.OrdinalIgnoreCase))
            {
                movedFiles += MoveLogFilesFromDirectory(currentDir, targetDir, ref touchedDirs);
            }

            var baseLegacyLogsDir = Path.Combine(baseDir, "logs");
            movedFiles += MoveLogFilesFromDirectory(baseLegacyLogsDir, targetDir, ref touchedDirs);

            if (!string.Equals(currentDir, baseDir, StringComparison.OrdinalIgnoreCase))
            {
                var currentLegacyLogsDir = Path.Combine(currentDir, "logs");
                movedFiles += MoveLogFilesFromDirectory(currentLegacyLogsDir, targetDir, ref touchedDirs);
            }

            // Sweep typical local build-output ancestors to eliminate stale bin/Debug/logs folders.
            var (sweptDirs, sweptFiles) = SweepBuildOutputLegacyLogs(baseDir, targetDir);
            movedFiles += sweptFiles;
            touchedDirs += sweptDirs;

            if (movedFiles > 0)
            {
                try
                {
                    var maintenancePath = Path.Combine(targetDir, "maintenance.log");
                    var line = $"[{DateTime.UtcNow:O}] startup legacy-log sweep: touchedDirs={touchedDirs}, movedFiles={movedFiles}, baseDir={baseDir}";
                    TryWrite(maintenancePath, line + Environment.NewLine);
                }
                catch { }
            }
        }

        private static (int touchedDirs, int movedFiles) SweepBuildOutputLegacyLogs(string baseDir, string targetDir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(baseDir) || string.IsNullOrWhiteSpace(targetDir)) return (0, 0);

                var visited = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var current = baseDir;
                var touchedDirs = 0;
                var movedFiles = 0;

                // Walk a few parent levels from AppContext.BaseDirectory
                // (for example .../bin/Debug/net9.0 -> .../bin/Debug -> .../bin -> project root).
                for (var i = 0; i < 6 && !string.IsNullOrWhiteSpace(current); i++)
                {
                    if (!visited.Add(current)) break;

                    var logsDir = Path.Combine(current, "logs");
                    movedFiles += MoveLogFilesFromDirectory(logsDir, targetDir, ref touchedDirs);
                    TryDeleteDirectoryIfEmpty(logsDir);

                    var parent = Directory.GetParent(current);
                    current = parent?.FullName ?? string.Empty;
                }

                return (touchedDirs, movedFiles);
            }
            catch { }

            return (0, 0);
        }

        private static int MoveLogFilesFromDirectory(string sourceDir, string targetDir, ref int touchedDirs)
        {
            var movedCount = 0;
            try
            {
                if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir)) return 0;
                if (string.Equals(Path.GetFullPath(sourceDir), Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase)) return 0;

                touchedDirs++;

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
                        movedCount++;
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
                        movedCount++;
                    }
                    catch { }
                }
            }
            catch { }

            return movedCount;
        }

        private static void TryDeleteDirectoryIfEmpty(string dirPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dirPath) || !Directory.Exists(dirPath)) return;
                if (Directory.EnumerateFileSystemEntries(dirPath).Any()) return;
                Directory.Delete(dirPath, recursive: false);
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
        public static string Settings => PathFor("settings.log");
        public static string Layout => PathFor("layout.log");
        public static string MarkdownErrors => PathFor("markdown-errors.log");
        public static string MarkdownTrace => PathFor("markdown-trace.log");

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
