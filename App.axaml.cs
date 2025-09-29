/*
    Avalonia App bootstrap: DI-less service wiring via AppServices, global theme, and window routing.
    - Applies theme after settings are loaded post-unlock.
    - Central place for unhandled exception logging and graceful shutdown hooks.
*/
// TODO[ANCHOR]: App - Global theme application after unlock
// TODO[ANCHOR]: App - Wire service singletons via AppServices
using System;
using System.Collections.Concurrent;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using P2PTalk.Services;
using P2PTalk.Utilities;
using P2PTalk.Views;
using P2PTalk.Containers;
using System.IO;

using Sodium;

namespace P2PTalk;

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
        if (SafeMode || P2PTalk.Utilities.RuntimeFlags.SafeMode)
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
                var s = P2PTalk.Services.AppServices.Settings.Settings;
                try
                {
                    if (SafeMode || P2PTalk.Utilities.RuntimeFlags.SafeMode)
                    {
                        try { TryWriteErrorTxt("Init.Network.Start.Suppressed.SafeMode", null); } catch { }
                        return;
                    }
                    P2PTalk.Services.AppServices.Network.StartIfMajorNode(s.Port, s.MajorNode);
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
            if (ex is null) P2PTalk.Utilities.ErrorLogger.LogException(new InvalidOperationException(header), source: "Trace");
            else P2PTalk.Utilities.ErrorLogger.LogException(ex, source: header);
        }
        catch { }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Ensure AppData root is migrated (P2PTalk -> ZTalk) before any storage access
            try { P2PTalk.Utilities.AppDataPaths.MigrateIfNeeded(); } catch { }
            // Clean legacy transient cache under old root if present
            try
            {
                var legacyCache = System.IO.Path.Combine(P2PTalk.Utilities.AppDataPaths.OldRoot, ".cache");
                if (System.IO.Directory.Exists(legacyCache))
                {
                    try { System.IO.Directory.Delete(legacyCache, true); } catch { }
                }
            }
            catch { }
            // Mirror App.SafeMode to RuntimeFlags early to ensure service singletons respect it
            try { P2PTalk.Utilities.RuntimeFlags.SafeMode = SafeMode; } catch { }
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
                    P2PTalk.Utilities.ErrorLogger.LogException(e.Exception, source: "UIThread.UnhandledException");
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
                            P2PTalk.Utilities.ErrorLogger.LogException(ex, source: "AppDomain.UnhandledException");
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
                        P2PTalk.Utilities.ErrorLogger.LogException(e.Exception, source: "TaskScheduler.UnobservedTaskException");
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
                try { desktop.Exit += (_, __) => { try { P2PTalk.Services.AppServices.Shutdown(); } catch { } try { TryWriteErrorTxt("App.Exit", null); } catch { } }; } catch { }
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
                    var cacheDir = System.IO.Path.Combine(P2PTalk.Utilities.AppDataPaths.Root, ".cache");
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
                // Initialize native crypto library (libsodium)
                try { TryWriteErrorTxt("Init.Sodium.Begin", null); SodiumCore.Init(); TryWriteErrorTxt("Init.Sodium.Done", null); }
                catch (System.Exception ex) { Logger.Log($"Sodium init failed: {ex.Message}"); throw; }

                // Log intended settings path and existence (do not decrypt before unlock)
                try { TryWriteErrorTxt("Init.Settings.Path", null); } catch { }
                var path = AppServices.Settings.GetSettingsPath();
                Logger.Log($"Settings path: {path}; Exists={System.IO.File.Exists(path)}");

                // Ensure identity and start network only after unlock
                try { TryWriteErrorTxt("Init.WAN.Events", null); } catch { }
                AppServices.Crawler.DiscoveredChanged += peers => AppServices.Peers.SetDiscovered(peers);
                // [WAN] Only start WAN/seed crawler when internet is available; skip otherwise. Suppress in SafeMode.
                if (!P2PTalk.Utilities.RuntimeFlags.SafeMode)
                {
                    try
                    {
                        if (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                            AppServices.Crawler.Start();
                        else
                            Utilities.Logger.Log("WAN crawler not started: no internet connectivity detected");
                    }
                    catch { AppServices.Crawler.Start(); }
                }
                try { TryWriteErrorTxt("Init.Theme.ApplyInitial", null); } catch { }
                // Use shared theme service so live engine and selector state stay consistent
                // Apply a quick initial theme from in-memory defaults; re-apply after settings load post-unlock
                try { AppServices.Theme.SetTheme(AppServices.Settings.Settings.Theme); } catch { }
                // Apply performance settings (GPU interpolation + throttles) early when available
                try
                {
                    var sPerf = AppServices.Settings.Settings;
                    var app = Avalonia.Application.Current;
                    if (app != null)
                    {
                        app.Resources["App.AvatarInterpolation"] = sPerf.DisableGpuAcceleration ? "None" : "HighQuality";
                    }
                    // Apply FPS throttle to the central UI pulse
                    try
                    {
                        var fps = Math.Max(0, sPerf.FpsThrottle);
                        var interval = fps <= 0 ? 16 : Math.Max(5, 1000 / Math.Max(1, fps));
                        AppServices.Updates.UpdateUiInterval("App.UI.Pulse", interval);
                    }
                    catch { }
                    // Apply refresh rate throttle as a secondary pulse
                    try
                    {
                        var hz = Math.Max(0, sPerf.RefreshRateThrottle);
                        const string key = "App.UI.Refresh";
                        if (hz <= 0) AppServices.Updates.UnregisterUi(key);
                        else
                        {
                            var interval = Math.Max(5, 1000 / Math.Max(1, hz));
                            AppServices.Updates.RegisterUiInterval(key, interval, () => { try { AppServices.Events.RaiseUiPulse(); } catch { } });
                        }
                    }
                    catch { }
                    // Initialize focus-aware background framerate enforcement and apply policy immediately
                    try { FocusFramerateService.Initialize(); FocusFramerateService.ApplyCurrentPolicy(); } catch { }
                }
                catch { }
                // Onboarding: ensure account exists; if not, show AccountCreationWindow first
                try { TryWriteErrorTxt("Init.AccountCheck", null); } catch { }
                if (!AppServices.Accounts.HasAccount())
                {
                    var acw = new Views.AccountCreationWindow();
                    desktop.MainWindow = acw;
                    try { TryWriteErrorTxt("Init.ACW.Show", null); } catch { }
                    acw.Show();
                    acw.Closed += (_, __) =>
                    {
                        if (AppServices.Accounts.HasAccount())
                        {
                            // Show main window immediately to avoid UI freeze while loading resources
                            var mw = new MainWindow();
                            desktop.MainWindow = mw;
                            try { TryWriteErrorTxt("Init.MW.Show.PostCreate", null); } catch { }
                            mw.Show();
                            try
                            {
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
                            catch { }
                            // Offload settings/contacts/peers/theme to background to keep UI responsive
                            _ = System.Threading.Tasks.Task.Run(() =>
                            {
                                try { AppServices.Settings.Load(AppServices.Passphrase); } catch (System.Exception ex) { Logger.Log($"Settings load after account creation failed: {ex.Message}"); }
                                try { var persisted = AppServices.PeersStore.Load(AppServices.Passphrase); AppServices.Peers.SetDiscovered(persisted); AppServices.Peers.IncludeContacts(); } catch { }
                                try
                                {
                                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                    {
                                        try { AppServices.Theme.SetTheme(AppServices.Settings.Settings.Theme); } catch { }
                                    });
                                }
                                catch { }
                            });
                            // Subscribe to config-changed for centralized networking control
                            try
                            {
                                AppServices.Events.NetworkConfigChanged += () =>
                                {
                                    try
                                    {
                                        if (P2PTalk.Utilities.RuntimeFlags.SafeMode)
                                        {
                                            try { TryWriteErrorTxt("NetworkConfigChanged.Suppressed.SafeMode", null); } catch { }
                                            return;
                                        }
                                        var ns = AppServices.Settings.Settings;
                                        AppServices.Network.StartIfMajorNode(ns.Port, ns.MajorNode);
                                    }
                                    catch (Exception ex) { Logger.Log($"Apply network config failed: {ex.Message}"); }
                                };
                            }
                            catch { }
                            // Start networking after a short delay (decoupled from window lifecycle)
                            // [PORT-ALERT] Wire a one-shot listener to WarningRaised to show a red toast for port conflicts.
                            try
                            {
                                AppServices.Network.WarningRaised += msg =>
                                {
                                    // Heuristic: show toast for preferred-port conflict messages only; non-blocking.
                                    if (msg.Contains("Preferred port", StringComparison.OrdinalIgnoreCase))
                                    {
                                        try
                                        {
                                            var vm = mw.DataContext as ViewModels.MainWindowViewModel;
                                            if (vm != null)
                                            {
                                                vm.PortAlertText = "Port is in use. Open Network (globe) -> change Port -> re-run P2PTalk.";
                                                vm.PortAlertVisible = true;
                                            }
                                        }
                                        catch { }
                                        // Log to network.log (centralized log location)
                                            try
                                            {
                                                if (P2PTalk.Utilities.LoggingPaths.Enabled)
                                                    System.IO.File.AppendAllText(P2PTalk.Utilities.LoggingPaths.Network, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] PORT {msg}\n");
                                            }
                                            catch { }
                                    }
                                };
                            }
                            catch { }
                            try { TryWriteErrorTxt("Init.Network.Schedule.PostCreate", null); } catch { }
                            ScheduleDelayedNetworkInit();
                        }
                        else
                        {
                            // User canceled creation: exit app
                            (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
                        }
                    };
                }
                else
                {
                    // If account exists, attempt auto-unlock before showing Unlock window
                    try { TryWriteErrorTxt("Init.AutoUnlock.Check", null); } catch { }
                    var rememberPref = false;
                    try { rememberPref = AppServices.Settings.GetRememberPreference(); } catch { }
                    if (rememberPref && AppServices.Settings.TryGetRememberedPassphrase(out var remembered) && !string.IsNullOrWhiteSpace(remembered))
                    {
                        try
                        {
                            // Validate and proceed to main without showing Unlock
                            var acc = AppServices.Accounts.LoadAccount(remembered);
                            AppServices.Passphrase = remembered;
                            // Load settings now and apply theme BEFORE showing the main window to avoid a default-theme flash
                            try
                            {
                                AppServices.Settings.Load(AppServices.Passphrase);
                                // Backfill DisplayName if needed (mirrors later background path)
                                if (string.IsNullOrWhiteSpace(AppServices.Settings.Settings.DisplayName) && !string.IsNullOrWhiteSpace(acc.DisplayName))
                                {
                                    AppServices.Settings.Settings.DisplayName = acc.DisplayName;
                                    AppServices.Settings.Save(AppServices.Passphrase);
                                }
                                // Apply saved theme on UI thread (we are on UI thread here)
                                AppServices.Theme.SetTheme(AppServices.Settings.Settings.Theme);
                            }
                            catch { }
                            // Ensure identity is loaded before showing main window so UID bindings are valid
                            try { AppServices.Identity.LoadFromAccount(acc); } catch { }
                            // Show main window after theme is applied for a seamless appearance
                            var mw = new MainWindow();
                            desktop.MainWindow = mw;
                            try { TryWriteErrorTxt("Init.MW.Show.AutoUnlock", null); } catch { }
                            mw.Show();
                            try
                            {
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
                            catch { }
                            try { mw.Closed += (_, __) => TryWriteErrorTxt("MainWindow.Closed", null); } catch { }
                            // Offload identity/settings/contacts/peers to background, apply theme on UI thread
                            _ = System.Threading.Tasks.Task.Run(() =>
                            {
                                // Settings already loaded above; keep fallback in case of transient error
                                try { if (AppServices.Settings.Settings is null) AppServices.Settings.Load(AppServices.Passphrase); } catch { AppServices.Settings.ResetToDefaults(AppServices.Passphrase); }
                                try { AppServices.Contacts.Load(AppServices.Passphrase); } catch { }
                                try { var persisted = AppServices.PeersStore.Load(AppServices.Passphrase); AppServices.Peers.SetDiscovered(persisted); AppServices.Peers.IncludeContacts(); } catch { }
                                try { AppServices.Settings.SetRememberPreference(true); } catch { }
                                // Theme was applied before showing MainWindow; no UI-thread theme reapply needed here
                            });
                            // Subscribe to config-changed for centralized networking control (auto-unlock path)
                            try
                            {
                                AppServices.Events.NetworkConfigChanged += () =>
                                {
                                    try
                                    {
                                        if (P2PTalk.Utilities.RuntimeFlags.SafeMode)
                                        {
                                            try { TryWriteErrorTxt("NetworkConfigChanged.Suppressed.SafeMode", null); } catch { }
                                            return;
                                        }
                                        var ns = AppServices.Settings.Settings;
                                        AppServices.Network.StartIfMajorNode(ns.Port, ns.MajorNode);
                                    }
                                    catch (Exception ex) { Logger.Log($"Apply network config failed: {ex.Message}"); }
                                };
                            }
                            catch { }
                            // Start networking after a short delay (decoupled from window lifecycle)
                            try { TryWriteErrorTxt("Init.Network.Schedule.AutoUnlock", null); } catch { }
                            ScheduleDelayedNetworkInit();
                            // After unlock, migrate legacy conversations by normalizing UIDs and saving back
                            _ = System.Threading.Tasks.Task.Run(() =>
                            {
                                try { TryWriteErrorTxt("Migration.Messages.Begin", null); } catch { }
                                try
                                {
                                    var dir = P2PTalk.Utilities.AppDataPaths.Combine("messages");
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
                                }
                                catch (System.Exception ex)
                                {
                                    try { TryWriteErrorTxt("Migration.Messages.Error", ex); } catch { }
                                }
                                finally { try { TryWriteErrorTxt("Migration.Messages.Done", null); } catch { } }
                            });
                            return;
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"Auto-unlock with remembered passphrase failed: {ex.Message}");
                            // Fall back to manual unlock
                        }
                    }

                    // Require unlock before main window
                    var unlock = new Views.UnlockWindow();
                    // Temporarily prevent app shutdown when the Unlock window closes
                    var originalShutdown = desktop.ShutdownMode;
                    try { desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown; } catch { }
                    desktop.MainWindow = unlock;
                    try { TryWriteErrorTxt("Init.Unlock.Show", null); } catch { }
                    unlock.Closed += (_, __) =>
                    {
                        // Only proceed to main window if unlock actually succeeded
                        try
                        {
                            if (string.IsNullOrWhiteSpace(AppServices.Passphrase))
                            {
                                try { TryWriteErrorTxt("Init.Unlock.Closed.NoPassphrase", null); } catch { }
                                // Restore shutdown behavior before exiting or returning control
                                try
                                {
                                    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d0)
                                        d0.ShutdownMode = originalShutdown;
                                }
                                catch { }
                                return; // user chose Close App or unlock failed; do not show MainWindow
                            }
                        }
                        catch { }
                        // Load settings and apply theme before showing MainWindow to avoid default-theme flash
                        try
                        {
                            AppServices.Settings.Load(AppServices.Passphrase);
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                try { AppServices.Theme.SetTheme(AppServices.Settings.Settings.Theme); } catch { }
                            });
                        }
                        catch { }
                        // Ensure identity is loaded before showing MainWindow so UID bindings are valid
                        try { var acc2Sync = AppServices.Accounts.LoadAccount(AppServices.Passphrase); AppServices.Identity.LoadFromAccount(acc2Sync); } catch { }
                        // Show main window after theme apply
                        var mw = new MainWindow();
                        desktop.MainWindow = mw;
                        try { TryWriteErrorTxt("Init.MW.Show.Unlock", null); } catch { }
                        mw.Show();
                        // Restore the original shutdown behavior now that MainWindow is up
                        try
                        {
                            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d1)
                                d1.ShutdownMode = originalShutdown;
                        }
                        catch { }
                        try
                        {
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
                        catch { }
                        try { mw.Closed += (_, __) => TryWriteErrorTxt("MainWindow.Closed", null); } catch { }
                        // After unlock, load settings/identity using the correct passphrase in background
                        _ = System.Threading.Tasks.Task.Run(() =>
                        {
                            // Settings already loaded above for theme; keep background load as a fallback
                            try { if (AppServices.Settings.Settings is null) AppServices.Settings.Load(AppServices.Passphrase); } catch (System.Exception ex) { Logger.Log($"Settings load after unlock failed: {ex.Message}"); }
                            // Identity already loaded synchronously; no-op here
                            // Theme already applied above; no reapply needed here
                        });
                        // Initialize network after a short delay; keep it independent of window lifecycle
                        try { TryWriteErrorTxt("Init.Network.Schedule.Unlock", null); } catch { }
                        ScheduleDelayedNetworkInit();
                        // Centralize future network config changes: UI raises an event; app applies networking
                        try
                        {
                            AppServices.Events.NetworkConfigChanged += () =>
                            {
                                try
                                {
                                    if (P2PTalk.Utilities.RuntimeFlags.SafeMode)
                                    {
                                        try { TryWriteErrorTxt("NetworkConfigChanged.Suppressed.SafeMode", null); } catch { }
                                        return;
                                    }
                                    var ns = AppServices.Settings.Settings;
                                    AppServices.Network.StartIfMajorNode(ns.Port, ns.MajorNode);
                                }
                                catch (Exception ex) { Logger.Log($"Apply network config failed: {ex.Message}"); }
                            };
                        }
                        catch { }
                        // Load persisted peers and contacts
                        try { var persisted = AppServices.PeersStore.Load(AppServices.Passphrase); AppServices.Peers.SetDiscovered(persisted); AppServices.Peers.IncludeContacts(); } catch { }
                        // After unlock, migrate legacy conversations by normalizing UIDs and saving back
                        _ = System.Threading.Tasks.Task.Run(() =>
                        {
                            try { TryWriteErrorTxt("Migration.Messages.Begin", null); } catch { }
                            try
                            {
                                var dir = P2PTalk.Utilities.AppDataPaths.Combine("messages");
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
                            }
                            catch (System.Exception ex)
                            {
                                try { TryWriteErrorTxt("Migration.Messages.Error", ex); } catch { }
                            }
                            finally { try { TryWriteErrorTxt("Migration.Messages.Done", null); } catch { } }
                        });
                        // Theme re-application handled above on UI thread after settings load
                    };
                    unlock.Show();
                }
            }
            catch (Exception ex)
            {
                try { P2PTalk.Utilities.ErrorLogger.LogException(ex, source: "App.OnFrameworkInitializationCompleted"); } catch { }
                try { TryWriteErrorTxt("App.OnFrameworkInitializationCompleted", ex); } catch { }
                // Gracefully shutdown to avoid a hung transparent window
                try { desktop.Shutdown(); } catch { }
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
