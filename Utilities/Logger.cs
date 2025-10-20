/*
    Lightweight logging helper: timestamps, levels, and optional file sinks.
    Supports both structured (JSON) and human-readable formats.
*/
using System;
using System.IO;
using System.Text.Json;

namespace ZTalk.Utilities
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Source { get; set; }
        public string? Category { get; set; }
    }

    public static class Logger
    {
        // Raised whenever a line is logged; UI can subscribe for live logs.
        public static event Action<string>? LineLogged;
        
        // Toggle between structured (JSON) and simple text format
        public static bool UseStructuredLogging { get; set; } = true;

        public static void Log(string message) => Log(message, LogLevel.Info);

        public static void Log(string message, LogLevel level, string? source = null, string? categoryOverride = null)
        {
            var timestamp = DateTime.Now;
            var entry = new LogEntry
            {
                Timestamp = timestamp,
                Level = level,
                Message = message,
                Source = source
            };

            // Determine category (network vs app) for routing
            var category = !string.IsNullOrEmpty(categoryOverride)
                ? categoryOverride.ToLowerInvariant()
                : DetermineCategory(message, source);
            entry.Category = category;

            // Format for console and UI
            var displayLine = UseStructuredLogging 
                ? FormatStructured(entry)
                : FormatSimple(entry);

            try { Console.WriteLine(displayLine); } catch { /* ignore */ }

            var routeToNetwork = category == "network";

            try
            {
                if (LoggingPaths.Enabled)
                {
                    try
                    {
                        // Append using a file stream that allows read/write/delete sharing so maintenance can rotate/replace files.
                        static void SafeAppend(string path, string text)
                        {
                            try
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                                using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                                using var sw = new StreamWriter(fs, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
                                sw.Write(text);
                            }
                            catch { /* best-effort */ }
                        }

                        var logContent = UseStructuredLogging 
                            ? JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = false }) + Environment.NewLine
                            : displayLine + Environment.NewLine;

                        var targetPath = GetLogFilePath(category, routeToNetwork);
                        SafeAppend(targetPath, logContent);
                    }
                    catch { /* swallow logging errors */ }
                }
            }
            catch { /* ignore logging failures */ }
            try { LineLogged?.Invoke(displayLine); } catch { /* best-effort */ }
        }

        /// <summary>
        /// Log a network-related message to the dedicated network heartbeat log.
        /// This intentionally does not append to the primary app log to reduce noise there.
        /// </summary>
        public static void NetworkLog(string message) => Log(message, LogLevel.Info, "Network", "network");

        public static void Debug(string message, string? source = null, string? categoryOverride = null) => Log(message, LogLevel.Debug, source, categoryOverride);
        public static void Info(string message, string? source = null, string? categoryOverride = null) => Log(message, LogLevel.Info, source, categoryOverride);
        public static void Warning(string message, string? source = null, string? categoryOverride = null) => Log(message, LogLevel.Warning, source, categoryOverride);
        public static void Error(string message, string? source = null, string? categoryOverride = null) => Log(message, LogLevel.Error, source, categoryOverride);

        private static string FormatStructured(LogEntry entry)
        {
            // Human-readable display format with structure indicators
            var levelTag = entry.Level switch
            {
                LogLevel.Debug => "[DBG]",
                LogLevel.Info => "[INF]",
                LogLevel.Warning => "[WRN]",
                LogLevel.Error => "[ERR]",
                _ => "[LOG]"
            };
            var sourceTag = !string.IsNullOrEmpty(entry.Source) ? $" [{entry.Source}]" : "";
            return $"{levelTag}{sourceTag} {entry.Timestamp:O}: {entry.Message}";
        }

        private static string FormatSimple(LogEntry entry)
        {
            var levelTag = entry.Level switch
            {
                LogLevel.Debug => "[DBG]",
                LogLevel.Info => "[LOG]",
                LogLevel.Warning => "[WRN]",
                LogLevel.Error => "[ERR]",
                _ => "[LOG]"
            };
            return $"{levelTag} {entry.Timestamp:O}: {entry.Message}";
        }

        private static string DetermineCategory(string message, string? source)
        {
            // Check source first
            if (!string.IsNullOrEmpty(source))
            {
                var s = source.ToLowerInvariant();
                if (s.Contains("network") || s.Contains("nat") || s.Contains("discovery") || s.Contains("peer"))
                    return "network";
            }

            // Heuristic routing: if the message contains network-specific tokens
            if (!string.IsNullOrEmpty(message))
            {
                var m = message.ToLowerInvariant();
                if (m.Contains("udp") || m.Contains("nat") || m.Contains("handshake") || 
                    m.Contains("session") || m.Contains("beacon") || m.Contains("relay") || 
                    m.Contains("upnp") || m.Contains("bind") || m.Contains("connect") || 
                    m.Contains("listen"))
                    return "network";
            }

            // Check stack trace for Services namespace
            try
            {
                var st = new System.Diagnostics.StackTrace();
                var fc = Math.Min(st.FrameCount, 10);
                for (int i = 1; i < fc; ++i)
                {
                    var meth = st.GetFrame(i)?.GetMethod();
                    var dt = meth?.DeclaringType;
                    if (dt != null)
                    {
                        var ns = dt.Namespace ?? string.Empty;
                        if (ns.StartsWith("ZTalk.Services", StringComparison.OrdinalIgnoreCase))
                            return "network";

                        var tn = dt.Name ?? string.Empty;
                        if (tn.Contains("Network", StringComparison.OrdinalIgnoreCase) ||
                            tn.Contains("Nat", StringComparison.OrdinalIgnoreCase) ||
                            tn.Contains("Discovery", StringComparison.OrdinalIgnoreCase) ||
                            tn.Contains("Peer", StringComparison.OrdinalIgnoreCase))
                            return "network";
                    }
                }
            }
            catch { }

            return "app";
        }

        private static string GetLogFilePath(string category, bool routeToNetwork)
        {
            if (routeToNetwork)
            {
                return LoggingPaths.NetworkHeartbeat;
            }

            return category switch
            {
                "theme" => LoggingPaths.ThemeEngine,
                "performance" => LoggingPaths.Performance,
                "startup" => LoggingPaths.Startup,
                "error" => LoggingPaths.Error,
                "ui" => LoggingPaths.UI,
                "interaction" => LoggingPaths.Interaction,
                "maintenance" => LoggingPaths.Maintenance,
                "audio" => LoggingPaths.Audio,
                "crypto" => LoggingPaths.Crypto,
                "encrypted" => LoggingPaths.EncryptedChat,
                _ => LoggingPaths.App
            };
        }
    }
}
