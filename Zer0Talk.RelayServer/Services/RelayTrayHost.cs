using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Zer0Talk.RelayServer.Services;

public static class RelayTrayHost
{
    private static TrayIcon? _tray;
    private static Window? _mainWindow;

    public static void Initialize(Window mainWindow)
    {
        _mainWindow = mainWindow;
        if (_tray != null) return;

        if (!RelayAppServices.Config.ShowInSystemTray)
            return;

        CreateTray(mainWindow);
    }

    public static void ApplyTrayVisibility(bool show)
    {
        if (show && _tray == null && _mainWindow != null)
        {
            CreateTray(_mainWindow);
        }
        else if (!show && _tray != null)
        {
            _tray.IsVisible = false;
            _tray.Dispose();
            _tray = null;
        }
    }

    private static void CreateTray(Window mainWindow)
    {
        _tray = new TrayIcon
        {
            ToolTipText = "Zer0Talk Relay",
            IsVisible = true,
            Icon = LoadIcon()
        };
        _tray.Clicked += (_, __) => ShowWindow(mainWindow);

        var menu = new NativeMenu();
        var openConsole = new NativeMenuItem("Open Console");
        openConsole.Click += (_, __) => ShowWindow(mainWindow);
        var showStatus = new NativeMenuItem("Show Status");
        showStatus.Click += (_, __) => ShowWindow(mainWindow);
        var pause = new NativeMenuItem("Pause Relay");
        pause.Click += (_, __) => RelayAppServices.Host.Pause();
        var resume = new NativeMenuItem("Resume Relay");
        resume.Click += (_, __) => RelayAppServices.Host.Resume();
        var shutdown = new NativeMenuItem("Emergency Shutdown");
        shutdown.Click += async (_, __) => await EmergencyShutdownAsync(mainWindow);
        var exitUi = new NativeMenuItem("Exit UI");
        exitUi.Click += (_, __) => ShutdownApp();

        menu.Items.Add(openConsole);
        menu.Items.Add(showStatus);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(pause);
        menu.Items.Add(resume);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(shutdown);
        menu.Items.Add(exitUi);

        _tray.Menu = menu;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit += (_, __) => _tray?.Dispose();
        }
    }

    private static WindowIcon? LoadIcon()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var icon = Path.Combine(baseDir, "Assets", "Icons", "Zer0Talk_Relay.png");
            if (File.Exists(icon)) return new WindowIcon(icon);
        }
        catch { }
        return null;
    }

    private static void ShowWindow(Window window)
    {
        try
        {
            if (!window.IsVisible) window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
            window.Focus();
            window.BringIntoView();
        }
        catch { }
    }

    private static async System.Threading.Tasks.Task EmergencyShutdownAsync(Window owner)
    {
        try
        {
            var dialog = new Views.ConfirmShutdownWindow
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var result = await dialog.ShowDialog<bool>(owner);
            if (!result) return;
        }
        catch { }

        try { RelayAppServices.Host.Stop(); } catch { }
        ShutdownApp();
    }

    private static void ShutdownApp()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
        catch { }
    }
}
