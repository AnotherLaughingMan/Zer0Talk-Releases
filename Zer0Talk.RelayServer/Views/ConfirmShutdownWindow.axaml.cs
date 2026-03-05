using Avalonia.Controls;

namespace Zer0Talk.RelayServer.Views;

public partial class ConfirmShutdownWindow : Window
{
    public ConfirmShutdownWindow()
    {
        InitializeComponent();
    }

    private void OnStopClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(false);
    }
}
