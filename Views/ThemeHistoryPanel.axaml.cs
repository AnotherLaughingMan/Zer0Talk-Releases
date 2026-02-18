using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Zer0Talk.Utilities;

namespace Zer0Talk.Views;

public partial class ThemeHistoryPanel : Window
{
    public ThemeHistoryPanel()
    {
        InitializeComponent();
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (WindowDragHelper.TryBeginMoveDrag(this, e))
        {
            e.Handled = true;
        }
    }

    private void OnCloseClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        Hide(); // Hide instead of close so it can be reused
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        try 
        { 
            if (DataContext is IDisposable d) 
                d.Dispose(); 
        } 
        catch { }
        try { DataContext = null; } catch { }
    }
}
