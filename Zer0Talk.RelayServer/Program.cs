using System;
using System.Threading.Tasks;
using Avalonia;
using Zer0Talk.RelayServer.Services;

namespace Zer0Talk.RelayServer;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        RelayDiagnostics.LogInfo("Program.Main", "Relay process starting");
        RegisterGlobalDiagnostics();

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args ?? Array.Empty<string>());
            RelayDiagnostics.LogInfo("Program.Main", "Relay process exited cleanly");
        }
        catch (Exception ex)
        {
            RelayDiagnostics.LogException("Program.Main", ex, markCrash: true);
            throw;
        }
    }

    private static void RegisterGlobalDiagnostics()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            RelayDiagnostics.LogException(
                "AppDomain.UnhandledException",
                e.ExceptionObject as Exception,
                markCrash: true);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            RelayDiagnostics.LogException("TaskScheduler.UnobservedTaskException", e.Exception, markCrash: false);
            try { e.SetObserved(); } catch { }
        };

        AppDomain.CurrentDomain.ProcessExit += (_, __) =>
        {
            RelayDiagnostics.LogInfo("AppDomain.ProcessExit", "Process exit event observed");
        };

        AppDomain.CurrentDomain.DomainUnload += (_, __) =>
        {
            RelayDiagnostics.LogInfo("AppDomain.DomainUnload", "Domain unload event observed");
        };
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
