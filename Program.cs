/*
    Entry point: sets up Avalonia app, service singletons, and startup flow.
    - Forces account creation if no user.p2e; otherwise shows UnlockWindow.
    - Defers settings/theme load until after unlock/account creation.
*/
// TODO[ANCHOR]: Program - Startup gating to AccountCreation/Unlock
// TODO[ANCHOR]: Program - Defer settings/theme until after unlock/account creation
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

using Avalonia;

using Zer0Talk.Services;
using Zer0Talk.Utilities;

using Sodium;

namespace Zer0Talk;

internal sealed class Program
{
    private static System.Threading.Timer? _hbTimer;
    private static int _hbCount;
    private static Mutex? _singleInstanceMutex;
    
    // Windows API for bringing existing window to front
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);
    
    private const int SW_RESTORE = 9;
    
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        bool allowMultiInstance = false;
        try
        {
            var argvEarly = args ?? Array.Empty<string>();
            allowMultiInstance = argvEarly.Any(a => string.Equals(a, "--multi-instance", StringComparison.OrdinalIgnoreCase));
        }
        catch { }

        // Ensure stable taskbar grouping and identity on Windows
        try
        {
            if (OperatingSystem.IsWindows())
            {
                SetCurrentProcessExplicitAppUserModelID(AppInfo.AppUserModelId);
            }
        }
        catch { }

        // Single-instance enforcement: prevent multiple Zer0Talk instances
        if (!allowMultiInstance)
        {
            try
            {
                var mutexName = "Global\\Zer0Talk_SingleInstance_" + Environment.UserName;
                _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
                
                if (!createdNew)
                {
                    // Another instance is already running
                    Console.WriteLine("Zer0Talk is already running. Only one instance is allowed.");
                    StartupLog("Startup.SingleInstance.AlreadyRunning");
                    
                    // Try to find and focus the existing window
                    try
                    {
                        var processes = Process.GetProcessesByName("Zer0Talk");
                        if (processes.Length == 0)
                        {
                            // Look for dotnet processes running Zer0Talk
                            processes = Process.GetProcesses()
                                .Where(p => p.ProcessName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                                .Where(p => 
                                {
                                    try
                                    {
                                        return p.MainModule?.FileName?.Contains("Zer0Talk", StringComparison.OrdinalIgnoreCase) == true ||
                                               p.MainWindowTitle?.Contains("Zer0Talk", StringComparison.OrdinalIgnoreCase) == true;
                                    }
                                    catch { return false; }
                                })
                                .ToArray();
                        }
                        
                        var existingProcess = processes.FirstOrDefault(p => p.Id != Environment.ProcessId);
                        if (existingProcess != null)
                        {
                            // Bring the existing window to front
                            try
                            {
                                if (existingProcess.MainWindowHandle != IntPtr.Zero)
                                {
                                    ShowWindow(existingProcess.MainWindowHandle, SW_RESTORE);
                                    SetForegroundWindow(existingProcess.MainWindowHandle);
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                    
                    return;
                }
            }
            catch (Exception ex)
            {
                StartupLog($"Startup.SingleInstance.Error: {ex.Message}");
                // Continue anyway - don't let mutex issues block startup
            }
        }
        else
        {
            StartupLog("Startup.MultiInstance.Enabled");
        }

        // Global exception logging: capture unhandled exceptions from UI and background threads.
        // Silent, non-intrusive logging to error.txt in the working directory.
        try
        {
            // Ensure error.txt exists as early as possible and mark entry
            StartupLog("Startup.Program.Begin");
                try { Zer0Talk.Utilities.AppDataPaths.MigrateIfNeeded(); } catch { }
            // Start 1Hz heartbeat for first 30 seconds
            try
            {
                _hbCount = 0;
                _hbTimer?.Dispose();
                _hbTimer = new System.Threading.Timer(_ =>
                {
                    try
                    {
                        var proc = Process.GetCurrentProcess();
                        var ws = proc.WorkingSet64;
                        var priv = proc.PrivateMemorySize64;
                        var gcMem = GC.GetTotalMemory(false);
                        System.Threading.ThreadPool.GetAvailableThreads(out var availW, out var availIO);
                        System.Threading.ThreadPool.GetMaxThreads(out var maxW, out var maxIO);
                        var msg = $"[Heartbeat] Memory.GC={gcMem} WS={ws} Private={priv} TP.Avail={availW}/{availIO} TP.Max={maxW}/{maxIO}";
                        StartupLog(msg);
                    }
                    catch { }
                    finally
                    {
                        var n = System.Threading.Interlocked.Increment(ref _hbCount);
                        if (n >= 30)
                        {
                            try
                            {
                                _hbTimer?.Dispose();
                            }
                            catch
                            {
                            }
                        }
                    }
                }, null, dueTime: 1000, period: 1000);
            }
            catch { }
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                ErrorLogger.LogException(ex, source: "AppDomain.UnhandledException");
                try { StartupLog("AppDomain.UnhandledException: " + (ex?.GetType().Name ?? "<null>") + (ex?.Message != null ? (" " + ex.Message) : string.Empty)); } catch { }
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                ErrorLogger.LogException(e.Exception, source: "TaskScheduler.UnobservedTaskException");
                try { e.SetObserved(); } catch { }
                try { StartupLog("TaskScheduler.UnobservedTaskException.Context sched=" + (System.Threading.Tasks.TaskScheduler.Current?.GetType().Name ?? "null")); } catch { }
                try { StartupLog("TaskScheduler.UnobservedTaskException: " + e.Exception.GetType().Name + " " + e.Exception.Message); } catch { }
            };
            // Capture FirstChanceExceptions before Avalonia initializes (earliest visibility)
            try
            {
                AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
                {
                    try { ErrorLogger.LogException(e.Exception, source: "FirstChance.Program"); } catch { }
                };
            }
            catch { }
            // Process exit: log exit code, threadpool stats, and thread count
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                try
                {
                    System.Threading.ThreadPool.GetAvailableThreads(out var availW, out var availIO);
                    System.Threading.ThreadPool.GetMaxThreads(out var maxW, out var maxIO);
                    var proc = Process.GetCurrentProcess();
                    var threads = proc.Threads?.Count ?? -1;
                    var info = $"[Process.Exit] exit={Environment.ExitCode} TP.Avail={availW}/{availIO} TP.Max={maxW}/{maxIO} Threads={threads}";
                    StartupLog(info);
                }
                catch { }
                finally 
                { 
                    ErrorLogger.FlushNow();
                    // Release single-instance mutex
                    try { _singleInstanceMutex?.ReleaseMutex(); _singleInstanceMutex?.Dispose(); } catch { }
                }
            };
            AppDomain.CurrentDomain.DomainUnload += (s, e) => ErrorLogger.FlushNow();
        }
        catch { /* best-effort wiring; never block startup */ }

        // Parse early flags (e.g., --safe-mode, --profile <name>)
        try
        {
            var argv = args ?? Array.Empty<string>();
            for (int i = 0; i < argv.Length; i++)
            {
                var a = argv[i];
                if (string.Equals(a, "--safe-mode", StringComparison.OrdinalIgnoreCase))
                {
                    App.SafeMode = true;
                    Zer0Talk.Utilities.RuntimeFlags.SafeMode = true;
                    StartupLog("Startup.SafeMode.Enabled");
                }
                else if (string.Equals(a, "--dev-ui", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(a, "--show-debug-ui", StringComparison.OrdinalIgnoreCase))
                {
                    Zer0Talk.Utilities.RuntimeFlags.ShowDebugUi = true;
                    StartupLog("Startup.DebugUi.Enabled");
                }
                else if (string.Equals(a, "--profile", StringComparison.OrdinalIgnoreCase))
                {
                    var val = (i + 1) < argv.Length ? argv[i + 1] : null;
                    Zer0Talk.Utilities.AppDataPaths.SetProfileSuffix(val);
                    if (!string.IsNullOrWhiteSpace(val)) StartupLog($"Startup.Profile={val}");
                    i++;
                }
            }
        }
        catch { }

        // Preflight: ensure crypto library is initialized; defer settings decryption until after unlock
        try { SodiumCore.Init(); }
        catch (Exception ex) { Logger.Log($"Sodium init failed (preflight): {ex.Message}"); throw; }

        if (args?.Length > 0 && string.Equals(args[0], "--init-settings", StringComparison.OrdinalIgnoreCase))
        {
            var spath = AppServices.Settings.GetSettingsPath();
            Logger.Log($"Initialized settings at {spath}");
            try
            {
                var dir = System.IO.Path.GetDirectoryName(spath)!;
                System.IO.Directory.CreateDirectory(dir);
                var probe = System.IO.Path.Combine(dir, "probe.txt");
                System.IO.File.WriteAllText(probe, DateTime.Now.ToString("O"));
                Logger.Log($"Wrote probe file: {probe}");
            }
            catch (Exception ex)
            {
                Logger.Log($"Probe write failed: {ex.Message}");
            }
            return;
        }

        // Platform/GUI availability check: prevent crashes on headless/unsupported environments.
        // Supported: Windows (interactive), macOS; Linux requires X11/Wayland (DISPLAY or WAYLAND_DISPLAY).
        if (!IsDesktopEnvironmentAvailable())
        {
            try
            {
                var ex = new PlatformNotSupportedException("Desktop GUI environment not available for Avalonia.");
                ErrorLogger.LogException(ex, source: "Startup.PlatformCheck");
            }
            catch
            {
            }

            // Exit gracefully without throwing.
            return;
        }

        // Start Avalonia; guard against failures and log before exiting.
        try
        {
            StartupLog("Startup.StartWithClassicDesktopLifetime.Begin");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args ?? Array.Empty<string>());
            // After framework initialized, attempt to set application icon from embedded .ico (best-effort, with PNG fallback)
            try
            {
                string assetsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Icons");
                string icoPath = System.IO.Path.Combine(assetsDir, "Icon.ico");
                string pngPath = System.IO.Path.Combine(assetsDir, "Icon.png");
                string? chosen = null;
                if (System.IO.File.Exists(icoPath)) chosen = icoPath; else if (System.IO.File.Exists(pngPath)) chosen = pngPath;
                if (chosen != null && Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life && life.MainWindow != null)
                {
                    try { life.MainWindow.Icon = new Avalonia.Controls.WindowIcon(chosen); } catch { }
                }
            }
            catch { }
            StartupLog("Startup.StartWithClassicDesktopLifetime.End");
        }
        catch (PlatformNotSupportedException ex)
        {
            ErrorLogger.LogException(ex, source: "Startup.StartWithClassicDesktopLifetime.PlatformNotSupported");
            StartupLog("Startup.StartWithClassicDesktopLifetime.PlatformNotSupported: " + ex.GetType().Name + " " + ex.Message);
            return;
        }
        catch (Exception ex)
        {
            ErrorLogger.LogException(ex, source: "Startup.StartWithClassicDesktopLifetime.Fatal");
            StartupLog("Startup.StartWithClassicDesktopLifetime.Fatal: " + ex.GetType().Name + " " + ex.Message);
            ErrorLogger.FlushNow();

            // Exit non-zero to signal failure when launched from console/tasks
            try
            {
                Environment.ExitCode = 1;
            }
            catch
            {
            }

            return;
        }
        finally
        {
            try
            {
                StartupLog($"[Startup.Program.End] exit={Environment.ExitCode}");
            }
            catch
            {
            }

            // Graceful service shutdown (best-effort) to release file locks (DLL/EXE) before process fully exits
            try
            {
                AppServices.Network?.Dispose();
                AppServices.Nat?.Dispose();
                AppServices.Guard?.RestoreLastCheckpoint(); // optional noop just to flush any pending state
            }
            catch { }
            try { ErrorLogger.FlushNow(); } catch { }

            try
            {
                ErrorLogger.FlushNow();
            }
            catch
            {
            }
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    // Detect whether a desktop GUI environment is available for Avalonia.
    // - Windows: require interactive session.
    // - macOS: assume GUI available when running as a user process.
    // - Linux: require DISPLAY (X11) or WAYLAND_DISPLAY (Wayland).
    private static bool IsDesktopEnvironmentAvailable()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Services and non-interactive sessions typically cannot host a GUI
                return Environment.UserInteractive;
            }
            if (OperatingSystem.IsMacOS())
            {
                // macOS user processes generally have GUI access
                return true;
            }
            if (OperatingSystem.IsLinux())
            {
                var display = Environment.GetEnvironmentVariable("DISPLAY");
                var wayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
                return !string.IsNullOrWhiteSpace(display) || !string.IsNullOrWhiteSpace(wayland);
            }
        }
        catch { }
        // Unknown or unsupported OS: treat as not available
        return false;
    }

    // Minimal helper to ensure we always write an error.txt alongside ErrorLogger for crash diagnostics.
    private static void StartupLog(string msg)
    {
        try
        {
            if (!Zer0Talk.Utilities.LoggingPaths.Enabled) return;
            var line = "[" + DateTime.Now.ToString("O") + "] " + msg;
            System.IO.File.AppendAllText(Zer0Talk.Utilities.LoggingPaths.Startup, line + Environment.NewLine);
        }
        catch { }
    }
}
