using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Zer0Talk.Services;

namespace Zer0Talk.Views;

public partial class ThemeSearchDialog : Window
{
    public ThemeSearchDialog()
    {
        InitializeComponent();
        this.Opened += (_, _) => RestoreWindowLayout();
        this.Closing += (_, _) => SaveWindowLayout();
    }

    private void RestoreWindowLayout()
    {
        try
        {
            var cached = LayoutCache.Load("ThemeSearchDialog");
            if (cached is not null)
            {
                var pos = Position;
                if (cached.X is double cx && cached.Y is double cy) 
                {
                    pos = new PixelPoint((int)cx, (int)cy);
                }
                
                double w = Width, h = Height;
                WindowBoundsHelper.EnsureVisible(this, ref w, ref h, ref pos);
                Position = pos;
            }
        }
        catch { }
    }

    private void SaveWindowLayout()
    {
        try
        {
            var layout = new LayoutCache.WindowLayout(Width, Height, Position.X, Position.Y, null);
            LayoutCache.Save("ThemeSearchDialog", layout);
        }
        catch { }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }

    private void OnCloseClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
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
