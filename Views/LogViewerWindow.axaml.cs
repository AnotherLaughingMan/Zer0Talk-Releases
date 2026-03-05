using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Zer0Talk.ViewModels;
using Zer0Talk.Services;
using Zer0Talk.Models;
using Zer0Talk.Utilities;
using System.Collections.Specialized;

namespace Zer0Talk.Views;

public partial class LogViewerWindow : Window
{
    private const double NearBottomThreshold = 24;
    private const double SmoothScrollDurationMs = 180;
    private const double MinSmoothScrollDistance = 8;
    private LogViewerViewModel? _viewModel;
    private LogDocumentViewModel? _activeDocument;
    private ScrollViewer? _activeScrollViewer;
    private bool _autoFollowBottom = true;
    private int _unseenEntryCount;
    private DispatcherTimer? _smoothScrollTimer;
    private double _smoothScrollStartY;
    private double _smoothScrollTargetY;
    private DateTime _smoothScrollStartedAtUtc;

    public LogViewerWindow()
    {
        InitializeComponent();
        if (JumpToLatestButton != null)
        {
            JumpToLatestButton.Click += JumpToLatest_Click;
        }
        AttachToViewModel(DataContext as LogViewerViewModel);
        DataContextChanged += OnDataContextChanged;
        Opened += OnOpened;
        Closed += OnClosed;

        try
        {
            var s = AppServices.Settings.Settings.LogViewerWindow;
            RestoreLayoutFromCacheOrSettings(s);
            this.Opened += (_, _) => RestoreLayoutFromCacheOrSettings(s);
            this.Closing += (_, _) => SaveLayoutAndSettings(s);
        }
        catch
        {
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.RefreshAsync();
        }

        AttachScrollViewer();
        _autoFollowBottom = true;
        _unseenEntryCount = 0;
        ScrollToBottom();
        UpdateJumpToLatestVisibility();

        Dispatcher.UIThread.Post(() =>
        {
            AttachScrollViewer();
            ScrollToBottom();
            UpdateJumpToLatestVisibility();
        }, DispatcherPriority.Background);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        StopSmoothScrollAnimation();

        if (_activeScrollViewer != null)
        {
            _activeScrollViewer.ScrollChanged -= OnLogScrollChanged;
            _activeScrollViewer = null;
        }

        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Dispose();
            _viewModel = null;
        }
        HookDocument(null);
        DataContextChanged -= OnDataContextChanged;

        if (JumpToLatestButton != null)
        {
            JumpToLatestButton.Click -= JumpToLatest_Click;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        AttachToViewModel(DataContext as LogViewerViewModel);
    }

    private void AttachToViewModel(LogViewerViewModel? vm)
    {
        if (_viewModel == vm) return;

        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        HookDocument(null);
        _viewModel = vm;

        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            HookDocument(_viewModel.SelectedTab);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogViewerViewModel.SelectedTab))
        {
            HookDocument(_viewModel?.SelectedTab);
        }
    }

    private void HookDocument(LogDocumentViewModel? doc)
    {
        if (_activeDocument != null)
        {
            _activeDocument.PropertyChanged -= OnDocumentPropertyChanged;
            _activeDocument.LinesAppended -= OnDocumentLinesAppended;
        }

        _activeDocument = doc;

        if (_activeDocument != null)
        {
            _activeDocument.PropertyChanged += OnDocumentPropertyChanged;
            _activeDocument.LinesAppended += OnDocumentLinesAppended;
            _autoFollowBottom = true;
            _unseenEntryCount = 0;
            QueueScrollToEnd();
            UpdateJumpToLatestVisibility();
        }
        else
        {
            _unseenEntryCount = 0;
            _autoFollowBottom = true;
            UpdateJumpToLatestVisibility();
        }
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogDocumentViewModel.Content))
        {
            if (_autoFollowBottom || IsNearBottom())
            {
                _autoFollowBottom = true;
                _unseenEntryCount = 0;
                QueueScrollToEnd();
                UpdateJumpToLatestVisibility();
            }
        }
    }

    private void OnDocumentLinesAppended(object? sender, int addedLines)
    {
        AttachScrollViewer();
        var shouldFollow = _autoFollowBottom || IsNearBottom();
        if (!shouldFollow)
        {
            _unseenEntryCount += Math.Max(0, addedLines);
            UpdateJumpToLatestVisibility();
            return;
        }

        _autoFollowBottom = true;
        _unseenEntryCount = 0;
        QueueScrollToEnd();
        UpdateJumpToLatestVisibility();
    }

    private void QueueScrollToEnd()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ScrollToBottom(smooth: true);
            UpdateJumpToLatestVisibility();
        }, DispatcherPriority.Background);
    }

    private void AttachScrollViewer()
    {
        var next = FindActiveScrollViewer();
        if (ReferenceEquals(_activeScrollViewer, next))
        {
            return;
        }

        if (_activeScrollViewer != null)
        {
            _activeScrollViewer.ScrollChanged -= OnLogScrollChanged;
        }

        _activeScrollViewer = next;
        if (_activeScrollViewer != null)
        {
            _activeScrollViewer.ScrollChanged += OnLogScrollChanged;
        }
    }

    private ScrollViewer? FindActiveScrollViewer()
    {
        // Find the LogContentScroll ScrollViewer in the ContentControl
        return this.GetVisualDescendants()
                   .OfType<ScrollViewer>()
                   .FirstOrDefault(s => s.Name == "LogContentScroll");
    }

    private void OnLogScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var extentGrew = Math.Abs(e.ExtentDelta.Y) > 0.01 && e.ExtentDelta.Y > 0;
        var offsetMoved = Math.Abs(e.OffsetDelta.Y) > 0.01;

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
        AttachScrollViewer();
        if (_activeScrollViewer == null) return true;

        var extent = _activeScrollViewer.Extent.Height;
        var viewport = _activeScrollViewer.Viewport.Height;
        var offset = _activeScrollViewer.Offset.Y;
        var remaining = extent - (offset + viewport);
        return remaining <= NearBottomThreshold;
    }

    private void ScrollToBottom(bool smooth = true)
    {
        AttachScrollViewer();
        if (_activeScrollViewer == null) return;

        var targetY = Math.Max(0, _activeScrollViewer.Extent.Height - _activeScrollViewer.Viewport.Height);
        var currentY = _activeScrollViewer.Offset.Y;
        if (!smooth || !IsSmoothScrollingEnabled() || Math.Abs(targetY - currentY) <= MinSmoothScrollDistance)
        {
            StopSmoothScrollAnimation();
            _activeScrollViewer.Offset = new Avalonia.Vector(_activeScrollViewer.Offset.X, targetY);
            return;
        }

        StartSmoothScroll(targetY);
    }

    private void JumpToLatest_Click(object? sender, RoutedEventArgs e)
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
        if (_activeScrollViewer == null)
        {
            return;
        }

        _smoothScrollStartY = _activeScrollViewer.Offset.Y;
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
        if (_activeScrollViewer == null)
        {
            StopSmoothScrollAnimation();
            return;
        }

        var maxY = Math.Max(0, _activeScrollViewer.Extent.Height - _activeScrollViewer.Viewport.Height);
        if (_smoothScrollTargetY > maxY)
        {
            _smoothScrollTargetY = maxY;
        }

        var elapsedMs = (DateTime.UtcNow - _smoothScrollStartedAtUtc).TotalMilliseconds;
        var progress = Math.Clamp(elapsedMs / SmoothScrollDurationMs, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        var nextY = _smoothScrollStartY + ((_smoothScrollTargetY - _smoothScrollStartY) * eased);

        _activeScrollViewer.Offset = new Avalonia.Vector(_activeScrollViewer.Offset.X, nextY);

        if (progress >= 1 || Math.Abs(_smoothScrollTargetY - nextY) <= 0.5)
        {
            _activeScrollViewer.Offset = new Avalonia.Vector(_activeScrollViewer.Offset.X, _smoothScrollTargetY);
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
            return AppServices.Settings.Settings.EnableSmoothScrolling;
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
        if (JumpToLatestCountBadge != null)
        {
            JumpToLatestCountBadge.IsVisible = count > 0;
        }

        if (JumpToLatestCountText != null)
        {
            JumpToLatestCountText.Text = count > 0 ? count.ToString() : "0";
        }

        var jumpToBottomText = AppServices.Localization.GetString("LogViewer.JumpToBottom", "Jump to bottom");
        ToolTip.SetTip(
            JumpToLatestButton,
            count > 0
            ? $"{jumpToBottomText} ({count} new)"
            : jumpToBottomText);
    }

    private void DragBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed) return;
            if (e.Source is Control control)
            {
                if (control.FindAncestorOfType<Button>() != null) return;
                if (control.FindAncestorOfType<TextBox>() != null) return;
            }
            if (WindowDragHelper.TryBeginMoveDrag(this, e))
            {
                e.Handled = true;
            }
        }
        catch
        {
        }
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e)
    {
        try { WindowState = WindowState.Minimized; } catch { }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        try { Close(); } catch { }
    }

    private void RestoreLayoutFromCacheOrSettings(WindowStateSettings settings)
    {
        double width = Width;
        double height = Height;
        var position = Position;
        bool haveState = false;
        try
        {
            var cached = LayoutCache.Load("LogViewerWindow");
            if (cached is not null)
            {
                if (cached.Width is double cw && cw > 0) width = cw;
                if (cached.Height is double ch && ch > 0) height = ch;
                if (cached.X is double cx && cached.Y is double cy)
                    position = new PixelPoint((int)cx, (int)cy);
                if (cached.State is int cs)
                {
                    WindowState = (WindowState)cs;
                    haveState = true;
                }
            }

            if (!haveState && settings.State is int st)
            {
                WindowState = (WindowState)st;
            }
        }
        catch
        {
        }

        WindowBoundsHelper.EnsureVisible(this, ref width, ref height, ref position);
        Width = width;
        Height = height;
        Position = position;
    }

    private void SaveLayoutAndSettings(WindowStateSettings settings)
    {
        try
        {
            var layout = new LayoutCache.WindowLayout(Width, Height, Position.X, Position.Y, (int)WindowState);
            LayoutCache.Save("LogViewerWindow", layout);
        }
        catch
        {
        }

        try
        {
            settings.State = (int)WindowState;
            AppServices.Settings.Save(AppServices.Passphrase);
        }
        catch
        {
        }
    }
}
