using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Zer0Talk.Models;
using Zer0Talk.Utilities;

namespace Zer0Talk.Services
{
    public sealed partial class LogMaintenanceService
    {
        private readonly SettingsService _settings;
        private readonly UpdateManager _updates;
        private readonly object _gate = new();
        private bool _timerRegistered;
        private string _lastSummary = "Log maintenance has not run yet.";
        private DateTime? _lastRunUtc;

        private const string TimerKey = "Logs.AutoTrim";
        private const int DefaultIntervalMs = 5 * 60 * 1000; // 5 minutes
        private const int MaxLogFilesToKeep = 120;

        public LogMaintenanceService(SettingsService settings, UpdateManager updates)
        {
            _settings = settings;
            _updates = updates;
        }

        public event Action<string>? MaintenanceCompleted;

        public string LastSummary => _lastSummary;
        public DateTime? LastRunUtc => _lastRunUtc;

        public void TryStart()
        {
#if DEBUG
            lock (_gate)
            {
                if (_timerRegistered) return;
                _updates.RegisterBgInterval(TimerKey, DefaultIntervalMs, RunScheduled);
                _timerRegistered = true;
            }
            _ = Task.Run(RunScheduled);
#endif
        }

        public void Stop()
        {
#if DEBUG
            lock (_gate)
            {
                if (!_timerRegistered) return;
                _updates.UnregisterBg(TimerKey);
                _timerRegistered = false;
            }
#endif
        }

        public string RunMaintenanceNow(string reason = "manual")
        {
#if DEBUG
            try
            {
                if (!LoggingPaths.Enabled)
                {
                    var message = "Logging disabled; maintenance skipped.";
                    Notify(message, DateTime.UtcNow);
                    return message;
                }

                var snapshot = _settings.Settings;
                var summary = RunMaintenanceCore(snapshot, reason);
                Notify(summary);
                return summary;
            }
            catch (Exception ex)
            {
                var error = $"Log maintenance failed: {ex.Message}";
                Notify(error, DateTime.UtcNow);
                return error;
            }
#else
            return "Log maintenance is only available in debug builds.";
#endif
        }
        
        public string TrimSingleLogFile(string logFilePath, string reason = "manual-single")
        {
#if DEBUG
            try
            {
                if (!LoggingPaths.Enabled)
                    return "Logging disabled; trim skipped.";
                    
                if (!File.Exists(logFilePath))
                    return $"File not found: {Path.GetFileName(logFilePath)}";
                
                var snapshot = _settings.Settings;
                var maxLines = Clamp(snapshot.DebugUiLogMaxLines, 100, 20000);
                var maxMegabytes = Clamp(snapshot.DebugLogMaxMegabytes, 1, 512);
                long maxBytes = (long)Math.Max(1, maxMegabytes) * 1024L * 1024L;
                
                var (linesRemoved, bytesReduced) = TrimLogFile(logFilePath, maxLines, maxBytes);
                
                var fileName = Path.GetFileName(logFilePath);
                var summary = linesRemoved > 0 || bytesReduced > 0
                    ? $"Trimmed {fileName}: removed {linesRemoved} lines, {bytesReduced:N0} bytes"
                    : $"No trim needed for {fileName}";
                    
                WriteMaintenanceLog($"[{DateTime.Now:O}] {reason} single-file trim: {summary}");
                return summary;
            }
            catch (Exception ex)
            {
                var error = $"Single file trim failed: {ex.Message}";
                WriteMaintenanceLog($"[{DateTime.Now:O}] {reason} single-file error: {error}");
                return error;
            }
#else
            return "Log maintenance is only available in debug builds.";
#endif
        }

#if DEBUG
        private void RunScheduled()
        {
            try
            {
                if (!LoggingPaths.Enabled) return;
                var snapshot = _settings.Settings;
                if (!snapshot.EnableDebugLogAutoTrim) return;
                var summary = RunMaintenanceCore(snapshot, "auto");
                Notify(summary);
            }
            catch { }
        }

        private string RunMaintenanceCore(AppSettings snapshot, string reason)
        {
            var maxLines = Clamp(snapshot.DebugUiLogMaxLines, 100, 20000);
            var retentionDays = Clamp(snapshot.DebugLogRetentionDays, 0, 30);
            var maxMegabytes = Clamp(snapshot.DebugLogMaxMegabytes, 1, 512);
            
            // Debug logging to diagnose why error.log isn't being trimmed
            WriteMaintenanceLog($"[DEBUG] Starting maintenance - maxLines={maxLines}, maxMB={maxMegabytes}, retention={retentionDays}d");

            try { Directory.CreateDirectory(LoggingPaths.LogsDirectory); } catch { }

            var sb = new StringBuilder();
            sb.Append('[').Append(DateTime.Now.ToString("O")).Append("] ");
            sb.Append(reason).Append(" maintenance: ");

            var logsDirectory = LoggingPaths.LogsDirectory;
            var files = EnumerateLogFiles(logsDirectory).ToArray();
            
            // Debug: log which files were found
            WriteMaintenanceLog($"[DEBUG] Found {files.Length} log files in {logsDirectory}");
            foreach (var f in files)
            {
                var size = File.Exists(f) ? new FileInfo(f).Length : 0;
                WriteMaintenanceLog($"[DEBUG] File: {Path.GetFileName(f)} ({size:N0} bytes)");
            }

            var trimmedFiles = 0;
            var trimmedLines = 0;
            long trimmedBytes = 0;
            long maxBytes = (long)Math.Max(1, maxMegabytes) * 1024L * 1024L;

            foreach (var file in files)
            {
                var (linesRemoved, bytesReduced) = TrimLogFile(file, maxLines, maxBytes);
                WriteMaintenanceLog($"[DEBUG] {Path.GetFileName(file)}: removed {linesRemoved} lines, {bytesReduced:N0} bytes");
                if (linesRemoved > 0 || bytesReduced > 0)
                {
                    trimmedFiles++;
                    trimmedLines += Math.Max(0, linesRemoved);
                    trimmedBytes += Math.Max(0, bytesReduced);
                }
            }

            if (trimmedFiles > 0)
            {
                sb.AppendFormat("trimmed {0} file{1}", trimmedFiles, trimmedFiles == 1 ? string.Empty : "s");
                if (trimmedLines > 0)
                {
                    sb.AppendFormat(" ({0} line{1} removed)", trimmedLines, trimmedLines == 1 ? string.Empty : "s");
                }
            }
            else
            {
                sb.Append("no trims");
            }

            sb.AppendFormat(", size delta {0:F1} KiB", trimmedBytes / 1024.0);

            int deleted = DeleteOldLogs(logsDirectory, retentionDays);
            if (retentionDays > 0)
            {
                sb.AppendFormat(", deleted {0} expired file{1} (>{2} day{3})",
                    deleted,
                    deleted == 1 ? string.Empty : "s",
                    retentionDays,
                    retentionDays == 1 ? string.Empty : "s");
            }
            else
            {
                sb.Append(", retention off");
            }

            var oldestPruned = PruneOldestLogsByCount(logsDirectory, MaxLogFilesToKeep);
            if (oldestPruned > 0)
            {
                sb.AppendFormat(", pruned {0} oldest log file{1} (cap {2})",
                    oldestPruned,
                    oldestPruned == 1 ? string.Empty : "s",
                    MaxLogFilesToKeep);
            }

            return sb.ToString();
        }

        private static IEnumerable<string> EnumerateLogFiles(string logsDirectory)
        {
            if (!Directory.Exists(logsDirectory))
                return Enumerable.Empty<string>();

            try
            {
                return Directory.EnumerateFiles(logsDirectory, "*", SearchOption.TopDirectoryOnly)
                    .Where(IsLogFile)
                    .ToArray();
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }

        private static bool IsLogFile(string path)
        {
            try
            {
                var ext = Path.GetExtension(path);
                if (string.IsNullOrWhiteSpace(ext)) return false;
                return ext.Equals(".log", StringComparison.OrdinalIgnoreCase) ||
                       ext.Equals(".txt", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

#endif
    }
}
