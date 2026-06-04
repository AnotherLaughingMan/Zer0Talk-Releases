using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Zer0Talk.RelayServer.Views;

namespace Zer0Talk.RelayServer;

public partial class App : Application
{
    private static bool _uiExceptionHandlerInstalled;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Services.RelayAppServices.Initialize();
        InstallUiExceptionHandler();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new RelayMainWindow();
            desktop.MainWindow = mainWindow;
            Services.RelayTrayHost.Initialize(mainWindow);

            if (Services.RelayDiagnostics.TryConsumeCrashMarker(out var crashSummary))
            {
                Services.RelayDiagnostics.LogInfo("App.Startup", $"Previous crash marker detected: {crashSummary}");
                Services.RelayDiagnostics.SetStartupAlert(crashSummary);
            }
        }

        if (Services.RelayAppServices.Config.AutoStart)
        {
            Services.RelayAppServices.Host.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void InstallUiExceptionHandler()
    {
        if (_uiExceptionHandlerInstalled)
        {
            return;
        }

        _uiExceptionHandlerInstalled = true;
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Services.RelayDiagnostics.LogException("UIThread.UnhandledException", e.Exception, markCrash: true);
            e.Handled = true;
        };
    }
}
