using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Zer0Talk.ViewModels;
using Zer0Talk.Services;

namespace Zer0Talk.Views;

public partial class ThemeEditorWindow : Window
{
    public ThemeEditorWindow()
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
        DataContext = new ThemeEditorViewModel();
        this.Opened += (_, _) => RestoreWindowLayout();
        this.Closing += (_, _) => SaveWindowLayout();
    }

    public ThemeEditorWindow(ThemeEditorViewModel viewModel)
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
        DataContext = viewModel;
        this.Opened += (_, _) => RestoreWindowLayout();
        this.Closing += (_, _) => SaveWindowLayout();
    }

    private void RestoreWindowLayout()
    {
        try
        {
            var cached = LayoutCache.Load("ThemeEditorWindow");
            if (cached is not null)
            {
                double w = Width, h = Height;
                var pos = Position;
                
                if (cached.Width is double cw && cw > 0) w = cw;
                if (cached.Height is double ch && ch > 0) h = ch;
                if (cached.X is double cx && cached.Y is double cy) pos = new PixelPoint((int)cx, (int)cy);
                
                WindowBoundsHelper.EnsureVisible(this, ref w, ref h, ref pos);
                Width = w;
                Height = h;
                Position = pos;
                
                if (cached.State is int cst) WindowState = (WindowState)cst;
            }
        }
        catch { }
    }

    private void SaveWindowLayout()
    {
        try
        {
            var layout = new LayoutCache.WindowLayout(Width, Height, Position.X, Position.Y, (int)WindowState);
            LayoutCache.Save("ThemeEditorWindow", layout);
        }
        catch { }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }

    private void OnMinimizeClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnCloseClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private void ColorPreview_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is ViewModels.ThemeEditorViewModel.ThemeColorEntry entry)
        {
            var vm = DataContext as ThemeEditorViewModel;
            if (vm?.EditColorCommand?.CanExecute(entry) == true)
            {
                vm.EditColorCommand.Execute(entry);
            }
        }
    }

    private void GradientStartColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm)
        {
            // Open color picker for gradient start color
            vm.OpenGradientStartColorPicker();
        }
    }

    private void GradientEndColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm)
        {
            // Open color picker for gradient end color
            vm.OpenGradientEndColorPicker();
        }
    }
}
