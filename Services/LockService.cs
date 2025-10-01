/*
    UI gating: coordinates non-closable, Topmost states for AccountCreation and Unlock flows.
    - Ensures main UI remains blocked until unlock succeeds.
*/
using System;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;

using ZTalk.Views;

namespace ZTalk.Services
{
    public class LockService
    {
        // Handles passphrase gating and auto-lock behavior
        public bool IsLocked { get; private set; } = true;
        public void Unlock(string passphrase)
        {
            AppServices.Passphrase = passphrase;
            IsLocked = false;
        }

        public void Lock()
        {
            IsLocked = true;
            // Do not purge remembered passphrase if user enabled Save Passphrase; respect preference for auto-login.
            try
            {
                var keep = false;
                try { keep = AppServices.Settings.GetRememberPreference(); } catch { }
                if (!keep)
                {
                    AppServices.Settings.PurgeRememberedPassphraseKeepPreference();
                }
            }
            catch { }
            AppServices.Passphrase = string.Empty;

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life)
            {
                // Create unlock window first; only close others after it is successfully shown
                UnlockWindow? unlock = null;
                var originalShutdown = life.ShutdownMode;
                var existingMain = life.MainWindow as Window;
                try
                {
                    // Ensure app doesn't exit when the last window (Unlock) closes
                    life.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
                    unlock = new UnlockWindow();
                    // Dim and block the main UI while locked
                    try
                    {
                        if (existingMain is Window mw0)
                        {
                            var view = mw0 as ZTalk.Views.MainWindow;
                            var overlay = view?.FindControl<Avalonia.Controls.Border>("LockOverlay");
                            if (overlay != null)
                            {
                                overlay.IsVisible = true;
                                try { overlay.Opacity = 1.0; } catch { }
                            }
                            // Apply background blur based on settings while locked
                            try
                            {
                                var body = view?.FindControl<Avalonia.Controls.Grid>("BodyGrid");
                                if (body != null)
                                {
                                    var r = 6;
                                    try { r = AppServices.Settings.Settings.LockBlurRadius; } catch { }
                                    if (r < 0) r = 0; if (r > 10) r = 10;
                                    if (r == 0)
                                    {
                                        body.Effect = null;
                                    }
                                    else
                                    {
                                        var effect = new BlurEffect { Radius = r };
                                        body.Effect = effect;
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                    unlock.Closed += (_, __) =>
                    {
                        // Always restore the original shutdown behavior when unlock window closes
                        try
                        {
                            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life2)
                            {
                                life2.ShutdownMode = originalShutdown;
                            }
                        }
                        catch { }

                        // Only proceed if an actual unlock occurred (non-empty passphrase).
                        if (string.IsNullOrWhiteSpace(AppServices.Passphrase))
                        {
                            return;
                        }

                        try { AppServices.Settings.Load(AppServices.Passphrase); } catch { }
                        try { var acc = AppServices.Accounts.LoadAccount(AppServices.Passphrase); AppServices.Identity.LoadFromAccount(acc); } catch { }
                        // Centralized networking: notify config changed and let app-level handler apply.
                        try { AppServices.Events.RaiseNetworkConfigChanged(); } catch { }
                        var themeService2 = new ZTalk.Services.ThemeService();
                        themeService2.SetTheme(AppServices.Settings.Settings.Theme);
                        try { AppServices.PeersClearTransientStatuses(); } catch { }
                        try { AppServices.Contacts.SetAllOffline(); } catch { }
                        try { AppServices.Contacts.ResetPresenceForUnlock(TimeSpan.FromSeconds(45)); } catch { }
                        try { AppServices.PresenceRefresh.RequestUnlockSweep(); } catch { }
                        try
                        {
                            // Restore the existing MainWindow: bring to front and hide overlay
                            if (existingMain != null)
                            {
                                try
                                {
                                    var view = existingMain as ZTalk.Views.MainWindow;
                                    var overlay = view?.FindControl<Avalonia.Controls.Border>("LockOverlay");
                                    if (overlay != null)
                                    {
                                        try { overlay.Opacity = 0.0; } catch { }
                                        // Hide after fade completes
                                        try
                                        {
                                            var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
                                            timer.Tick += (_, __) => { try { overlay.IsVisible = false; } catch { } timer.Stop(); };
                                            timer.Start();
                                        }
                                        catch { overlay.IsVisible = false; }
                                    }
                                    // Clear any blur effect applied during lock
                                    try
                                    {
                                        var body = view?.FindControl<Avalonia.Controls.Grid>("BodyGrid");
                                        if (body != null) body.Effect = null;
                                    }
                                    catch { }
                                }
                                catch { }
                                try { existingMain.Activate(); } catch { }
                            }
                            // ShutdownMode already restored above
                        }
                        catch { }
                    };
                    // Center unlock over the existing main window and keep it topmost
                    try { unlock.Topmost = true; } catch { }
                    if (existingMain != null)
                    {
                        try { unlock.WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner; } catch { }
                        unlock.Show(existingMain);
                    }
                    else
                    {
                        unlock.Show();
                    }
                }
                catch (Exception ex)
                {
                    // If we failed to show unlock, do NOT close other windows; log and bail out
                    try { ZTalk.Utilities.Logger.Log($"LockService: failed to show Unlock window: {ex.Message}"); } catch { }
                    return;
                }

                try
                {
                    // Close all other windows except unlock (snapshot to avoid modifying during enumeration)
                    var windows = life.Windows.ToList();
                    foreach (var w in windows)
                    {
                        if (w != unlock && w != existingMain)
                        {
                            try { w.Close(); } catch { }
                        }
                    }
                }
                catch { }
            }
        }
    }
}
