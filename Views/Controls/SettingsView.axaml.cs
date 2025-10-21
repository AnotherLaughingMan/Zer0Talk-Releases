using System;
using System.Collections;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml;

namespace Zer0Talk.Views.Controls
{
    public partial class SettingsView : UserControl
    {
        private ListBoxItem? _debugMenuItem;

        public SettingsView()
        {
            InitializeComponent();
#if DEBUG
            EnableDebugPanel();
#endif
            this.AttachedToVisualTree += (_, __) => WireMenu();
            this.AttachedToVisualTree += (_, __) => WireDiscoveredPeersList();
            // Ensure we re-wire when the DataContext changes so bindings/events react to the current VM
            this.DataContextChanged += (_, __) => { WireMenu(); WireDiscoveredPeersList(); };
#if true
            this.AttachedToVisualTree += (_, __) => WireLegacyThemeCombo();
#endif
#if DEBUG
            // Inject Debug-only CCD simulation UI
            this.AttachedToVisualTree += (_, __) => AddDebugCcdSimulator();
#endif
        }

        private void WireDiscoveredPeersList()
        {
            var listBox = this.FindControl<ListBox>("DiscoveredPeersList");
            if (listBox == null) return;

            // Avoid duplicate handlers if re-wired
            listBox.SelectionChanged -= DiscoveredPeersList_SelectionChanged;
            listBox.SelectionChanged += DiscoveredPeersList_SelectionChanged;

            // Sync initial selection to the VM if needed
            try
            {
                if (DataContext is Zer0Talk.ViewModels.SettingsViewModel vm && listBox.SelectedItems != null)
                {
                    var selected = new System.Collections.Generic.List<Zer0Talk.Models.Peer>();
                    foreach (var item in listBox.SelectedItems)
                        if (item is Zer0Talk.Models.Peer peer) selected.Add(peer);

                    var netVm = typeof(Zer0Talk.ViewModels.SettingsViewModel)
                        .GetField("NetworkVm", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        ?.GetValue(vm);

                    if (netVm != null)
                    {
                        var selectedPeersProp = netVm.GetType().GetProperty("SelectedPeers");
                        selectedPeersProp?.SetValue(netVm, selected);
                    }
                }
            }
            catch { }
        }

        private void DiscoveredPeersList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                var listBox = sender as ListBox;
                if (listBox == null) return;
                if (!(DataContext is Zer0Talk.ViewModels.SettingsViewModel vm)) return;

                var selected = new System.Collections.Generic.List<Zer0Talk.Models.Peer>();
                if (listBox.SelectedItems != null)
                {
                    foreach (var item in listBox.SelectedItems)
                        if (item is Zer0Talk.Models.Peer peer) selected.Add(peer);
                }

                var netVm = typeof(Zer0Talk.ViewModels.SettingsViewModel)
                    .GetField("NetworkVm", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.GetValue(vm);

                if (netVm != null)
                {
                    var selectedPeersProp = netVm.GetType().GetProperty("SelectedPeers");
                    selectedPeersProp?.SetValue(netVm, selected);
                }
            }
            catch { }
        }

        private void WireLegacyThemeCombo()
        {
            try
            {
                var combo = this.FindControl<ComboBox>("LegacyThemeCombo");
                if (combo == null) return;

                // Avoid duplicate handlers
                combo.SelectionChanged -= LegacyThemeCombo_SelectionChanged;
                combo.SelectionChanged += LegacyThemeCombo_SelectionChanged;
            }
            catch { }
        }

        private void LegacyThemeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (DataContext is Zer0Talk.ViewModels.SettingsViewModel vm)
                {
                    var combo = sender as ComboBox;
                    if (combo?.SelectedItem is Zer0Talk.ViewModels.SettingsViewModel.LegacyThemeOption opt)
                    {
                        vm.SelectedLegacyThemeId = opt.ThemeId ?? string.Empty;
                    }
                    else
                    {
                        vm.SelectedLegacyThemeId = string.Empty;
                    }
                }
            }
            catch { }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // [LAYOUT] Wire left menu selection to show corresponding panel on the right.
        private void WireMenu()
        {
            var menu = this.FindControl<ListBox>("MenuList");
            if (menu == null) return;
            menu.SelectionChanged += (_, __) =>
            {
                var idx = menu.SelectedIndex;
                UpdatePanels(idx);
                try
                {
                    var s = Zer0Talk.Services.AppServices.Settings.Settings;
                    var max = HasDebugPanel() ? 9 : 8;
                    if (idx >= 0 && idx <= max && s.LastSettingsMenuIndex != idx)
                    {
                        s.LastSettingsMenuIndex = idx;
                        // Persist asynchronously to avoid UI stalls
                        _ = System.Threading.Tasks.Task.Run(() => Zer0Talk.Services.AppServices.Settings.Save(Zer0Talk.Services.AppServices.Passphrase));
                    }
                }
                catch { }
            };
            // Restore last selected menu (default Profile index 2) and update panels
            try
            {
                var saved = Zer0Talk.Services.AppServices.Settings.Settings.LastSettingsMenuIndex;
                var max = HasDebugPanel() ? 9 : 8;
                if (saved < 0 || saved > max) saved = Math.Min(2, max);
                menu.SelectedIndex = saved;
            }
            catch
            {
                if (menu.SelectedIndex < 0) menu.SelectedIndex = 2;
            }
            UpdatePanels(menu.SelectedIndex);
            // Force data-bound controls to re-sync with the VM after initial layout to avoid default index glitches.
            try
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var combo = this.FindControl<ComboBox>("ThemeCombo");
                        if (combo != null && DataContext is Zer0Talk.ViewModels.SettingsViewModel vm)
                        {
                            // Reapply the VM value to ensure UI reflects saved theme if the control defaulted to 0.
                            combo.SelectedIndex = vm.ThemeIndex;
                        }
                    }
                    catch { }
                });
            }
            catch { }
        }

        // [LAYOUT] Simple panel switcher with explicit visibility toggles.
    private void UpdatePanels(int index)
        {
            var profile = this.FindControl<ScrollViewer>("ProfilePanel");
            var general = this.FindControl<ScrollViewer>("GeneralPanel");
            var hotkeys = this.FindControl<ScrollViewer>("HotkeysPanel");
            var appearance = this.FindControl<ScrollViewer>("AppearancePanel");
            var network = this.FindControl<ScrollViewer>("NetworkPanel");
            var logout = this.FindControl<ScrollViewer>("LogoutPanel");
            var performance = this.FindControl<ScrollViewer>("PerformancePanel");
            var accessibility = this.FindControl<ScrollViewer>("AccessibilityPanel");
            var about = this.FindControl<ScrollViewer>("AboutPanel");
            var danger = this.FindControl<ScrollViewer>("DangerPanel");
            var debugPanel = this.FindControl<ScrollViewer>("DebugPanel");
            // Don't return early if only network panel is missing - allow other panels to show
            if (profile == null || general == null || hotkeys == null || appearance == null || logout == null || performance == null || accessibility == null || about == null || danger == null) return;
            var hasDebug = HasDebugPanel() && debugPanel != null;
            var maxIndex = hasDebug ? 11 : 10;
            if (index < 0) index = 0;
            if (index > maxIndex) index = maxIndex;
            // Order: Appearance(0), General(1), Hotkeys(2), Profile(3), Network(4), Performance(5), Accessibility(6), Debug(7*), About(8/9), Danger Zone(9/10), Logout(10/11)
            appearance.IsVisible = index == 0;
            general.IsVisible = index == 1;
            hotkeys.IsVisible = index == 2;
            profile.IsVisible = index == 3;
            if (network != null) network.IsVisible = index == 4;
            performance.IsVisible = index == 5;
            accessibility.IsVisible = index == 6;
            if (debugPanel != null)
                debugPanel.IsVisible = hasDebug && index == 7;
            about.IsVisible = index == (hasDebug ? 8 : 7);
            danger.IsVisible = index == (hasDebug ? 9 : 8);
            logout.IsVisible = index == (hasDebug ? 10 : 9);
            
            // Wire hotkey capture when Hotkeys panel is shown
            if (index == 2)
            {
                WireHotkeyCapture();
            }
        }

        

#if DEBUG
        private void AddDebugCcdSimulator()
        {
            try
            {
                var stack = this.FindControl<StackPanel>("PerformanceStack");
                if (stack == null) return;
                // Create a border with label + ComboBox for CCD simulation
                var border = new Border
                {
                    Margin = new Thickness(0, 4, 0, 0),
                    Padding = new Thickness(8),
                    CornerRadius = new CornerRadius(6),
                    Background = this.TryFindResource("App.Surface", out var surf) ? (IBrush)surf! : Brushes.Transparent,
                    BorderBrush = this.TryFindResource("App.Border", out var br) ? (IBrush)br! : Brushes.Gray,
                    BorderThickness = new Thickness(1)
                };
                var inner = new StackPanel { Spacing = 6 };
                var header = new TextBlock { Text = "Simulate CCD Configuration", FontWeight = Avalonia.Media.FontWeight.SemiBold };
                ToolTip.SetTip(header, "Debug-only toggle for CCD simulation and UI testing");
                inner.Children.Add(header);

                var grid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("160,*"),
                    ColumnSpacing = 8,
                    RowDefinitions = new RowDefinitions("Auto"),
                    RowSpacing = 6
                };
                var label = new TextBlock { Text = "CCD Mode" };
                Grid.SetColumn(label, 0);
                Grid.SetRow(label, 0);
                grid.Children.Add(label);
                var combo = new ComboBox();
                Grid.SetColumn(combo, 1); Grid.SetRow(combo, 0);
                combo.ItemsSource = new[] { "Auto (Hardware)", "No CCDs", "Single CCD (X3D)", "Dual CCDs" };
                // Bind to VM debug property if available
                combo.AttachedToVisualTree += (_, __) =>
                {
                    try
                    {
                        if (DataContext is Zer0Talk.ViewModels.SettingsViewModel vm)
                        {
                            combo.SelectedIndex = vm.DebugCcdModeIndex;
                            combo.SelectionChanged += (_, __2) => vm.DebugCcdModeIndex = combo.SelectedIndex;
                        }
                    }
                    catch { }
                };
                grid.Children.Add(combo);
                inner.Children.Add(grid);
                border.Child = inner;
                // Insert near top after header
                stack.Children.Insert(1, border);
            }
            catch { }
        }
#endif

        private void WireHotkeyCapture()
        {
            try
            {
                var hotkeyBox = this.FindControl<Border>("LockHotkeyBox");
                if (hotkeyBox == null) return;

                // Remove previous handlers to avoid duplicates
                hotkeyBox.PointerPressed -= OnLockHotkeyBoxPressed;
                hotkeyBox.PointerPressed += OnLockHotkeyBoxPressed;
            }
            catch { }

            try
            {
                var clearInputBox = this.FindControl<Border>("ClearInputHotkeyBox");
                if (clearInputBox == null) return;

                // Remove previous handlers to avoid duplicates
                clearInputBox.PointerPressed -= OnClearInputHotkeyBoxPressed;
                clearInputBox.PointerPressed += OnClearInputHotkeyBoxPressed;
            }
            catch { }
        }

        private void OnLockHotkeyBoxPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            try
            {
                if (DataContext is Zer0Talk.ViewModels.SettingsViewModel vm)
                {
                    vm.StartCapturingLockHotkey();
                }
            }
            catch { }
        }

        private void OnClearInputHotkeyBoxPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            try
            {
                if (DataContext is Zer0Talk.ViewModels.SettingsViewModel vm)
                {
                    vm.StartCapturingClearInputHotkey();
                }
            }
            catch { }
        }

        // [API] Back-compat with callers that used tab-based implementation.
        // Maps common section names to the left menu selection.
        public void SwitchToTab(string header)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(header)) return;
                var key = header.Trim().ToLowerInvariant();
                var hasDebug = HasDebugPanel();
                // Map headers to indices with Danger Zone pinned last
                int index = key switch
                {
                    "appearance" => 0,
                    "general" => 1,
                    "hotkeys" => 2,
                    "profile" => 3,
                    "network" => 4,
                    "performance" => 5,
                    "accessibility" => 6,
                    "debug" => hasDebug ? 7 : -1,
                    "debug tools" => hasDebug ? 7 : -1,
                    "about" => hasDebug ? 8 : 7,
                    "danger" => hasDebug ? 9 : 8,
                    "danger zone" => hasDebug ? 9 : 8,
                    "logout" => hasDebug ? 10 : 9,
                    _ => 3 // default to Profile
                };
                var menu = this.FindControl<ListBox>("MenuList");
                if (menu != null)
                {
                    menu.SelectedIndex = index;
                    UpdatePanels(index);
                }
            }
            catch { }
        }

#if DEBUG
        private void EnableDebugPanel()
        {
            try
            {
                var menu = this.FindControl<ListBox>("MenuList");
                if (menu == null) return;
                if (_debugMenuItem != null) return;
                if (menu.Items is IList list)
                {
                    var item = new ListBoxItem { Content = "Debug Tools" };
                    _debugMenuItem = item;
                    var insertIndex = Math.Min(7, list.Count); // Insert after Accessibility (index 6)
                    list.Insert(insertIndex, item);
                }
            }
            catch { }
        }
#endif

        private bool HasDebugPanel()
        {
#if DEBUG
            return _debugMenuItem != null;
#else
            return false;
#endif
        }
    }
}
