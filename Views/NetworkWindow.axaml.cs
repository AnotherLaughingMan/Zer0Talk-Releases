/*
    DEPRECATED: Use Settings > Network instead. This window is kept for reference only.
    
    Network window code-behind: persists window state and tab selection.
    - Hosts Network and Peers tabs; Topmost is persisted in AppSettings.
*/
using System;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Zer0Talk.Models;
using Zer0Talk.Services;
using Zer0Talk.ViewModels;

namespace Zer0Talk.Views;

public partial class NetworkWindow : Window
{
    // Event-driven: no periodic UI polling here
    private const string UpdatesKey = "NetworkWindow.UI"; // key kept for cleanup symmetry (unused interval)
    private System.Action? _uiPulseHandler;
    private System.Action? _onNatChanged;
    private System.Action<bool, int?>? _onNetworkListeningChanged;
    private System.Action? _onPeersChanged;
    // PERF: offload adapter scanning from UI thread; guard with simple concurrency + throttle
    private volatile bool _adaptersRefreshRunning; // protects against reentrancy
    private DateTime _lastAdaptersRefreshUtc = DateTime.MinValue; // coarse throttle
    public NetworkWindow()
    {
        InitializeComponent();
        if (DataContext is NetworkViewModel vm)
        {
            vm.CloseRequested += (_, _) => Close();
            AppServices.Network.WarningRaised += msg => vm.InfoMessage = msg;
            // Real-time updates: event-driven (NAT, network, peers) + centralized UI pulse
            var throttled = AppServices.Updates.GetUiThrottled(UpdatesKey + ".throttle", 200, () => CollectAndRender());
            _onNatChanged = () => throttled();
            _onNetworkListeningChanged = (_, __) => throttled();
            _onPeersChanged = () => throttled();
            AppServices.Events.NatChanged += _onNatChanged;
            AppServices.Events.NetworkListeningChanged += _onNetworkListeningChanged;
            AppServices.Events.PeersChanged += _onPeersChanged;
            _uiPulseHandler = () => throttled();
            AppServices.Events.UiPulse += _uiPulseHandler;
            // Subscribe to global log stream so the Logging tab updates in real time, independent of Save/tab selection.
            Zer0Talk.Utilities.Logger.LineLogged += OnLineLogged;
            // Horizontal wheel scroll support for the Logging tab
            this.Opened += (_, __) =>
            {
                try
                {
                    var lb = this.FindControl<ListBox>("LoggingList");
                    if (lb != null)
                        lb.AddHandler(InputElement.PointerWheelChangedEvent, OnLogWheel, RoutingStrategies.Tunnel);
                    // Keep NAT indicator blink and status fresh even when no events fire (fallback; centralized pulse preferred)
                    AppServices.Updates.RegisterUiInterval(UpdatesKey + ".blink", 500, () => CollectAndRender());
                }
                catch { }
            };
        }
        this.Opened += (_, _) => RestoreLayoutFromCacheOrSettings(AppServices.Settings.Settings.NetworkWindow);
        this.Closing += (_, _) => SaveLayoutAndSettings(AppServices.Settings.Settings.NetworkWindow);
        // Removed runtime geometry persistence hooks to prevent frequent writes to settings.
        this.Closing += (_, __) =>
        {
            try { AppServices.Updates.UnregisterUi(UpdatesKey + ".blink"); } catch { }
            try { if (_uiPulseHandler != null) AppServices.Events.UiPulse -= _uiPulseHandler; } catch { }
            try { Zer0Talk.Utilities.Logger.LineLogged -= OnLineLogged; } catch { }
        };
        // Global lock hotkey for this window
        this.AddHandler(InputElement.KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);

        // No legacy timer: strictly event-driven to reduce load
    }

    private void OpenMonitoring_Click(object? sender, RoutedEventArgs e)
    {
        try { Zer0Talk.Services.WindowManager.ShowSingleton<MonitoringWindow>(); } catch { }
    }

    // Horizontal wheel handler for Logging tab
    private void OnLogWheel(object? sender, PointerWheelEventArgs e)
    {
        try
        {
            if (sender is not ListBox lb) return;
            var scroller = lb.GetVisualDescendants().FirstOrDefault(v => v is ScrollViewer) as ScrollViewer;
            if (scroller is null) return;
            var delta = e.Delta; double dx = 0;
            if (Math.Abs(delta.X) > 0.01) dx = -delta.X * 40; else if ((e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift) dx = -delta.Y * 40;
            if (Math.Abs(dx) > 0.01) { scroller.Offset = new Vector(scroller.Offset.X + dx, scroller.Offset.Y); e.Handled = true; }
        }
        catch { }
    }

    // [LAYOUT] Restore via cache with fallback to settings; ensure visibility; keep Topmost sourced from settings only.
    private void RestoreLayoutFromCacheOrSettings(WindowStateSettings s)
    {
        double w = Width, h = Height; var pos = Position; bool haveState = false;
        try
        {
            var cached = LayoutCache.Load("NetworkWindow");
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
        // Default to Topmost true; persist/override via settings
        Topmost = s.Topmost ?? true;
    }
    // [LAYOUT] Save geometry to cache and persist Topmost to settings on close only.
    private void SaveLayoutAndSettings(WindowStateSettings s)
    {
        try
        {
            var layout = new LayoutCache.WindowLayout(Width, Height, Position.X, Position.Y, (int)WindowState);
            LayoutCache.Save("NetworkWindow", layout);
        }
        catch { }
        try
        {
            s.Topmost = Topmost;
            AppServices.Settings.Save(AppServices.Passphrase);
        }
        catch { }
        try { AppServices.Updates.UnregisterUi(UpdatesKey + ".throttle"); } catch { }
    }

    public void SwitchToTab(string header)
    {
        var tabs = this.FindControl<TabControl>("NetworkTabs");
        if (tabs is null) return;
        foreach (var obj in tabs.Items)
        {
            if (obj is TabItem ti && string.Equals(ti.Header?.ToString(), header, StringComparison.OrdinalIgnoreCase))
            {
                tabs.SelectedItem = ti;
                break;
            }
        }
    }

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        try
        {
            // Use HotkeyManager for consistent global hotkey handling
            if (HotkeyManager.Instance.HandleKeyEvent(e))
                return;
        }
        catch { }
    }

    // [DRAGBAR] Begin moving window on left-click; ignore clicks on interactive controls; no double-click handling here.
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
            }
            BeginMoveDrag(e);
        }
        catch { }
    }
    private void CollectAndRender()
    {
        try
        {
            // Always refresh status and animations regardless of selected tab
            if (DataContext is NetworkViewModel vm0)
            {
                // Update derived properties including NAT indicator inputs
                vm0.NotifyNetworkStatus();
                // Also refresh adapters' live status so the Adapters tab stays current without Save
                // IMPORTANT: The original implementation queried network interfaces on the UI thread.
                // That causes app-wide lag while the Network window is open. We now offload the scan
                // to a background thread and only apply minimal changes on the UI thread.
                QueueAdaptersRefresh();
                if (vm0.NatIndicatorBlink)
                {
                    vm0.NatIndicatorOpacity = vm0.NatIndicatorOpacity < 0.6 ? 1.0 : 0.3;
                }
                else if (vm0.NatIndicatorOpacity != 1.0)
                {
                    vm0.NatIndicatorOpacity = 1.0;
                }
            }

            // Monitoring chart/rates removed from this window; MonitoringWindow is responsible.
        }
        catch { }
    }

    // Schedules a background scan of network interfaces and applies updates to adapter items on the UI thread.
    // Changes isolated to minimize layout churn and avoid redundant updates.
    private void QueueAdaptersRefresh()
    {
        // Coarse throttle to at most ~once per 2 seconds; UiPulse and throttles may fire frequently.
        var now = DateTime.UtcNow;
        if ((now - _lastAdaptersRefreshUtc) < TimeSpan.FromSeconds(2)) return;
        if (_adaptersRefreshRunning) return;
        _adaptersRefreshRunning = true;
        _lastAdaptersRefreshUtc = now;

        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var map = new System.Collections.Generic.Dictionary<string, (string Address, string Status)>();
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    try
                    {
                        var props = ni.GetIPProperties();
                        var ip = "";
                        foreach (var ua in props.UnicastAddresses)
                        {
                            if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            { ip = ua.Address.ToString(); break; }
                        }
                        var status = ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up ? "Active" : "Inactive";
                        map[ni.Id] = (string.IsNullOrEmpty(ip) ? "(no IPv4)" : ip, status);
                    }
                    catch { }
                }

                // Apply to VM on UI thread; only mutate properties when changed to reduce layout work
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        if (DataContext is NetworkViewModel vm && vm.Adapters != null)
                        {
                            foreach (var a in vm.Adapters)
                            {
                                if (map.TryGetValue(a.Id, out var info))
                                {
                                    if (!string.Equals(a.Address, info.Address, StringComparison.Ordinal)) a.Address = info.Address;
                                    if (!string.Equals(a.Status, info.Status, StringComparison.Ordinal)) a.Status = info.Status;
                                }
                            }
                        }
                    }
                    catch { }
                });
            }
            catch { }
            finally { _adaptersRefreshRunning = false; }
        });
    }

    private static string FormatRate(double bytesPerSec)
    {
        string[] units = new[] { "B/s", "KB/s", "MB/s", "GB/s" };
        int i = 0;
        while (bytesPerSec >= 1024 && i < units.Length - 1) { bytesPerSec /= 1024; i++; }
        return $"{bytesPerSec:0.#} {units[i]}";
    }

    // Chart rendering removed; now in MonitoringWindow

    // XAML event to ensure canvas is ready
    private void TrafficCanvas_Attached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // Intentionally empty; retained if XAML hooks it
    }

    private void OnLineLogged(string line)
    {
        try
        {
            if (DataContext is not NetworkViewModel vm) return;
            Dispatcher.UIThread.Post(() => vm.AppendLog(line));
        }
        catch { }
    }

    // Drag & Drop for Adapters reordering
    private Point _dragStartPoint;
    private object? _dragItem;

    private void AdaptersList_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            var lb = this.FindControl<ListBox>("AdaptersList");
            if (lb is null) return;
            _dragStartPoint = e.GetPosition(lb);
            _dragItem = GetItemAt(lb, _dragStartPoint);
        }
        catch { }
    }

    private async void AdaptersList_PointerMoved(object? sender, PointerEventArgs e)
    {
        try
        {
            if (_dragItem == null) return;
            var lb = this.FindControl<ListBox>("AdaptersList");
            if (lb is null) return;
            if (e.GetCurrentPoint(lb).Properties.IsLeftButtonPressed)
            {
                var pos = e.GetPosition(lb);
                if (Math.Abs(pos.X - _dragStartPoint.X) > 4 || Math.Abs(pos.Y - _dragStartPoint.Y) > 4)
                {
#pragma warning disable CS0618 // Type or member is obsolete
                    var data = new DataObject();
                    data.Set("application/x-p2ptalk-adapter", _dragItem);
                    await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
#pragma warning restore CS0618
                    _dragItem = null;
                }
            }
        }
        catch { }
    }

    private void AdaptersList_DragOver(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        if (e.Data.Contains("application/x-p2ptalk-adapter"))
        {
            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
        }
#pragma warning restore CS0618
    }

    private void AdaptersList_Drop(object? sender, DragEventArgs e)
    {
        try
        {
            var lb = this.FindControl<ListBox>("AdaptersList");
            if (lb is null) return;
#pragma warning disable CS0618 // Type or member is obsolete
            if (!e.Data.Contains("application/x-p2ptalk-adapter")) return;
            var dragged = e.Data.Get("application/x-p2ptalk-adapter");
#pragma warning restore CS0618
            if (dragged is null) return;
            var targetPos = e.GetPosition(lb);
            var target = GetItemAt(lb, targetPos);
            if (DataContext is not NetworkViewModel vm) return;
            var items = vm.Adapters;
            if (items is null) return;
            var fromItem = dragged as NetworkViewModel.AdapterItem;
            var toItem = target;
            var from = fromItem != null ? items.IndexOf(fromItem) : -1;
            int to = toItem != null ? items.IndexOf(toItem) : items.Count - 1;
            if (from >= 0 && to >= 0 && from != to)
            {
                items.Move(from, to);
                vm.SelectedAdapter = items[to];
            }
        }
        catch { }
    }

    private Zer0Talk.ViewModels.NetworkViewModel.AdapterItem? GetItemAt(ListBox lb, Point pos)
    {
        try
        {
            if (DataContext is not NetworkViewModel vm) return null;
            var count = vm.Adapters?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                if (lb.ContainerFromIndex(i) is Control c)
                {
                    var origin = c.TranslatePoint(new Point(0, 0), lb);
                    if (origin.HasValue)
                    {
                        var rect = new Rect(origin.Value, c.Bounds.Size);
                        if (rect.Contains(pos))
                            return vm.Adapters![i];
                    }
                }
            }
        }
        catch { }
        return null;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        try { if (_onNatChanged != null) AppServices.Events.NatChanged -= _onNatChanged; } catch { }
        try { if (_onNetworkListeningChanged != null) AppServices.Events.NetworkListeningChanged -= _onNetworkListeningChanged; } catch { }
        try { if (_onPeersChanged != null) AppServices.Events.PeersChanged -= _onPeersChanged; } catch { }
        try { AppServices.Updates.UnregisterUi(UpdatesKey + ".throttle"); } catch { }
        try { if (DataContext is IDisposable d) d.Dispose(); } catch { }
        try { DataContext = null; } catch { }
    }
}
