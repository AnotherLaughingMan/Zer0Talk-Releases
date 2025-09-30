using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ZTalk.Models;
using P2PTalk.Utilities;

namespace P2PTalk.Services
{
    public sealed class LogMaintenanceService
    {
        private readonly SettingsService _settings;
        private readonly UpdateManager _updates;
        private readonly object _gate = new();
        private bool _timerRegistered;
        private string _lastSummary = "Log maintenance has not run yet.";
        private DateTime? _lastRunUtc;

        private const string TimerKey = "Logs.AutoTrim";
        private const int DefaultIntervalMs = 5 * 60 * 1000; // 5 minutes

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

            try { Directory.CreateDirectory(LoggingPaths.LogsDirectory); } catch { }

            var sb = new StringBuilder();
            sb.Append('[').Append(DateTime.Now.ToString("O")).Append("] ");
            sb.Append(reason).Append(" maintenance: ");

            var (trimmedLines, trimmedBytes) = TrimUiLog(maxLines, maxMegabytes);
            if (trimmedLines > 0)
            {
                sb.AppendFormat("trimmed {0} lines", trimmedLines);
            }
            else
            {
                sb.Append("no line trim");
            }

            sb.AppendFormat(", size delta {0:F1} KiB", trimmedBytes / 1024.0);

            int deleted = DeleteOldLogs(LoggingPaths.LogsDirectory, retentionDays);
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

            return sb.ToString();
        }

        private (int trimmedLines, long trimmedBytes) TrimUiLog(int maxLines, int maxMegabytes)
        {
            var path = LoggingPaths.UI;
            if (!File.Exists(path)) return (0, 0);

            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch { return (0, 0); }

            var total = lines.Length;
            var info = new FileInfo(path);
            long originalSize = info.Exists ? info.Length : 0;
            long maxBytes = (long)maxMegabytes * 1024L * 1024L;

            if (total <= maxLines && originalSize <= maxBytes)
                return (0, 0);

            int keepCount = Math.Min(maxLines, total);
            if (originalSize > maxBytes && total <= maxLines)
            {
                keepCount = Math.Max(1, maxLines / 2);
            }

            int skip = Math.Max(0, total - keepCount);
            var trimmed = lines.Skip(skip).ToArray();

            try { File.WriteAllLines(path, trimmed); }
            catch { return (0, 0); }

            long newSize;
            try { newSize = new FileInfo(path).Length; }
            catch { newSize = originalSize; }

            return (skip, Math.Max(0, originalSize - newSize));
        }

        private static int DeleteOldLogs(string logsDirectory, int retentionDays)
        {
            if (retentionDays <= 0) return 0;
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            int deleted = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(logsDirectory, "*.log", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.LastWriteTimeUtc < cutoff)
                        {
                            info.Delete();
                            deleted++;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return deleted;
        }

        private void Notify(string summary, DateTime? runUtc = null)
        {
            _lastSummary = summary;
            _lastRunUtc = runUtc ?? DateTime.UtcNow;
            WriteMaintenanceLog(summary);
            try { MaintenanceCompleted?.Invoke(summary); } catch { }
        }

        private static void WriteMaintenanceLog(string message)
        {
            if (!LoggingPaths.Enabled) return;
            try
            {
                File.AppendAllText(LoggingPaths.Maintenance, message + Environment.NewLine);
            }
            catch { }
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
#endif
    }
}
