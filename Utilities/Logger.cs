/*
    Lightweight logging helper: timestamps, levels, and optional file sinks.
*/
using System;
using System.IO;

namespace ZTalk.Utilities
{
    public static class Logger
    {
        // Raised whenever a line is logged; UI can subscribe for live logs.
        public static event Action<string>? LineLogged;
        public static void Log(string message)
        {
            // TODO: Implement structured logging
            var line = $"[LOG] {DateTime.Now:O}: {message}";
            try { Console.WriteLine(line); } catch { /* ignore */ }

            // Heuristic routing: if the caller is inside the Services namespace (common network services)
            // or the message contains network-specific tokens, write to the network heartbeat log instead
            // of the primary app log to reduce noise in app.log.
            var routeToNetwork = false;
            try
            {
                // Quick token check to catch obvious network messages
                if (!string.IsNullOrEmpty(message))
                {
                    var m = message.ToLowerInvariant();
                    if (m.Contains("udp") || m.Contains("nat") || m.Contains("handshake") || m.Contains("session") || m.Contains("beacon") || m.Contains("relay") || m.Contains("upnp") || m.Contains("bind") || m.Contains("connect") || m.Contains("listen"))
                        routeToNetwork = true;
                }

                if (!routeToNetwork)
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
                            {
                                routeToNetwork = true;
                                break;
                            }
                            var tn = dt.Name ?? string.Empty;
                            if (tn.IndexOf("Network", StringComparison.OrdinalIgnoreCase) >= 0 || tn.IndexOf("Nat", StringComparison.OrdinalIgnoreCase) >= 0 || tn.IndexOf("Discovery", StringComparison.OrdinalIgnoreCase) >= 0 || tn.IndexOf("Peer", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                routeToNetwork = true;
                                break;
                            }
                        }
                    }
                }
            }
            catch { }

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

                        if (routeToNetwork)
                        {
                            SafeAppend(LoggingPaths.NetworkHeartbeat, line + Environment.NewLine);
                        }
                        else
                        {
                            SafeAppend(LoggingPaths.App, line + Environment.NewLine);
                        }
                    }
                    catch { /* swallow logging errors */ }
                }
            }
            catch { /* ignore logging failures */ }
            try { LineLogged?.Invoke(line); } catch { /* best-effort */ }
        }

        /// <summary>
        /// Log a network-related message to the dedicated network heartbeat log.
        /// This intentionally does not append to the primary app log to reduce noise there.
        /// </summary>
        public static void NetworkLog(string message)
        {
            var line = $"[NET] {DateTime.Now:O}: {message}";
            try { Console.WriteLine(line); } catch { }
            try
            {
                if (LoggingPaths.Enabled)
                    File.AppendAllText(LoggingPaths.NetworkHeartbeat, line + Environment.NewLine);
            }
            catch { }
        }
    }
}
