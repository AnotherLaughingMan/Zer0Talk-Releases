using System;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Zer0Talk.Services;
using Zer0Talk.Utilities;
using Zer0Talk.ViewModels;

namespace Zer0Talk.Views;

public partial class MonitoringWindow : Window, IDisposable
{
    private const int SessionSyntheticRateKey = -1;
    // Scoped refresh: MonitoringWindow owns its interval; event-loop on a dedicated thread with cancellation.
    private int _intervalMs;
    private System.Collections.Generic.Dictionary<int, (long In, long Out, DateTime AtUtc)> _lastTotals = new();
    private System.Collections.Generic.Dictionary<int, (double In, double Out)> _smoothedRates = new();
    private (long In, long Out, DateTime AtUtc) _lastSessionTotals;
    private bool _skipNextRateSample = true;
    private System.Threading.CancellationTokenSource? _cts;
    // Flag to stop UI updates during teardown to avoid contention and crashes
    private volatile bool _isClosing;
    // Auto-scroll state for diagnostics log
    private bool _autoScrollLog = true;

    public MonitoringWindow()
    {
        InitializeComponent();
        // Restore saved window geometry ASAP so it applies before first render
        try
        {
            var s = AppServices.Settings.Settings.MonitoringWindow;
            RestoreLayoutFromCacheOrSettings(s);
            // Also restore on Opened in case platform defers bounds until then
            this.Opened += (_, __) => RestoreLayoutFromCacheOrSettings(s);
            // Non-blocking close: cancel background loop, then persist geometry to cache (no runtime writes)
            this.Closing += OnMonitoringWindowClosing;
        }
        catch { }
        if (DataContext is MonitoringViewModel vm)
        {
            // Load persisted UI preferences (e.g., log font size)
            vm.LoadPersistedPreferences();
            // Load persisted interval with a safe default to prevent division-by-zero during first ticks
            _intervalMs = AppServices.Settings.Settings.MonitoringIntervalMs;
            if (_intervalMs <= 0) _intervalMs = 500;

            // Subscribe BEFORE setting initial index so the initial mapping also restarts the loop
            vm.OnIntervalChanged += idx =>
            {
                _intervalMs = MonitoringViewModel.IndexToMs(idx);
                if (_intervalMs <= 0) _intervalMs = 500;
                // Reset deltas so next tick computes clean rates at the new cadence
                _lastTotals.Clear();
                _smoothedRates.Clear();
                _lastSessionTotals = default;
                _skipNextRateSample = true;
                // Restart background loop with new interval
                RestartLoop();
                // Persist selection
                AppServices.Settings.Settings.MonitoringIntervalMs = _intervalMs;
                _ = System.Threading.Tasks.Task.Run(() => AppServices.Settings.Save(AppServices.Passphrase));
            };

            // Map persisted milliseconds to the corresponding index (this will trigger OnIntervalChanged)
            vm.SetIntervalFromMilliseconds(_intervalMs);
        }
        else
        {
            // Fallback: ensure a loop still runs with a sane default
            _intervalMs = _intervalMs <= 0 ? 500 : _intervalMs;
            RestartLoop();
        }
        // If DataContext path already restarted the loop, no-op; otherwise ensure it starts once
        if (_cts is null)
        {
            RestartLoop();
        }

        // Surface network stack logs (SSDP/UPnP/etc.) in the Monitoring log for diagnostics
        try { Zer0Talk.Utilities.Logger.LineLogged += OnLoggerLine; } catch { }

        // EVENT-DRIVEN STATUS UPDATES
        // Switch NAT/ports/discovery and diagnostics summary to event-driven updates instead of the refresh loop.
        // This avoids polling for largely static values and reduces UI churn.
        try
        {
            // NAT state changes (mapping/discovery/punch/etc.)
            AppServices.Nat.Changed += OnStatusEvent;
            // Discovery state changes (attempt cycles, completed/failed)
            AppServices.Discovery.Changed += OnStatusEvent;
            // Network listener port/bind changes
            AppServices.Network.ListeningChanged += OnNetworkListeningChanged;
            // Peer list changes are a good proxy for discovery/session activity
            AppServices.Events.PeersChanged += OnStatusEvent;
            // Handshake completion indicates diagnostics counters changed; update summary
            AppServices.Network.HandshakeCompleted += OnHandshakeCompleted;
            // UI pulse is a lightweight app-wide event; use it to gently refresh diagnostics summary without a local timer
            AppServices.Events.UiPulse += OnUiPulse;
        }
        catch { }

        // Seed UI once on open so the panel reflects current status immediately
        try
        {
            OnStatusEvent();
            if (DataContext is MonitoringViewModel seedVm)
            {
                var snap = AppServices.Network.GetDiagnosticsSnapshot();
                Dispatcher.UIThread.Post(() => seedVm.UpdateDiagnostics(snap));
            }
        }
        catch { }

#if DEBUG
        try
        {
            // Add Debug-only checkpoint buttons into sidebar stack panel
            var panel = this.FindControl<StackPanel>("DebugCheckpointStack");
            if (panel != null)
            {
                // Save Checkpoint: Down arrow with line underneath
                var saveBtn = new Button { Classes = { "icon-button" } };
                ToolTip.SetTip(saveBtn, "Save Checkpoint - Saves the current NAT discovery state for debugging");
                saveBtn.Bind(Button.CommandProperty, new Avalonia.Data.Binding("SaveCheckpointCommand"));
                
                var saveCanvas = new Canvas { Width = 26, Height = 26 };
                var savePath = new Avalonia.Controls.Shapes.Path
                {
                    Stroke = Brushes.White,
                    StrokeThickness = 2.0,
                    Data = Avalonia.Media.Geometry.Parse("M13,2 L13,16 M8,11 L13,16 L18,11 M5,20 L21,20")
                };
                saveCanvas.Children.Add(savePath);
                saveBtn.Content = saveCanvas;
                
                // Restore Checkpoint: Up arrow with line underneath
                var restoreBtn = new Button { Classes = { "icon-button" } };
                ToolTip.SetTip(restoreBtn, "Restore Checkpoint - Restores a previously saved NAT discovery state for debugging");
                restoreBtn.Bind(Button.CommandProperty, new Avalonia.Data.Binding("RestoreCheckpointCommand"));
                
                var restoreCanvas = new Canvas { Width = 26, Height = 26 };
                var restorePath = new Avalonia.Controls.Shapes.Path
                {
                    Stroke = Brushes.White,
                    StrokeThickness = 2.0,
                    Data = Avalonia.Media.Geometry.Parse("M13,18 L13,4 M8,9 L13,4 L18,9 M5,20 L21,20")
                };
                restoreCanvas.Children.Add(restorePath);
                restoreBtn.Content = restoreCanvas;
                
                panel.Children.Add(saveBtn);
                panel.Children.Add(restoreBtn);
            }
        }
        catch { }
#endif

        // Hook diagnostics log scroll behavior.
        // We detect if the user has scrolled away from the bottom to disable auto-scroll.
        // Pressing End key will snap to bottom and re-enable auto-scroll.
        try
        {
            var scroll = this.FindControl<ScrollViewer>("LogScroll");
            if (scroll != null)
            {
                scroll.PropertyChanged += (_, e) =>
                {
                    if (e.Property == ScrollViewer.OffsetProperty)
                    {
                        var atBottom = Math.Abs(scroll.Offset.Y + scroll.Viewport.Height - scroll.Extent.Height) < 2.0;
                        _autoScrollLog = atBottom;
                    }
                };
                this.AddHandler(KeyDownEvent, (s, e) =>
                {
                    if (e.Key == Key.End)
                    {
                        try
                        {
                            scroll.ScrollToEnd();
                            _autoScrollLog = true;
                            e.Handled = true;
                        }
                        catch { }
                    }
                }, RoutingStrategies.Tunnel);
            }
        }
        catch { }
    }

    private void OnLoggerLine(string line)
    {
        try
        {
            if (_isClosing) return;
            if (DataContext is not MonitoringViewModel vm) return;
            // Only forward relevant discovery/NAT lines to avoid flooding
            var msg = line ?? string.Empty;
            if (msg.Contains("SSDP:", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("UPnP", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("gateway", StringComparison.OrdinalIgnoreCase))
            {
                // Trim the leading [LOG] timestamp for compactness
                var idx = msg.IndexOf(": ", StringComparison.Ordinal);
                var compact = idx >= 0 && idx + 2 < msg.Length ? msg[(idx + 2)..] : msg;
                Dispatcher.UIThread.Post(() => vm.AppendLog(compact));
            }
        }
        catch { }
    }

    // [DRAGBAR] Begin moving window on left-click; ignore clicks on interactive controls.
    private void DragBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed) return;
            if (e.Source is Avalonia.Visual c)
            {
                if (c is Button || c.FindAncestorOfType<Button>() != null) return;
                if (c is TextBox || c.FindAncestorOfType<TextBox>() != null) return;
                if (c is ComboBox || c.FindAncestorOfType<ComboBox>() != null) return;
            }
            if (WindowDragHelper.TryBeginMoveDrag(this, e))
            {
                e.Handled = true;
            }
        }
        catch { }
    }

    // [LAYOUT] Restore via cache with fallback to settings; ensure on-screen.
    private void RestoreLayoutFromCacheOrSettings(Zer0Talk.Models.WindowStateSettings s)
    {
        double w = Width, h = Height; var pos = Position; bool haveState = false;
        try
        {
            var cached = LayoutCache.Load("MonitoringWindow");
            if (cached is not null)
            {
                if (cached.Width is double cw && cw > 0) w = cw;
                if (cached.Height is double ch && ch > 0) h = ch;
                if (cached.X is double cx && cached.Y is double cy) pos = new PixelPoint((int)cx, (int)cy);
                if (cached.State is int cst) { WindowState = (WindowState)cst; haveState = true; }
            }
            if (!haveState && s.State is int st)
            {
                WindowState = (WindowState)st;
            }
        }
        catch { }
        WindowBoundsHelper.EnsureVisible(this, ref w, ref h, ref pos);
        Width = w; Height = h; Position = pos;
    }

    private void Tick()
    {
        try
        {
            if (_isClosing) return; // avoid any work while the window is closing
            if (DataContext is not MonitoringViewModel vm) return;
            // REFRESH-DRIVEN: Only handle blink animation here. Status itself updates via event handlers.
            if (vm.NatIndicatorBlink)
                vm.NatIndicatorOpacity = vm.NatIndicatorOpacity < 0.6 ? 1.0 : 0.3;
            else if (vm.NatIndicatorOpacity != 1.0) vm.NatIndicatorOpacity = 1.0;

            // Rates
            // Pull diagnostics snapshot from the persistent NetworkService store; this call is safe and cheap
            // and data is continuously updated by the networking stack even when no peers are active.
            var snapshot = AppServices.Network.GetPortStatsSnapshot();
            // If we momentarily have no keys (e.g., early startup or a transient service switch), preserve
            // last-known ports as a zero-baseline so the chart/traffic dots don't "disappear".
            if (snapshot.Count == 0 && _lastTotals.Count > 0)
            {
                foreach (var k in _lastTotals.Keys)
                {
                    snapshot[k] = (0, 0);
                }
            }
            var sampleAtUtc = DateTime.UtcNow;
            var rates = new System.Collections.Generic.Dictionary<int, (double In, double Out)>();
            foreach (var kv in snapshot)
            {
                var port = kv.Key; var tot = kv.Value;
                if (!_lastTotals.TryGetValue(port, out var prev))
                {
                    _lastTotals[port] = (tot.TotalIn, tot.TotalOut, sampleAtUtc);
                    if (!_skipNextRateSample)
                    {
                        rates[port] = (0, 0);
                    }
                    continue;
                }
                var din = (double)Math.Max(0, tot.TotalIn - prev.In);
                var dout = (double)Math.Max(0, tot.TotalOut - prev.Out);
                var elapsedSeconds = (sampleAtUtc - prev.AtUtc).TotalSeconds;
                if (elapsedSeconds <= 0) elapsedSeconds = (_intervalMs <= 0 ? 500.0 : _intervalMs) / 1000.0;
                if (elapsedSeconds < 0.05) elapsedSeconds = 0.05;
                rates[port] = (din / elapsedSeconds, dout / elapsedSeconds);
                _lastTotals[port] = (tot.TotalIn, tot.TotalOut, sampleAtUtc);
            }

            // Session-level throughput: AeadTransport byte counters cover ALL active sessions
            // regardless of connection direction (inbound, outbound, relay). CountingStream /
            // port-stats only see inbound listener connections; this fills the gap for relay and
            // outbound sessions so the graph always reflects actual peer traffic.
            var sessNow = AppServices.Network.GetAllSessionBytesSnapshot();
            if (_lastSessionTotals.AtUtc != default)
            {
                var sessElapsed = (sampleAtUtc - _lastSessionTotals.AtUtc).TotalSeconds;
                if (sessElapsed >= 0.05)
                {
                    var sessIn  = (double)Math.Max(0, sessNow.TotalIn  - _lastSessionTotals.In)  / sessElapsed;
                    var sessOut = (double)Math.Max(0, sessNow.TotalOut - _lastSessionTotals.Out) / sessElapsed;
                    // Merge under the TCP (listening port) key: take the higher of port-stats vs
                    // session-stats so we never under-report traffic.
                    if (AppServices.Network.ListeningPort is int sessLp)
                    {
                        rates.TryGetValue(sessLp, out var curVal);
                        rates[sessLp] = (Math.Max(curVal.In, sessIn), Math.Max(curVal.Out, sessOut));
                    }
                    else
                    {
                        // Keep session traffic visible when the listener key is transiently unavailable.
                        rates.TryGetValue(SessionSyntheticRateKey, out var curVal);
                        rates[SessionSyntheticRateKey] = (Math.Max(curVal.In, sessIn), Math.Max(curVal.Out, sessOut));
                    }
                }
            }
            _lastSessionTotals = (sessNow.TotalIn, sessNow.TotalOut, sampleAtUtc);

            if (_skipNextRateSample)
            {
                _skipNextRateSample = false;
                rates.Clear();
            }
            var isRealtime = _intervalMs <= 250;
            if (isRealtime)
            {
                const double alphaRise = 0.58;
                const double alphaFall = 0.26;
                foreach (var kv in rates)
                {
                    if (_smoothedRates.TryGetValue(kv.Key, out var prior))
                    {
                        var inAlpha = kv.Value.In >= prior.In ? alphaRise : alphaFall;
                        var outAlpha = kv.Value.Out >= prior.Out ? alphaRise : alphaFall;
                        _smoothedRates[kv.Key] = (
                            prior.In + ((kv.Value.In - prior.In) * inAlpha),
                            prior.Out + ((kv.Value.Out - prior.Out) * outAlpha));
                    }
                    else
                    {
                        _smoothedRates[kv.Key] = kv.Value;
                    }
                }
                var stale = _smoothedRates.Keys.Except(rates.Keys).ToList();
                foreach (var key in stale) _smoothedRates.Remove(key);
                vm.UpdateRates(new System.Collections.Generic.Dictionary<int, (double In, double Out)>(_smoothedRates));
            }
            else
            {
                if (_smoothedRates.Count > 0) _smoothedRates.Clear();
                vm.UpdateRates(rates);
            }
            // Append a lightweight diagnostics line for visibility in the log. Keeps text small and theme-matched in XAML.
            try
            {
                if (rates.Count > 0)
                {
                    var tcp = AppServices.Network.ListeningPort is int lp && rates.TryGetValue(lp, out var tr) ? (tr.In + tr.Out) : 0;
                    var udp = AppServices.Network.UdpBoundPort is int up && rates.TryGetValue(up, out var ur) ? (ur.In + ur.Out) : 0;
                    var outbound = AppServices.Network.LastAutoClientPort is int ap && rates.TryGetValue(ap, out var orr) ? (orr.In + orr.Out) : 0;
                    var line = $"{DateTime.Now:HH:mm:ss} - TCP {tcp:0} B/s - UDP {udp:0} B/s - OUT {outbound:0} B/s";
                    vm.AppendLog(line);

                    // Auto-scroll behavior is intentionally conservative: only scroll to bottom if the user is already near the bottom.
                    // If _autoScrollLog is true (user at bottom or pressed End), keep the view pinned to the newest entry.
                    try
                    {
                        if (_autoScrollLog)
                        {
                            var scroll = this.FindControl<ScrollViewer>("LogScroll");
                            scroll?.ScrollToEnd();
                        }
                    }
                    catch { }
                }
            }
            catch { }
            // Diagnostics snapshot
            // REFRESH-DRIVEN: Traffic chart and periodic log lines update at the configured refresh rate.
            // Diagnostics summary string shifts to event-driven (OnUiPulse/OnHandshakeCompleted),
            // so we no longer update it here to avoid double work.
        }
        catch { }
    }

    // EVENT HANDLERS (UI-thread marshaling inside each)
    private void OnStatusEvent()
    {
        try
        {
            if (_isClosing) return;
            if (DataContext is not MonitoringViewModel vm) return;
            // Marshal to UI thread to safely notify bindings.
            Dispatcher.UIThread.Post(() => vm.NotifyNetworkStatus());
        }
        catch { }
    }

    private void OnNetworkListeningChanged(bool isListening, int? port)
    {
        // Network listener state affects bound port labels; refresh status bindings on event.
        OnStatusEvent();
    }

    private void OnHandshakeCompleted(bool inbound, string peerUid, string? via)
    {
        try
        {
            if (_isClosing) return;
            if (DataContext is not MonitoringViewModel vm) return;
            var snap = AppServices.Network.GetDiagnosticsSnapshot();
            Dispatcher.UIThread.Post(() => vm.UpdateDiagnostics(snap));
        }
        catch { }
    }

    private void OnUiPulse()
    {
        // Lightweight periodic app pulse; update diagnostics summary only (not full status)
        try
        {
            if (_isClosing) return;
            if (DataContext is not MonitoringViewModel vm) return;
            var snap = AppServices.Network.GetDiagnosticsSnapshot();
            Dispatcher.UIThread.Post(() => vm.UpdateDiagnostics(snap));
        }
        catch { }
    }

    private void RestartLoop()
    {
        try { _cts?.Cancel(); } catch { }
        _cts = new System.Threading.CancellationTokenSource();
        var token = _cts.Token;
        var configured = _intervalMs == 0 ? 500 : _intervalMs;
        var interval = configured <= 250 ? 50 : Math.Max(250, configured);
        // Dedicated thread loop (Task.Run on ThreadPool; posts UI updates via Dispatcher)
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Always marshal updates onto the UI thread; all VM property changes and Canvas manipulations
                    // happen inside Tick() on the Dispatcher to avoid threading issues.
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Tick());
                }
                catch { }
                try { await System.Threading.Tasks.Task.Delay(interval, token); } catch { }
            }
        }, token);
    }

    // Chart rendering moved to TrafficHistoryView (data-bound to ViewModel.History); no imperative canvas updates needed.

    // Non-blocking close: stop loop immediately and offload persistence to background to avoid UI stall
    private void OnMonitoringWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            _isClosing = true; // gate further UI updates
            try { _cts?.Cancel(); } catch { }
            // Capture state on UI thread, then persist asynchronously to avoid blocking close
            try
            {
                var layout = new LayoutCache.WindowLayout(Width, Height, Position.X, Position.Y, (int)WindowState);
                // Sidecar write is small and synchronous; keep try/catch to avoid blocking close on failure.
                LayoutCache.Save("MonitoringWindow", layout);
            }
            catch { }
        }
        catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        // Unsubscribe from all events to avoid leaks and callbacks after close.
        try { Zer0Talk.Utilities.Logger.LineLogged -= OnLoggerLine; } catch { }
        try { AppServices.Nat.Changed -= OnStatusEvent; } catch { }
        try { AppServices.Discovery.Changed -= OnStatusEvent; } catch { }
        try { AppServices.Network.ListeningChanged -= OnNetworkListeningChanged; } catch { }
        try { AppServices.Events.PeersChanged -= OnStatusEvent; } catch { }
        try { AppServices.Network.HandshakeCompleted -= OnHandshakeCompleted; } catch { }
        try { AppServices.Events.UiPulse -= OnUiPulse; } catch { }
        try { _cts?.Cancel(); _cts?.Dispose(); _cts = null; } catch { }
        base.OnClosed(e);
    }

    public void Dispose()
    {
        try
        {
            if (_isClosing == false) _isClosing = true;
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            _cts = null;
            // Suppress finalization as there is no finalizer, but follow CA1816 guidance for future-proofing
            GC.SuppressFinalize(this);
        }
        catch { }
    }
}
