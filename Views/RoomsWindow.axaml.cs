using System;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using Zer0Talk.ViewModels;

namespace Zer0Talk.Views;

public partial class RoomsWindow : Window
{
    public RoomsWindow()
    {
        InitializeComponent();
        this.Closed += OnClosed;
    }

    private static void InitializeComponent(RoomsWindow w)
    {
        AvaloniaXamlLoader.Load(w);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // ─── Chrome ────────────────────────────────────────────────

    private void DragBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }
        catch { }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        try { Close(); } catch { }
    }

    // ─── Copy Room ID ──────────────────────────────────────────

    private async void CopyRoomId_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var roomId = (DataContext as RoomsViewModel)?.SelectedRoomId;
            if (string.IsNullOrEmpty(roomId)) return;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(roomId);
        }
        catch { }
    }

    // ─── Lifecycle ────────────────────────────────────────────

    private void OnClosed(object? sender, EventArgs e)
    {
        try
        {
            if (DataContext is RoomsViewModel vm)
                vm.Dispose();
            DataContext = null;
        }
        catch { }
    }
}
