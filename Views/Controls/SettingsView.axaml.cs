using System;
using System.Collections;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml;

namespace ZTalk.Views.Controls
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
#if DEBUG
            // Inject Debug-only CCD simulation UI
            this.AttachedToVisualTree += (_, __) => AddDebugCcdSimulator();
#endif
        }
        
        private void WireDiscoveredPeersList()
        {
            try
            {
                var listBox = this.FindControl<ListBox>("DiscoveredPeersList");
                if (listBox == null) return;
                
                // Update ViewModel's SelectedPeers when ListBox selection changes
                listBox.SelectionChanged += (_, __) =>
                {
                    try
                    {
                        if (!(DataContext is ZTalk.ViewModels.SettingsViewModel vm)) return;
                        
                        var selected = new System.Collections.Generic.List<ZTalk.Models.Peer>();
                        if (listBox.SelectedItems != null)
                        {
                            foreach (var item in listBox.SelectedItems)
                            {
                                if (item is ZTalk.Models.Peer peer)
                                {
                                    selected.Add(peer);
                                }
                            }
                        }
                        
                        // Get NetworkViewModel and update its SelectedPeers
                        var netVm = typeof(ZTalk.ViewModels.SettingsViewModel)
                            .GetField("NetworkVm", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                            ?.GetValue(vm);
                        
                        if (netVm != null)
                        {
                            var selectedPeersProp = netVm.GetType().GetProperty("SelectedPeers");
                            selectedPeersProp?.SetValue(netVm, selected);
                        }
                    }
                    catch { }
                };
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
                    var s = ZTalk.Services.AppServices.Settings.Settings;
                    var max = HasDebugPanel() ? 9 : 8;
                    if (idx >= 0 && idx <= max && s.LastSettingsMenuIndex != idx)
                    {
                        s.LastSettingsMenuIndex = idx;
                        // Persist asynchronously to avoid UI stalls
                        _ = System.Threading.Tasks.Task.Run(() => ZTalk.Services.AppServices.Settings.Save(ZTalk.Services.AppServices.Passphrase));
                    }
                }
                catch { }
            };
            // Restore last selected menu (default Profile index 2) and update panels
            try
            {
                var saved = ZTalk.Services.AppServices.Settings.Settings.LastSettingsMenuIndex;
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
                        if (combo != null && DataContext is ZTalk.ViewModels.SettingsViewModel vm)
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
            var appearance = this.FindControl<ScrollViewer>("AppearancePanel");
            var network = this.FindControl<ScrollViewer>("NetworkPanel");
            var logout = this.FindControl<ScrollViewer>("LogoutPanel");
            var performance = this.FindControl<ScrollViewer>("PerformancePanel");
            var accessibility = this.FindControl<ScrollViewer>("AccessibilityPanel");
            var about = this.FindControl<ScrollViewer>("AboutPanel");
            var danger = this.FindControl<ScrollViewer>("DangerPanel");
            var debugPanel = this.FindControl<ScrollViewer>("DebugPanel");
            // Don't return early if only network panel is missing - allow other panels to show
            if (profile == null || general == null || appearance == null || logout == null || performance == null || accessibility == null || about == null || danger == null) return;
            var hasDebug = HasDebugPanel() && debugPanel != null;
            var maxIndex = hasDebug ? 10 : 9;
            if (index < 0) index = 0;
            if (index > maxIndex) index = maxIndex;
            // Order: Appearance(0), General(1), Profile(2), Network(3), Performance(4), Accessibility(5), Debug(6*), About(7/8), Danger Zone(8/9), Logout(9/10)
            appearance.IsVisible = index == 0;
            general.IsVisible = index == 1;
            profile.IsVisible = index == 2;
            if (network != null) network.IsVisible = index == 3;
            performance.IsVisible = index == 4;
            accessibility.IsVisible = index == 5;
            if (debugPanel != null)
                debugPanel.IsVisible = hasDebug && index == 6;
            about.IsVisible = index == (hasDebug ? 7 : 6);
            danger.IsVisible = index == (hasDebug ? 8 : 7);
            logout.IsVisible = index == (hasDebug ? 9 : 8);
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
                        if (DataContext is ZTalk.ViewModels.SettingsViewModel vm)
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
                    "profile" => 2,
                    "network" => 3,
                    "performance" => 4,
                    "accessibility" => 5,
                    "debug" => hasDebug ? 6 : -1,
                    "debug tools" => hasDebug ? 6 : -1,
                    "about" => hasDebug ? 7 : 6,
                    "danger" => hasDebug ? 8 : 7,
                    "danger zone" => hasDebug ? 8 : 7,
                    "logout" => hasDebug ? 9 : 8,
                    _ => 2 // default to Profile
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
                    var insertIndex = Math.Min(6, list.Count); // Insert after Accessibility (index 5)
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
