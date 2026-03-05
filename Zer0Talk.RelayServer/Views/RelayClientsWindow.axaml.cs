using Avalonia.Controls;
using Zer0Talk.RelayServer.Services;
using Zer0Talk.RelayServer.ViewModels;

namespace Zer0Talk.RelayServer.Views;

public partial class RelayClientsWindow : Window
{
    public RelayClientsWindow()
    {
        InitializeComponent();
    }

    private void CloseWindow_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try { Close(); } catch { }
    }

    private void BlockClient_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not RelayMainWindowViewModel vm) return;
        if (sender is not Control control || control.DataContext is not RelayClientInfo client) return;
        if (vm.BlockClientCommand.CanExecute(client)) vm.BlockClientCommand.Execute(client);
    }
}
