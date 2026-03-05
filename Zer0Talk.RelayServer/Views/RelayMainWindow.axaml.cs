using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Zer0Talk.RelayServer.Services;
using Zer0Talk.RelayServer.ViewModels;

namespace Zer0Talk.RelayServer.Views;

public partial class RelayMainWindow : Window
{
    private const double RelayConsoleNearBottomThreshold = 24;
    private const double SmoothScrollDurationMs = 180;
    private const double MinSmoothScrollDistance = 8;
    private RelayClientsWindow? _clientsWindow;
    private RelayProbeAuditWindow? _probeAuditWindow;
    private INotifyCollectionChanged? _relayConsoleCollection;
    private ScrollViewer? _relayConsoleScrollViewer;
    private bool _relayConsoleAutoFollowBottom = true;
    private int _relayConsoleUnseenEntryCount;
    private DispatcherTimer? _smoothScrollTimer;
    private double _smoothScrollStartY;
    private double _smoothScrollTargetY;
    private DateTime _smoothScrollStartedAtUtc;

    public RelayMainWindow()
    {
        InitializeComponent();
        DataContext = new RelayMainWindowViewModel();
        if (DataContext is RelayMainWindowViewModel vm)
        {
            vm.ShowClientsWindowRequested += OnShowClientsWindowRequested;
            vm.OpenProbeAuditLogRequested += OnOpenProbeAuditLogRequested;
            vm.StopRelayRequested += OnStopRelayRequested;
            vm.PauseResumeRequested += OnPauseResumeRequested;
        }
        Opened += OnOpened;
        DataContextChanged += OnDataContextChanged;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        AttachRelayConsoleAutoFollow();
        ScrollRelayConsoleToBottom();
        Dispatcher.UIThread.Post(() => ScrollRelayConsoleToBottom(), DispatcherPriority.Background);
        UpdateRelayConsoleJumpToLatestVisibility();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        AttachRelayConsoleAutoFollow();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        StopSmoothScrollAnimation();
        DetachRelayConsoleAutoFollow();
    }

    private void AttachRelayConsoleAutoFollow()
    {
        DetachRelayConsoleAutoFollow();

        if (DataContext is not RelayMainWindowViewModel vm) return;

        _relayConsoleCollection = vm.Logs;
        _relayConsoleCollection.CollectionChanged += OnRelayConsoleCollectionChanged;

        if (RelayConsoleList != null)
        {
            _relayConsoleScrollViewer = RelayConsoleList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (_relayConsoleScrollViewer != null)
            {
                _relayConsoleScrollViewer.ScrollChanged += OnRelayConsoleScrollChanged;
            }
        }

        _relayConsoleAutoFollowBottom = true;
        _relayConsoleUnseenEntryCount = 0;
        UpdateRelayConsoleJumpToLatestVisibility();
    }

    private void DetachRelayConsoleAutoFollow()
    {
        if (_relayConsoleScrollViewer != null)
        {
            _relayConsoleScrollViewer.ScrollChanged -= OnRelayConsoleScrollChanged;
        }

        if (_relayConsoleCollection != null)
        {
            _relayConsoleCollection.CollectionChanged -= OnRelayConsoleCollectionChanged;
            _relayConsoleCollection = null;
        }

        _relayConsoleScrollViewer = null;
        _relayConsoleAutoFollowBottom = true;
        _relayConsoleUnseenEntryCount = 0;
        UpdateRelayConsoleJumpToLatestVisibility();
    }

    private void OnRelayConsoleCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        EnsureRelayConsoleScrollViewer();

        var shouldFollow = _relayConsoleAutoFollowBottom || IsRelayConsoleNearBottom();
        if (!shouldFollow)
        {
            _relayConsoleUnseenEntryCount += CountAddedEntries(e);
            UpdateRelayConsoleJumpToLatestVisibility();
            return;
        }

        _relayConsoleAutoFollowBottom = true;
        _relayConsoleUnseenEntryCount = 0;
        ScrollRelayConsoleToBottom();
        Dispatcher.UIThread.Post(() =>
        {
            if (_relayConsoleAutoFollowBottom || IsRelayConsoleNearBottom())
            {
                ScrollRelayConsoleToBottom();
            }

            UpdateRelayConsoleJumpToLatestVisibility();
        }, DispatcherPriority.Background);

        UpdateRelayConsoleJumpToLatestVisibility();
    }

    private void ScrollRelayConsoleToBottom(bool smooth = true)
    {
        EnsureRelayConsoleScrollViewer();

        if (_relayConsoleScrollViewer == null) return;

        var targetY = Math.Max(0, _relayConsoleScrollViewer.Extent.Height - _relayConsoleScrollViewer.Viewport.Height);
        var currentY = _relayConsoleScrollViewer.Offset.Y;
        if (!smooth || !IsSmoothScrollingEnabled() || Math.Abs(targetY - currentY) <= MinSmoothScrollDistance)
        {
            StopSmoothScrollAnimation();
            _relayConsoleScrollViewer.Offset = new Avalonia.Vector(_relayConsoleScrollViewer.Offset.X, targetY);
            return;
        }

        StartSmoothScroll(targetY);
    }

    private void EnsureRelayConsoleScrollViewer()
    {
        if (_relayConsoleScrollViewer != null || RelayConsoleList == null) return;

        _relayConsoleScrollViewer = RelayConsoleList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_relayConsoleScrollViewer != null)
        {
            _relayConsoleScrollViewer.ScrollChanged += OnRelayConsoleScrollChanged;
        }
    }

    private void OnRelayConsoleScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var extentGrew = Math.Abs(e.ExtentDelta.Y) > 0.01 && e.ExtentDelta.Y > 0;
        var offsetMoved = Math.Abs(e.OffsetDelta.Y) > 0.01;

        if (extentGrew && !offsetMoved && _relayConsoleAutoFollowBottom)
        {
            ScrollRelayConsoleToBottom();
            _relayConsoleAutoFollowBottom = true;
            _relayConsoleUnseenEntryCount = 0;
            UpdateRelayConsoleJumpToLatestVisibility();
            return;
        }

        _relayConsoleAutoFollowBottom = IsRelayConsoleNearBottom();
        if (_relayConsoleAutoFollowBottom)
        {
            _relayConsoleUnseenEntryCount = 0;
        }

        UpdateRelayConsoleJumpToLatestVisibility();
    }

    private bool IsRelayConsoleNearBottom()
    {
        if (_relayConsoleScrollViewer == null) return true;

        var extent = _relayConsoleScrollViewer.Extent.Height;
        var viewport = _relayConsoleScrollViewer.Viewport.Height;
        var offset = _relayConsoleScrollViewer.Offset.Y;
        var remaining = extent - (offset + viewport);
        return remaining <= RelayConsoleNearBottomThreshold;
    }

    private static int CountAddedEntries(NotifyCollectionChangedEventArgs e)
    {
        return e.Action switch
        {
            NotifyCollectionChangedAction.Add => e.NewItems?.Count ?? 0,
            NotifyCollectionChangedAction.Reset => 0,
            _ => 0
        };
    }

    private void RelayConsoleJumpToLatest_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _relayConsoleAutoFollowBottom = true;
        _relayConsoleUnseenEntryCount = 0;
        ScrollRelayConsoleToBottom(smooth: true);

        Dispatcher.UIThread.Post(() =>
        {
            ScrollRelayConsoleToBottom(smooth: true);
            UpdateRelayConsoleJumpToLatestVisibility();
        }, DispatcherPriority.Background);
    }

    private void StartSmoothScroll(double targetY)
    {
        if (_relayConsoleScrollViewer == null)
        {
            return;
        }

        _smoothScrollStartY = _relayConsoleScrollViewer.Offset.Y;
        _smoothScrollTargetY = targetY;
        _smoothScrollStartedAtUtc = DateTime.UtcNow;

        if (_smoothScrollTimer == null)
        {
            _smoothScrollTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(16),
                DispatcherPriority.Background,
                OnSmoothScrollTick);
        }

        if (!_smoothScrollTimer.IsEnabled)
        {
            _smoothScrollTimer.Start();
        }
    }

    private void OnSmoothScrollTick(object? sender, EventArgs e)
    {
        if (_relayConsoleScrollViewer == null)
        {
            StopSmoothScrollAnimation();
            return;
        }

        var maxY = Math.Max(0, _relayConsoleScrollViewer.Extent.Height - _relayConsoleScrollViewer.Viewport.Height);
        if (_smoothScrollTargetY > maxY)
        {
            _smoothScrollTargetY = maxY;
        }

        var elapsedMs = (DateTime.UtcNow - _smoothScrollStartedAtUtc).TotalMilliseconds;
        var progress = Math.Clamp(elapsedMs / SmoothScrollDurationMs, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        var nextY = _smoothScrollStartY + ((_smoothScrollTargetY - _smoothScrollStartY) * eased);

        _relayConsoleScrollViewer.Offset = new Avalonia.Vector(_relayConsoleScrollViewer.Offset.X, nextY);

        if (progress >= 1 || Math.Abs(_smoothScrollTargetY - nextY) <= 0.5)
        {
            _relayConsoleScrollViewer.Offset = new Avalonia.Vector(_relayConsoleScrollViewer.Offset.X, _smoothScrollTargetY);
            StopSmoothScrollAnimation();
        }
    }

    private void StopSmoothScrollAnimation()
    {
        if (_smoothScrollTimer == null)
        {
            return;
        }

        _smoothScrollTimer.Stop();
        _smoothScrollTimer.Tick -= OnSmoothScrollTick;
        _smoothScrollTimer = null;
    }

    private static bool IsSmoothScrollingEnabled()
    {
        try
        {
            return RelayAppServices.Config.EnableSmoothScrolling;
        }
        catch
        {
            return true;
        }
    }

    private void UpdateRelayConsoleJumpToLatestVisibility()
    {
        if (RelayConsoleJumpToLatestButton == null)
        {
            return;
        }

        var show = !_relayConsoleAutoFollowBottom && !IsRelayConsoleNearBottom();
        RelayConsoleJumpToLatestButton.IsVisible = show;

        var count = Math.Max(0, _relayConsoleUnseenEntryCount);
        ToolTip.SetTip(
            RelayConsoleJumpToLatestButton,
            count > 0
                ? $"Jump to latest ({count} new)"
                : "Jump to latest entries");
    }

    private async void OnStopRelayRequested()
    {
        if (DataContext is not RelayMainWindowViewModel vm) return;

        var confirmed = await ShowConfirmationDialogAsync(
            "Confirm Stop Relay",
            "Stop relay service now? All active sessions will terminate immediately.",
            "Stop Relay");

        if (!confirmed) return;
        vm.ExecuteStopRelayFromUi();
    }

    private async void OnPauseResumeRequested()
    {
        if (DataContext is not RelayMainWindowViewModel vm) return;

        // Resume is safe and should stay single-click.
        if (RelayAppServices.Host.IsPaused)
        {
            vm.ExecutePauseResumeFromUi();
            return;
        }

        var confirmed = await ShowConfirmationDialogAsync(
            "Confirm Pause Relay",
            "Pause relay service now? New relay operations will queue or timeout until resumed.",
            "Pause Relay");

        if (!confirmed) return;
        vm.ExecutePauseResumeFromUi();
    }

    private async Task<bool> ShowConfirmationDialogAsync(string title, string message, string confirmText)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 440,
            MinWidth = 400,
            Height = 190,
            MinHeight = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var root = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("*,Auto"),
            RowSpacing = 12
        };

        root.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        Grid.SetRow(actions, 1);

        var cancel = new Button { Content = "Cancel", MinWidth = 86, IsCancel = true };
        var confirm = new Button { Content = confirmText, MinWidth = 100, IsDefault = true };
        actions.Children.Add(cancel);
        actions.Children.Add(confirm);
        root.Children.Add(actions);

        dialog.Content = root;

        var result = false;
        cancel.Click += (_, __) => { result = false; dialog.Close(); };
        confirm.Click += (_, __) => { result = true; dialog.Close(); };
        await dialog.ShowDialog(this);
        return result;
    }

    private async void ShowActiveSessionsHelp_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "Active Sessions - Operator Help",
            Width = 560,
            MinWidth = 500,
            Height = 360,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true
        };

        var root = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("*,Auto"),
            RowSpacing = 12
        };

        var details = new TextBlock
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
             Text = "Active Sessions lists live relay tunnels currently forwarding traffic between paired clients.\n\n" +
                 "Important: the relay server itself will not appear as an entry here. This list is only for active client pairings routed through this relay.\n\n" +
                 "Expected behavior:\n" +
                 "- One row per active session key\n" +
                 "- Session appears after successful pairing\n" +
                 "- Session disappears when peers disconnect, timeout, or are cleaned up\n\n" +
                 "Operational use:\n" +
                 "- Validate that pairings are transitioning from pending to active\n" +
                 "- Identify stale/ghost sessions during incident response\n" +
                 "- Select a row and use Disconnect to force-close a problematic tunnel"
        };
        root.Children.Add(details);

        var close = new Button
        {
            Content = "Close",
            MinWidth = 90,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true
        };
        Grid.SetRow(close, 1);
        close.Click += (_, __) => dialog.Close();
        root.Children.Add(close);

        dialog.Content = root;
        await dialog.ShowDialog(this);
    }

    private void OnOpenProbeAuditLogRequested()
    {
        if (DataContext is not RelayMainWindowViewModel vm) return;

        if (_probeAuditWindow == null)
        {
            _probeAuditWindow = new RelayProbeAuditWindow
            {
                DataContext = vm,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            _probeAuditWindow.Closed += (_, __) => _probeAuditWindow = null;
            _probeAuditWindow.Show(this);
            return;
        }

        try
        {
            if (!_probeAuditWindow.IsVisible)
            {
                _probeAuditWindow.Show(this);
            }

            _probeAuditWindow.WindowState = WindowState.Normal;
            _probeAuditWindow.Activate();
            _probeAuditWindow.Focus();
        }
        catch { }
    }

    private void OnShowClientsWindowRequested()
    {
        if (DataContext is not RelayMainWindowViewModel vm) return;

        if (_clientsWindow == null)
        {
            _clientsWindow = new RelayClientsWindow
            {
                DataContext = vm,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            _clientsWindow.Closed += (_, __) => _clientsWindow = null;
            _clientsWindow.Show(this);
            return;
        }

        try
        {
            if (!_clientsWindow.IsVisible)
            {
                _clientsWindow.Show(this);
            }

            _clientsWindow.WindowState = WindowState.Normal;
            _clientsWindow.Activate();
            _clientsWindow.Focus();
        }
        catch { }
    }

    private void CopyClientUid_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not RelayMainWindowViewModel vm) return;
        if (sender is not Control control || control.DataContext is not RelayClientInfo client) return;
        if (vm.CopyClientUidCommand.CanExecute(client)) vm.CopyClientUidCommand.Execute(client);
    }

    private void CopyClientPublicKey_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not RelayMainWindowViewModel vm) return;
        if (sender is not Control control || control.DataContext is not RelayClientInfo client) return;
        if (vm.CopyClientPublicKeyCommand.CanExecute(client)) vm.CopyClientPublicKeyCommand.Execute(client);
    }

    private void CopyClientCombined_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not RelayMainWindowViewModel vm) return;
        if (sender is not Control control || control.DataContext is not RelayClientInfo client) return;
        if (vm.CopyClientCombinedCommand.CanExecute(client)) vm.CopyClientCombinedCommand.Execute(client);
    }


    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (RelayAppServices.Config.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        if (DataContext is RelayMainWindowViewModel vm)
        {
            vm.ShowClientsWindowRequested -= OnShowClientsWindowRequested;
            vm.OpenProbeAuditLogRequested -= OnOpenProbeAuditLogRequested;
            vm.StopRelayRequested -= OnStopRelayRequested;
            vm.PauseResumeRequested -= OnPauseResumeRequested;
        }

        try { _clientsWindow?.Close(); } catch { }
        try { _probeAuditWindow?.Close(); } catch { }
        _clientsWindow = null;
        _probeAuditWindow = null;
    }
}
