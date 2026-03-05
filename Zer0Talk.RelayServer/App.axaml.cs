using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Zer0Talk.RelayServer.Views;

namespace Zer0Talk.RelayServer;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Services.RelayAppServices.Initialize();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new RelayMainWindow();
            desktop.MainWindow = mainWindow;
            Services.RelayTrayHost.Initialize(mainWindow);

            if (Services.RelayAppServices.Config.StartMinimized)
            {
                mainWindow.WindowState = Avalonia.Controls.WindowState.Minimized;
                mainWindow.ShowInTaskbar = false;
                mainWindow.Hide();
            }
        }

        if (Services.RelayAppServices.Config.AutoStart)
        {
            Services.RelayAppServices.Host.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
