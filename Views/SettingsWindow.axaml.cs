/*
    Settings window code-behind: persists window state and handles tab selection helpers.
    - Binds Topmost checkbox; saves Topmost to AppSettings.
*/
using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using ZTalk.Models;
using P2PTalk.Services;
using P2PTalk.ViewModels;

namespace P2PTalk.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        if (DataContext is SettingsViewModel vm)
            vm.CloseRequested += (_, _) => Close();
        this.Opened += (_, _) => RestoreLayoutFromCacheOrSettings(AppServices.Settings.Settings.SettingsWindow);
        this.Closing += (_, _) => SaveLayoutAndSettings(AppServices.Settings.Settings.SettingsWindow);
        // Removed runtime geometry persistence hooks to prevent frequent writes.
        // Global lock hotkey for this window
        this.AddHandler(InputElement.KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
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
            BeginMoveDrag(e);
        }
        catch { }
    }

    // [LAYOUT] Restore via cache with fallback to settings; ensure visible; keep Topmost setting applied.
    private void RestoreLayoutFromCacheOrSettings(WindowStateSettings s)
    {
        double w = Width, h = Height; var pos = Position; bool haveState = false;
        try
        {
            var cached = LayoutCache.Load("SettingsWindow");
            if (cached is not null)
            {
                if (cached.Width is double cw && cw > 0) w = cw;
                if (cached.Height is double ch && ch > 0) h = ch;
                if (cached.X is double cx && cached.Y is double cy) pos = new PixelPoint((int)cx, (int)cy);
                if (cached.State is int cst) { WindowState = (WindowState)cst; haveState = true; }
            }
            if (!haveState && s.State is int st)
            {
                WindowState = (WindowState)st;
            }
        }
        catch { }
        WindowBoundsHelper.EnsureVisible(this, ref w, ref h, ref pos);
        Width = w; Height = h; Position = pos;
        Topmost = s.Topmost ?? true;
    }
    // [LAYOUT] Save geometry to cache and persist Topmost to settings on close only.
    private void SaveLayoutAndSettings(WindowStateSettings s)
    {
        try
        {
            var layout = new LayoutCache.WindowLayout(Width, Height, Position.X, Position.Y, (int)WindowState);
            LayoutCache.Save("SettingsWindow", layout);
        }
        catch { }
        try
        {
            s.Topmost = Topmost;
            AppServices.Settings.Save(AppServices.Passphrase);
        }
        catch { }
    }

    public void SwitchToTab(string header)
    {
        var tabs = this.FindControl<TabControl>("SettingsTabs");
        if (tabs is null) return;
        foreach (var obj in tabs.Items)
        {
            if (obj is TabItem ti && string.Equals(ti.Header?.ToString(), header, StringComparison.OrdinalIgnoreCase))
            {
                tabs.SelectedItem = ti;
                break;
            }
        }
    }

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.L &&
            (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control &&
            (e.KeyModifiers & KeyModifiers.Alt) == KeyModifiers.Alt &&
            (e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift)
        {
            try { new P2PTalk.Services.LockService().Lock(); } catch { }
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        try { if (DataContext is IDisposable d) d.Dispose(); } catch { }
        try { DataContext = null; } catch { }
    }
}
