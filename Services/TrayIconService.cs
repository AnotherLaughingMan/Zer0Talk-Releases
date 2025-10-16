/*
    System Tray Icon Service: Manages the application system tray icon and context menu.
    - Shows/hides tray icon based on settings
    - Provides Show/Hide/Exit menu items
    - Handles tray icon click to show/restore window
*/
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using ZTalk.Views;

namespace ZTalk.Services
{
    public class TrayIconService : IDisposable
    {
        private TrayIcon? _trayIcon;
        private bool _disposed;
        private readonly IClassicDesktopStyleApplicationLifetime? _lifetime;

        public TrayIconService()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
            {
                _lifetime = lifetime;
            }
        }

        public void Initialize()
        {
            if (_trayIcon != null) return;

            try
            {
                _trayIcon = new TrayIcon
                {
                    IsVisible = false,
                    ToolTipText = "ZTalk"
                };

                // Load icon - try from assets
                try
                {
                    var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Icons", "Icon.ico");
                    if (System.IO.File.Exists(iconPath))
                    {
                        using var iconStream = System.IO.File.OpenRead(iconPath);
                        _trayIcon.Icon = new WindowIcon(iconStream);
                    }
                    else
                    {
                        Utilities.Logger.Log($"TrayIcon: Icon file not found at {iconPath}, using default");
                    }
                }
                catch (Exception ex)
                {
                    // Fallback: use default icon
                    Utilities.Logger.Log($"TrayIcon: Could not load custom icon: {ex.Message}");
                }

                // Create context menu
                var menu = new NativeMenu();

                var showItem = new NativeMenuItem { Header = "Show ZTalk" };
                showItem.Click += (s, e) => ShowMainWindow();
                menu.Add(showItem);

                menu.Add(new NativeMenuItemSeparator());

                var exitItem = new NativeMenuItem { Header = "Exit" };
                exitItem.Click += (s, e) => ExitApplication();
                menu.Add(exitItem);

                _trayIcon.Menu = menu;

                // Handle tray icon click to show window
                _trayIcon.Clicked += (s, e) => ShowMainWindow();

                Utilities.Logger.Log("TrayIcon: Initialized successfully");
            }
            catch (Exception ex)
            {
                Utilities.Logger.Log($"TrayIcon: Initialization failed: {ex.Message}");
            }
        }

        public void SetVisible(bool visible)
        {
            if (_trayIcon == null)
            {
                if (visible) Initialize();
                else return;
            }

            try
            {
                if (_trayIcon != null)
                {
                    _trayIcon.IsVisible = visible;
                    Utilities.Logger.Log($"TrayIcon: Visibility set to {visible}");
                }
            }
            catch (Exception ex)
            {
                Utilities.Logger.Log($"TrayIcon: Failed to set visibility: {ex.Message}");
            }
        }

        private void ShowMainWindow()
        {
            try
            {
                if (_lifetime?.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.Show();
                    mainWindow.WindowState = WindowState.Normal;
                    mainWindow.Activate();
                    Utilities.Logger.Log("TrayIcon: Main window restored");
                }
            }
            catch (Exception ex)
            {
                Utilities.Logger.Log($"TrayIcon: Failed to show main window: {ex.Message}");
            }
        }

        private void ExitApplication()
        {
            try
            {
                Utilities.Logger.Log("TrayIcon: Exit requested");
                
                // Perform graceful shutdown
                AppServices.Shutdown();

                // Close application
                if (_lifetime != null)
                {
                    _lifetime.Shutdown();
                }
            }
            catch (Exception ex)
            {
                Utilities.Logger.Log($"TrayIcon: Exit failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            GC.SuppressFinalize(this);

            try
            {
                if (_trayIcon != null)
                {
                    _trayIcon.IsVisible = false;
                    _trayIcon.Dispose();
                    _trayIcon = null;
                }
            }
            catch { }
        }
    }
}
