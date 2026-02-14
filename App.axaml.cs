/*
    Avalonia App bootstrap: DI-less service wiring via AppServices, global theme, and window routing.
    - Applies theme after settings are loaded post-unlock.
    - Central place for unhandled exception logging and graceful shutdown hooks.
*/
// TODO[ANCHOR]: App - Global theme application after unlock
// TODO[ANCHOR]: App - Wire service singletons via AppServices
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using Zer0Talk.Services;
using Zer0Talk.Utilities;
using Zer0Talk.Views;
using Zer0Talk.Containers;
using System.IO;

using Sodium;

namespace Zer0Talk;

public partial class App : Application
{
    // Safe mode: skip network/discovery to isolate startup problems
    public static bool SafeMode { get; set; }
    private static readonly ConcurrentDictionary<string, DateTime> _firstChanceRecent = new();
#if DEBUG
    private static System.Threading.Timer? _heartbeat;
#endif
    private static bool ShouldLogFirstChance(Exception ex)
    {
        try
        {
            // Filter expected noise: decrypt failures during discovery/network chatter
            if (ex is System.Security.Cryptography.CryptographicException &&
                (ex.StackTrace?.Contains("Sodium.SecretAeadXChaCha20Poly1305.Decrypt", StringComparison.Ordinal) == true ||
                 ex.Message.Contains("Error decrypting message", StringComparison.OrdinalIgnoreCase)))
                return false;

            // Throttle duplicates (same type+message) for a short window
            var key = ex.GetType().FullName + ":" + (ex.Message ?? string.Empty);
            var now = DateTime.UtcNow;
            if (_firstChanceRecent.TryGetValue(key, out var last) && (now - last) < TimeSpan.FromSeconds(2))
                return false;
            _firstChanceRecent[key] = now;
        }
        catch { }
        return true;
    }
    // Networking: schedule a one-shot delayed initialization so UI can settle before sockets/NAT start.
    private static bool _networkInitScheduled;
    private static void ScheduleDelayedNetworkInit()
    {
        if (SafeMode || Zer0Talk.Utilities.RuntimeFlags.SafeMode)
        {
            try { TryWriteErrorTxt("Init.Network.Skipped.SafeMode", null); } catch { }
            return;
        }
        if (_networkInitScheduled) return;
        _networkInitScheduled = true;
        // Artificial delay (1.5s) keeps launch smooth; networking remains UI-independent.
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(1500);
                try { TryWriteErrorTxt("Init.Network.Start.Begin", null); } catch { }
                var s = Zer0Talk.Services.AppServices.Settings.Settings;
                try
                {
                    if (SafeMode || Zer0Talk.Utilities.RuntimeFlags.SafeMode)
                    {
                        try { TryWriteErrorTxt("Init.Network.Start.Suppressed.SafeMode", null); } catch { }
                        return;
                    }
                    Zer0Talk.Services.AppServices.Network.StartIfMajorNode(s.Port, s.MajorNode);
                    try { TryWriteErrorTxt("Init.Network.Start.Done", null); } catch { }
                }
                catch (Exception exNet)
                {
                    try { TryWriteErrorTxt("Init.Network.Start.Error", exNet); } catch { }
                    throw;
                }
            }
            catch (Exception ex)
            {
                Utilities.Logger.Log($"Delayed network init failed: {ex.Message}");
            }
        });
    }
    public override void Initialize()
    {
        try
        {
            AvaloniaXamlLoader.Load(this);
        }
        catch (Exception ex)
        {
            try { TryWriteErrorTxt("App.Initialize.Error", ex); } catch { }
            throw;
        }
    }
    // Minimal helper to ensure we always write to error.txt via the single-writer ErrorLogger.
    private static void TryWriteErrorTxt(string header, Exception? ex)
    {
        try
        {
            if (ex is null) Zer0Talk.Utilities.ErrorLogger.LogException(new InvalidOperationException(header), source: "Trace");
            else Zer0Talk.Utilities.ErrorLogger.LogException(ex, source: header);
        }
        catch { }
    }
    
    private static void SafeStartupLog(string msg)
    {
        try
        {
            if (!Zer0Talk.Utilities.LoggingPaths.Enabled) return;
            var line = "[" + DateTime.Now.ToString("O") + "] " + msg;
            System.IO.File.AppendAllText(Zer0Talk.Utilities.LoggingPaths.Startup, line + Environment.NewLine);
        }
        catch { }
    }
    

    
    private static void ShowAppropriateWindow(IClassicDesktopStyleApplicationLifetime desktop, Views.LoadingWindow? loadingWindow)
    {
        try
        {
            SafeStartupLog("ShowAppropriateWindow.Start");
            var hasAccount = AppServices.Accounts.HasAccount();
            SafeStartupLog($"ShowAppropriateWindow.HasAccount={hasAccount}");
            
            if (!hasAccount)
            {
                // Show account creation window
                try
                {
                    SafeStartupLog("ShowAppropriateWindow.AccountCreationWindow.Creating");
                    
                    // Set shutdown mode BEFORE creating window to prevent premature app exit
                    desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
                    SafeStartupLog("ShowAppropriateWindow.ShutdownMode.Set");
                    
                    var acw = new Views.AccountCreationWindow();
                    SafeStartupLog("ShowAppropriateWindow.AccountCreationWindow.Created");
                    
                    desktop.MainWindow = acw;
                    SafeStartupLog("ShowAppropriateWindow.AccountCreationWindow.SetAsMainWindow");
                    
                    loadingWindow?.Close();
                    SafeStartupLog("ShowAppropriateWindow.LoadingWindow.Closed");
                    
                    acw.Show();
                    SafeStartupLog("ShowAppropriateWindow.AccountCreationWindow.Shown");
                    
                    acw.Closed += (_, __) =>
                    {
                        SafeStartupLog("ShowAppropriateWindow.AccountCreationWindow.Closed");
                        if (AppServices.Accounts.HasAccount())
                        {
                            SafeStartupLog("ShowAppropriateWindow.AccountCreationWindow.Closed.AccountExists");
                            ShowMainWindow(desktop);
                            SetupPostInitialization(desktop);
                        }
                        else
                        {
                            SafeStartupLog("ShowAppropriateWindow.AccountCreationWindow.Closed.NoAccount.Shutdown");
                            desktop.Shutdown();
                        }
                    };
                }
                catch (Exception ex)
                {
                    SafeStartupLog($"ShowAppropriateWindow.AccountCreationWindow.Error: {ex.GetType().Name} - {ex.Message}");
                    TryWriteErrorTxt("ShowAppropriateWindow.AccountCreationWindow.Exception", ex);
                    throw;
                }
            }
            else
            {
                // Check for auto-unlock
                var rememberPref = false;
                try { rememberPref = AppServices.Settings.GetRememberPreference(); } catch { }
                SafeStartupLog($"ShowAppropriateWindow.RememberPref={rememberPref}");
                
                if (rememberPref && AppServices.Settings.TryGetRememberedPassphrase(out var remembered) && !string.IsNullOrWhiteSpace(remembered))
                {
                    SafeStartupLog("ShowAppropriateWindow.AutoLogin.Attempting");
                    try
                    {
                        // Auto-unlock and go to main window
                        TryWriteErrorTxt("Auto-unlock attempt", null);
                        var acc = AppServices.Accounts.LoadAccount(remembered);
                        AppServices.Passphrase = remembered;
                        SafeStartupLog("ShowAppropriateWindow.AutoLogin.AccountLoaded");
                        
                        // Load settings and apply theme (LoadingManager already did crypto/audio/theme init)
                        try { AppServices.Settings.Load(AppServices.Passphrase); SafeStartupLog("ShowAppropriateWindow.AutoLogin.Settings.Loaded"); } catch (Exception ex) { SafeStartupLog($"ShowAppropriateWindow.AutoLogin.Settings.Error: {ex.Message}"); throw; }
                        
                        // Apply language setting
                        try 
                        {
                            var langCode = GetLanguageCode(AppServices.Settings.Settings.Language);
                            AppServices.Localization.LoadLanguage(langCode);
                            SafeStartupLog($"ShowAppropriateWindow.AutoLogin.Language.Applied: {langCode}");
                        } 
                        catch (Exception ex) 
                        { 
                            SafeStartupLog($"ShowAppropriateWindow.AutoLogin.Language.Error: {ex.Message}"); 
                        }
                        
                        try { AppServices.Theme.SetTheme(AppServices.Settings.Settings.Theme); SafeStartupLog("ShowAppropriateWindow.AutoLogin.Theme.Applied"); } catch (Exception ex) { SafeStartupLog($"ShowAppropriateWindow.AutoLogin.Theme.Error: {ex.Message}"); throw; }
                        
                        // Enable Theme Engine Phase 2
                        try 
                        { 
                            if (AppServices.ThemeEngine.AdvancePhase())
                            {
                                SafeStartupLog("ThemeEngine.Phase2.Enabled");
                                
                                // Load custom themes
                                var customThemeCount = AppServices.ThemeEngine.LoadCustomThemes();
                                SafeStartupLog($"ThemeEngine.Phase2.LoadedCustomThemes: {customThemeCount}");
                                
                                // Re-apply theme through engine
                                AppServices.ThemeEngine.SetTheme(AppServices.Settings.Settings.Theme);
                                SafeStartupLog("ThemeEngine.Phase2.ThemeReapplied");

                                var persistedThemeId = AppServices.Settings.Settings.ThemeId;
                                if (!string.IsNullOrWhiteSpace(persistedThemeId))
                                {
                                    if (AppServices.ThemeEngine.SetThemeById(persistedThemeId))
                                    {
                                        SafeStartupLog($"ThemeEngine.Phase2.ThemeReappliedById: {persistedThemeId}");
                                    }
                                }
                            }
                        } 
                        catch (Exception ex) 
                        { 
                            SafeStartupLog($"ThemeEngine.Phase2.Error: {ex.Message}"); 
                        }
                        
                        try { AppServices.Identity.LoadFromAccount(acc); SafeStartupLog("ShowAppropriateWindow.AutoLogin.Identity.Loaded"); } catch (Exception ex) { SafeStartupLog($"ShowAppropriateWindow.AutoLogin.Identity.Error: {ex.Message}"); throw; }
                        SafeStartupLog("ShowAppropriateWindow.AutoLogin.AllSettingsLoaded");
                        
                        // Show main window
                        SafeStartupLog("ShowAppropriateWindow.AutoLogin.ShowingMainWindow");
                        ShowMainWindow(desktop);
                        SetupPostInitialization(desktop);
                        loadingWindow?.Close();
                        SafeStartupLog("ShowAppropriateWindow.AutoLogin.Complete");
                        
                        // Load additional data in background
                        _ = System.Threading.Tasks.Task.Run(() =>
                        {
                            try { AppServices.Contacts.Load(AppServices.Passphrase); } catch { }
                            try { var persisted = AppServices.PeersStore.Load(AppServices.Passphrase); AppServices.Peers.SetDiscovered(persisted); AppServices.Peers.IncludeContacts(); } catch { }
                        });
                        
                        ScheduleDelayedNetworkInit();
                        return;
                    }
                    catch (Exception ex)
                    {
                        TryWriteErrorTxt("Auto-unlock failed", ex);
                        // Fall through to manual unlock
                    }
                }
                
                // Show unlock window
                SafeStartupLog("ShowAppropriateWindow.UnlockWindow.Creating");
                
                // Set shutdown mode BEFORE creating window to prevent premature app exit
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
                SafeStartupLog("ShowAppropriateWindow.UnlockWindow.ShutdownMode.Set");
                
                var unlock = new Views.UnlockWindow();
                SafeStartupLog("ShowAppropriateWindow.UnlockWindow.Created");
                
                desktop.MainWindow = unlock;
                SafeStartupLog("ShowAppropriateWindow.UnlockWindow.SetAsMainWindow");
                
                loadingWindow?.Close();
                SafeStartupLog("ShowAppropriateWindow.UnlockWindow.LoadingClosed");
                
                unlock.Show();
                SafeStartupLog("ShowAppropriateWindow.UnlockWindow.Shown");
                
                unlock.Closed += (_, __) =>
                {
                    if (!string.IsNullOrWhiteSpace(AppServices.Passphrase))
                    {
                        try
                        {
                            AppServices.Settings.Load(AppServices.Passphrase);
                            
                            // Apply language setting
                            try 
                            {
                                var langCode = GetLanguageCode(AppServices.Settings.Settings.Language);
                                AppServices.Localization.LoadLanguage(langCode);
                                SafeStartupLog($"Unlock.Language.Applied: {langCode}");
                            } 
                            catch (Exception ex) 
                            { 
                                SafeStartupLog($"Unlock.Language.Error: {ex.Message}"); 
                            }
                            
                            AppServices.Theme.SetTheme(AppServices.Settings.Settings.Theme);
                            
                            // Enable Theme Engine Phase 2
                            try 
                            { 
                                if (AppServices.ThemeEngine.AdvancePhase())
                                {
                                    SafeStartupLog("ThemeEngine.Phase2.Enabled");
                                    
                                    // Load custom themes
                                    var customThemeCount = AppServices.ThemeEngine.LoadCustomThemes();
                                    SafeStartupLog($"ThemeEngine.Phase2.LoadedCustomThemes: {customThemeCount}");
                                    
                                    // Re-apply theme through engine
                                    AppServices.ThemeEngine.SetTheme(AppServices.Settings.Settings.Theme);
                                    SafeStartupLog("ThemeEngine.Phase2.ThemeReapplied");

                                    var persistedThemeId = AppServices.Settings.Settings.ThemeId;
                                    if (!string.IsNullOrWhiteSpace(persistedThemeId))
                                    {
                                        if (AppServices.ThemeEngine.SetThemeById(persistedThemeId))
                                        {
                                            SafeStartupLog($"ThemeEngine.Phase2.ThemeReappliedById: {persistedThemeId}");
                                        }
                                    }
                                }
                            } 
                            catch (Exception ex) 
                            { 
                                SafeStartupLog($"ThemeEngine.Phase2.Error: {ex.Message}"); 
                            }
                            
                            var acc = AppServices.Accounts.LoadAccount(AppServices.Passphrase);
                            AppServices.Identity.LoadFromAccount(acc);
                        }
                        catch { }
                        
                        ShowMainWindow(desktop);
                        SetupPostInitialization(desktop);
                        
                        _ = System.Threading.Tasks.Task.Run(() =>
                        {
                            try { AppServices.Contacts.Load(AppServices.Passphrase); } catch { }
                            try { var persisted = AppServices.PeersStore.Load(AppServices.Passphrase); AppServices.Peers.SetDiscovered(persisted); AppServices.Peers.IncludeContacts(); } catch { }
                        });
                        
                        ScheduleDelayedNetworkInit();
                    }
                };
            }
        }
        catch (Exception ex)
        {
            TryWriteErrorTxt("ShowAppropriateWindow.Error", ex);
        }
    }
    
    private static void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            var mw = new MainWindow();
            desktop.MainWindow = mw;
            mw.Show();

            try
            {
                var settings = AppServices.Settings.Settings;
                if (settings.StartMinimized)
                {
                    if (settings.ShowInSystemTray)
                    {
                        mw.Hide();
                        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                        {
                            lifetime.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
                        }
                    }
                    else
                    {
                        mw.WindowState = Avalonia.Controls.WindowState.Minimized;
                    }
                }
            }
            catch { }
            
            // Set up window cleanup
            mw.Closed += (_, __) =>
            {
                try
                {
                    if (desktop.Windows is not null)
                    {
                        var copy = new System.Collections.Generic.List<Avalonia.Controls.Window>(desktop.Windows);
                        foreach (var w in copy)
                        {
                            try { if (w != mw) w.Close(); } catch { }
                        }
                    }
                }
                catch { }
            };
        }
        catch (Exception ex)
        {
            TryWriteErrorTxt("ShowMainWindow.Error", ex);
        }
    }
    
    private static void SetupPostInitialization(IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Set up network config change handling
        try
        {
            AppServices.Events.NetworkConfigChanged += () =>
            {
                try
                {
                    if (Zer0Talk.Utilities.RuntimeFlags.SafeMode) return;
                    var ns = AppServices.Settings.Settings;
                    AppServices.Network.StartIfMajorNode(ns.Port, ns.MajorNode);
                }
                catch (Exception ex) { Logger.Log($"Apply network config failed: {ex.Message}"); }
            };
        }
        catch { }
        
        // Initialize system tray if enabled in settings
        try
        {
            var settings = AppServices.Settings.Settings;
            if (settings.ShowInSystemTray)
            {
                AppServices.TrayIcon.Initialize();
                AppServices.TrayIcon.SetVisible(true);
                SafeStartupLog("SystemTray: Initialized and shown");
            }
            else
            {
                SafeStartupLog("SystemTray: Disabled in settings");
            }
        }
        catch (Exception ex)
        {
            try { Logger.Log($"Failed to initialize system tray on startup: {ex.Message}"); } catch { }
            SafeStartupLog($"SystemTray: Failed to initialize - {ex.Message}");
        }
        
        // Setup other post-initialization tasks as needed
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try 
            { 
                TryWriteErrorTxt("Migration.Messages.Begin", null);
                var dir = Zer0Talk.Utilities.AppDataPaths.Combine("messages");
                if (Directory.Exists(dir))
                {
                    var mc = new MessageContainer();
                    var files = Directory.GetFiles(dir, "*.p2e");
                    foreach (var f in files)
                    {
                        try
                        {
                            var peer = Path.GetFileNameWithoutExtension(f) ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(peer))
                                _ = mc.LoadMessages(peer, AppServices.Passphrase);
                        }
                        catch { }
                    }
                }
                TryWriteErrorTxt("Migration.Messages.Done", null);
            }
            catch (System.Exception ex)
            {
                TryWriteErrorTxt("Migration.Messages.Error", ex);
            }
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Ensure AppData root is migrated (old name -> Zer0Talk) before any storage access
            try { Zer0Talk.Utilities.AppDataPaths.MigrateIfNeeded(); } catch { }
            // Clean legacy transient cache under old root if present
            try
            {
                var legacyCache = System.IO.Path.Combine(Zer0Talk.Utilities.AppDataPaths.OldRoot, ".cache");
                if (System.IO.Directory.Exists(legacyCache))
                {
                    try { System.IO.Directory.Delete(legacyCache, true); } catch { }
                }
            }
            catch { }
            // Mirror App.SafeMode to RuntimeFlags early to ensure service singletons respect it
            try { Zer0Talk.Utilities.RuntimeFlags.SafeMode = SafeMode; } catch { }
            // Capture first-chance exceptions for diagnostics around click crashes
            try
            {
                System.AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
                {
                    try { if (ShouldLogFirstChance(e.Exception)) TryWriteErrorTxt("FirstChanceException", e.Exception); } catch { }
                };
                // Also capture process lifecycle events
                AppDomain.CurrentDomain.ProcessExit += (s, e) => { try { TryWriteErrorTxt("Process.Exit", null); } catch { } };
                AppDomain.CurrentDomain.DomainUnload += (s, e) => { try { TryWriteErrorTxt("AppDomain.DomainUnload", null); } catch { } };
            }
            catch { }
            // Now that Avalonia is initialized, hook UI-thread unhandled exceptions safely
            try
            {
                Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (s, e) =>
                {
                    Zer0Talk.Utilities.ErrorLogger.LogException(e.Exception, source: "UIThread.UnhandledException");
                    TryWriteErrorTxt("UIThread.UnhandledException", e.Exception);
                    e.Handled = true; // do not crash; log silently
                };
            }
            catch { }

            // Also capture non-UI thread exceptions
            try
            {
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    try
                    {
                        if (e.ExceptionObject is Exception ex)
                        {
                            Zer0Talk.Utilities.ErrorLogger.LogException(ex, source: "AppDomain.UnhandledException");
                            TryWriteErrorTxt("AppDomain.UnhandledException", ex);
                        }
                        else
                        {
                            TryWriteErrorTxt("AppDomain.UnhandledException (non-Exception)", null);
                        }
                    }
                    catch { }
                };
                System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
                {
                    try
                    {
                        Zer0Talk.Utilities.ErrorLogger.LogException(e.Exception, source: "TaskScheduler.UnobservedTaskException");
                        TryWriteErrorTxt("TaskScheduler.UnobservedTaskException", e.Exception);
                        e.SetObserved();
                    }
                    catch { }
                };
            }
            catch { }

            // Wrap the remainder of desktop initialization to catch early startup failures
            try
            {
                // Create/append a startup trace so error.txt is guaranteed to exist next to the executable
                try { TryWriteErrorTxt("App.Started", null); } catch { }
                // Log graceful exit path too
                try { desktop.Exit += (_, __) => { try { Zer0Talk.Services.AppServices.Shutdown(); } catch { } try { TryWriteErrorTxt("App.Exit", null); } catch { } }; } catch { }
#if DEBUG
                // Lightweight heartbeat (10s) already; keep it
                try
                {
                    _heartbeat?.Dispose();
                    _heartbeat = new System.Threading.Timer(_ =>
                    {
                        try { TryWriteErrorTxt("Heartbeat(App)", null); } catch { }
                    }, null, dueTime: System.TimeSpan.FromSeconds(10), period: System.TimeSpan.FromSeconds(10));
                }
                catch { }
#endif
                try { TryWriteErrorTxt("Init.Cache.Prepare", null); } catch { }
                // Prepare transient cache folder and ensure it is cleaned on startup
                try
                {
                    var cacheDir = System.IO.Path.Combine(Zer0Talk.Utilities.AppDataPaths.Root, ".cache");
                    if (System.IO.Directory.Exists(cacheDir))
                    {
                        try { System.IO.Directory.Delete(cacheDir, true); } catch { }
                    }
                    System.IO.Directory.CreateDirectory(cacheDir);
                    desktop.Exit += (_, __) =>
                    {
                        try { if (System.IO.Directory.Exists(cacheDir)) System.IO.Directory.Delete(cacheDir, true); } catch { }
                    };
                }
                catch { }
                
                // Create and show loading window
                Views.LoadingWindow? loadingWindow = null;
                try
                {
                    TryWriteErrorTxt("Init.LoadingWindow.Create", null);
                    SafeStartupLog("Init.LoadingWindow.Create");
                    loadingWindow = new Views.LoadingWindow();
                    desktop.MainWindow = loadingWindow;
                    loadingWindow.Show();
                    TryWriteErrorTxt("Init.LoadingWindow.Shown", null);
                    SafeStartupLog("Init.LoadingWindow.Shown");
                }
                catch (Exception ex)
                {
                    TryWriteErrorTxt("Init.LoadingWindow.Error", ex);
                    SafeStartupLog($"Init.LoadingWindow.Error: {ex.Message}");
                    // If loading window fails, try without it
                    loadingWindow = null;
                }
                
                // Initialize via LoadingManager on UI thread
                _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {
                        TryWriteErrorTxt("Init.LoadingManager.Begin", null);
                        SafeStartupLog("Init.LoadingManager.Begin");
                        
                        var loadingManager = new Services.LoadingManager(loadingWindow?.ViewModel);
                        await loadingManager.InitializeApplicationAsync();
                        
                        TryWriteErrorTxt("Init.LoadingManager.Complete", null);
                        SafeStartupLog("Init.LoadingManager.Complete");
                        
                        // Switch to appropriate window
                        try
                        {
                            SafeStartupLog("Init.ShowAppropriateWindow.Begin");
                            ShowAppropriateWindow(desktop, loadingWindow);
                            SafeStartupLog("Init.ShowAppropriateWindow.Complete");
                        }
                        catch (Exception ex)
                        {
                            TryWriteErrorTxt("Init.ShowWindow.Error", ex);
                            SafeStartupLog($"Init.ShowWindow.Error: {ex.Message}");
                            try { desktop.Shutdown(); } catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        TryWriteErrorTxt("Init.LoadingManager.Error", ex);
                        SafeStartupLog($"Init.LoadingManager.Error: {ex.Message}");
                        try 
                        { 
                            try { loadingWindow?.Close(); } catch { }
                            try { desktop.Shutdown(); } catch { }
                        } 
                        catch { }
                    }
                });
            }
            catch (Exception ex)
            {
                try { Zer0Talk.Utilities.ErrorLogger.LogException(ex, source: "App.OnFrameworkInitializationCompleted"); } catch { }
                try { TryWriteErrorTxt("App.OnFrameworkInitializationCompleted", ex); } catch { }
                // Gracefully shutdown to avoid a hung transparent window
                try { desktop.Shutdown(); } catch { }
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
    
    private static string GetLanguageCode(string displayName)
    {
        // Map display names to ISO 639-1 codes
        return displayName switch
        {
            "English (US)" => "en",
            "Spanish" => "es",
            "French" => "fr",
            "German" => "de",
            "Japanese" => "ja",
            "Chinese (Simplified)" => "zh-CN",
            "Chinese (Traditional)" => "zh-TW",
            "Portuguese" => "pt",
            "Russian" => "ru",
            "Italian" => "it",
            _ => "en" // Default to English
        };
    }
}
