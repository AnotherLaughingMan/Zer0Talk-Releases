using System;
using System.Linq;
using System.IO;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace ZTalk.Services
{
    // Enforces a lower UI pulse rate when the app is unfocused or minimized, restoring when foregrounded.
    public static class FocusFramerateService
    {
        private static bool _initialized;
        private static DispatcherTimer? _poll; // lightweight scan to discover new windows
        private static bool _isForeground = true;
        private static int _appliedIntervalMs = -1;
        private static readonly System.Collections.Generic.HashSet<Window> _tracked = new();

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                // Initial attach to existing windows
                AttachToExistingWindows();
                // Light scan every 3s to attach to newly opened windows (no heavy work here)
                _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                _poll.Tick += (_, __) => AttachToExistingWindows();
                _poll.Start();
            }
            catch { }
        }

        public static void ApplyCurrentPolicy()
        {
            try { SampleAndApply(forceApply: true); } catch { }
        }

        private static void SampleAndApply(bool forceApply = false)
        {
            try
            {
                bool anyActive = ComputeAnyActive();
                if (!forceApply && anyActive == _isForeground && _appliedIntervalMs > 0)
                    return;
                _isForeground = anyActive;
                var s = AppServices.Settings.Settings;
                int interval;
                if (_isForeground)
                {
                    var fps = Math.Max(0, s.FpsThrottle);
                    interval = fps <= 0 ? 16 : Math.Max(5, 1000 / Math.Max(1, fps));
                    AppServices.Updates.UpdateUiInterval("App.UI.Pulse", interval);
                    WritePerfLog($"[Foreground] Restored framerate: FPS={fps} (interval {interval}ms)");
                }
                else
                {
                    var bg = Math.Max(1, s.BackgroundFramerateFps);
                    interval = Math.Max(5, 1000 / bg);
                    AppServices.Updates.UpdateUiInterval("App.UI.Pulse", interval);
                    WritePerfLog($"[Background] Minimum framerate set to {bg} FPS (interval {interval}ms)");
                }
                _appliedIntervalMs = interval;
            }
            catch { }
        }

        private static bool ComputeAnyActive()
        {
            var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (desktop?.Windows == null) return true; // be conservative
            foreach (var w in desktop.Windows.OfType<Window>())
            {
                try
                {
                    if (w.IsActive && w.WindowState != WindowState.Minimized)
                        return true;
                }
                catch { }
            }
            return false;
        }

        private static void AttachToExistingWindows()
        {
            try
            {
                var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                if (desktop?.Windows == null) return;
                foreach (var w in desktop.Windows.OfType<Window>())
                {
                    if (_tracked.Contains(w)) continue;
                    try
                    {
                        _tracked.Add(w);
                        w.Opened += OnWindowChanged;
                        w.Closed += OnWindowClosed;
                        w.Activated += OnWindowChanged;
                        w.Deactivated += OnWindowChanged;
                        w.PropertyChanged += OnWindowPropertyChanged;
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void OnWindowClosed(object? sender, EventArgs e)
        {
            try
            {
                if (sender is Window w)
                {
                    w.Opened -= OnWindowChanged;
                    w.Closed -= OnWindowClosed;
                    w.Activated -= OnWindowChanged;
                    w.Deactivated -= OnWindowChanged;
                    w.PropertyChanged -= OnWindowPropertyChanged;
                    _tracked.Remove(w);
                }
                SampleAndApply();
            }
            catch { }
        }

        private static void OnWindowChanged(object? sender, EventArgs e)
        {
            try { SampleAndApply(); } catch { }
        }

    private static void OnWindowPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
        {
            try
            {
                if (e.Property == Window.WindowStateProperty)
                {
                    SampleAndApply();
                }
            }
            catch { }
        }

        private static bool PerfLoggingEnabled
        {
            get
            {
                try
                {
                    var baseDir = AppContext.BaseDirectory;
                    var flagPath = System.IO.Path.Combine(baseDir, "logs", "performance.logging.enabled");
                    return System.IO.File.Exists(flagPath);
                }
                catch { return false; }
            }
        }
        private static void WritePerfLog(string line)
        {
            try
            {
                if (!PerfLoggingEnabled || !Utilities.LoggingPaths.Enabled) return;
                var path = Utilities.LoggingPaths.Performance;
                System.IO.File.AppendAllText(path, $"{DateTime.Now:O} {line}{Environment.NewLine}");
            }
            catch { }
        }

        // Manual test hook for logging (no-ops if flag not enabled)
        public static void TriggerPerformanceLogTest(string note = "ManualTest")
        {
            try { WritePerfLog($"[Test] {note}"); } catch { }
        }
    }
}
