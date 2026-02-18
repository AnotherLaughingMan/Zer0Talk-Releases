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

namespace Zer0Talk.Views;

public partial class LogViewerWindow : Window
{
    private LogViewerViewModel? _viewModel;
    private LogDocumentViewModel? _activeDocument;

    public LogViewerWindow()
    {
        InitializeComponent();
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
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Dispose();
            _viewModel = null;
        }
        HookDocument(null);
        DataContextChanged -= OnDataContextChanged;
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
        else if (e.PropertyName == nameof(LogViewerViewModel.AutoscrollEnabled))
        {
            if (_viewModel?.AutoscrollEnabled == true)
            {
                QueueScrollToEnd();
            }
        }
    }

    private void HookDocument(LogDocumentViewModel? doc)
    {
        if (_activeDocument != null)
        {
            _activeDocument.PropertyChanged -= OnDocumentPropertyChanged;
        }

        _activeDocument = doc;

        if (_activeDocument != null)
        {
            _activeDocument.PropertyChanged += OnDocumentPropertyChanged;
            QueueScrollToEnd();
        }
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogDocumentViewModel.Content))
        {
            QueueScrollToEnd();
        }
    }

    private void QueueScrollToEnd()
    {
        if (_viewModel?.AutoscrollEnabled != true) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel?.AutoscrollEnabled != true) return;

            var scroll = FindActiveScrollViewer();
            scroll?.ScrollToEnd();
        }, DispatcherPriority.Background);
    }

    private ScrollViewer? FindActiveScrollViewer()
    {
        // Find the LogContentScroll ScrollViewer in the ContentControl
        return this.GetVisualDescendants()
                   .OfType<ScrollViewer>()
                   .FirstOrDefault(s => s.Name == "LogContentScroll");
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
