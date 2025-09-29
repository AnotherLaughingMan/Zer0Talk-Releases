using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml;

namespace P2PTalk.Views.Controls
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
            this.AttachedToVisualTree += (_, __) => WireMenu();
#if DEBUG
            // Inject Debug-only CCD simulation UI
            this.AttachedToVisualTree += (_, __) => AddDebugCcdSimulator();
#endif
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
                    var s = P2PTalk.Services.AppServices.Settings.Settings;
                    if (idx >= 0 && idx <= 7 && s.LastSettingsMenuIndex != idx)
                    {
                        s.LastSettingsMenuIndex = idx;
                        // Persist asynchronously to avoid UI stalls
                        _ = System.Threading.Tasks.Task.Run(() => P2PTalk.Services.AppServices.Settings.Save(P2PTalk.Services.AppServices.Passphrase));
                    }
                }
                catch { }
            };
            // Restore last selected menu (default Profile index 2) and update panels
            try
            {
                var saved = P2PTalk.Services.AppServices.Settings.Settings.LastSettingsMenuIndex;
                if (saved < 0 || saved > 7) saved = 2;
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
                        if (combo != null && DataContext is P2PTalk.ViewModels.SettingsViewModel vm)
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
            var logout = this.FindControl<ScrollViewer>("LogoutPanel");
            var performance = this.FindControl<ScrollViewer>("PerformancePanel");
            var accessibility = this.FindControl<ScrollViewer>("AccessibilityPanel");
            var about = this.FindControl<ScrollViewer>("AboutPanel");
            var danger = this.FindControl<ScrollViewer>("DangerPanel");
            if (profile == null || general == null || appearance == null || logout == null || performance == null || accessibility == null || about == null || danger == null) return;
            // Order: Appearance(0), General(1), Profile(2), Performance(3), Accessibility(4), About(5), Danger Zone(6), Logout(7)
            appearance.IsVisible = index == 0;
            general.IsVisible = index == 1;
            profile.IsVisible = index == 2;
            performance.IsVisible = index == 3;
            accessibility.IsVisible = index == 4;
            about.IsVisible = index == 5;
            danger.IsVisible = index == 6;
            logout.IsVisible = index == 7;
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
                        if (DataContext is P2PTalk.ViewModels.SettingsViewModel vm)
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
                // Map headers to indices with Danger Zone pinned last
                int index = key switch
                {
                    "appearance" => 0,
                    "general" => 1,
                    "profile" => 2,
                    "performance" => 3,
                    "accessibility" => 4,
                    "about" => 5,
                    "danger" => 6,
                    "danger zone" => 6,
                    "logout" => 7,
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
    }
}
