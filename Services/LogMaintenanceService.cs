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

        private (int trimmedLines, long trimmedBytes) TrimLogFile(string path, int maxLines, long maxBytes)
        {
            try
            {
                if (!File.Exists(path)) return (0, 0);

                var lines = TryReadAllLines(path);
                if (lines is null) return (0, 0);

                var total = lines.Length;
                long originalSize = 0;
                try { originalSize = new FileInfo(path).Length; }
                catch { }

                // Special-case: for the primary error log, delete it entirely on maintenance
                // to avoid needing a temp file or replace operations. This is intended as
                // an emergency cleanup action when error.log grows or becomes locked.
                try
                {
                    var name = Path.GetFileName(path) ?? string.Empty;
                    if (name.Equals("error.log", StringComparison.OrdinalIgnoreCase))
                    {
                        const int attempts = 5;
                        bool deleted = false;
                        for (var i = 0; i < attempts; i++)
                        {
                            try
                            {
                                File.Delete(path);
                                deleted = true;
                                break;
                            }
                            catch (IOException) when (i < attempts - 1)
                            {
                                Thread.Sleep(100 * (i + 1));
                            }
                            catch (UnauthorizedAccessException) when (i < attempts - 1)
                            {
                                Thread.Sleep(100 * (i + 1));
                            }
                            catch (Exception ex)
                            {
                                WriteMaintenanceLog($"[DEBUG] Deletion attempt {i + 1}/{attempts} failed for {Path.GetFileName(path)}: {ex.Message}");
                                if (i == attempts - 1) deleted = false;
                                Thread.Sleep(100 * (i + 1));
                            }
                        }

                        if (deleted)
                        {
                            WriteMaintenanceLog($"[DEBUG] Deleted {Path.GetFileName(path)} during maintenance; freed {originalSize:N0} bytes");
                            return (total, originalSize);
                        }
                        else
                        {
                            WriteMaintenanceLog($"[DEBUG] Failed to delete {Path.GetFileName(path)} after attempts; falling back to trimming.");
                        }
                    }
                }
                catch { }

                if (total <= maxLines && (maxBytes <= 0 || originalSize <= maxBytes))
                    return (0, 0);

                int keepCount = total;
                
                // Apply line limit first
                if (total > maxLines)
                {
                    keepCount = Math.Max(1, Math.Min(maxLines, total));
                }
                
                // If still over size limit after line trimming, estimate further reduction needed
                if (maxBytes > 0 && originalSize > maxBytes)
                {
                    // Estimate bytes per line and calculate target line count for size limit
                    var avgBytesPerLine = (double)originalSize / total;
                    var targetLinesForSize = (int)(maxBytes / avgBytesPerLine);
                    
                    // Take the more restrictive of the two limits
                    keepCount = Math.Max(1, Math.Min(keepCount, targetLinesForSize));
                    
                    WriteMaintenanceLog($"[DEBUG] Size-based trim: {Path.GetFileName(path)} avgBytes/line={avgBytesPerLine:F1}, targetLines={targetLinesForSize}, finalKeep={keepCount}");
                }

                if (keepCount >= total)
                    return (0, 0);

                int skip = Math.Max(0, total - keepCount);
                var trimmed = lines.Skip(skip).ToArray();

                if (!TryWriteAllLines(path, trimmed))
                {
                    return (0, 0);
                }

                long newSize;
                try { newSize = new FileInfo(path).Length; }
                catch { newSize = originalSize; }

                return (skip, Math.Max(0, originalSize - newSize));
            }
            catch
            {
                return (0, 0);
            }
        }

        private static string[]? TryReadAllLines(string path)
        {
            const int attempts = 5; // Increased attempts for busy files like error.log
            for (var i = 0; i < attempts; i++)
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    var list = new List<string>();
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        list.Add(line);
                    }
                    return list.ToArray();
                }
                catch (IOException) when (i < attempts - 1)
                {
                    // Exponential backoff for busy files
                    Thread.Sleep(100 * (i + 1));
                }
                catch (UnauthorizedAccessException) when (i < attempts - 1)
                {
                    Thread.Sleep(100 * (i + 1));
                }
                catch (Exception ex)
                {
                    WriteMaintenanceLog($"[DEBUG] Read attempt {i + 1}/{attempts} failed for {Path.GetFileName(path)}: {ex.Message}");
                    if (i == attempts - 1) return null;
                    Thread.Sleep(100 * (i + 1));
                }
            }

            return null;
        }

        private static bool TryWriteAllLines(string path, string[] lines)
        {
            const int attempts = 5; // Increased attempts
            for (var i = 0; i < attempts; i++)
            {
                try
                {
                    // For error.log we avoid a separate temp file and write directly to the target
                    var fileName = Path.GetFileName(path) ?? string.Empty;
                    if (fileName.Equals("error.log", StringComparison.OrdinalIgnoreCase))
                    {
                        // Open with sharing that allows delete/rename while appending readers/writers exist
                        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                        {
                            for (var index = 0; index < lines.Length; index++)
                            {
                                writer.WriteLine(lines[index]);
                            }
                        }
                    }
                    else
                    {
                        // Write to temp file first, then replace - safer for large files
                        var tempPath = path + ".tmp";
                        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                        {
                            for (var index = 0; index < lines.Length; index++)
                            {
                                writer.WriteLine(lines[index]);
                            }
                        }
                        
                        // Atomic replace
                        File.Move(tempPath, path, overwrite: true);
                    }
                     return true;
                }
                catch (IOException) when (i < attempts - 1)
                {
                    Thread.Sleep(100 * (i + 1));
                }
                catch (UnauthorizedAccessException) when (i < attempts - 1)
                {
                    Thread.Sleep(100 * (i + 1));
                }
                catch (Exception ex)
                {
                    WriteMaintenanceLog($"[DEBUG] Write attempt {i + 1}/{attempts} failed for {Path.GetFileName(path)}: {ex.Message}");
                    if (i == attempts - 1) return false;
                    Thread.Sleep(100 * (i + 1));
                }
            }

            return false;
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

