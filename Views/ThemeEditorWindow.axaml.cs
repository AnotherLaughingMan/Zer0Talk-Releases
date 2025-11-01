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
        this.Closing += (_, _) => ClearHistoryOnClose();
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
        this.Closing += (_, _) => ClearHistoryOnClose();
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

    private void ClearHistoryOnClose()
    {
        try
        {
            if (DataContext is ThemeEditorViewModel vm)
            {
                vm.CloseHistoryPanel();
                vm.ClearHistory();
            }
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

    private void SystemAccentColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm)
        {
            vm.OpenSystemAccentColorPicker();
        }
    }

    private void SystemAccentColor2_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm)
        {
            vm.OpenSystemAccentColor2Picker();
        }
    }

    private void SystemAccentColor3_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm)
        {
            vm.OpenSystemAccentColor3Picker();
        }
    }

    private void SystemAccentColor4_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm)
        {
            vm.OpenSystemAccentColor4Picker();
        }
    }

    private void SystemAccentColorLight_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm)
        {
            vm.OpenSystemAccentColorLightPicker();
        }
    }

    private void SystemListLowColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm)
        {
            vm.OpenSystemListLowColorPicker();
        }
    }

    private void SystemListMediumColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm)
        {
            vm.OpenSystemListMediumColorPicker();
        }
    }

    private void SystemAltHighColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm)
        {
            vm.OpenSystemAltHighColorPicker();
        }
    }

    private void SystemAltMediumHighColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm)
        {
            vm.OpenSystemAltMediumHighColorPicker();
        }
    }

    private void SystemAltMediumColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm)
        {
            vm.OpenSystemAltMediumColorPicker();
        }
    }

    private void SystemAltMediumLowColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm)
        {
            vm.OpenSystemAltMediumLowColorPicker();
        }
    }

    private void SystemAltLowColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm)
        {
            vm.OpenSystemAltLowColorPicker();
        }
    }

    // SystemBase color event handlers
    private void SystemBaseHighColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm) vm.OpenSystemBaseHighColorPicker();
    }

    private void SystemBaseMediumHighColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm) vm.OpenSystemBaseMediumHighColorPicker();
    }

    private void SystemBaseMediumColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm) vm.OpenSystemBaseMediumColorPicker();
    }

    private void SystemBaseMediumLowColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm) vm.OpenSystemBaseMediumLowColorPicker();
    }

    private void SystemBaseLowColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm) vm.OpenSystemBaseLowColorPicker();
    }

    // SystemChrome color event handlers
    private void SystemChromeAltLowColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm) vm.OpenSystemChromeAltLowColorPicker();
    }

    private void SystemChromeBlackHighColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm) vm.OpenSystemChromeBlackHighColorPicker();
    }

    private void SystemChromeBlackLowColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm) vm.OpenSystemChromeBlackLowColorPicker();
    }

    private void SystemChromeBlackMediumColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm) vm.OpenSystemChromeBlackMediumColorPicker();
    }

    private void SystemChromeBlackMediumLowColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm) vm.OpenSystemChromeBlackMediumLowColorPicker();
    }

    private void SystemChromeDisabledHighColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm) vm.OpenSystemChromeDisabledHighColorPicker();
    }

    private void SystemChromeDisabledLowColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm) vm.OpenSystemChromeDisabledLowColorPicker();
    }

    private void SystemChromeGrayColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm) vm.OpenSystemChromeGrayColorPicker();
    }

    private void SystemChromeHighColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm) vm.OpenSystemChromeHighColorPicker();
    }

    private void SystemChromeLowColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm) vm.OpenSystemChromeLowColorPicker();
    }

    private void SystemChromeMediumColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm) vm.OpenSystemChromeMediumColorPicker();
    }

    private void SystemChromeMediumLowColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm) vm.OpenSystemChromeMediumLowColorPicker();
    }

    private void SystemChromeWhiteColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeEditorViewModel vm) vm.OpenSystemChromeWhiteColorPicker();
    }
}

