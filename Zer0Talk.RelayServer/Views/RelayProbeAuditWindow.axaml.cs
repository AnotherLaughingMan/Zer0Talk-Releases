using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Specialized;
using System.Linq;
using Zer0Talk.RelayServer.Services;
using Zer0Talk.RelayServer.ViewModels;

namespace Zer0Talk.RelayServer.Views;

public partial class RelayProbeAuditWindow : Window
{
    private const double NearBottomThreshold = 24;
    private const double SmoothScrollDurationMs = 180;
    private const double MinSmoothScrollDistance = 8;
    private ScrollViewer? _probeScrollViewer;
    private INotifyCollectionChanged? _trackedCollection;
    private bool _autoFollowBottom = true;
    private int _unseenEntryCount;
    private DispatcherTimer? _smoothScrollTimer;
    private double _smoothScrollStartY;
    private double _smoothScrollTargetY;
    private DateTime _smoothScrollStartedAtUtc;

    public RelayProbeAuditWindow()
    {
        InitializeComponent();
        if (JumpToLatestButton != null)
        {
            JumpToLatestButton.Click += JumpToLatest_Click;
        }
        Opened += OnOpened;
        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        AttachScrollViewer();
        AttachCollectionSubscription();
        ScrollToBottom();
        UpdateJumpToLatestVisibility();

        // Template visuals can materialize after Opened, so retry once on the UI queue.
        Dispatcher.UIThread.Post(() =>
        {
            AttachScrollViewer();
            ScrollToBottom();
            UpdateJumpToLatestVisibility();
        }, DispatcherPriority.Background);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        AttachCollectionSubscription();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        StopSmoothScrollAnimation();

        if (_probeScrollViewer != null)
        {
            _probeScrollViewer.ScrollChanged -= OnProbeScrollChanged;
            _probeScrollViewer = null;
        }

        if (_trackedCollection != null)
        {
            _trackedCollection.CollectionChanged -= OnProbeEntriesChanged;
            _trackedCollection = null;
        }

        if (JumpToLatestButton != null)
        {
            JumpToLatestButton.Click -= JumpToLatest_Click;
        }

        UpdateJumpToLatestVisibility();
    }

    private void AttachScrollViewer()
    {
        if (_probeScrollViewer != null) return;
        if (ProbeAuditList == null) return;

        _probeScrollViewer = ProbeAuditList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_probeScrollViewer != null)
        {
            _probeScrollViewer.ScrollChanged += OnProbeScrollChanged;
        }
    }

    private void AttachCollectionSubscription()
    {
        if (_trackedCollection != null)
        {
            _trackedCollection.CollectionChanged -= OnProbeEntriesChanged;
            _trackedCollection = null;
        }

        if (DataContext is not RelayMainWindowViewModel vm) return;

        _trackedCollection = vm.ProbeAuditLineEntries;
        _trackedCollection.CollectionChanged += OnProbeEntriesChanged;
    }

    private void OnProbeEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        AttachScrollViewer();
        var shouldFollow = _autoFollowBottom || IsNearBottom();
        if (!shouldFollow)
        {
            _unseenEntryCount += CountAddedEntries(e);
            UpdateJumpToLatestVisibility();
            return;
        }

        _autoFollowBottom = true;
        _unseenEntryCount = 0;
        ScrollToBottom();

        // Content extent can update after collection notifications; snap again once layout settles.
        Dispatcher.UIThread.Post(() =>
        {
            if (_autoFollowBottom || IsNearBottom())
            {
                ScrollToBottom();
            }

            UpdateJumpToLatestVisibility();
        }, DispatcherPriority.Background);

        UpdateJumpToLatestVisibility();
    }

    private void OnProbeScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var extentGrew = Math.Abs(e.ExtentDelta.Y) > 0.01 && e.ExtentDelta.Y > 0;
        var offsetMoved = Math.Abs(e.OffsetDelta.Y) > 0.01;

        // New entries should not disable auto-follow when the user is already at the bottom.
        if (extentGrew && !offsetMoved && _autoFollowBottom)
        {
            ScrollToBottom();
            _autoFollowBottom = true;
            _unseenEntryCount = 0;
            UpdateJumpToLatestVisibility();
            return;
        }

        _autoFollowBottom = IsNearBottom();
        if (_autoFollowBottom)
        {
            _unseenEntryCount = 0;
        }
        UpdateJumpToLatestVisibility();
    }

    private bool IsNearBottom()
    {
        if (_probeScrollViewer == null) return true;

        var extent = _probeScrollViewer.Extent.Height;
        var viewport = _probeScrollViewer.Viewport.Height;
        var offset = _probeScrollViewer.Offset.Y;
        var remaining = extent - (offset + viewport);
        return remaining <= NearBottomThreshold;
    }

    private void ScrollToBottom(bool smooth = true)
    {
        AttachScrollViewer();
        if (_probeScrollViewer == null) return;

        var targetY = Math.Max(0, _probeScrollViewer.Extent.Height - _probeScrollViewer.Viewport.Height);
        var currentY = _probeScrollViewer.Offset.Y;
        if (!smooth || !IsSmoothScrollingEnabled() || Math.Abs(targetY - currentY) <= MinSmoothScrollDistance)
        {
            StopSmoothScrollAnimation();
            _probeScrollViewer.Offset = new Avalonia.Vector(_probeScrollViewer.Offset.X, targetY);
            return;
        }

        StartSmoothScroll(targetY);
    }

    private void JumpToLatest_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _autoFollowBottom = true;
        _unseenEntryCount = 0;
        ScrollToBottom(smooth: true);

        Dispatcher.UIThread.Post(() =>
        {
            ScrollToBottom(smooth: true);
            UpdateJumpToLatestVisibility();
        }, DispatcherPriority.Background);
    }

    private void StartSmoothScroll(double targetY)
    {
        if (_probeScrollViewer == null)
        {
            return;
        }

        _smoothScrollStartY = _probeScrollViewer.Offset.Y;
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
        if (_probeScrollViewer == null)
        {
            StopSmoothScrollAnimation();
            return;
        }

        var maxY = Math.Max(0, _probeScrollViewer.Extent.Height - _probeScrollViewer.Viewport.Height);
        if (_smoothScrollTargetY > maxY)
        {
            _smoothScrollTargetY = maxY;
        }

        var elapsedMs = (DateTime.UtcNow - _smoothScrollStartedAtUtc).TotalMilliseconds;
        var progress = Math.Clamp(elapsedMs / SmoothScrollDurationMs, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        var nextY = _smoothScrollStartY + ((_smoothScrollTargetY - _smoothScrollStartY) * eased);

        _probeScrollViewer.Offset = new Avalonia.Vector(_probeScrollViewer.Offset.X, nextY);

        if (progress >= 1 || Math.Abs(_smoothScrollTargetY - nextY) <= 0.5)
        {
            _probeScrollViewer.Offset = new Avalonia.Vector(_probeScrollViewer.Offset.X, _smoothScrollTargetY);
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

    private void UpdateJumpToLatestVisibility()
    {
        if (JumpToLatestButton == null)
        {
            return;
        }

        var show = !_autoFollowBottom && !IsNearBottom();
        JumpToLatestButton.IsVisible = show;

        var count = Math.Max(0, _unseenEntryCount);
        ToolTip.SetTip(
            JumpToLatestButton,
            count > 0
                ? $"Jump to latest ({count} new)"
                : "Jump to latest entries");
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
}
