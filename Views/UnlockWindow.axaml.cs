/*
    Unlock window code-behind: enforces non-closable Topmost until unlock succeeds.
    - Persists size/position via %AppData% sidecar JSON prior to app unlock.
    - Sidecar also consolidates "remember passphrase" preference into the same JSON (replaces remember.pref).
*/
using System;
using System.IO;
using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

using Zer0Talk.Utilities;
using Zer0Talk.ViewModels;

namespace Zer0Talk.Views
{
    public partial class UnlockWindow : Window
    {
    private bool _allowClose;
        private UnlockViewModel? _vm;

        private void HookViewModel(UnlockViewModel? vm)
        {
            try
            {
                if (_vm != null)
                {
                    _vm.CloseRequested -= OnVmCloseRequested;
                    _vm.PropertyChanged -= OnVmPropertyChanged;
                }
                _vm = vm;
                if (_vm != null)
                {
                    _vm.CloseRequested += OnVmCloseRequested;
                    _vm.PropertyChanged += OnVmPropertyChanged;
                }
            }
            catch { }
        }

        private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            try
            {
                if (e.PropertyName == nameof(UnlockViewModel.RememberPassphrase))
                {
                    var vm = _vm;
                    var tb = this.FindControl<TextBox>("PassphraseTextBox");
                    if (vm != null && tb != null)
                    {
                        if (vm.RememberPassphrase)
                        {
                            if (Zer0Talk.Services.AppServices.Settings.TryGetRememberedPassphrase(out var stored) && !string.IsNullOrEmpty(stored))
                            {
                                vm.Passphrase = stored;
                                tb.Text = stored;
                                TryShowUnlockToast("Passphrase loaded from secure storage", "\uE73E" /* Checkmark icon */);
                            }
                            else
                            {
                                TryShowUnlockToast("Remember Passphrase enabled - Passphrase will be saved after successful unlock", "\uE7BA" /* Warning icon */);
                            }
                        }
                        else
                        {
                            // Security: flush stored passphrase from secure storage when toggled off
                            try
                            {
                                Zer0Talk.Services.AppServices.Settings.ClearRememberedPassphrase();
                            }
                            catch { }
                            
                            vm.Passphrase = string.Empty;
                            tb.Text = string.Empty;
                            TryShowUnlockToast("Passphrase cleared and flushed from secure storage", "\uE946" /* Info icon */);
                        }
                    }
                }
                else if (e.PropertyName == nameof(UnlockViewModel.ErrorMessage))
                {
                    var vm = _vm;
                    if (vm != null && !string.IsNullOrWhiteSpace(vm.ErrorMessage))
                    {
                        TryShowUnlockToast(vm.ErrorMessage, "\uE783" /* Error icon */);
                    }
                }
            }
            catch { }
        }
        
            private System.Threading.CancellationTokenSource? _unlockToastCts;
            private async void TryShowUnlockToast(string text, string icon = "\uE946" /* Info icon */)
            {
                var previous = System.Threading.Interlocked.Exchange(ref _unlockToastCts, null);
                try { previous?.Cancel(); } catch { }
                previous?.Dispose();

                var cts = new System.Threading.CancellationTokenSource();
                _unlockToastCts = cts;
                try
                {
                    var border = this.FindControl<Border>("UnlockToast");
                    var tb = this.FindControl<TextBlock>("UnlockToastText");
                    var iconTb = this.FindControl<TextBlock>("UnlockToastIcon");
                    var dismissBtn = this.FindControl<Button>("UnlockToastDismiss");
                    if (border == null || tb == null) return;
                    var token = cts.Token;
                    
                    // Set content
                    tb.Text = text;
                    if (iconTb != null) iconTb.Text = icon;
                    
                    // Wire dismiss button
                    if (dismissBtn != null)
                    {
                        dismissBtn.Click -= DismissToast_Click;
                        dismissBtn.Click += DismissToast_Click;
                    }
                    
                    border.IsVisible = true;
                    try { if (Zer0Talk.Utilities.LoggingPaths.Enabled) System.IO.File.AppendAllText(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Toast][Unlock] Show: '{text}'{Environment.NewLine}"); } catch { }
                    
                    // Slide down animation (from Margin="0,-40,0,0" to "0,8,0,0")
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                    {
                        border.Margin = new Avalonia.Thickness(0, 8, 0, 0);
                        border.Opacity = 1.0;
                    });
                    
                    // Display for 3 seconds (better readability)
                    await System.Threading.Tasks.Task.Delay(3000, token);
                    if (!token.IsCancellationRequested)
                    {
                        // Fade out
                        border.Opacity = 0.0;
                        await System.Threading.Tasks.Task.Delay(200, token);
                        if (!token.IsCancellationRequested)
                        {
                            border.IsVisible = false;
                            // Reset position for next show
                            border.Margin = new Avalonia.Thickness(0, -40, 0, 0);
                        }
                        try { if (Zer0Talk.Utilities.LoggingPaths.Enabled) System.IO.File.AppendAllText(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Toast][Unlock] Auto-hide{Environment.NewLine}"); } catch { }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Swallow cancellation
                }
                catch { }
                finally
                {
                    if (System.Threading.Interlocked.CompareExchange(ref _unlockToastCts, null, cts) == cts)
                    {
                        // field cleared
                    }
                    cts.Dispose();
                }
            }
            
            private void DismissToast_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
            {
                try
                {
                    var cts = _unlockToastCts;
                    if (cts != null)
                    {
                        cts.Cancel();
                    }
                    var border = this.FindControl<Border>("UnlockToast");
                    if (border != null)
                    {
                        border.Opacity = 0.0;
                        border.IsVisible = false;
                        border.Margin = new Avalonia.Thickness(0, -40, 0, 0);
                    }
                    try { if (Zer0Talk.Utilities.LoggingPaths.Enabled) System.IO.File.AppendAllText(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Toast][Unlock] User dismissed{Environment.NewLine}"); } catch { }
                }
                catch { }
            }

        private void OnVmCloseRequested(object? sender, EventArgs e)
        {
            _allowClose = true;
            try
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try { Close(); } catch { }
                });
            }
            catch { }
        }
        public UnlockWindow()
        {
            InitializeComponent();
            // Robustly hook CloseRequested regardless of when DataContext is assigned
            HookViewModel(DataContext as UnlockViewModel);
            this.DataContextChanged += (_, __) => HookViewModel(DataContext as UnlockViewModel);
            this.Opened += (_, __) => RestorePlainState();
            this.Opened += (_, __) =>
            {
                try
                {
                    var enabled = Zer0Talk.Services.AppServices.Settings?.Settings?.BlockScreenCapture ?? true;
                    Zer0Talk.Services.ScreenCaptureProtection.SetExcludeFromCapture(this, enabled);
                }
                catch { }
            };
            // Log spacing/layout correction (debug builds only via LoggingPaths policy)
            try { Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.Theme, $"[{DateTime.UtcNow:O}] unlock.layout spacing=12px buttons updated\n"); } catch { }
            try { Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.Theme, $"[{DateTime.UtcNow:O}] unlock.toggles labels=restored\n"); } catch { }
            this.Closing += (s, e) =>
            {
                if (!_allowClose)
                {
                    try { this.Topmost = true; this.Activate(); } catch { }
                    e.Cancel = true;
                    return;
                }
                SavePlainState();
            };
            this.KeyDown += UnlockWindow_KeyDown;
            // Reflect remember preference into the textbox: prefill if enabled, clear if disabled
            try
            {
                var tb = this.FindControl<TextBox>("PassphraseTextBox");
                var remember = Zer0Talk.Services.AppServices.Settings.GetRememberPreference();
                if (remember)
                {
                    if (Zer0Talk.Services.AppServices.Settings.TryGetRememberedPassphrase(out var stored) && !string.IsNullOrEmpty(stored))
                    {
                        if (DataContext is Zer0Talk.ViewModels.UnlockViewModel uvm)
                        {
                            uvm.Passphrase = stored;
                            uvm.RememberPassphrase = true;
                        }
                        if (tb != null) tb.Text = stored;
                    }
                }
                else
                {
                    if (DataContext is Zer0Talk.ViewModels.UnlockViewModel uvm)
                    {
                        uvm.Passphrase = string.Empty;
                        uvm.RememberPassphrase = false;
                    }
                    if (tb != null) tb.Text = string.Empty;
                }
            }
            catch { }
        }

        private void UnlockWindow_KeyDown(object? sender, KeyEventArgs e)
        {
            // No modal overlay to close; Esc just returns focus to passphrase box
            if (e.Key == Key.Escape)
            {
                try
                {
                    var tb = this.FindControl<TextBox>("PassphraseTextBox");
                    tb?.Focus();
                }
                catch { }
            }
        }

        private async void LostPassphrase_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try { Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.Theme, $"[{DateTime.UtcNow:O}] unlock.launch lostpassphrase.dialog\n"); } catch { }
            var dlg = new LostPassphraseDialog
            {
                Topmost = true,
                DataContext = this.DataContext // share same VM for recovery operations
            };
            await dlg.ShowDialog(this);
            try { Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.Theme, $"[{DateTime.UtcNow:O}] unlock.returnFrom lostpassphrase.dialog\n"); } catch { }
        }

        private void CloseApp_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            // Explicit user intent to exit: allow closing and shut down the app
            _allowClose = true;
            try
            {
                // Ensure we don't consider this as an unlock; clear passphrase.
                try { Zer0Talk.Services.AppServices.Passphrase = string.Empty; } catch { }
                // Close this window and shut down the application lifetime
                Close();
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
                {
                    life.Shutdown();
                }
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                HookViewModel(null);
                var cts = System.Threading.Interlocked.Exchange(ref _unlockToastCts, null);
                if (cts != null)
                {
                    try { cts.Cancel(); } catch { }
                    cts.Dispose();
                }
            }
            catch { }
            base.OnClosed(e);
        }

        // [DRAGBAR] Begin moving window on left-click; ignore clicks on interactive controls.
        private void DragBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                var point = e.GetCurrentPoint(this);
                if (!point.Properties.IsLeftButtonPressed) return;
                if (e.Source is Avalonia.Visual c)
                {
                    if (c is Button || c.FindAncestorOfType<Button>() != null) return;
                    if (c is TextBox || c.FindAncestorOfType<TextBox>() != null) return;
                }
                if (WindowDragHelper.TryBeginMoveDrag(this, e))
                {
                    e.Handled = true;
                }
            }
            catch { }
        }

        private string GetStatePath()
        {
            var dir = Zer0Talk.Utilities.AppDataPaths.Root;
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "unlock.window.json");
        }

        private void RestorePlainState()
        {
            try
            {
                var path = GetStatePath();
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path);
                var s = JsonSerializer.Deserialize<PlainState>(json);
                if (s == null) return;
                if (s.Width is double w && w > 0) Width = w;
                if (s.Height is double h && h > 0) Height = h;
                if (s.X is double x && s.Y is double y) Position = new Avalonia.PixelPoint((int)x, (int)y);
                if (DataContext is UnlockViewModel vm && s.RememberPreference is bool rp) vm.RememberPassphrase = rp; // restore remember preference
            }
            catch { }
        }

        private void SavePlainState()
        {
            try
            {
                // Load existing to preserve RememberPreference if present
                var path = GetStatePath();
                PlainState? existing = null;
                try { if (File.Exists(path)) existing = JsonSerializer.Deserialize<PlainState?>(File.ReadAllText(path)); } catch { }
                var s = new PlainState
                {
                    Width = Width,
                    Height = Height,
                    X = Position.X,
                    Y = Position.Y,
                    RememberPreference = (DataContext as UnlockViewModel)?.RememberPassphrase ?? existing?.RememberPreference
                };
                var json = JsonSerializer.Serialize(s, SerializationDefaults.Indented);
                File.WriteAllText(path, json);
            }
            catch { }
        }

        private sealed class PlainState
        {
            public double? Width { get; set; }
            public double? Height { get; set; }
            public double? X { get; set; }
            public double? Y { get; set; }
            public bool? RememberPreference { get; set; }
        }
    }
}
