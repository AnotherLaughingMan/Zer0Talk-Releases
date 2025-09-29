/*
    ErrorLogger: global exception logging to error.txt (working directory).
    - Thread-safe, non-blocking writes using a background queue and a single writer task.
    - Appends entries with timestamp, exception type, message, stack trace, and source.
*/
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace P2PTalk.Utilities
{
    public static class ErrorLogger
    {
        // Single-producer/multi-producer queue for log lines
        private static readonly BlockingCollection<string> _queue = new(new ConcurrentQueue<string>());
        private static readonly Lazy<Task> _writer = new(() => Task.Run(WriterLoop));
        private static volatile bool _initialized;

    private static string LogPath => LoggingPaths.Error;

        private static void EnsureStarted()
        {
            if (_initialized) return;
            _initialized = true;
            _ = _writer.Value; // start the writer loop lazily
        }

        // Public API: log an exception without throwing; safe on any thread
        public static void LogException(Exception? ex, string? source = null)
        {
            try
            {
                EnsureStarted();
                var sb = new StringBuilder();
                var now = DateTime.Now;
                sb.Append('[').Append(now.ToString("O")).Append("] ");
                if (!string.IsNullOrWhiteSpace(source)) sb.Append(source).Append(':').Append(' ');
                if (ex is null)
                {
                    sb.Append("<null exception>");
                }
                else
                {
                    sb.Append(ex.GetType().FullName).Append(':').Append(' ').Append(ex.Message);
                    if (ex.StackTrace is string st && st.Length > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine(st);
                    }
                    // Include inner exceptions, if any
                    var inner = ex.InnerException;
                    while (inner != null)
                    {
                        sb.AppendLine();
                        sb.Append("Inner: ").Append(inner.GetType().FullName).Append(':').Append(' ').Append(inner.Message);
                        if (!string.IsNullOrEmpty(inner.StackTrace))
                        {
                            sb.AppendLine();
                            sb.AppendLine(inner.StackTrace);
                        }
                        inner = inner.InnerException;
                    }
                }
                _queue.Add(sb.ToString());
            }
            catch { /* never throw from logger */ }
        }

        // Flush pending writes synchronously (best-effort). Call on process exit.
        public static void FlushNow()
        {
            try
            {
                EnsureStarted();
                // Give the writer a moment to drain
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (_queue.Count > 0 && sw.ElapsedMilliseconds < 250)
                {
                    Thread.Sleep(20);
                }
            }
            catch { }
        }

        // Background writer loop: single file append stream
        private static async Task WriterLoop()
        {
            try
            {
                // Open with append semantics; leave open and reuse to minimize contention
                using var fs = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
                foreach (var line in _queue.GetConsumingEnumerable())
                {
                    try
                    {
                        await writer.WriteLineAsync(line).ConfigureAwait(false);
                    }
                    catch { /* swallow write errors to avoid recursion */ }
                }
            }
            catch { /* failed to open log file; drop messages */ }
        }
    }
}
