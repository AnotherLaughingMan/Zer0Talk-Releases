using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Diagnostics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using System.Threading;
using System.IO;
using System.Collections.Specialized;

using ZTalk.Models;
using Models = ZTalk.Models;
using P2PTalk.Services;
using P2PTalk.Utilities;
using P2PTalk.ViewModels;
using P2PTalk.Views.Controls;

using RelayCommand = P2PTalk.ViewModels.RelayCommand;


namespace P2PTalk.Views;

public partial class MainWindow : Window, INotifyPropertyChanged, IDisposable
{
    // Commands exposed for XAML bindings (avoid XAML Click handler errors)
    public System.Windows.Input.ICommand OpenProfileSettingsCommand { get; }
    public System.Windows.Input.ICommand CopyUidCommand { get; }
    public System.Windows.Input.ICommand SetStatusOnlineCommand { get; }
    public System.Windows.Input.ICommand SetStatusAwayCommand { get; }
    public System.Windows.Input.ICommand SetStatusDndCommand { get; }
    public System.Windows.Input.ICommand SetStatusOfflineCommand { get; }
    public System.Windows.Input.ICommand StatusDurationCommand { get; }
    // Scoped refresh: event-driven only for MainWindow (no periodic app-wide loop)
    private const string NatThrottleKey = "MainWindow.UI.throttle";
    private const double ChatInputDefaultMinHeight = 56d;
    private const double ChatInputDefaultMaxHeight = 200d;
    private System.Action? _natThrottled;
    // Geometry is now persisted via lightweight LayoutCache on close only (no runtime throttled writes)
    // NOTE: We intentionally removed frequent writes to settings.p2e to avoid I/O overhead.
    // Remember last non-zero width of the Diagnostics (right) panel within the session
    private double? _rightPanelLastWidth;
    // [LAYOUT] Remember last non-nav width of the left panel (contacts area) within the session
    private double? _leftPanelLastWidth;
    // [LAYOUT] Capture original MinWidth of the left panel container to restore after expand
    private double? _leftPanelOriginalMinWidth;
    private Action? _uiPulseHandler; // keep a reference for unsubscribe
#if DEBUG
    // DEBUG controls and flags
    private ToggleSwitch? _toggleVerified;
    private ToggleSwitch? _toggleTrusted;
    private bool _updatingToggles;
    private bool _syncingDebugSwitches;
#endif
    // Settings overlay
    private ViewModels.SettingsViewModel? _settingsVm;
    private ViewModels.SettingsViewModel SettingsProxy => _settingsVm ??= new ViewModels.SettingsViewModel();
    // Regression toast
    private CancellationTokenSource? _rgToastCts;
    // Verification popup state
    private Window? _verifyReqPopup;
    private string? _verifyReqUid;
    public MainWindow()
    {
        InitializeComponent();
        try
        {
            // Pre-layout collapse of diagnostics column to prevent initial flash of an empty right panel.
            var settings = AppServices.Settings.Settings;
            if (settings.MainRightWidth is null or <= 0)
            {
                var grid = this.FindControl<Grid>("BodyGrid");
                if (grid?.ColumnDefinitions is { Count: >= 6 })
                {
                    // Column indices: 0=nav,1=contacts,2=divider,3=chat,4=divider(chat/diag),5=diagnostics
                    var diagCol = grid.ColumnDefinitions[5];
                    var dividerR = grid.ColumnDefinitions[4];
                    // Capture original MinWidth once
                    if (_rightColumnOriginalMinWidthDefinition is null)
                        _rightColumnOriginalMinWidthDefinition = diagCol.MinWidth;
                    // Collapse divider + diagnostics column hard before first measure
                    dividerR.Width = new GridLength(0);
                    diagCol.MinWidth = 0;
                    diagCol.Width = new GridLength(0);
                    var rightRoot = this.FindControl<Border>("RightPanelRoot");
                    if (rightRoot != null)
                    {
                        rightRoot.IsVisible = false;
                        rightRoot.MinWidth = 0;
                    }
                }
            }
        }
        catch { }
        // Initialize trackers that depend on 'this'
        try
        {
            _focusFlip = new FlipFlopTracker(this, "[Flicker][FocusFlip]", 180, "Got", "Lost");
            _visibleFlip = new FlipFlopTracker(this, "[Flicker][VisibleFlip]", 160, "Show", "Hide");
        }
        catch { }
        // Global focus/pointer diagnostics to UI.log (fine-grained focus change tracing)
        try
        {
            this.AddHandler(InputElement.GotFocusEvent, OnAnyGotFocus, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            this.AddHandler(InputElement.LostFocusEvent, OnAnyLostFocus, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            this.AddHandler(InputElement.PointerEnteredEvent, OnAnyPointerEnter, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            this.AddHandler(InputElement.PointerExitedEvent, OnAnyPointerLeave, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            this.AddHandler(Control.ContextRequestedEvent, OnAnyContextRequested, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            this.AddHandler(InputElement.PointerPressedEvent, OnAnyPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            this.AddHandler(InputElement.PointerReleasedEvent, OnAnyPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            this.AddHandler(InputElement.PointerWheelChangedEvent, OnAnyPointerWheel, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            this.AddHandler(InputElement.KeyDownEvent, OnAnyKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            this.AddHandler(InputElement.KeyUpEvent, OnAnyKeyUp, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            this.AddHandler(InputElement.TextInputEvent, OnAnyTextInput, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        }
        catch { }
        try
        {
            this.Opened += (_, __) => AttachFlickerWatchers();
            this.Opened += (_, __) => InitializeMessageInputSizing();
            this.Activated += (_, __) => WriteUiCategory("[Window]", "Activated");
            this.Deactivated += (_, __) => WriteUiCategory("[Window]", "Deactivated");
        }
        catch { }
    // Regression Guard: ensure window stays resizable even if future style/theme sets it off
    try { if (!CanResize) { CanResize = true; InteractionLogger.Log("[RegressionGuard] MainWindow.CanResize was false at init; corrected to true."); } } catch { }
    // Additional guard: ensure system decorations/border remain so user can resize
    try { if (SystemDecorations == SystemDecorations.None) { SystemDecorations = SystemDecorations.BorderOnly; InteractionLogger.Log("[RegressionGuard] SystemDecorations restored to BorderOnly."); } } catch { }
        // Initialize commands
        OpenProfileSettingsCommand = new RelayCommand(_ => ShowSettingsOverlay("Profile"));
        CopyUidCommand = new RelayCommand(_ =>
        {
            try { CopyUid_Click(this, new RoutedEventArgs()); } catch { }
        });
        SetStatusOnlineCommand = new RelayCommand(_ =>
        {
            // Online should apply immediately and cancel any timers
            _presenceCts?.Cancel();
            _presenceRestoreTarget = null;
            SetPresence(true, false, false, false);
            try { InteractionLogger.Log("[AvatarMenu] Online applied with no timer"); } catch { }
        });
        SetStatusAwayCommand = new RelayCommand(_ =>
        {
            // Manual change should cancel any pending auto-restore timer
            _presenceCts?.Cancel();
            _presenceRestoreTarget = null;
            SetPresence(false, true, false, false);
            try { InteractionLogger.Log("[AvatarMenu] Idle applied (manual); timers canceled"); } catch { }
        });
        SetStatusDndCommand = new RelayCommand(_ =>
        {
            _presenceCts?.Cancel();
            _presenceRestoreTarget = null;
            SetPresence(false, false, true, false);
            try { InteractionLogger.Log("[AvatarMenu] Do Not Disturb applied (manual); timers canceled"); } catch { }
        });
        SetStatusOfflineCommand = new RelayCommand(_ =>
        {
            _presenceCts?.Cancel();
            _presenceRestoreTarget = null;
            SetPresence(false, false, false, true);
            try { InteractionLogger.Log("[AvatarMenu] Invisible applied (manual); timers canceled"); } catch { }
        });
        StatusDurationCommand = new RelayCommand(p =>
        {
            try
            {
                var s = p as string;
                if (string.IsNullOrWhiteSpace(s)) return;
                var parts = s.Split('|');
                if (parts.Length != 2) return;
                var statusStr = parts[0].Trim();
                var durStr = parts[1].Trim();
                var status = statusStr switch
                {
                    "Online" => Models.PresenceStatus.Online,
                    "Idle" => Models.PresenceStatus.Idle,
                    "Do Not Disturb" => Models.PresenceStatus.DoNotDisturb,
                    "Invisible" => Models.PresenceStatus.Invisible,
                    _ => Models.PresenceStatus.Online
                };
                // If Online slips through, apply immediately and ignore timing
                if (status == Models.PresenceStatus.Online)
                {
                    _presenceCts?.Cancel();
                    _presenceRestoreTarget = null;
                    ApplyPresence(status);
                    try { InteractionLogger.Log("[AvatarMenu] Online selected via duration path; timers ignored"); } catch { }
                    return;
                }
                ApplyPresence(status);
                if (durStr.Equals("Forever", StringComparison.OrdinalIgnoreCase))
                {
                    _presenceCts?.Cancel();
                    _presenceRestoreTarget = null;
                    try { InteractionLogger.Log($"[AvatarMenu] Presence {status} for Forever"); } catch { }
                    return;
                }
                if (!TimeSpan.TryParse(durStr, out var duration)) return;
                _presenceCts?.Cancel();
                _presenceCts = new System.Threading.CancellationTokenSource();
                var token = _presenceCts.Token;
                _presenceRestoreTarget = Models.PresenceStatus.Online;
                try { InteractionLogger.Log($"[AvatarMenu] Presence {status} for {duration}"); } catch { }
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        await System.Threading.Tasks.Task.Delay(duration, token);
                        if (!token.IsCancellationRequested && _presenceRestoreTarget is Models.PresenceStatus target)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() => ApplyPresence(target));
                            try { InteractionLogger.Log($"[AvatarMenu] Presence auto-restored to {target}"); } catch { }
                        }
                    }
                    catch { }
                }, token);
            }
            catch { }
        });
        // Trace restoration intent for audit
        try { P2PTalk.Utilities.ErrorLogger.LogException(new InvalidOperationException("[Restore] MainWindow: applying LKG layout/selection defaults"), source: "Restore.MainWindow"); } catch { }
        try { WriteSettingsLog("[Restore] MainWindow: applying LKG layout/selection defaults"); } catch { }
        try { P2PTalk.Utilities.ErrorLogger.LogException(new InvalidOperationException("[Theme] Restore: disable Windows11 focus adorner for Contacts (no template override)"), source: "Theme.Restore"); } catch { }
        try { WriteThemeLog("[Restore] Theme: Disabled FocusAdorner for ContactsList items and contact-card border (template restored)"); } catch { }
        // Log sovereign UI restorations
        try { WriteThemeLog("[Restore] Theme: Reinforced Win11 selection/highlight override for Contacts scope"); } catch { }
        try { WriteThemeLog("[Restore] Theme: Added presence dot overlay to user avatar and increased avatar size vs contacts"); } catch { }
        try { P2PTalk.Utilities.ErrorLogger.LogException(new InvalidOperationException("[Theme] Restore: presence dot + avatar sizing applied"), source: "Theme.Restore"); } catch { }
        // Log layout: restored left nav rail button sizing/alignment (top-stack, horizontal center)
        try { WriteLayoutLog("[Restore] Layout: Nav rail 64x56 buttons, icons ~26px, padding 8px, spacing 6px, top-stacked, horizontally centered"); } catch { }
        try { P2PTalk.Utilities.ErrorLogger.LogException(new InvalidOperationException("[Restore] Layout: Nav rail top-stacked + horizontal centering applied"), source: "Layout.Restore"); } catch { }
        if (AppServices.Settings.Settings.MainWindow is { } s)
        {
            // Restore window layout from sidecar cache (fallback to previous settings values if cache missing)
            this.Opened += (_, _) => RestoreLayoutFromCacheOrSettings(s);
            // Save geometry only once on close
            this.Closing += OnMainWindowClosing;
        }

        // Apply privacy: block screen capture on Windows based on setting
        this.Opened += (_, __) =>
        {
            try
            {
                var enabled = AppServices.Settings?.Settings?.BlockScreenCapture ?? true;
                P2PTalk.Services.ScreenCaptureProtection.SetExcludeFromCapture(this, enabled);
            }
            catch { }
        };

        // Hook tunneling KeyDown on message input to intercept Enter
        var input = this.FindControl<TextBox>("MessageInput");
        if (input is not null)
        {
            input.AddHandler(InputElement.KeyDownEvent, OnMessageInputKeyDown, RoutingStrategies.Tunnel);
        }

        // Global lock hotkey: Ctrl+Alt+Shift+L
        this.AddHandler(InputElement.KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);

        // Diagnostics refresh via NAT/listening/peers events and centralized UI pulse; add a local keep-alive as fallback.
        _natThrottled = AppServices.Updates.GetUiThrottled(NatThrottleKey, 250, () => CollectAndRender());
        AppServices.Events.NatChanged += () => _natThrottled?.Invoke();
        AppServices.Events.NetworkListeningChanged += (_, __) => _natThrottled?.Invoke();
        AppServices.Events.PeersChanged += () => _natThrottled?.Invoke();
        _uiPulseHandler = () => _natThrottled?.Invoke();
        AppServices.Events.UiPulse += _uiPulseHandler;
        // Render once after open to populate snapshot
        this.Opened += (_, __) => CollectAndRender();
    // After open, stick chat to bottom initially using one-shot follow
    this.Opened += (_, __) =>
    {
        // Let one-shot decide based on actual layout; don't preset baseline height.
        Dispatcher.UIThread.Post(ScrollChatToBottomOneShot);
    };
        // Keep the diagnostics panel and indicator fresh even if no events fire (e.g., NAT blink, port labels)
        this.Opened += (_, __) => { try { AppServices.Updates.RegisterUiInterval("MainWindow.UI.blink", 500, () => CollectAndRender()); } catch { } };

        // Manual verification: subscribe to inbound request/cancel and toast a small popup to act
        try
        {
            AppServices.ContactRequests.VerifyRequestReceived += uid => Dispatcher.UIThread.Post(() => ShowVerificationRequestPopup(uid));
            AppServices.ContactRequests.VerifyRequestCancelled += uid => Dispatcher.UIThread.Post(() => DismissVerificationRequestPopup(uid));
        }
        catch { }

        // Apply saved widths (0 hides panel)
        this.Opened += (_, __) => ApplyInitialPanelVisibility();

        // Initialize presence flags from settings
        this.Opened += (_, __) =>
        {
            try
            {
                var s = AppServices.Settings.Settings.Status;
                SetPresence(s == Models.PresenceStatus.Online, s == Models.PresenceStatus.Idle, s == Models.PresenceStatus.DoNotDisturb, s == Models.PresenceStatus.Invisible);
            }
            catch { }
        };

        // Attach deep diagnostics and robust selection/context handling for the contacts list
        this.Opened += (_, __) => HookContactsDiagnostics();
        this.Opened += (_, __) =>
        {
            try
            {
                // Delay to ensure visual tree contains popup roots
                Dispatcher.UIThread.Post(() => AttachHoverBarStabilityHandlers());
            }
            catch { }
        };
        // UI regression checkpoint + guard: ensure Nav rail layout stays as specified
        this.Opened += (_, __) => { try { CheckpointNavRail(); } catch { } };
        // Close Full Profile overlay via Esc from the window scope
        this.AddHandler(InputElement.KeyDownEvent, OnWindowKeyDownForOverlays, RoutingStrategies.Tunnel);
        // Set top-bar app icon with fallback to embedded resource
        this.Opened += (_, __) =>
        {
            try
            {
                var img = this.FindControl<Image>("TitleAppIcon");
                if (img is null) return;

                try
                {
                    // Try direct file path first
                    var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "Icon.ico");
                    if (File.Exists(iconPath))
                    {
                        using var fs = File.OpenRead(iconPath);
                        img.Source = new Avalonia.Media.Imaging.Bitmap(fs);
                        return;
                    }
                }
                catch { }

                try
                {
                    // Fallback to embedded resource
                    using var stream = AssetLoader.Open(new Uri("avares://ZTalk/Assets/Icons/Icon.ico"));
                    img.Source = new Avalonia.Media.Imaging.Bitmap(stream);
                }
                catch { }
            }
            catch { }
        };
        // Log that the avatar mini-menu is restored
        try { InteractionLogger.Log("[AvatarMenu] Initialized mini-menu on avatar"); } catch { }
        // Log correction of Online timer behavior
        try { InteractionLogger.Log("[AvatarMenu] Correction: Online has no timer submenu and cancels timers"); } catch { }
        this.Opened += (_, __) =>
        {
            try
            {
                VerifyAndGuardNavRail();
                // Re-check once after initial layout pass
                Dispatcher.UIThread.Post(() => { try { VerifyAndGuardNavRail(); } catch { } });
                // Hook messages collection changes to manage scrolling
                if (DataContext is MainWindowViewModel vm)
                {
                    _messagesChangedHandler ??= (_, args) => OnMessagesCollectionChanged(args);
                    vm.Messages.CollectionChanged += _messagesChangedHandler;
                    AttachMessageHandlers(vm.Messages);
                }
            }
            catch { }
        };

        // Removed runtime geometry persistence hooks to prevent frequent writes.

        // Mini-profile removed: no popup wiring

        // Subscribe to regression notifications for a lightweight developer toast
        try { AppServices.Events.RegressionDetected += OnRegressionDetected; } catch { }

#if DEBUG
        try
        {
            // DEBUG-ONLY: Inject tiny ToggleSwitch controls for visual testing of shields on the
            // Simulated Contact profile card. These do NOT appear in Release builds.
            var panel = this.FindControl<StackPanel>("DebugSimulatedProfilePanel");
            if (panel != null)
            {
                panel.IsVisible = true;
                panel.Children.Clear();

                // Use compact ToggleSwitches; only one can be ON at a time (mutually exclusive)
                _toggleVerified = new ToggleSwitch
                {
                    Content = "Verified",
                    FontSize = 11,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                _toggleTrusted = new ToggleSwitch
                {
                    Content = "Trusted",
                    FontSize = 11,
                };
                _toggleVerified.IsCheckedChanged += (_, __) => OnVerifiedSwitchChanged(_toggleVerified.IsChecked == true);
                _toggleTrusted.IsCheckedChanged += (_, __) => OnTrustedSwitchChanged(_toggleTrusted.IsChecked == true);
                panel.Children.Add(_toggleVerified);
                panel.Children.Add(_toggleTrusted);

                // Presence quick toggles (debug-only, simulated contacts)
                var btnOnline = new Button { Content = "Online", FontSize = 11, Margin = new Thickness(6,0,0,0) };
                var btnOffline = new Button { Content = "Offline", FontSize = 11 };
                var btnIdle = new Button { Content = "Idle", FontSize = 11 };
                var btnDnd = new Button { Content = "DND", FontSize = 11 };
                btnOnline.Click += (_, __) => SetSimulatedPresence(Models.PresenceStatus.Online);
                btnOffline.Click += (_, __) => SetSimulatedPresence(Models.PresenceStatus.Offline);
                btnIdle.Click += (_, __) => SetSimulatedPresence(Models.PresenceStatus.Idle);
                btnDnd.Click += (_, __) => SetSimulatedPresence(Models.PresenceStatus.DoNotDisturb);
                panel.Children.Add(btnOnline);
                panel.Children.Add(btnOffline);
                panel.Children.Add(btnIdle);
                panel.Children.Add(btnDnd);

                // Initialize switch state from current selection
                SyncDebugSwitchesFromSelection();
                // Keep switches in sync when selection changes
                try
                {
                    if (DataContext is MainWindowViewModel vm)
                        vm.PropertyChanged += OnVmPropertyChangedForDebugSwitches;
                }
                catch { }
            }
        }
        catch { }
#endif
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private bool _stickToBottom = true;
    private static readonly TimeSpan AutoFollowGuardWindow = TimeSpan.FromMilliseconds(1800);
    private const double AutoFollowGuardGapTolerance = 720.0;
    private DateTime _autoFollowGuardUntilUtc;
    private double _lastViewportHeight;
    private double _lastExtentHeight;
    private bool _suppressNextAutoScroll;
    private int _unreadSinceLastBottom;
    private DateTime _lastScrollActivityUtc;
    private bool _followChat = true; // Discord-like follow mode: true only when at bottom
    private bool _bringIntoViewPosted; // coalesce render-phase auto-scroll
    private DateTime _lastBringIntoViewAtUtc; // suppress bursts at startup/add storms
    private double _lastAutoScrollExtentHeight; // guard: only auto-scroll when extent grows
    private ScrollAnimator? _chatScrollAnimator;
    private readonly HashSet<Message> _trackedMessages = new();
    private NotifyCollectionChangedEventHandler? _messagesChangedHandler;

    private void SafeUiLog(string message)
    {
        try
        {
            var line = $"{DateTime.Now:O} [UI] {message}{Environment.NewLine}";
            if (P2PTalk.Utilities.LoggingPaths.Enabled)
                P2PTalk.Utilities.LoggingPaths.TryWrite(P2PTalk.Utilities.LoggingPaths.UI, line);
        }
        catch { }
    }

    // header container configuration now handled by XAML styles

    private static string DescribeElement(object? o)
    {
        try
        {
            if (o is null) return "<null>";
            if (o is Control c)
            {
                var name = string.IsNullOrWhiteSpace(c.Name) ? "<no-name>" : c.Name;
                string? dc = null;
                try { var dt = c.DataContext?.GetType(); if (dt != null) dc = dt.Name; } catch { }
                return dc is null ? $"{c.GetType().Name}#{name}" : $"{c.GetType().Name}#{name} dc={dc}";
            }
            return o.GetType().Name;
        }
        catch { return "<desc-error>"; }
    }

    private static void WriteUiCategory(string category, string message)
    {
        try
        {
            if (!P2PTalk.Utilities.LoggingPaths.Enabled) return;
            var text = $"{DateTime.Now:O} {category} {message}{Environment.NewLine}";
            P2PTalk.Utilities.LoggingPaths.TryWrite(P2PTalk.Utilities.LoggingPaths.UI, text);
        }
        catch { }
    }

    private void OnAnyGotFocus(object? sender, RoutedEventArgs e)
    {
        try
        {
            var src = DescribeElement(e.Source);
            var snd = DescribeElement(sender);
            string cur = "";
            try { cur = DescribeElement(this.FocusManager?.GetFocusedElement()); } catch { }
            WriteUiCategory("[Focus]", $"GotFocus src={src} sender={snd} current={cur}");
            try { _focusTracker.Note("Got", e.Source); _focusFlip?.Note("Got", e.Source); } catch { }
            _focusNullSince = null;
            try
            {
                // Remember if the last focus was inside the contacts list to avoid stealing focus later
                var lb = this.FindControl<ListBox>("ContactsList");
                var wasInContacts = false;
                if (lb != null)
                {
                    if (e.Source is Avalonia.Visual v)
                    {
                        var container = v.FindAncestorOfType<ListBoxItem>();
                        if (container != null && ReferenceEquals(container.FindAncestorOfType<ListBox>(), lb))
                            wasInContacts = true;
                        else if (ReferenceEquals(v.FindAncestorOfType<ListBox>(), lb))
                            wasInContacts = true;
                    }
                }
                _lastFocusWasContactsList = wasInContacts;
            }
            catch { }
        }
        catch { }
    }

    private void OnAnyLostFocus(object? sender, RoutedEventArgs e)
    {
        try
        {
            var src = DescribeElement(e.Source);
            var snd = DescribeElement(sender);
            string cur = "";
            try { cur = DescribeElement(this.FocusManager?.GetFocusedElement()); } catch { }
            WriteUiCategory("[Focus]", $"LostFocus src={src} sender={snd} current={cur}");
            try
            {
                _focusTracker.Note("Lost", e.Source);
                _focusFlip?.Note("Lost", e.Source);
            }
            catch { }
            try
            {
                var isNullNow = this.FocusManager?.GetFocusedElement() is null;
                if (isNullNow)
                {
                    WriteUiCategory("[Focus][Null]", $"Focus became null after LostFocus; src={src}");
                    _focusDropMicro.Note("Drop", e.Source);
                    if (_focusNullSince is null)
                    {
                        _focusNullSince = DateTime.UtcNow;
                        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
                        {
                            try
                            {
                                if (_focusNullSince is DateTime start && this.FocusManager?.GetFocusedElement() is null)
                                {
                                    var dt = DateTime.UtcNow - start;
                                    WriteUiCategory("[Focus][NullGap]", $"Still null after {dt.TotalMilliseconds:0}ms; lastSrc={src}");
                                }
                            }
                            catch { }
                        }, TimeSpan.FromMilliseconds(100));
                    }
                }
            }
            catch { }
        }
        catch { }
    }

    private Control? _lastHoverControl;
    private void OnAnyPointerEnter(object? sender, PointerEventArgs e)
    {
        try
        {
            Control? ctrl = null;
            if (e.Source is Control c) ctrl = c; else if (e.Source is Avalonia.Visual v) ctrl = v.FindAncestorOfType<Control>();
            if (ctrl == null) return;
            if (!ReferenceEquals(_lastHoverControl, ctrl))
            {
                _lastHoverControl = ctrl;
                Avalonia.Point p = default;
                try { p = e.GetPosition(ctrl); } catch { }
                WriteUiCategory("[Pointer]", $"Enter {DescribeElement(ctrl)} at ({p.X:0.0},{p.Y:0.0})");
                try { _hoverTracker.Note("Enter", ctrl); } catch { }
            }
        }
        catch { }
    }

    private void OnAnyPointerLeave(object? sender, PointerEventArgs e)
    {
        try
        {
            Control? ctrl = null;
            if (e.Source is Control c) ctrl = c; else if (e.Source is Avalonia.Visual v) ctrl = v.FindAncestorOfType<Control>();
            if (ctrl == null) return;
            Avalonia.Point p = default;
            try { p = e.GetPosition(ctrl); } catch { }
            WriteUiCategory("[Pointer]", $"Leave {DescribeElement(ctrl)} at ({p.X:0.0},{p.Y:0.0})");
            if (ReferenceEquals(_lastHoverControl, ctrl)) _lastHoverControl = null;
            try { _hoverTracker.Note("Leave", ctrl); } catch { }
        }
        catch { }
    }

    internal sealed class MicroChurnTracker
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _last = new();
        private readonly TimeSpan _threshold;
        private readonly string _category;
        public MicroChurnTracker(string category, int milliseconds)
        {
            _category = category;
            _threshold = TimeSpan.FromMilliseconds(milliseconds);
        }
        public void Note(string action, object? element)
        {
            try
            {
                var key = DescribeElement(element) + ":" + action;
                var now = DateTime.UtcNow;
                if (_last.TryGetValue(key, out var prev))
                {
                    var dt = now - prev;
                    if (dt <= _threshold)
                    {
                        WriteUiCategory(_category, $"micro {action} dt={dt.TotalMilliseconds:0}ms on {DescribeElement(element)}");
                    }
                }
                _last[key] = now;
            }
            catch { }
        }
    }

    internal sealed class FlipFlopTracker
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string action, DateTime at)> _last = new();
        private readonly TimeSpan _threshold;
        private readonly string _category;
        private readonly string _a;
        private readonly string _b;
        private readonly MainWindow _owner;
        public FlipFlopTracker(MainWindow owner, string category, int milliseconds, string actionA, string actionB)
        {
            _owner = owner;
            _category = category;
            _threshold = TimeSpan.FromMilliseconds(milliseconds);
            _a = actionA;
            _b = actionB;
        }
        public void Note(string action, object? element)
        {
            try
            {
                var key = DescribeElement(element) ?? "<null>";
                var now = DateTime.UtcNow;
                if (_last.TryGetValue(key, out var prev))
                {
                    bool isFlip = (prev.action == _a && action == _b) || (prev.action == _b && action == _a);
                    if (isFlip)
                    {
                        var dt = now - prev.at;
                        if (dt <= _threshold)
                        {
                            WriteUiCategory(_category, $"flip {prev.action}->{action} dt={dt.TotalMilliseconds:0}ms on {key}");
                            try { _owner._lastFocusFlipAtUtc = now; } catch { }
                        }
                    }
                }
                _last[key] = (action, now);
            }
            catch { }
        }
    }

    private readonly MicroChurnTracker _focusTracker = new("[Flicker][Focus]", 180);
    private readonly MicroChurnTracker _hoverTracker = new("[Flicker][Pointer]", 120);
    private readonly MicroChurnTracker _focusDropMicro = new("[Flicker][FocusDrop]", 250);
    private readonly FlipFlopTracker? _focusFlip;

    private void AttachFlickerWatchers()
    {
        try
        {
            WatchControlByName("LeftPanelRoot");
            WatchControlByName("RightPanelRoot");
            WatchControlByName("ContactsList");
            WatchControlByName("ChatScroll");
            WatchControlByName("SettingsOverlay");
            WatchControlByName("InlineSettingsHost");
            WatchControlByName("BodyGrid");
        }
        catch { }
    }

    private void WatchControlByName(string name)
    {
        try
        {
            var c = this.FindControl<Control>(name);
            if (c != null) AttachWatchers(c);
        }
        catch { }
    }

    private void AttachWatchers(Control c)
    {
        try
        {
            c.AttachedToVisualTree += (_, __) => WriteUiCategory("[Tree]", $"Attach {DescribeElement(c)}");
            c.DetachedFromVisualTree += (_, __) => WriteUiCategory("[Tree]", $"Detach {DescribeElement(c)}");
            c.PropertyChanged += OnWatchedControlPropertyChanged;
        }
        catch { }
    }

    private void OnWatchedControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        try
        {
            if (sender is not Control c) return;
            if (e.Property == Visual.IsVisibleProperty)
            {
                var visible = c.IsVisible;
                WriteUiCategory("[Visible]", $"{(visible ? "Shown" : "Hidden")} {DescribeElement(c)}");
                try { var act = visible ? "Show" : "Hide"; _visMicro.Note(act, c); _visibleFlip?.Note(act, c); } catch { }
            }
            else if (e.Property == Visual.BoundsProperty)
            {
                var b = c.Bounds;
                WriteUiCategory("[Bounds]", $"{DescribeElement(c)} -> ({b.X:0},{b.Y:0},{b.Width:0},{b.Height:0})");
            }
            else if (e.Property == Visual.OpacityProperty)
            {
                var o = c.Opacity;
                WriteUiCategory("[Opacity]", $"{DescribeElement(c)} -> {o:0.00}");
                try { _opacityMicro.Note("Opacity", c); } catch { }
            }
        }
        catch { }
    }

    private readonly MicroChurnTracker _visMicro = new("[Flicker][Visible]", 160);
    private readonly MicroChurnTracker _opacityMicro = new("[Flicker][Opacity]", 100);
    private readonly FlipFlopTracker? _visibleFlip;

    private DateTime? _focusNullSince;

    private void OnAnyContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        try
        {
            var src = DescribeElement(e.Source);
            var snd = DescribeElement(sender);
            // De-dupe repeated ContextRequested bursts by routing stage/Handled flag
            if (e.Handled) return;
            string device = e.TryGetPosition(this, out var p) ? $"Pointer({p.X:0.0},{p.Y:0.0})" : "Keyboard";
            WriteUiCategory("[Context]", $"Requested src={src} sender={snd} via={device}");
            _lastContextRequestedAtUtc = DateTime.UtcNow;
            // Begin selection freeze while context menu lifecycle is active
            if (DataContext is MainWindowViewModel vm) vm.BeginSelectionFreeze();
            // Schedule quiet-hold release after menus/hover go idle
            TryReleaseSelectionFreezeSoon();
        }
        catch { }
    }

    private void ChatScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        try
        {
            var sv = sender as ScrollViewer;
            if (sv == null) return;
            _lastScrollActivityUtc = DateTime.UtcNow;
            _lastViewportHeight = sv.Viewport.Height;
            _lastExtentHeight = sv.Extent.Height;
            var bottomGap = GetBottomGap(sv);
            var atBottom = IsAtBottom(sv);
            var guardActive = DateTime.UtcNow < _autoFollowGuardUntilUtc;
            if (!atBottom && guardActive)
            {
                if (bottomGap <= AutoFollowGuardGapTolerance)
                {
                    if (!_stickToBottom)
                    {
                        SafeUiLog($"[AutoScroll][Guard] preserving follow (gap={bottomGap:F1})");
                    }
                    atBottom = true;
                }
                else
                {
                    guardActive = false;
                    _autoFollowGuardUntilUtc = DateTime.MinValue;
                }
            }
            _followChat = atBottom;
            _stickToBottom = atBottom; // keep legacy flag in sync for other helpers
            // Discord-like behavior: when at bottom, clear unread counter; otherwise keep/show jump button
            if (_followChat)
            {
                if (_unreadSinceLastBottom != 0)
                {
                    _unreadSinceLastBottom = 0;
                    UpdateJumpToBottomUi();
                }
            }
            else
            {
                UpdateJumpToBottomUi();
            }
            SafeUiLog($"ScrollChanged: offsetY={sv.Offset.Y:F1}, extentH={sv.Extent.Height:F1}, viewportH={sv.Viewport.Height:F1}, bottomGap={bottomGap:F1}, stick={_stickToBottom}, guard={(guardActive ? "Y" : "N")}");
        }
        catch { }
    }

    private void ChatScroll_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        try
        {
            if (sender is not ScrollViewer sv) return;
            _lastViewportHeight = sv.Viewport.Height;
            var viewportWidth = sv.Viewport.Width;
            if (viewportWidth > 0 && DataContext is MainWindowViewModel vm)
            {
                vm.ChatViewportWidth = viewportWidth;
            }
            if ((e.PreviousSize.Width <= 0 && e.PreviousSize.Height <= 0) || (Math.Abs(e.PreviousSize.Height - e.NewSize.Height) < 0.5 && Math.Abs(e.PreviousSize.Width - e.NewSize.Width) < 0.5))
                return;

            var guardActive = DateTime.UtcNow < _autoFollowGuardUntilUtc;
            if (!_stickToBottom && !guardActive) return;

            CancelChatScrollAnimation();

            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var stillGuard = DateTime.UtcNow < _autoFollowGuardUntilUtc;
                    if (_stickToBottom || stillGuard)
                    {
                        var force = !_stickToBottom && stillGuard;
                        TryScrollChatToBottom(force);
                    }
                }
                catch { }
            }, DispatcherPriority.Background);
        }
        catch { }
    }

    private void OnAnyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            var src = DescribeElement(e.Source);
            var p = e.GetPosition(this);
            var kind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
            WriteUiCategory("[Pointer]", $"Pressed src={src} at ({p.X:0.0},{p.Y:0.0}) kind={kind}");
            CancelChatScrollAnimation();
            if (IsSourceWithinChatScrollBar(e.Source, out var el))
            {
                _stickToBottom = false;
                _autoFollowGuardUntilUtc = DateTime.MinValue;
                _scrollbarPressedActive = true;
                _scrollbarInteractionUntilUtc = DateTime.UtcNow.AddMilliseconds(1500);
                WriteUiCategory("[AutoScroll]", $"Holdoff: ChatScroll {el} press (user drag)");
            }
        }
        catch { }
    }

    private void OnAnyPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        try
        {
            var src = DescribeElement(e.Source);
            var p = e.GetPosition(this);
            WriteUiCategory("[Pointer]", $"Released src={src} at ({p.X:0.0},{p.Y:0.0}) button={e.InitialPressMouseButton}");
            if (_scrollbarPressedActive && IsSourceWithinChatScrollBar(e.Source, out var el))
            {
                _scrollbarPressedActive = false;
                _scrollbarInteractionUntilUtc = DateTime.UtcNow.AddMilliseconds(500);
                WriteUiCategory("[AutoScroll]", $"Holdoff: ChatScroll {el} release (settle)");
            }
            // Try to release selection freeze once interactions quiet down
            TryReleaseSelectionFreezeSoon();
        }
        catch { }
    }

    private void OnAnyPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        try
        {
            var src = DescribeElement(e.Source);
            WriteUiCategory("[Pointer]", $"Wheel src={src} delta=({e.Delta.X:0.00},{e.Delta.Y:0.00})");
            CancelChatScrollAnimation();
            // If the wheel occurs over ChatScroll, assume user is navigating; hold off auto-scroll briefly.
            if (e.Source is Visual v)
            {
                var sv = v.FindAncestorOfType<ScrollViewer>();
                if (sv?.Name == "ChatScroll")
                {
                    // Only unstick on upward scroll attempts (Delta.Y > 0)
                    if (e.Delta.Y > 0.0001)
                    {
                        _stickToBottom = false;
                        _autoFollowGuardUntilUtc = DateTime.MinValue;
                        var now = DateTime.UtcNow;
                        bool wasInactive = now >= _userScrollHoldoffUntilUtc;
                        _userScrollHoldoffUntilUtc = now.AddMilliseconds(700);
                        // Log on activation or after cooldown to avoid spam
                        if (wasInactive || (now - _lastWheelHoldoffLogAtUtc) > TimeSpan.FromMilliseconds(400))
                        {
                            _lastWheelHoldoffLogAtUtc = now;
                            WriteUiCategory("[AutoScroll]", "Holdoff due to user wheel on ChatScroll");
                        }
                    }
                }
            }
        }
        catch { }
    }

    private void OnAnyKeyDown(object? sender, KeyEventArgs e)
    {
        try
        {
            var src = DescribeElement(e.Source);
            WriteUiCategory("[Key]", $"Down src={src} key={e.Key} mods={e.KeyModifiers}");
        }
        catch { }
    }

    private void OnAnyKeyUp(object? sender, KeyEventArgs e)
    {
        try
        {
            var src = DescribeElement(e.Source);
            WriteUiCategory("[Key]", $"Up src={src} key={e.Key} mods={e.KeyModifiers}");
            // Key-ups that close menus should allow releasing the freeze shortly after
            TryReleaseSelectionFreezeSoon();
        }
        catch { }
    }

    private void OnAnyTextInput(object? sender, TextInputEventArgs e)
    {
        try
        {
            var src = DescribeElement(e.Source);
            WriteUiCategory("[Key]", $"Text src={src} len={e.Text?.Length ?? 0}");
        }
        catch { }
    }

    private void AttachMessageHandlers(System.Collections.IEnumerable? items)
    {
        try
        {
            if (items == null) return;
            foreach (var item in items)
            {
                if (item is Message msg && _trackedMessages.Add(msg))
                {
                    msg.PropertyChanged += OnMessagePropertyChanged;
                }
            }
        }
        catch { }
    }

    private void DetachMessageHandlers(System.Collections.IEnumerable? items)
    {
        try
        {
            if (items == null) return;
            foreach (var item in items)
            {
                if (item is Message msg && _trackedMessages.Remove(msg))
                {
                    msg.PropertyChanged -= OnMessagePropertyChanged;
                }
            }
        }
        catch { }
    }

    private void ClearMessageHandlers()
    {
        try
        {
            foreach (var msg in _trackedMessages.ToArray())
            {
                try { msg.PropertyChanged -= OnMessagePropertyChanged; } catch { }
            }
            _trackedMessages.Clear();
        }
        catch { }
    }

    private void OnMessagesCollectionChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    {
        try
        {
            switch (args.Action)
            {
                case System.Collections.Specialized.NotifyCollectionChangedAction.Add:
                    AttachMessageHandlers(args.NewItems);
                    break;
                case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
                    DetachMessageHandlers(args.OldItems);
                    break;
                case System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
                    DetachMessageHandlers(args.OldItems);
                    AttachMessageHandlers(args.NewItems);
                    break;
                case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                    ClearMessageHandlers();
                    break;
            }

            if (_suppressNextAutoScroll) { _suppressNextAutoScroll = false; return; }

            var wasFollowing = _stickToBottom;
            // Ignore replace/move for autoscroll and unread purposes
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace ||
                args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Move)
            {
                SafeUiLog($"MessagesChanged: {args.Action}; no autoscroll/unread change");
                return;
            }
            // Reset (e.g., switching conversations): keep bottom if we were at bottom and clear unread
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                var sv0 = this.FindControl<ScrollViewer>("ChatScroll");
                var atBottomBefore = sv0 != null && IsAtBottom(sv0);
                BeginRehydrationBatch(atBottomBefore);
                return;
            }
            // Additions at end: follow only if at bottom (follow mode); otherwise count unread and show jump
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                var addCount = args.NewItems?.Count ?? 1;
                var sv = this.FindControl<ScrollViewer>("ChatScroll");
                bool appendedAtEnd = false;
                try
                {
                    if (DataContext is MainWindowViewModel vm && args.NewStartingIndex >= 0)
                    {
                        var total = vm.Messages.Count;
                        // New items appended at end if starting index places the block as the last items
                        appendedAtEnd = (args.NewStartingIndex + addCount) == total;
                    }
                }
                catch { }
                // If in rehydration batch, just extend the batch window and skip unread/autoscroll
                if (_rehydrating)
                {
                    ExtendRehydrationWindow();
                    return;
                }
                // Re-evaluate bottom state using actual scroll metrics to avoid stale _followChat
                bool atBottomNow = sv != null && IsAtBottom(sv);
                // More aggressive autoscroll - follow if we were following OR if we're near the bottom
                bool nearBottom = sv != null && GetBottomGap(sv) <= 100.0; // within 100px of bottom
                bool shouldFollow = appendedAtEnd && (wasFollowing || atBottomNow || nearBottom);
                if (shouldFollow)
                {
                    _followChat = true;
                    _stickToBottom = true;
                    ForceScrollToBottom(wasFollowing ? "Add while following" : "Add while newly at bottom");
                }
                else if (appendedAtEnd && !atBottomNow)
                {
                    _followChat = atBottomNow;
                    _stickToBottom = atBottomNow;
                    _unreadSinceLastBottom += addCount;
                    UpdateJumpToBottomUi();
                }
                else
                {
                    _followChat = atBottomNow;
                    _stickToBottom = atBottomNow;
                    // Insertions not at end (e.g., history load) should not affect unread or autoscroll
                    SafeUiLog($"MessagesChanged: Non-end Add (idx={args.NewStartingIndex}, count={addCount}); no unread change");
                }
            }
            // Removals: preserve visual anchor by maintaining offset from bottom when not sticking
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove && !_followChat)
            {
                var sv = this.FindControl<ScrollViewer>("ChatScroll");
                if (sv != null)
                {
                    var bottomGap = GetBottomGap(sv);
                    SafeUiLog($"MessagesChanged: Remove detected; preserving bottomGap={bottomGap:F1}");
                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            sv.UpdateLayout();
                            var targetY = Math.Max(0, sv.Extent.Height - sv.Viewport.Height - bottomGap);
                            SafeUiLog($"PreserveOnRemove: set offsetY -> {targetY:F1} (extentH={sv.Extent.Height:F1}, viewportH={sv.Viewport.Height:F1})");
                            sv.Offset = new Vector(sv.Offset.X, targetY);
                        }
                        catch { }
                    });
                }
            }
        }
        catch { }
    }

    private bool _rehydrating;
    private DateTime _rehydrateUntilUtc;
    private bool _rehydrateWasAtBottom;
    private int _rehydrateTicket;

    private void BeginRehydrationBatch(bool wasAtBottom)
    {
        try
        {
            _rehydrating = true;
            _rehydrateWasAtBottom = wasAtBottom;
            _rehydrateUntilUtc = DateTime.UtcNow.AddMilliseconds(250);
            var my = ++_rehydrateTicket;
            Dispatcher.UIThread.Post(() => CheckRehydrationComplete(my), DispatcherPriority.Background);
        }
        catch { }
    }

    private void ExtendRehydrationWindow()
    {
        try { _rehydrateUntilUtc = DateTime.UtcNow.AddMilliseconds(250); } catch { }
    }

    private void CheckRehydrationComplete(int ticket)
    {
        try
        {
            if (ticket != _rehydrateTicket) return;
            if (DateTime.UtcNow < _rehydrateUntilUtc)
            {
                // Not done yet; re-check later
                Dispatcher.UIThread.Post(() => CheckRehydrationComplete(ticket), DispatcherPriority.Background);
                return;
            }
            _rehydrating = false;
            if (_rehydrateWasAtBottom)
            {
                // After reload is quiet, stick to bottom once
                ForceScrollToBottom("Rehydration complete (was at bottom)");
            }
        }
        catch { }
    }

    private void ScrollChatToBottomOneShot()
    {
        try
        {
            if (!this.IsActive) return;
            if (!IsSafeToAutoScroll()) return;
            if (_bringIntoViewPosted) return;
            _bringIntoViewPosted = true;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    _bringIntoViewPosted = false;
                    var now = DateTime.UtcNow;
                    if ((now - _lastBringIntoViewAtUtc) <= TimeSpan.FromMilliseconds(60))
                        return; // coalesce rapid repeats

                    var sv = this.FindControl<ScrollViewer>("ChatScroll");
                    if (sv == null) return;

                    // Only act if there's actually somewhere to scroll (prevents no-op BringIntoView spam)
                    if (!ShouldScrollToBottom(sv))
                        return;

                    // Suppress if extent hasn't grown since our last auto-scroll
                    if (sv.Extent.Height <= _lastAutoScrollExtentHeight + 0.1)
                        return;

                    var targetY = Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
                    if (BringChatEndIntoView())
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                var sv2 = this.FindControl<ScrollViewer>("ChatScroll");
                                if (sv2 == null) return;
                                sv2.UpdateLayout();
                                var finalTarget = Math.Max(0, sv2.Extent.Height - sv2.Viewport.Height);
                                var usedAnimation = SmoothScrollToOffset(sv2, finalTarget);
                                if (!usedAnimation && Math.Abs(sv2.Offset.Y - finalTarget) > 0.5)
                                {
                                    try { sv2.ScrollToEnd(); }
                                    catch { sv2.Offset = new Vector(sv2.Offset.X, finalTarget); }
                                }
                            }
                            catch { }
                        }, DispatcherPriority.Background);
                    }
                    else
                    {
                        var usedAnimation = SmoothScrollToOffset(sv, targetY);
                        if (!usedAnimation && Math.Abs(sv.Offset.Y - targetY) > 0.5)
                        {
                            try { sv.ScrollToEnd(); }
                            catch { sv.Offset = new Vector(sv.Offset.X, targetY); }
                        }
                    }
                    _lastAutoScrollExtentHeight = sv.Extent.Height;
                    _lastBringIntoViewAtUtc = now;
                }
                catch { }
            }, DispatcherPriority.Render);
        }
        catch { }
    }

    private void JumpToBottom_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            _followChat = true;
            _stickToBottom = true;
            _unreadSinceLastBottom = 0;
            UpdateJumpToBottomUi();
            // User intent must override any holdoffs: force scroll now
            ForceScrollToBottom("JumpToPresent click");
        }
        catch { }
    }

    private void UpdateJumpToBottomUi()
    {
        try
        {
            var btn = this.FindControl<Button>("JumpToBottomButton");
            var lbl = this.FindControl<TextBlock>("JumpToBottomLabel");
            if (btn != null)
            {
                btn.IsVisible = !_stickToBottom && _unreadSinceLastBottom > 0;
                SafeUiLog($"JumpUI: visible={btn.IsVisible}, unread={_unreadSinceLastBottom}");
            }
            if (lbl != null)
            {
                lbl.Text = _unreadSinceLastBottom > 0 ? $"Jump to present ({_unreadSinceLastBottom})" : "Jump to present";
            }
        }
        catch { }
    }

#if DEBUG
    private void AddIncomingTestMessage(string content)
    {
        try
        {
            if (DataContext is not MainWindowViewModel vm) return;
            var contact = vm.SelectedContact ?? vm.Contacts.FirstOrDefault();
            if (contact == null) return;
            var self = P2PTalk.Services.AppServices.Identity.UID ?? string.Empty;
            var msg = new Message
            {
                Id = Guid.NewGuid(),
                SenderUID = contact.UID,
                RecipientUID = self,
                Content = content,
                Timestamp = DateTime.UtcNow,
                ReceivedUtc = DateTime.UtcNow,
                DeliveryStatus = "Received"
            };
            vm.Messages.Add(msg);
            SafeUiLog($"[Test] Injected incoming message for {contact.UID}: {content}");
        }
        catch { }
    }
#endif

    // Removed old auto-scroll scheduler in favor of one-shot follow

    private void TryScrollChatToBottom(bool force)
    {
        try
        {
            var sv = this.FindControl<ScrollViewer>("ChatScroll");
            if (sv == null) return;
            if (!force && !_stickToBottom) return;
            if (!IsSafeToAutoScroll())
            {
                WriteUiCategory("[AutoScroll]", $"Skipped TryScrollToBottom(force={force}) due to unsafe state");
                return;
            }
            if (!this.IsActive) return; // avoid background window churn
            var canScroll = ShouldScrollToBottom(sv);
            if (!canScroll)
            {
                // No-op when already at bottom
            }
            else
            {
                var fromY = sv.Offset.Y;
                var targetY = Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
                var delta = Math.Abs(targetY - fromY);
                var guardActivated = false;
                if (BringChatEndIntoView())
                {
                    guardActivated = true;
                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            var sv2 = this.FindControl<ScrollViewer>("ChatScroll");
                            if (sv2 == null) return;
                            sv2.UpdateLayout();
                            var startY = sv2.Offset.Y;
                            var finalTarget = Math.Max(0, sv2.Extent.Height - sv2.Viewport.Height);
                            var usedAnimation = SmoothScrollToOffset(sv2, finalTarget);
                            if (!usedAnimation && Math.Abs(sv2.Offset.Y - finalTarget) > 0.5)
                            {
                                try { sv2.ScrollToEnd(); }
                                catch { sv2.Offset = new Vector(sv2.Offset.X, finalTarget); }
                            }
                            SafeUiLog($"TryScrollToBottom(force={force}): fromY={startY:F1} -> targetY={finalTarget:F1} (mode={(usedAnimation ? "anim" : "snap")}, extentH={sv2.Extent.Height:F1}, viewportH={sv2.Viewport.Height:F1})");
                        }
                        catch { }
                    }, DispatcherPriority.Background);
                }
                else
                {
                    var usedAnimation = SmoothScrollToOffset(sv, targetY);
                    if (!usedAnimation && Math.Abs(sv.Offset.Y - targetY) > 0.5)
                    {
                        try { sv.ScrollToEnd(); }
                        catch { sv.Offset = new Vector(sv.Offset.X, targetY); }
                    }
                    if (delta > 0.5)
                    {
                        guardActivated = true;
                    }
                    SafeUiLog($"TryScrollToBottom(force={force}): fromY={fromY:F1} -> targetY={targetY:F1} (mode={(usedAnimation ? "anim" : "snap")}, extentH={sv.Extent.Height:F1}, viewportH={sv.Viewport.Height:F1})");
                }
                if (guardActivated)
                {
                    _autoFollowGuardUntilUtc = DateTime.UtcNow.Add(AutoFollowGuardWindow);
                }
            }
        }
        catch { }
    }

    private bool BringChatEndIntoView()
    {
        try
        {
            var anchor = this.FindControl<Border>("ChatEndAnchor");
            if (anchor == null) return false;
            anchor.BringIntoView();
            WriteUiCategory("[AutoScroll][BringIntoView]", $"Requested for {DescribeElement(anchor)}");
            return true;
        }
        catch { return false; }
    }

    private ScrollAnimator? EnsureChatScrollAnimator(ScrollViewer sv)
    {
        try
        {
            if (_chatScrollAnimator is { } existing && existing.IsFor(sv))
            {
                return existing;
            }

            _chatScrollAnimator?.Dispose();
            _chatScrollAnimator = new ScrollAnimator(sv);
            return _chatScrollAnimator;
        }
        catch { return null; }
    }

    private void CancelChatScrollAnimation()
    {
        try { _chatScrollAnimator?.Cancel(); }
        catch { }
    }

    private bool SmoothScrollToOffset(ScrollViewer sv, double targetY)
    {
        try
        {
            sv.UpdateLayout();
            targetY = Math.Max(0, targetY);
            var animator = EnsureChatScrollAnimator(sv);
            if (animator != null)
            {
                return animator.MoveTo(targetY);
            }
        }
        catch { }

        try
        {
            var current = sv.Offset;
            sv.Offset = new Vector(current.X, targetY);
        }
        catch { }
        return false;
    }

    private static double GetBottomGap(ScrollViewer sv)
    {
        try
        {
            return Math.Max(0, sv.Extent.Height - sv.Offset.Y - sv.Viewport.Height);
        }
        catch { return 0; }
    }

    private static bool IsAtBottom(ScrollViewer sv)
    {
        try
        {
            const double threshold = 2.0; // px: treat within 2px as at bottom
            return GetBottomGap(sv) <= threshold;
        }
        catch { return false; }
    }

    private void ForceScrollToBottom(string reason)
    {
        try
        {
            var sv = this.FindControl<ScrollViewer>("ChatScroll");
            if (sv == null) return;
            _autoFollowGuardUntilUtc = DateTime.UtcNow.Add(AutoFollowGuardWindow);
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    sv.UpdateLayout();
                    var targetY = Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
                    var fromY = sv.Offset.Y;
                    var usedAnimation = SmoothScrollToOffset(sv, targetY);
                    if (!usedAnimation && Math.Abs(sv.Offset.Y - targetY) > 0.5)
                    {
                        try { sv.ScrollToEnd(); }
                        catch { sv.Offset = new Vector(sv.Offset.X, targetY); }
                    }
                    _lastAutoScrollExtentHeight = sv.Extent.Height;
                    _lastBringIntoViewAtUtc = DateTime.UtcNow;
                    _followChat = true;
                    _stickToBottom = true;
                    _autoFollowGuardUntilUtc = DateTime.UtcNow.Add(AutoFollowGuardWindow);
                    _unreadSinceLastBottom = 0;
                    UpdateJumpToBottomUi();
                    SafeUiLog($"[AutoScroll][Force] {reason}: fromY={fromY:F1} -> {targetY:F1} (mode={(usedAnimation ? "anim" : "snap")}, extentH={sv.Extent.Height:F1}, viewportH={sv.Viewport.Height:F1})");
                }
                catch { }
            }, DispatcherPriority.Render);
        }
        catch { }
    }

    private static bool ShouldScrollToBottom(ScrollViewer sv)
    {
        try
        {
            // If nothing to scroll or already at bottom within epsilon, skip.
            var extent = sv.Extent.Height;
            var view = sv.Viewport.Height;
            var offsetY = sv.Offset.Y;
            if (extent <= 0 || view <= 0) return false;
            var targetY = Math.Max(0, extent - view);
            const double eps = 0.5; // sub-pixel threshold
            return Math.Abs(offsetY - targetY) > eps;
        }
        catch { return false; }
    }

    private void MessageContent_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        try
        {
            var guardActive = DateTime.UtcNow < _autoFollowGuardUntilUtc;
            if (!_stickToBottom && !guardActive) return;
            if (e.PreviousSize.Width <= 0 && e.PreviousSize.Height <= 0) return;
            if (Math.Abs(e.NewSize.Height - e.PreviousSize.Height) < 0.5) return;

            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var guardNow = DateTime.UtcNow < _autoFollowGuardUntilUtc;
                    var forceFollow = !_stickToBottom && guardNow;
                    TryScrollChatToBottom(force: forceFollow);
                }
                catch { }
            }, DispatcherPriority.Background);
        }
        catch { }
    }

    private void OnMessagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        try
        {
            if (sender is not Message message) return;
            if (e.PropertyName is not (nameof(Message.LinkPreview) or nameof(Message.HasLinkPreview))) return;

            var preview = message.LinkPreview;
            var hasPreview = preview != null && !preview.IsEmpty;
            SafeUiLog($"[AutoScroll][Preview] property={e.PropertyName} message={message.Id} hasPreview={hasPreview} stick={_stickToBottom}");
            if (!hasPreview) return;

            var messageId = message.Id;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var sv = this.FindControl<ScrollViewer>("ChatScroll");
                    if (sv == null) return;
                    var bottomGap = GetBottomGap(sv);
                    var atBottom = bottomGap <= 2.0;
                    var guardActive = DateTime.UtcNow < _autoFollowGuardUntilUtc;
                    SafeUiLog($"[AutoScroll][Preview] followCheck message={messageId} stick={_stickToBottom} bottomBefore={atBottom}, gap={bottomGap:F1}, guard={(guardActive ? "Y" : "N")}");
                    if (!_stickToBottom && !atBottom)
                    {
                        if (!(guardActive && bottomGap <= AutoFollowGuardGapTolerance)) return;
                        SafeUiLog($"[AutoScroll][Preview] guard-follow message={messageId} gap={bottomGap:F1}");
                    }
                    ForceScrollToBottom("Link preview materialized");
                }
                catch { }
            }, DispatcherPriority.Background);
        }
        catch { }
    }

    private bool IsSafeToAutoScroll()
    {
        try
        {
            // Defer during focus flip windows (recent flip within 50ms)
            if (IsFocusUnstable()) return false;
            // Defer immediately after a context request to avoid racing menu open
            if ((DateTime.UtcNow - _lastContextRequestedAtUtc) <= TimeSpan.FromMilliseconds(400)) return false;
            // Defer while user is interacting with the chat scrollbar or during wheel holdoff
            if (DateTime.UtcNow < _userScrollHoldoffUntilUtc) return false;
            if (IsChatScrollInteractionActive()) return false;
            if (IsAnyPopupOpen()) return false;
            if (IsHoverBarRecentlyActive()) return false; // let hover bar interactions settle
            // Decouple Contacts list: if focus or pointer is within ContactsList, skip autoscroll
            try
            {
                var contacts = this.FindControl<ListBox>("ContactsList");
                if (contacts != null)
                {
                    if (contacts.IsPointerOver) return false;
                    var focused = this.FocusManager?.GetFocusedElement();
                    if (focused is Visual fv && fv.FindAncestorOfType<ListBox>()?.Name == "ContactsList") return false;
                }
            }
            catch { }
            var fe = this.FocusManager?.GetFocusedElement();
            if (fe is null) return true;
            if (fe is MenuItem) return false;
            if (fe is Button) return false; // do not disturb message/action buttons
            if (fe is IInputElement ie && IsPopupElement(ie)) return false;
            return true;
        }
        catch { return true; }
    }

    private DateTime _lastFocusFlipAtUtc;
    private bool IsFocusUnstable()
    {
        try
        {
            // Called from FocusFlip tracker via Note; when flips happen we update this timestamp
            var dt = DateTime.UtcNow - _lastFocusFlipAtUtc;
            return dt <= TimeSpan.FromMilliseconds(50);
        }
        catch { return false; }
    }

    private bool IsPopupElement(IInputElement element)
    {
        try
        {
            if (element is Visual v)
            {
                    var pr = v.FindAncestorOfType<PopupRoot>();
                    if (pr == null) return false;
                    // Ignore transient hover action popups (MsgActionsHost) for gating decisions
                    if (IsHoverActionsPopup(pr)) return false;
                    return true;
            }
        }
        catch { }
        return false;
    }

    private bool IsAnyPopupOpen()
    {
        try
        {
            foreach (var pr in this.GetVisualDescendants().OfType<PopupRoot>())
            {
                if (!pr.IsEffectivelyVisible) continue;
                if (IsHoverActionsPopup(pr)) continue;
                return true;
            }
        }
        catch { }
        return false;
    }

    private static bool IsHoverActionsPopup(PopupRoot pr)
    {
        try
        {
            foreach (var b in pr.GetVisualDescendants().OfType<Border>())
            {
                if (string.Equals(b.Name, "MsgActionsHost", StringComparison.Ordinal))
                    return true;
            }
        }
        catch { }
        return false;
    }

    private bool _hoverBarPointerInside;
    private DateTime _hoverBarLastExitUtc;

    private void AttachHoverBarStabilityHandlers()
    {
        try
        {
            foreach (var pr in this.GetVisualDescendants().OfType<PopupRoot>())
            {
                if (!IsHoverActionsPopup(pr)) continue;
                pr.PointerEntered -= HoverPopup_PointerEntered;
                pr.PointerExited  -= HoverPopup_PointerExited;
                pr.PointerEntered += HoverPopup_PointerEntered;
                pr.PointerExited  += HoverPopup_PointerExited;
            }
        }
        catch { }
    }

    private void HoverPopup_PointerEntered(object? sender, PointerEventArgs e)
    {
        _hoverBarPointerInside = true;
            try
            {
                if (DataContext is MainWindowViewModel vm) vm.BeginSelectionFreeze();
            }
            catch { }
    }

    private void HoverPopup_PointerExited(object? sender, PointerEventArgs e)
    {
        _hoverBarPointerInside = false;
        _hoverBarLastExitUtc = DateTime.UtcNow;
            TryReleaseSelectionFreezeSoon();
    }

    private bool IsHoverBarRecentlyActive()
    {
        if (_hoverBarPointerInside) return true;
        return (DateTime.UtcNow - _hoverBarLastExitUtc) <= TimeSpan.FromMilliseconds(150);
    }

    private DateTime _lastContextRequestedAtUtc;
    private DateTime _lastContextMenuOpenedAtUtc;

    private bool IsAnyContextMenuOpen()
    {
        try
        {
            foreach (var cm in this.GetVisualDescendants().OfType<ContextMenu>())
            {
                if (cm.IsOpen) return true;
            }
        }
        catch { }
        return false;
    }

    private DateTime _freezeQuietStartUtc;
    private CancellationTokenSource? _freezePollCts;
    private static readonly TimeSpan FreezeQuietHold = TimeSpan.FromMilliseconds(180);

    private void TryReleaseSelectionFreezeSoon()
    {
        try
        {
            _freezePollCts?.Cancel();
        }
        catch { }
    _freezePollCts = new System.Threading.CancellationTokenSource();
    var token = _freezePollCts!.Token;
        _freezeQuietStartUtc = DateTime.UtcNow;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    // Keep waiting while context menu is open or hover is active/recent
                    var anyContext = false;
                    var hoverActive = false;
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        anyContext = IsAnyContextMenuOpen();
                        hoverActive = IsHoverBarRecentlyActive();
                    });
                    if (anyContext || hoverActive)
                    {
                        _freezeQuietStartUtc = DateTime.UtcNow; // reset quiet window
                    }
                    if ((DateTime.UtcNow - _freezeQuietStartUtc) >= FreezeQuietHold)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (DataContext is MainWindowViewModel vm) vm.EndSelectionFreeze();
                        });
                        return;
                    }
                    await System.Threading.Tasks.Task.Delay(50, token);
                }
            }
            catch { }
        }, token);
    }
    private DateTime _userScrollHoldoffUntilUtc;
    private bool _scrollbarPressedActive;
    private DateTime _scrollbarInteractionUntilUtc;
    private DateTime _lastWheelHoldoffLogAtUtc;

    private bool IsChatScrollInteractionActive()
    {
        try
        {
            var sv = this.FindControl<ScrollViewer>("ChatScroll");
            if (sv == null) return false;
            if (_scrollbarPressedActive || DateTime.UtcNow < _scrollbarInteractionUntilUtc) return true;
            var anyThumbHot = sv.GetVisualDescendants().OfType<Thumb>().Any(t => t.IsPointerOver);
            if (anyThumbHot) return true;
            var anyBarHot = sv.GetVisualDescendants().OfType<ScrollBar>().Any(sb => sb.IsPointerOver);
            return anyBarHot;
        }
        catch { return false; }
    }

    private bool IsSourceWithinChatScrollBar(object? source, out string element)
    {
        element = "unknown";
        try
        {
            if (source is Visual v)
            {
                var sv = v.FindAncestorOfType<ScrollViewer>();
                if (sv?.Name == "ChatScroll")
                {
                    if (v.FindAncestorOfType<Thumb>() != null) { element = "Thumb"; return true; }
                    if (v.FindAncestorOfType<ScrollBar>() != null) { element = "ScrollBar"; return true; }
                }
            }
        }
        catch { }
        return false;
    }

    // Called from ViewModel before removing an item to preserve scroll position when not stuck to bottom
    public void PreserveScrollOnRemoval()
    {
        try
        {
            var sv = this.FindControl<ScrollViewer>("ChatScroll");
            if (sv == null) return;
            if (_stickToBottom) return;
            CancelChatScrollAnimation();
            var bottomGap = Math.Max(0, sv.Extent.Height - sv.Offset.Y - sv.Viewport.Height);
            SafeUiLog($"PreserveScrollOnRemoval: bottomGap snapshot={bottomGap:F1}");
            _ = Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    sv.UpdateLayout();
                    var targetY = Math.Max(0, sv.Extent.Height - sv.Viewport.Height - bottomGap);
                    SafeUiLog($"PreserveScrollOnRemoval: set offsetY -> {targetY:F1} (extentH={sv.Extent.Height:F1}, viewportH={sv.Viewport.Height:F1})");
                    sv.Offset = new Vector(sv.Offset.X, targetY);
                }
                catch { }
            });
        }
        catch { }
    }

    // Allow dragging the window by the custom drag bar at the top
    private void DragBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        }
        catch { }
    }

    // Toggle visibility/width of the Left panel (contacts). Nav rail is a separate column and always visible.
    private void ToggleLeftPanel_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var s2 = AppServices.Settings.Settings;
            var grid = this.FindControl<Grid>("BodyGrid");
            var leftRoot = this.FindControl<Grid>("LeftPanelRoot");
            var dividerContactsChat = grid?.ColumnDefinitions.Count >= 3 ? grid.ColumnDefinitions[2] : null;
            if (grid?.ColumnDefinitions is { Count: >= 6 })
            {
                var contactsCol = grid.ColumnDefinitions[1];
                if (_leftColumnOriginalMinWidthDefinition is null)
                    _leftColumnOriginalMinWidthDefinition = contactsCol.MinWidth;
                double current = contactsCol.Width.Value;
                bool willCollapse = current > 0.1;
                double target = willCollapse ? 0.0 : Math.Max(_leftPanelLastWidth ?? (s2.MainLeftWidth ?? 280.0), 200.0);
                if (willCollapse) _leftPanelLastWidth = current;

                // Fast path: if this action would collapse the left AND right is already collapsed, collapse both instantly (no animation)
                if (willCollapse && grid.ColumnDefinitions[5].Width.Value <= 0.1)
                {
                    CollapseBothInstant(grid);
                    if (leftRoot != null)
                    {
                        if (_leftPanelOriginalMinWidth is null) _leftPanelOriginalMinWidth = leftRoot.MinWidth;
                        leftRoot.IsVisible = false;
                        leftRoot.MinWidth = 0;
                    }
                    // Preserve previously saved width (do not overwrite with zero)
                    _ = System.Threading.Tasks.Task.Run(() => AppServices.Settings.Save(AppServices.Passphrase));
                    return;
                }

                var anim = AnimateColumnWidth(contactsCol, target);
                if (dividerContactsChat != null)
                    dividerContactsChat.Width = (target <= 0.1) ? new GridLength(0) : new GridLength(1);
                if (target <= 0.1)
                {
                    contactsCol.MinWidth = 0;
                    grid.ColumnDefinitions[3].Width = new GridLength(1, GridUnitType.Star);
                }
                else
                {
                    contactsCol.MinWidth = _leftColumnOriginalMinWidthDefinition ?? contactsCol.MinWidth;
                }
                if (leftRoot != null)
                {
                    if (_leftPanelOriginalMinWidth is null) _leftPanelOriginalMinWidth = leftRoot.MinWidth;
                    leftRoot.IsVisible = target > 0.0;
                    leftRoot.MinWidth = (target <= 0.1) ? 0.0 : (_leftPanelOriginalMinWidth ?? leftRoot.MinWidth);
                }
                s2.MainLeftWidth = (target > 0.1) ? target : s2.MainLeftWidth; // don't override stored width with 0
                grid.InvalidateMeasure();
                grid.InvalidateArrange();
                _ = anim.ContinueWith(_ => Dispatcher.UIThread.Post(NormalizeCentralLayout));
            }
            _ = System.Threading.Tasks.Task.Run(() => AppServices.Settings.Save(AppServices.Passphrase));
        }
        catch { }
    }

    // Open the avatar context menu on click
    private void UserAvatar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            var props = e.GetCurrentPoint(this).Properties;
            if (props.IsRightButtonPressed || props.IsLeftButtonPressed)
            {
                if (sender is Border b && b.ContextMenu is ContextMenu cm)
                {
                    cm.PlacementTarget = b;
                    cm.Open(b);
                    e.Handled = true;
                    try { InteractionLogger.Log("[AvatarMenu] Opened"); } catch { }
                }
                if (props.IsRightButtonPressed)
                    try { InteractionLogger.Log("[AvatarMenu] Open requested (right-click)"); } catch { }
                else
                    try { InteractionLogger.Log("[AvatarMenu] Open requested (left-click)"); } catch { }
            }
        }
        catch { }
    }

    // Presence flags used by the avatar menu checks
    public bool IsOnline { get; private set; } = true;
    public bool IsAway { get; private set; }
    public bool IsDnd { get; private set; }
    public bool IsOffline { get; private set; }
    public string CurrentPresenceText
        => IsOnline ? "Online" : IsAway ? "Idle" : IsDnd ? "Do Not Disturb" : "Invisible";

    private void SetPresence(bool online, bool away, bool dnd, bool offline)
    {
        try
        {
            IsOnline = online; IsAway = away; IsDnd = dnd; IsOffline = offline;
            // Notify bindings
            RaisePropChanged(nameof(IsOnline));
            RaisePropChanged(nameof(IsAway));
            RaisePropChanged(nameof(IsDnd));
            RaisePropChanged(nameof(IsOffline));
            RaisePropChanged(nameof(CurrentPresenceText));
            // Persist to settings and broadcast to peers
            try
            {
                var status = online ? Models.PresenceStatus.Online : away ? Models.PresenceStatus.Idle : dnd ? Models.PresenceStatus.DoNotDisturb : Models.PresenceStatus.Invisible;
                if (AppServices.Settings.Settings.Status != status)
                {
                    AppServices.Settings.Settings.Status = status;
                    AppServices.Settings.Save(AppServices.Passphrase);
                }
                Utilities.Logger.Log($"Presence set to {status}");
                try { InteractionLogger.Log($"[AvatarMenu] Presence set to {status}"); } catch { }
                BroadcastPresence(status);
            }
            catch { }
        }
        catch { }
    }

    private void SetStatusOnline_Click(object? sender, RoutedEventArgs e) => SetStatusOnlineCommand.Execute(null);
    private void SetStatusAway_Click(object? sender, RoutedEventArgs e) => SetStatusAwayCommand.Execute(null);
    private void SetStatusDnd_Click(object? sender, RoutedEventArgs e) => SetStatusDndCommand.Execute(null);
    private void SetStatusOffline_Click(object? sender, RoutedEventArgs e) => SetStatusOfflineCommand.Execute(null);

    private static void BroadcastPresence(Models.PresenceStatus status)
    {
        try
        {
            // Use fast path to active sessions only
            P2PTalk.Services.AppServices.Network.BroadcastPresenceToActiveSessions(status);
        }
        catch { }
    }

    // Presence duration support
    private System.Threading.CancellationTokenSource? _presenceCts;
    private Models.PresenceStatus? _presenceRestoreTarget;
    // Original MinWidth values for side columns so we can restore after collapse
    private double? _leftColumnOriginalMinWidthDefinition;
    private double? _rightColumnOriginalMinWidthDefinition;

    private void StatusDuration_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not MenuItem mi) return;
            var parentMi = mi.GetVisualAncestors().OfType<MenuItem>().Skip(1).FirstOrDefault();
            var statusLabel = (parentMi?.Header as string) ?? "Online";
            var tag = (mi.Tag as string) ?? string.Empty;
            var parameter = string.IsNullOrWhiteSpace(tag) ? null : $"{statusLabel}|{tag}";
            if (parameter != null) StatusDurationCommand.Execute(parameter);
        }
        catch { }
    }

    private void ApplyPresence(Models.PresenceStatus status)
    {
        switch (status)
        {
            case Models.PresenceStatus.Online: SetPresence(true, false, false, false); break;
            case Models.PresenceStatus.Idle: SetPresence(false, true, false, false); break;
            case Models.PresenceStatus.DoNotDisturb: SetPresence(false, false, true, false); break;
            case Models.PresenceStatus.Invisible: SetPresence(false, false, false, true); break;
            default: SetPresence(true, false, false, false); break;
        }
    }

    private async void CopyUid_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var uid = P2PTalk.Services.AppServices.Identity.UID ?? string.Empty;
            if (string.IsNullOrWhiteSpace(uid)) return;
            var cb = this.Clipboard;
            if (cb != null)
            {
                await cb.SetTextAsync(uid);
                try { InteractionLogger.Log("[AvatarMenu] Copied UID"); } catch { }
            }
        }
        catch { }
    }

    private async void CopyContactUid_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var uid = (sender as MenuItem)?.CommandParameter as string;
            if (string.IsNullOrWhiteSpace(uid)) return;

            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var clipboard = lifetime?.MainWindow?.Clipboard ?? this.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(uid);
                try { InteractionLogger.Log("[Contacts] Copied UID"); } catch { }
            }
        }
        catch (Exception ex)
        {
            SafeLogContextError(ex, sender);
        }
    }

    private async void CopySelectedContactKey_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not MainWindowViewModel vm) return;
            var c = vm.SelectedContact;
            if (c == null) return;
            // Prefer observed peer pubkey via PeerManager; fallback to expected key on contact
            string? hex = null;
            try
            {
                var peer = AppServices.Peers.Peers.FirstOrDefault(x => string.Equals(x.UID, c.UID, StringComparison.OrdinalIgnoreCase));
                hex = peer?.PublicKeyHex;
            }
            catch { }
            if (string.IsNullOrWhiteSpace(hex)) hex = c.ExpectedPublicKeyHex;
            if (string.IsNullOrWhiteSpace(hex)) return;
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            if (lifetime?.MainWindow?.Clipboard != null)
                await lifetime.MainWindow.Clipboard.SetTextAsync(hex);
            try { InteractionLogger.Log("[FullProfile] Copied public key"); } catch { }
        }
        catch { }
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    private void RaisePropChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    // [LAYOUT] Restore geometry using lightweight cache; fallback to previous settings values; ensure visible on current monitors.
    private void RestoreLayoutFromCacheOrSettings(WindowStateSettings s)
    {
        double w = Width, h = Height;
        var pos = Position;
        try
        {
            var cached = LayoutCache.Load("MainWindow");
            if (cached is not null)
            {
                if (cached.Width is double cw && cw > 0) w = cw;
                if (cached.Height is double ch && ch > 0) h = ch;
                if (cached.X is double cx && cached.Y is double cy) pos = new PixelPoint((int)cx, (int)cy);
                if (cached.State is int cst) WindowState = (WindowState)cst;
            }
            else
            {
                // One-time migration path: use previous settings values if present
                if (s.Width is double sw && sw > 0) w = sw;
                if (s.Height is double sh && sh > 0) h = sh;
                if (s.X is double sx && s.Y is double sy) pos = new PixelPoint((int)sx, (int)sy);
                if (s.State is int st) WindowState = (WindowState)st;
            }
        }
        catch { }
        WindowBoundsHelper.EnsureVisible(this, ref w, ref h, ref pos);
        Width = w; Height = h; Position = pos;
    }
    // [LAYOUT] Save current geometry to cache on close only; keep panel widths persisted via settings separately.
    private void SaveLayoutAndPanels(WindowStateSettings s)
    {
        try
        {
            // Save geometry to cache file (not settings.p2e)
            var layout = new LayoutCache.WindowLayout(Width, Height, Position.X, Position.Y, (int)WindowState);
            LayoutCache.Save("MainWindow", layout);
        }
        catch { }
        // Persist panel widths (this is not window geometry and remains in settings)
        try
        {
            var grid = this.FindControl<Grid>("BodyGrid");
            if (grid?.ColumnDefinitions is { Count: >= 4 })
            {
                var currentLeft = grid.ColumnDefinitions[1].Width.Value; // contacts
                // Persist left width only when expanded
                if (currentLeft > 0.1)
                {
                    AppServices.Settings.Settings.MainLeftWidth = currentLeft;
                }
                // Always persist right width as-is
                // Right panel now resides at column index 5 (after two 1px divider columns)
                AppServices.Settings.Settings.MainRightWidth = grid.ColumnDefinitions[5].Width.Value;
            }
            _ = System.Threading.Tasks.Task.Run(() => AppServices.Settings.Save(AppServices.Passphrase));
        }
        catch { }
    }

    private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            if (AppServices.Settings.Settings.MainWindow is { } s)
            {
                // Save geometry to cache and persist panel widths to settings on close only
                SaveLayoutAndPanels(s);
            }

            // Perform graceful shutdown of services (idempotent, safe to call once).
            try { AppServices.Shutdown(); } catch { }
        }
        catch { }
    }

    
    private void Settings_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Open inline settings overlay instead of separate window
        ShowSettingsOverlay();
    }

    private void Monitoring_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        P2PTalk.Services.WindowManager.ShowSingleton<MonitoringWindow>();
    }

    private void Logs_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        P2PTalk.Services.WindowManager.ShowSingleton<LogViewerWindow>();
    }

    private void OpenLogs_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try { P2PTalk.Services.WindowManager.ShowSingleton<NetworkWindow>()?.SwitchToTab("Logging"); } catch { }
    }

    private void Home_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try { ErrorLogger.LogException(new InvalidOperationException("[Restore] Home_Click invoked"), source: "Restore.MainWindow"); } catch { }
        // For now, Home focuses the chat center; extend to switch views if needed
        try { this.Focus(); } catch { }
    }

    // Opens the Network window (Peers/Network settings). Bound to multiple Buttons via Click in XAML.
    private void Network_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            P2PTalk.Services.WindowManager.ShowSingleton<NetworkWindow>()?.Activate();
        }
        catch (Exception ex)
        {
            try { Logger.Log($"Network_Click failed: {ex.Message}"); } catch { }
        }
    }

    private void AddContact_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var w = new AddContactWindow();
        w.Show(this);
    }

    // Ensure right-clicking a contact doesn't crash; log detailed info to error.txt and select item under cursor.
    private void ContactsList_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        try
        {
            // Trace entry
            SafeLogContextTrace("ContactsList_ContextRequested: enter", e.Source);
            if (IsAnyContextMenuOpen()) { e.Handled = true; return; }
            if ((DateTime.UtcNow - _lastContextMenuOpenedAtUtc) <= TimeSpan.FromMilliseconds(250)) { e.Handled = true; return; }
            var lb = this.FindControl<ListBox>("ContactsList");
            if (lb is null) return;

            // Determine the item under mouse and select it so context menu acts on the correct contact.
            if (e.Source is Avalonia.Visual visual)
            {
                var container = visual.FindAncestorOfType<ListBoxItem>();
                if (container?.DataContext is Contact contact)
                {
                    lb.SelectedItem = contact;
                    SafeLogContextTrace($"ContactsList_ContextRequested: selected {contact.UID}", e.Source);
                }
            }

            // Try to open the item's own ContextMenu if present in the template.
            // Our template places the ContextMenu on the inner Border.contact-card, so search upward for a Border with a ContextMenu.
            try
            {
                if (e.Source is Avalonia.Visual src)
                {
                    var borderWithMenu = (e.Source as Border) ?? src.FindAncestorOfType<Border>();
                    if (borderWithMenu?.ContextMenu != null)
                    {
                        try { if (DataContext is MainWindowViewModel vm) vm.BeginSelectionFreeze(); } catch { }
                        void OnClosed(object? _, EventArgs __)
                        {
                            try { if (DataContext is MainWindowViewModel vm2) vm2.EndSelectionFreeze(); } catch { }
                            try { if (borderWithMenu.ContextMenu != null) borderWithMenu.ContextMenu.Closed -= OnClosed; } catch { }
                        }
                        try { borderWithMenu.ContextMenu.Closed += OnClosed; } catch { }
                        borderWithMenu.ContextMenu.PlacementTarget = borderWithMenu;
                        borderWithMenu.ContextMenu.Open(borderWithMenu);
                        _lastContextMenuOpenedAtUtc = DateTime.UtcNow;
                        e.Handled = true;
                        SafeLogContextTrace("ContactsList_ContextRequested: opened ContextMenu", e.Source);
                        return;
                    }
                }
            }
            catch (Exception inner)
            {
                // Log but do not block context action
                SafeLogContextError(inner, e.Source);
            }
        }
        catch (Exception ex)
        {
            SafeLogContextError(ex, e.Source);
        }
    }

    // Handles context requests directly on the contact card border. Ensures selection and opens the menu.
    private void ContactCard_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        try
        {
            SafeLogContextTrace("ContactCard_ContextRequested: enter", sender);
            if (IsAnyContextMenuOpen()) { e.Handled = true; return; }
            if ((DateTime.UtcNow - _lastContextMenuOpenedAtUtc) <= TimeSpan.FromMilliseconds(250)) { e.Handled = true; return; }
            var lb = this.FindControl<ListBox>("ContactsList");
            if (lb is null) return;
            if (sender is not Border card) return;

            // Find the ListBoxItem and ensure selection
            var item = card.FindAncestorOfType<ListBoxItem>();
            if (item?.DataContext is Contact contact)
            {
                lb.SelectedItem = contact;
                SafeLogContextTrace($"ContactCard_ContextRequested: selected {contact.UID}", sender);
                
                // Open the ListBoxItem's context menu
                var menu = item?.ContextMenu ?? card.ContextMenu;
                if (menu != null)
                {
                    try { if (DataContext is MainWindowViewModel vm) vm.BeginSelectionFreeze(); } catch { }
                    void OnClosed(object? _, EventArgs __)
                    {
                        try { if (DataContext is MainWindowViewModel vm2) vm2.EndSelectionFreeze(); } catch { }
                        try { menu.Closed -= OnClosed; } catch { }
                    }
                    try { menu.Closed += OnClosed; } catch { }
                    menu.PlacementTarget = menu == item?.ContextMenu ? item : card;
                    menu.Open(menu.PlacementTarget);
                    _lastContextMenuOpenedAtUtc = DateTime.UtcNow;
                    e.Handled = true;
                    SafeLogContextTrace("ContactCard_ContextRequested: opened ContextMenu", sender);
                }
            }
        }
        catch (Exception ex)
        {
            SafeLogContextError(ex, sender);
        }
    }

    // Pointer-level hook: on right-button press, select the item under cursor and open its context menu.
    private void ContactsList_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            var props = e.GetCurrentPoint(this).Properties;
            var isRight = props.IsRightButtonPressed;
            var isLeft = props.IsLeftButtonPressed;
            if (!isRight && !isLeft) return;
            if (isRight) SafeLogContextTrace("ContactsList_PointerPressed: right button", e.Source);

            if (IsAnyContextMenuOpen()) { e.Handled = true; return; }
            if ((DateTime.UtcNow - _lastContextMenuOpenedAtUtc) <= TimeSpan.FromMilliseconds(250)) { e.Handled = true; return; }

            var lb = this.FindControl<ListBox>("ContactsList");
            if (lb is null) return;

            // Resolve contact from event source
            Contact? contact = null;
            Border? anchor = null;
            if (e.Source is Avalonia.Visual v)
            {
                (contact, anchor) = ResolveContactFromSource(v);
            }

            SafeLogContextTrace($"ContactsList_PointerPressed: resolve={(contact?.UID ?? "<null>")}", e.Source);

            // No group headers remain

            if (contact != null)
            {
                // Prevent further default processing that might be crashing; perform selection/menu open deferred.
                e.Handled = true;
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var lb2 = this.FindControl<ListBox>("ContactsList") ?? lb;
                        if (lb2 != null)
                        {
                            lb2.SelectedItem = contact;
                            SafeLogContextTrace($"ContactsList_PointerPressed: selected {contact.UID}", e.Source);
                        }
                        if (isRight)
                        {
                            var target = anchor ?? (lb2?.SelectedItem is not null ? (lb2.ContainerFromItem(lb2.SelectedItem) as ListBoxItem)?.FindDescendantOfType<Border>() : null);
                            if (target?.ContextMenu != null)
                            {
                                try { if (DataContext is MainWindowViewModel vm) vm.BeginSelectionFreeze(); } catch { }
                                void OnClosed(object? _, EventArgs __)
                                {
                                    try { if (DataContext is MainWindowViewModel vm2) vm2.EndSelectionFreeze(); } catch { }
                                    try { target.ContextMenu.Closed -= OnClosed; } catch { }
                                }
                                try { target.ContextMenu.Closed += OnClosed; } catch { }
                                target.ContextMenu.PlacementTarget = target;
                                target.ContextMenu.Open(target);
                                _lastContextMenuOpenedAtUtc = DateTime.UtcNow;
                                SafeLogContextTrace("ContactsList_PointerPressed: opened ContextMenu", e.Source);
                            }
                        }
                    }
                    catch (Exception ex2)
                    {
                        SafeLogContextError(ex2, e.Source);
                    }
                });
            }
            else { SafeLogContextTrace("ContactsList_PointerPressed: failed to resolve contact", e.Source); }
            // Post a deferred trace to capture the selection index after the press is processed
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var lb2 = this.FindControl<ListBox>("ContactsList");
                    string suid = "", sname = "";
                    if (lb2?.SelectedItem is Contact sc) { suid = sc.UID; sname = sc.DisplayName; }
                    SafeLogContextTrace($"ContactsList_PointerPressed[Deferred]: SelIndex={lb2?.SelectedIndex} uid={suid} name={sname}", sender);
                }
                catch (Exception ex2) { SafeLogContextError(ex2, sender); }
            });
        }
        catch (Exception ex)
        {
            SafeLogContextError(ex, e.Source);
        }
    }

    // Trace helper to force-create error.txt in AppData and log right-click flow (non-exceptional).
    private static void SafeLogContextTrace(string message, object? source)
    {
        try
        {
            string sourceType = source?.GetType().FullName ?? "<null>";
            int selectedIndex = -1, itemsCount = -1;
            try
            {
                var app = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime);
                var main = app?.MainWindow as MainWindow;
                var list = main?.FindControl<ListBox>("ContactsList");
                if (list != null)
                {
                    selectedIndex = list.SelectedIndex;
                    itemsCount = (list.Items as System.Collections.ICollection)?.Count ?? -1;
                }
            }
            catch { }

            var now = DateTime.Now.ToString("O");
            var line = $"TRACE {message} | Source={sourceType} | SelIndex={selectedIndex} Items={itemsCount} @ {now}";
            try { P2PTalk.Utilities.ErrorLogger.LogException(new InvalidOperationException(line), source: "Trace"); } catch { }
        }
        catch { }
    }

    // Error logger: writes exception details near the executable (error.txt) and mirrors to Logger/ErrorLogger.
    private static void SafeLogContextError(Exception ex, object? source)
    {
        try
        {
            string sourceType = source?.GetType().FullName ?? "<null>";
            int selectedIndex = -1, itemsCount = -1;
            string selUid = string.Empty, selName = string.Empty;
            try
            {
                var app = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime);
                var main = app?.MainWindow as MainWindow;
                var list = main?.FindControl<ListBox>("ContactsList");
                if (list != null)
                {
                    selectedIndex = list.SelectedIndex;
                    itemsCount = (list.Items as System.Collections.ICollection)?.Count ?? -1;
                    if (list.SelectedItem is Contact sc) { selUid = sc.UID; selName = sc.DisplayName; }
                }
            }
            catch { }

            var now = DateTime.Now.ToString("O");
            try { P2PTalk.Utilities.ErrorLogger.LogException(ex, source: $"UI.Context {sourceType} [{now}] Sel={selectedIndex}/{itemsCount} uid={selUid} name={selName}"); } catch { }
            try { Logger.Log($"ERROR: {ex.Message}"); } catch { }
            try { ErrorLogger.LogException(ex, source: sourceType); } catch { }
        }
        catch { /* never throw from logger */ }
    }

    // Try to resolve the Contact and a reasonable anchor Border from an event source visual.
    private static (Contact? contact, Border? anchor) ResolveContactFromSource(Avalonia.Visual v)
    {
        Contact? contact = null;
        Border? anchor = null;
        try
        {
            if (v is Control ctrl && ctrl.DataContext is Contact dc)
            {
                contact = dc;
            }
            // Only contacts are present in the list
            if (contact is null)
            {
                var item = v.FindAncestorOfType<ListBoxItem>();
                if (item?.DataContext is Contact c2) contact = c2;
                anchor = item?.FindDescendantOfType<Border>();
            }
            if (anchor is null)
            {
                anchor = (v as Border) ?? v.FindAncestorOfType<Border>();
            }
        }
        catch { }
        return (contact, anchor);
    }

    // Wire tunneling/bubbling handlers for robust diagnostics even if templates change
    private void HookContactsDiagnostics()
    {
        try
        {
            var lb = this.FindControl<ListBox>("ContactsList");
            if (lb is null) return;
            // Prevent double-wiring if Opened fires multiple times
            lb.AddHandler(InputElement.PointerPressedEvent, ContactsList_PointerPressedTrace, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
            lb.AddHandler(ContextRequestedEvent, ContactsList_ContextRequestedTrace, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
            // Ensure main selection/open handler runs once at Bubble stage (even if inner elements handled it)
            lb.AddHandler(InputElement.PointerPressedEvent, ContactsList_PointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
            lb.AddHandler(ContextRequestedEvent, ContactsList_ContextRequested, RoutingStrategies.Bubble, handledEventsToo: true);
            // Extra: stage-specific traces to understand selection timing
            lb.AddHandler(InputElement.PointerPressedEvent, ContactsList_PointerPressedTunnel, RoutingStrategies.Tunnel, handledEventsToo: true);
            lb.AddHandler(InputElement.PointerPressedEvent, ContactsList_PointerPressedBubble, RoutingStrategies.Bubble, handledEventsToo: true);
            lb.AddHandler(InputElement.PointerReleasedEvent, ContactsList_PointerReleasedBubble, RoutingStrategies.Bubble, handledEventsToo: true);
            lb.SelectionChanged += ContactsList_SelectionChangedTrace;
            SafeLogContextTrace("HookContactsDiagnostics: attached handlers", lb);
            try { WriteSettingsLog("[Restore] MainWindow: reattached full Contacts diagnostics and handlers"); } catch { }
        }
        catch (Exception ex)
        {
            SafeLogContextError(ex, this);
        }
    }

    // Stage-specific pointer diagnostics
    private void ContactsList_PointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            var lb = this.FindControl<ListBox>("ContactsList");
            var pos = lb is null ? default : e.GetPosition(lb);
            var props = e.GetCurrentPoint(this).Properties;
            var btn = props.IsRightButtonPressed ? "Right" : props.IsLeftButtonPressed ? "Left" : props.IsMiddleButtonPressed ? "Middle" : "Other";
            string uid = "", name = "";
            if (e.Source is Avalonia.Visual v)
            {
                var item = v.FindAncestorOfType<ListBoxItem>();
                if (item?.DataContext is Contact c) { uid = c.UID; name = c.DisplayName; }
            }
            SafeLogContextTrace($"ContactsList_PointerPressed[Tunnel]: {btn} at ({pos.X:0.0},{pos.Y:0.0}) uid={uid} name={name}", e.Source);
        }
        catch (Exception ex) { SafeLogContextError(ex, e.Source); }
    }

    private void ContactsList_PointerPressedBubble(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            var lb = this.FindControl<ListBox>("ContactsList");
            var pos = lb is null ? default : e.GetPosition(lb);
            var props = e.GetCurrentPoint(this).Properties;
            var btn = props.IsRightButtonPressed ? "Right" : props.IsLeftButtonPressed ? "Left" : props.IsMiddleButtonPressed ? "Middle" : "Other";
            string uid = "", name = "";
            if (e.Source is Avalonia.Visual v)
            {
                var item = v.FindAncestorOfType<ListBoxItem>();
                if (item?.DataContext is Contact c) { uid = c.UID; name = c.DisplayName; }
            }
            SafeLogContextTrace($"ContactsList_PointerPressed[Bubble]: {btn} at ({pos.X:0.0},{pos.Y:0.0}) uid={uid} name={name}", e.Source);
        }
        catch (Exception ex) { SafeLogContextError(ex, e.Source); }
    }

    private void ContactsList_PointerReleasedBubble(object? sender, PointerReleasedEventArgs e)
    {
        try
        {
            var lb = this.FindControl<ListBox>("ContactsList");
            var pos = lb is null ? default : e.GetPosition(lb);
            string uid = "", name = "";
            if (e.Source is Avalonia.Visual v)
            {
                var item = v.FindAncestorOfType<ListBoxItem>();
                if (item?.DataContext is Contact c) { uid = c.UID; name = c.DisplayName; }
            }
            SafeLogContextTrace($"ContactsList_PointerReleased[Bubble]: at ({pos.X:0.0},{pos.Y:0.0}) uid={uid} name={name}", e.Source);
            // Also post a deferred trace to capture selection after the event completes
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var lb2 = this.FindControl<ListBox>("ContactsList");
                    string suid = "", sname = "";
                    if (lb2?.SelectedItem is Contact sc) { suid = sc.UID; sname = sc.DisplayName; }
                    SafeLogContextTrace($"ContactsList_PointerReleased[Deferred]: SelIndex={lb2?.SelectedIndex} uid={suid} name={sname}", sender);
                }
                catch (Exception ex2) { SafeLogContextError(ex2, sender); }
            });
        }
        catch (Exception ex) { SafeLogContextError(ex, e.Source); }
    }

    // Trace any pointer press on contacts list (left/right/middle) with coordinates and contact under cursor
    private void ContactsList_PointerPressedTrace(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            var lb = this.FindControl<ListBox>("ContactsList");
            var pos = lb is null ? default : e.GetPosition(lb);
            var props = e.GetCurrentPoint(this).Properties;
            var btn = props.IsRightButtonPressed ? "Right" : props.IsLeftButtonPressed ? "Left" : props.IsMiddleButtonPressed ? "Middle" : "Other";
            string uid = "", name = "";
            if (e.Source is Avalonia.Visual v)
            {
                var item = v.FindAncestorOfType<ListBoxItem>();
                if (item?.DataContext is Contact c)
                { uid = c.UID; name = c.DisplayName; }
            }
            SafeLogContextTrace($"ContactsList_PointerPressedTrace: {btn} at ({pos.X:0.0},{pos.Y:0.0}) uid={uid} name={name}", e.Source);
        }
        catch (Exception ex)
        {
            SafeLogContextError(ex, e.Source);
        }
    }

    // Trace context menu requests regardless of Border hookup
    private void ContactsList_ContextRequestedTrace(object? sender, ContextRequestedEventArgs e)
    {
        try { SafeLogContextTrace("ContactsList_ContextRequestedTrace: enter", e.Source); }
        catch (Exception ex) { SafeLogContextError(ex, e.Source); }
    }

    // Trace selection changes with new UID/index
    private void ContactsList_SelectionChangedTrace(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            var lb = this.FindControl<ListBox>("ContactsList");
            string uid = "", name = "";
            if (lb?.SelectedItem is Contact c) { uid = c.UID; name = c.DisplayName; }
            SafeLogContextTrace($"ContactsList_SelectionChangedTrace: index={lb?.SelectedIndex} uid={uid} name={name}", sender);
        }
        catch (Exception ex)
        {
            SafeLogContextError(ex, sender);
        }
    }

    // Open full profile on double-tap for simulated contacts only
    private void ContactCard_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        try
        {
            var lb = this.FindControl<ListBox>("ContactsList");
            if (lb?.SelectedItem is Contact c && c.IsSimulated)
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.ShowFullProfileCommand?.Execute(c.UID);
                    SafeLogContextTrace("ContactCard_DoubleTapped: opened profile for simulated", sender);
                }
            }
        }
        catch (Exception ex)
        {
            SafeLogContextError(ex, sender);
        }
    }

    private void Lock_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try { new P2PTalk.Services.LockService().Lock(); } catch { }
    }

    private void BoldButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplyInlineFormatting("**", "**", "bold text", sender);
        e.Handled = true;
    }

    private void ItalicButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplyInlineFormatting("*", "*", "italic text", sender);
        e.Handled = true;
    }

    private void UnderlineButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplyInlineFormatting("__", "__", "underline", sender);
        e.Handled = true;
    }

    private void StrikeButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplyInlineFormatting("~~", "~~", "strike", sender);
        e.Handled = true;
    }

    private void SpoilerButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplySpoilerFormatting(sender);
        e.Handled = true;
    }

    private void QuoteButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplyQuoteFormatting("quote text", sender);
        e.Handled = true;
    }

    private void CodeButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplyCodeFormatting(sender);
        e.Handled = true;
    }

    private TextBox? GetMessageInput()
    {
        try { return this.FindControl<TextBox>("MessageInput"); } catch { return null; }
    }

    private void InitializeMessageInputSizing()
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var input = GetMessageInput();
                    if (input != null)
                    {
                        AdjustMessageInputHeight(input);
                    }
                }
                catch { }
            }, DispatcherPriority.Background);
        }
        catch { }
    }

    private System.Threading.CancellationTokenSource? _inputResizeCts;
    private DateTime _lastInputResize = DateTime.MinValue;

    private void MessageInput_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox input)
        {
            return;
        }

        // Skip resize during active text selection to prevent freezing
        if (input.SelectionStart != input.SelectionEnd)
        {
            return;
        }

        try
        {
            // Throttle resize operations to prevent spam during typing
            var now = DateTime.UtcNow;
            if ((now - _lastInputResize).TotalMilliseconds < 50)
            {
                _inputResizeCts?.Cancel();
                _inputResizeCts = new System.Threading.CancellationTokenSource();
                var token = _inputResizeCts.Token;
                
                _ = System.Threading.Tasks.Task.Delay(50, token).ContinueWith(t =>
                {
                    if (!t.IsCanceled)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                if (!token.IsCancellationRequested)
                                {
                                    AdjustMessageInputHeight(input);
                                    _lastInputResize = DateTime.UtcNow;
                                }
                            }
                            catch { }
                        }, DispatcherPriority.Background);
                    }
                }, token);
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        AdjustMessageInputHeight(input);
                        _lastInputResize = DateTime.UtcNow;
                    }
                    catch { }
                }, DispatcherPriority.Background);
            }
        }
        catch { }
    }

    private void AdjustMessageInputHeight(TextBox input)
    {
        try
        {
            var maintainFollow = false;
            ScrollViewer? chatScroll = null;
            try
            {
                chatScroll = this.FindControl<ScrollViewer>("ChatScroll");
                if (chatScroll != null)
                {
                    maintainFollow = _stickToBottom || _followChat || IsAtBottom(chatScroll);
                }
                else
                {
                    maintainFollow = _stickToBottom || _followChat;
                }
            }
            catch
            {
                maintainFollow = _stickToBottom || _followChat;
            }

            var min = input.MinHeight > 0 ? input.MinHeight : ChatInputDefaultMinHeight;
            var max = input.MaxHeight > 0 && double.IsFinite(input.MaxHeight) ? input.MaxHeight : ChatInputDefaultMaxHeight;

            var previous = input.Height;
            // Only remeasure if we actually need to
            var width = input.Bounds.Width;
            if (width <= 0 && input.Parent is Control parent)
            {
                width = parent.Bounds.Width;
            }
            if (width <= 0)
            {
                width = 400; // fallback reasonable width
            }

            // Estimate height based on line count to avoid expensive measure
            var text = input.Text ?? string.Empty;
            var lineCount = Math.Max(1, text.Split('\n').Length);
            var estimatedHeight = lineCount * 20 + 16; // rough estimate
            var desired = Math.Max(estimatedHeight, min);

            var clamped = Math.Clamp(desired, min, max);
            if (!double.IsFinite(clamped))
            {
                clamped = max;
            }

            var shouldAdjust = double.IsNaN(previous) || Math.Abs(previous - clamped) > 0.1;
            if (shouldAdjust && maintainFollow)
            {
                _autoFollowGuardUntilUtc = DateTime.UtcNow.Add(AutoFollowGuardWindow);
            }

            if (shouldAdjust)
            {
                input.Height = clamped;
                if (maintainFollow)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            TryScrollChatToBottom(force: true);
                        }
                        catch { }
                    }, DispatcherPriority.Background);
                }
            }
            else
            {
                input.Height = previous;
            }
        }
        catch { }
    }

    private void ApplyInlineFormatting(string prefix, string suffix, string placeholder, object? source)
    {
        try
        {
            var input = GetMessageInput();
            if (input is null) return;

            var text = input.Text ?? string.Empty;
            var length = text.Length;
            var rawStart = input.SelectionStart;
            var rawEnd = input.SelectionEnd;
            var start = Math.Min(rawStart, rawEnd);
            start = Math.Max(0, Math.Min(start, length));
            var end = Math.Max(rawStart, rawEnd);
            end = Math.Max(0, Math.Min(end, length));

            var hasSelection = end > start;
            string updatedText;
            int newSelectionStart;
            int newSelectionEnd;

            if (!hasSelection)
            {
                var insertion = string.Concat(prefix, placeholder, suffix);
                updatedText = text.Insert(start, insertion);
                newSelectionStart = start + prefix.Length;
                newSelectionEnd = newSelectionStart + placeholder.Length;
            }
            else
            {
                var selectedText = text.Substring(start, end - start);
                var replacement = string.Concat(prefix, selectedText, suffix);
                updatedText = text.Remove(start, end - start).Insert(start, replacement);
                newSelectionStart = start + prefix.Length;
                newSelectionEnd = newSelectionStart + selectedText.Length;
            }

            input.Text = updatedText;
            input.SelectionStart = newSelectionStart;
            input.SelectionEnd = newSelectionEnd;
            input.CaretIndex = newSelectionEnd;
            input.Focus();
        }
        catch (Exception ex)
        {
            SafeLogContextError(ex, source);
        }
    }

    private void ApplySpoilerFormatting(object? source)
    {
        try
        {
            var input = GetMessageInput();
            if (input is null) return;

            var text = input.Text ?? string.Empty;
            var length = text.Length;
            var rawStart = input.SelectionStart;
            var rawEnd = input.SelectionEnd;
            var start = Math.Min(rawStart, rawEnd);
            start = Math.Max(0, Math.Min(start, length));
            var end = Math.Max(rawStart, rawEnd);
            end = Math.Max(0, Math.Min(end, length));
            var selectionLength = end - start;

            if (selectionLength <= 0)
            {
                const string placeholder = "spoiler";
                var insertion = $"||{placeholder}||";
                var updated = text.Insert(start, insertion);
                input.Text = updated;
                var selectStart = start + 2;
                var selectEnd = selectStart + placeholder.Length;
                input.SelectionStart = selectStart;
                input.SelectionEnd = selectEnd;
                input.CaretIndex = selectEnd;
                input.Focus();
                return;
            }

            var selected = text.Substring(start, selectionLength);
            var normalized = selected.Replace("\r\n", "\n").Replace('\r', '\n');
            var endsWithLineBreak = normalized.EndsWith("\n", StringComparison.Ordinal);
            var lines = normalized.Split('\n');
            var formattedLines = lines.Select(line =>
            {
                if (line.Length == 0) return "|| ||";
                return $"||{line}||";
            });
            var replacementNormalized = string.Join(Environment.NewLine, formattedLines);
            if (endsWithLineBreak)
            {
                replacementNormalized += Environment.NewLine;
            }

            var updatedText = text.Remove(start, selectionLength).Insert(start, replacementNormalized);
            input.Text = updatedText;
            var caret = start + replacementNormalized.Length;
            input.SelectionStart = caret;
            input.SelectionEnd = caret;
            input.CaretIndex = caret;
            input.Focus();
        }
        catch (Exception ex)
        {
            SafeLogContextError(ex, source);
        }
    }

    private void ApplyQuoteFormatting(string placeholder, object? source)
    {
        try
        {
            var input = GetMessageInput();
            if (input is null) return;

            var text = input.Text ?? string.Empty;
            var length = text.Length;
            var rawStart = input.SelectionStart;
            var rawEnd = input.SelectionEnd;
            var start = Math.Min(rawStart, rawEnd);
            start = Math.Max(0, Math.Min(start, length));
            var end = Math.Max(rawStart, rawEnd);
            end = Math.Max(0, Math.Min(end, length));
            var hasSelection = end > start;

            string updatedText;
            int newSelectionStart;
            int newSelectionEnd;

            if (!hasSelection)
            {
                var insertion = $"> {placeholder}{Environment.NewLine}";
                updatedText = text.Insert(start, insertion);
                newSelectionStart = start + 2;
                newSelectionEnd = newSelectionStart + placeholder.Length;
            }
            else
            {
                var selectedText = text.Substring(start, end - start);
                var normalized = selectedText.Replace("\r\n", "\n").Replace('\r', '\n');
                var lines = normalized.Split('\n');
                var quotedLines = lines.Select(line => $"> {line}");
                var replacement = string.Join(Environment.NewLine, quotedLines);
                if (normalized.EndsWith("\n", StringComparison.Ordinal))
                {
                    replacement += Environment.NewLine;
                }
                updatedText = text.Remove(start, end - start).Insert(start, replacement);
                newSelectionStart = start;
                newSelectionEnd = start + replacement.Length;
            }

            input.Text = updatedText;
            input.SelectionStart = newSelectionStart;
            input.SelectionEnd = newSelectionEnd;
            input.CaretIndex = newSelectionEnd;
            input.Focus();
        }
        catch (Exception ex)
        {
            SafeLogContextError(ex, source);
        }
    }

    private void ApplyCodeFormatting(object? source)
    {
        try
        {
            var input = GetMessageInput();
            if (input is null) return;

            var text = input.Text ?? string.Empty;
            var length = text.Length;
            var rawStart = input.SelectionStart;
            var rawEnd = input.SelectionEnd;
            var start = Math.Min(rawStart, rawEnd);
            start = Math.Max(0, Math.Min(start, length));
            var end = Math.Max(rawStart, rawEnd);
            end = Math.Max(0, Math.Min(end, length));
            var selectionLength = end - start;

            if (selectionLength <= 0)
            {
                var placeholder = "code";
                var block = $"```{Environment.NewLine}{placeholder}{Environment.NewLine}```";
                var needsLeadingBlock = RequiresLeadingNewline(text, start);
                var needsTrailingBlock = RequiresTrailingNewline(text, end);
                var insertion = string.Concat(needsLeadingBlock ? Environment.NewLine : string.Empty,
                                              block,
                                              needsTrailingBlock ? Environment.NewLine : string.Empty);
                var updatedText = text.Insert(start, insertion);
                input.Text = updatedText;
                var selectStart = start + (needsLeadingBlock ? Environment.NewLine.Length : 0) + 3 + Environment.NewLine.Length;
                var selectEnd = selectStart + placeholder.Length;
                input.SelectionStart = selectStart;
                input.SelectionEnd = selectEnd;
                input.CaretIndex = selectEnd;
                input.Focus();
                return;
            }

            var selectedText = text.Substring(start, selectionLength);
            if (!selectedText.Contains('\n') && !selectedText.Contains('\r'))
            {
                ApplyInlineFormatting("`", "`", "code", source);
                return;
            }

            var normalized = selectedText.Replace("\r\n", "\n").Replace('\r', '\n');
            var endsWithBreak = normalized.EndsWith("\n", StringComparison.Ordinal);
            var splitLines = normalized.Split('\n');
            if (endsWithBreak && splitLines.Length > 0 && splitLines[^1].Length == 0)
            {
                splitLines = splitLines[..^1];
            }
            if (splitLines.Length == 0)
            {
                splitLines = new[] { "code" };
            }
            var content = string.Join(Environment.NewLine, splitLines);

            var blockPrefix = $"```{Environment.NewLine}";
            var blockSuffix = $"{Environment.NewLine}```";
            var needsLeading = RequiresLeadingNewline(text, start);
            var needsTrailing = RequiresTrailingNewline(text, end);

            var replacementCore = string.Concat(blockPrefix, content, blockSuffix);
            var replacement = string.Concat(needsLeading ? Environment.NewLine : string.Empty,
                                            replacementCore,
                                            needsTrailing || endsWithBreak ? Environment.NewLine : string.Empty);

            var updated = text.Remove(start, selectionLength).Insert(start, replacement);
            input.Text = updated;
            var selectionStart = start + (needsLeading ? Environment.NewLine.Length : 0) + blockPrefix.Length;
            var selectionEnd = selectionStart + content.Length;
            input.SelectionStart = selectionStart;
            input.SelectionEnd = selectionEnd;
            input.CaretIndex = selectionEnd;
            input.Focus();
        }
        catch (Exception ex)
        {
            SafeLogContextError(ex, source);
        }
    }

    private static bool RequiresLeadingNewline(string text, int index)
    {
        if (index <= 0 || text.Length == 0) return false;
        var prev = text[index - 1];
        if (prev == '\n') return false;
        if (prev == '\r') return false;
        return true;
    }

    private static bool RequiresTrailingNewline(string text, int index)
    {
        if (index >= text.Length) return false;
        var next = text[index];
        if (next == '\n') return false;
        if (next == '\r') return false;
        return true;
    }

    private void OnMessageInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        // Shift+Enter should insert a new line
        if ((e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift) return;

        // Always prevent newline for plain Enter
        e.Handled = true;
        if (DataContext is MainWindowViewModel vm && (vm.SendCommand?.CanExecute(null) ?? false))
        {
            vm.SendCommand.Execute(null);
        }
    }

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        try
        {
            if (e.Key == Key.L &&
                (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control &&
                (e.KeyModifiers & KeyModifiers.Alt) == KeyModifiers.Alt &&
                (e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift)
            {
                try { new P2PTalk.Services.LockService().Lock(); } catch { }
                e.Handled = true;
            }
#if DEBUG
            if (e.Key == Key.N &&
                (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control &&
                (e.KeyModifiers & KeyModifiers.Alt) == KeyModifiers.Alt &&
                (e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift)
            {
                try { AddIncomingTestMessage($"Test msg {DateTime.Now:HH:mm:ss.fff}"); } catch { }
                e.Handled = true;
            }
            if (e.Key == Key.U &&
                (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control &&
                (e.KeyModifiers & KeyModifiers.Alt) == KeyModifiers.Alt &&
                (e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift)
            {
                try
                {
                    var sv = this.FindControl<ScrollViewer>("ChatScroll");
                    if (sv != null)
                    {
                        var target = Math.Max(0, sv.Offset.Y - Math.Max(120, sv.Viewport.Height / 2));
                        SafeUiLog($"[Test] ScrollUp: fromY={sv.Offset.Y:F1} -> {target:F1}");
                        sv.Offset = new Vector(sv.Offset.X, target);
                    }
                }
                catch { }
                e.Handled = true;
            }
            if (e.Key == Key.J &&
                (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control &&
                (e.KeyModifiers & KeyModifiers.Alt) == KeyModifiers.Alt &&
                (e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift)
            {
                try { JumpToBottom_Click(null, new RoutedEventArgs()); } catch { }
                e.Handled = true;
            }
#endif
        }
        catch { }
    }

    // Prevent clicks inside the profile content from bubbling to the backdrop
    private void FullProfileContent_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try { e.Handled = true; } catch { }
    }

    // Backdrop click closes the Full Profile overlay
    private void FullProfileBackdrop_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.CloseFullProfileCommand?.Execute(null);
            }
            e.Handled = true;
        }
        catch { }
    }

    private void OnRegressionDetected(string msg)
    {
        try
        {
            _rgToastCts?.Cancel();
            _rgToastCts = new System.Threading.CancellationTokenSource();
            var token = _rgToastCts.Token;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var border = this.FindControl<Border>("RegressionToast");
                    var text = this.FindControl<TextBlock>("RegressionToastText");
                    if (border != null && text != null)
                    {
                        text.Text = msg;
                        border.IsVisible = true;
                        try { SafeUiLog($"[Toast][Regression] Show: '{msg}'"); } catch { }
                    }
                }
                catch (Exception ex) { SafeLogContextError(ex, this); }
            });
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(3000, token);
                    if (!token.IsCancellationRequested)
                        Dispatcher.UIThread.Post(() => { try { var b = this.FindControl<Border>("RegressionToast"); if (b != null) { b.IsVisible = false; try { SafeUiLog("[Toast][Regression] Auto-hide"); } catch { } } } catch { } });
                }
                catch { }
            }, token);
        }
        catch { }
    }

    private void OnWindowKeyDownForOverlays(object? sender, KeyEventArgs e)
    {
        try
        {
            if (e.Key == Key.Escape)
            {
                if (DataContext is MainWindowViewModel vm && vm.IsFullProfileOpen)
                {
                    vm.CloseFullProfileCommand?.Execute(null);
                    e.Handled = true;
                    return;
                }
                HideSettingsOverlayIfConfirmed();
                e.Handled = true;
            }
        }
        catch { }
    }

    private void CollectAndRender()
    {
        try
        {
            // Minimal snapshot refresh for diagnostics panel and status labels.
            var txt = this.FindControl<TextBlock>("DiagNatStatus");
            if (txt != null)
            {
                try { txt.Text = AppServices.Network?.ToString() ?? string.Empty; } catch { }
            }
        }
        catch { }
    }

    // Apply saved widths to side panels (0 width = hidden)
    private void ApplyInitialPanelVisibility()
    {
        try
        {
            var grid = this.FindControl<Grid>("BodyGrid");
            if (grid?.ColumnDefinitions is { Count: >= 4 })
            {
                var leftRoot = this.FindControl<Grid>("LeftPanelRoot");
                var rightRoot = this.FindControl<Border>("RightPanelRoot");
                var s2 = AppServices.Settings.Settings;
                        var left = s2.MainLeftWidth ?? 280.0; // contacts default
                        // Default diagnostics panel closed unless user previously opened (treat null or <=0 as collapsed)
                        var rightStored = s2.MainRightWidth;
                        var right = (rightStored is null || rightStored <= 0) ? 0.0 : rightStored.Value;
                var leftWidth = left > 0 ? left : 0.0;
                var contactsCol = grid.ColumnDefinitions.Count >= 2 ? grid.ColumnDefinitions[1] : null;
                var dividerL = grid.ColumnDefinitions.Count >= 3 ? grid.ColumnDefinitions[2] : null;
                var chatCol = grid.ColumnDefinitions.Count >= 4 ? grid.ColumnDefinitions[3] : null;
                var dividerR = grid.ColumnDefinitions.Count >= 5 ? grid.ColumnDefinitions[4] : null;
                var diagCol = grid.ColumnDefinitions.Count >= 6 ? grid.ColumnDefinitions[5] : null;

                if (contactsCol != null)
                {
                    if (_leftColumnOriginalMinWidthDefinition is null)
                        _leftColumnOriginalMinWidthDefinition = contactsCol.MinWidth;
                    contactsCol.Width = new GridLength(leftWidth, GridUnitType.Pixel);
                    contactsCol.MinWidth = (leftWidth <= 0.1) ? 0.0 : (_leftColumnOriginalMinWidthDefinition ?? contactsCol.MinWidth);
                }
                if (dividerL != null)
                {
                    dividerL.Width = (leftWidth <= 0.1) ? new GridLength(0) : new GridLength(1);
                }
                if (diagCol != null)
                {
                    if (_rightColumnOriginalMinWidthDefinition is null)
                        _rightColumnOriginalMinWidthDefinition = diagCol.MinWidth;
                    diagCol.Width = new GridLength(right <= 0 ? 0 : right, GridUnitType.Pixel);
                    diagCol.MinWidth = (right <= 0.1) ? 0.0 : (_rightColumnOriginalMinWidthDefinition ?? diagCol.MinWidth);
                }
                if (dividerR != null)
                {
                    dividerR.Width = (right <= 0.1) ? new GridLength(0) : new GridLength(1);
                }
                if (chatCol != null)
                {
                    // Ensure chat expands to available space when any side is collapsed
                    if (leftWidth <= 0.1 || right <= 0.1)
                        chatCol.Width = new GridLength(1, GridUnitType.Star);
                }
                if (leftRoot != null)
                {
                    if (_leftPanelOriginalMinWidth is null) _leftPanelOriginalMinWidth = leftRoot.MinWidth;
                    leftRoot.IsVisible = leftWidth > 0; // hide when collapsed
                    leftRoot.MinWidth = (leftWidth <= 0.1) ? 0.0 : (_leftPanelOriginalMinWidth ?? leftRoot.MinWidth);
                }
                if (rightRoot != null)
                {
                    rightRoot.IsVisible = (right > 0);
                    rightRoot.MinWidth = (right > 0) ? 240 : 0;
                }
                grid.InvalidateMeasure();
                grid.InvalidateArrange();
                _rightPanelLastWidth = right > 0 ? right : null;
                _leftPanelLastWidth = (leftWidth > 0.1) ? leftWidth : null;
                try { WriteSettingsLog($"[Restore] MainWindow: panels applied left={leftWidth:0} right={right:0}"); } catch { }
            }
        }
        catch { }
    }

    private void ToggleMaximizeRestore()
    {
        try { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; } catch { }
    }
    private void Minimize_Click(object? sender, RoutedEventArgs e)
    {
        try { WindowState = WindowState.Minimized; } catch { }
    }

    private void Maximize_Click(object? sender, RoutedEventArgs e)
    {
        try { ToggleMaximizeRestore(); } catch { }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        try { Close(); } catch { }
    }

    private void ToggleRightPanel_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var s2 = AppServices.Settings.Settings;
            var grid = this.FindControl<Grid>("BodyGrid");
            var rightRoot = this.FindControl<Border>("RightPanelRoot");
            var dividerChatDiag = grid?.ColumnDefinitions.Count >= 5 ? grid.ColumnDefinitions[4] : null;
            if (grid?.ColumnDefinitions is { Count: >= 6 })
            {
                var diagCol = grid.ColumnDefinitions[5];
                if (_rightColumnOriginalMinWidthDefinition is null)
                    _rightColumnOriginalMinWidthDefinition = diagCol.MinWidth;
                double current = diagCol.Width.Value;
                bool willCollapse = current > 0.1;
                double target = willCollapse ? 0.0 : _rightPanelLastWidth ?? (s2.MainRightWidth is double w && w > 0 ? w : 300.0);
                if (willCollapse) _rightPanelLastWidth = current;

                // Fast path: if this action would collapse the right AND left is already collapsed, collapse both instantly.
                if (willCollapse && grid.ColumnDefinitions[1].Width.Value <= 0.1)
                {
                    CollapseBothInstant(grid);
                    if (rightRoot != null)
                    {
                        rightRoot.IsVisible = false;
                        rightRoot.MinWidth = 0;
                    }
                    // Preserve previous width (don't overwrite with zero)
                    _ = System.Threading.Tasks.Task.Run(() => AppServices.Settings.Save(AppServices.Passphrase));
                    return;
                }

                var anim = AnimateColumnWidth(diagCol, target);
                if (dividerChatDiag != null)
                    dividerChatDiag.Width = (target <= 0.1) ? new GridLength(0) : new GridLength(1);
                if (target <= 0.1)
                {
                    diagCol.MinWidth = 0;
                    grid.ColumnDefinitions[3].Width = new GridLength(1, GridUnitType.Star);
                }
                else
                {
                    diagCol.MinWidth = _rightColumnOriginalMinWidthDefinition ?? diagCol.MinWidth;
                }
                if (rightRoot != null)
                {
                    rightRoot.IsVisible = target > 0;
                    rightRoot.MinWidth = target > 0 ? 240 : 0;
                }
                s2.MainRightWidth = target; // right side persisted directly like original logic
                grid.InvalidateMeasure();
                grid.InvalidateArrange();
                _ = anim.ContinueWith(_ => Dispatcher.UIThread.Post(NormalizeCentralLayout));
                try { WriteSettingsLog($"[Restore] MainWindow: ToggleRightPanel -> {target:0}"); } catch { }
            }
            _ = System.Threading.Tasks.Task.Run(() => AppServices.Settings.Save(AppServices.Passphrase));
        }
        catch { }
    }

    private async System.Threading.Tasks.Task AnimateColumnWidth(ColumnDefinition column, double target, int steps = 8, int totalMs = 120)
    {
        try
        {
            double start = column.Width.Value;
            double end = target;
            if (Math.Abs(end - start) < 0.5)
            {
                column.Width = new GridLength(end, GridUnitType.Pixel);
                return;
            }
            int delay = steps > 0 ? totalMs / steps : 1;
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                double eased = 1 - (1 - t) * (1 - t); // ease-out
                double w = start + (end - start) * eased;
                column.Width = new GridLength(w, GridUnitType.Pixel);
                await System.Threading.Tasks.Task.Delay(delay);
            }
            column.Width = new GridLength(end, GridUnitType.Pixel);
        }
        catch { }
    }

    // Collapse both side panels instantly and maximize the chat column (no animation)
    private void CollapseBothInstant(Grid grid)
    {
        try
        {
            if (grid.ColumnDefinitions.Count < 6) return;
            var contactsCol = grid.ColumnDefinitions[1];
            var dividerL = grid.ColumnDefinitions[2];
            var chatCol = grid.ColumnDefinitions[3];
            var dividerR = grid.ColumnDefinitions[4];
            var diagCol = grid.ColumnDefinitions[5];
            contactsCol.MinWidth = 0; diagCol.MinWidth = 0;
            contactsCol.Width = new GridLength(0);
            diagCol.Width = new GridLength(0);
            dividerL.Width = new GridLength(0);
            dividerR.Width = new GridLength(0);
            chatCol.Width = new GridLength(1, GridUnitType.Star);
            grid.InvalidateMeasure();
            grid.InvalidateArrange();
            Dispatcher.UIThread.Post(NormalizeCentralLayout);
        }
        catch { }
    }

    // Write to a simple settings log beside the executable for traceability
    private static void WriteSettingsLog(string line)
    {
        try
        {
            if (!P2PTalk.Utilities.LoggingPaths.Enabled) return;
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "logs", "settings.log");
            var text = $"{DateTime.Now:O} {line}{Environment.NewLine}";
            P2PTalk.Utilities.LoggingPaths.TryWrite(path, text);
        }
        catch { }
    }

    // Write to a simple theme log beside the executable for traceability
    private static void WriteThemeLog(string line)
    {
        try
        {
            if (!P2PTalk.Utilities.LoggingPaths.Enabled) return;
            var text = $"{DateTime.Now:O} {line}{Environment.NewLine}";
            P2PTalk.Utilities.LoggingPaths.TryWrite(P2PTalk.Utilities.LoggingPaths.Theme, text);
        }
        catch { }
    }

    // Write to a simple layout log beside the executable for traceability
    private static void WriteLayoutLog(string line)
    {
        try
        {
            if (!P2PTalk.Utilities.LoggingPaths.Enabled) return;
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "logs", "layout.log");
            var text = $"{DateTime.Now:O} {line}{Environment.NewLine}";
            P2PTalk.Utilities.LoggingPaths.TryWrite(path, text);
        }
        catch { }
    }

    // Ensure the central chat column fully stretches when both side panels are collapsed.
    private void NormalizeCentralLayout()
    {
        try
        {
            var grid = this.FindControl<Grid>("BodyGrid");
            if (grid?.ColumnDefinitions is not { Count: >= 6 }) return;
            var contacts = grid.ColumnDefinitions[1];
            var dividerL = grid.ColumnDefinitions[2];
            var chat = grid.ColumnDefinitions[3];
            var dividerR = grid.ColumnDefinitions[4];
            var diag = grid.ColumnDefinitions[5];
            bool leftHidden = contacts.Width.Value <= 0.1;
            bool rightHidden = diag.Width.Value <= 0.1;
            // Ensure min widths not forcing layout when collapsed
            if (leftHidden) contacts.MinWidth = 0; else if (_leftColumnOriginalMinWidthDefinition is not null) contacts.MinWidth = _leftColumnOriginalMinWidthDefinition.Value;
            if (rightHidden) diag.MinWidth = 0; else if (_rightColumnOriginalMinWidthDefinition is not null) diag.MinWidth = _rightColumnOriginalMinWidthDefinition.Value;
            if (leftHidden) dividerL.Width = new GridLength(0, GridUnitType.Pixel);
            if (rightHidden) dividerR.Width = new GridLength(0, GridUnitType.Pixel);
            if (leftHidden && rightHidden)
            {
                // Force chat to star and clear any pixel residue.
                if (chat.Width.IsAbsolute || chat.Width.IsAuto)
                    chat.Width = new GridLength(1, GridUnitType.Star);
                // Remove any inadvertent max width constraints on the chat root grid
                var chatRoot = chat is not null ? this.GetVisualDescendants().OfType<Grid>().FirstOrDefault(g => g.Name == null && Grid.GetColumn(g) == 3) : null;
                if (chatRoot != null && double.IsNaN(chatRoot.MaxWidth) == false && chatRoot.MaxWidth < 2000)
                {
                    chatRoot.MaxWidth = double.PositiveInfinity;
                }
            }
            grid.InvalidateMeasure();
            grid.InvalidateArrange();
        }
        catch { }
    }


    // [UI Guard] Capture a simple checkpoint of the Nav rail layout for traceability
    private static void CheckpointNavRail()
    {
        try { WriteLayoutLog("[Checkpoint] Nav rail: expect Width=64 Height=56 Padding=8,0 Spacing=6..10 Centered Top-stacked Icons~26px"); } catch { }
    }

    // [UI Guard] Validate and correct the left nav rail layout; log any corrections
    private void VerifyAndGuardNavRail()
    {
        try
        {
            var nav = this.FindControl<Border>("NavRail");
            if (nav == null) return;
            // Find the first StackPanel inside the nav rail that holds the buttons
            var stack = nav.GetVisualDescendants().OfType<StackPanel>().FirstOrDefault();
            if (stack == null) return;

            bool corrected = false;

            // Stack alignment and spacing
            if (stack.HorizontalAlignment != HorizontalAlignment.Center) { stack.HorizontalAlignment = HorizontalAlignment.Center; corrected = true; }
            if (stack.VerticalAlignment != VerticalAlignment.Top) { stack.VerticalAlignment = VerticalAlignment.Top; corrected = true; }
            // Spacing target is 6 (allowed 6..10)
            if (stack.Spacing < 6 || stack.Spacing > 10) { stack.Spacing = 6; corrected = true; }

            // Buttons sizing/alignment/padding and icon size
            foreach (var btn in stack.Children.OfType<Button>())
            {
                if (btn.Width != 64) { btn.Width = 64; corrected = true; }
                if (btn.Height != 56) { btn.Height = 56; corrected = true; }
                if (btn.HorizontalAlignment != HorizontalAlignment.Center) { btn.HorizontalAlignment = HorizontalAlignment.Center; corrected = true; }
                if (btn.HorizontalContentAlignment != HorizontalAlignment.Center) { btn.HorizontalContentAlignment = HorizontalAlignment.Center; corrected = true; }
                if (btn.VerticalContentAlignment != VerticalAlignment.Center) { btn.VerticalContentAlignment = VerticalAlignment.Center; corrected = true; }
                if (btn.Padding.Left != 8 || btn.Padding.Right != 8 || btn.Padding.Top != 0 || btn.Padding.Bottom != 0)
                { btn.Padding = new Thickness(8, 0, 8, 0); corrected = true; }
                var icon = btn.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault();
                if (icon != null && (icon.FontSize < 24 || icon.FontSize > 28)) { icon.FontSize = 26; corrected = true; }
            }

            if (corrected)
            {
                var msg = "[Guard] Nav rail layout corrected to 64x56 buttons, Padding 8,0, Spacing 6, centered, icons ~26px";
                try { WriteLayoutLog(msg); } catch { }
                try { P2PTalk.Utilities.ErrorLogger.LogException(new InvalidOperationException(msg), source: "Layout.Guard"); } catch { }
            }
        }
        catch { }
    }
    // [SETTINGS-OVERLAY] Inline host and proxy interactions

#if DEBUG
    private void SetSimulatedPresence(Models.PresenceStatus status)
    {
        try
        {
            if (DataContext is not MainWindowViewModel vm) return;
            var c = vm.SelectedContact;
            if (c?.IsSimulated != true) return;
            try { P2PTalk.Services.AppServices.Contacts.SetPresence(c.UID, status, System.TimeSpan.FromSeconds(60), Models.PresenceSource.Manual); } catch { }
            c.Presence = status;
        }
        catch { }
    }

    // Apply mutual exclusivity: only one switch appears ON at a time.
    // Shield rendering rules (converter):
    //  - Grey: Verified && !Trusted
    //  - Green: Verified && Trusted
    // Persistence: Verified => Contact.IsVerified; Trusted => Contact.IsTrusted; both saved to contacts.p2e.
    private void OnVerifiedSwitchChanged(bool isOn)
    {
        if (_updatingToggles) return;
        try
        {
            if (DataContext is not MainWindowViewModel vm) return;
            var c = vm.SelectedContact;
            if (c?.IsSimulated != true) return;

            if (isOn)
            {
                // Verified-only: show Grey shield (Verified=true, Trusted=false)
                PersistSimulatedFlags(verified: true, trusted: false);
                // Enforce exclusivity in UI
                _updatingToggles = true;
                try { if (_toggleTrusted != null) _toggleTrusted.IsChecked = false; } finally { _updatingToggles = false; }
            }
            else
            {
                // Turning off Verified: clear both; no shields
                PersistSimulatedFlags(verified: false, trusted: false);
            }
            // Avoid re-raising SelectedContact to prevent re-entrancy loops.
        }
        catch { }
    }

    private void OnTrustedSwitchChanged(bool isOn)
    {
        if (_updatingToggles) return;
        try
        {
            if (DataContext is not MainWindowViewModel vm) return;
            var c = vm.SelectedContact;
            if (c?.IsSimulated != true) return;

            if (isOn)
            {
                // Trusted ON: persist Verified=false, Trusted=true (green shield shown via converter in Debug for simulated)
                PersistSimulatedFlags(verified: false, trusted: true);
                _updatingToggles = true;
                try { if (_toggleVerified != null) _toggleVerified.IsChecked = false; } finally { _updatingToggles = false; }
            }
            else
            {
                // Turning off Trusted: clear both
                PersistSimulatedFlags(verified: false, trusted: false);
            }
            // Avoid re-raising SelectedContact to prevent re-entrancy loops.
        }
        catch { }
    }

    // Persist changes to the contact model and services.
    private void PersistSimulatedFlags(bool verified, bool trusted)
    {
        try
        {
            if (DataContext is not MainWindowViewModel vm) return;
            var c = vm.SelectedContact;
            if (c?.IsSimulated != true) return;

            // Persisted verification toggle for testing and manual assignment
            c.IsVerified = verified;
            try { AppServices.Contacts.SetIsVerified(c.UID, verified, AppServices.Passphrase); } catch { }
            // Keep transient runtime verification in sync for UI shields
            c.PublicKeyVerified = verified;
            // Trusted flag persisted via services to keep stores in sync
            try { AppServices.Contacts.SetTrusted(c.UID, trusted, AppServices.Passphrase); } catch { }
            try { AppServices.Peers.SetTrusted(c.UID, trusted); } catch { }
            c.IsTrusted = trusted;
        }
        catch { }
    }

    private void SyncDebugSwitchesFromSelection()
    {
        try
        {
            if (_syncingDebugSwitches) return;
            _syncingDebugSwitches = true;
            if (DataContext is not MainWindowViewModel vm) return;
            var c = vm.SelectedContact;
            if (_toggleVerified == null || _toggleTrusted == null) return;
            // Show the debug toggle panel only for simulated contacts
            try { var panel = this.FindControl<StackPanel>("DebugSimulatedProfilePanel"); if (panel != null) panel.IsVisible = c?.IsSimulated == true; } catch { }
            _updatingToggles = true;
            try
            {
                if (c?.IsSimulated == true)
                {
                    // Initialize transient verification from persisted flag so shields reflect stored state
                    c.PublicKeyVerified = c.IsVerified;
                    // Map current model flags (persisted) to mutually exclusive UI switches:
                    // Prefer Trusted when set; otherwise Verified; else both OFF
                    // - Else both OFF
                    if (c.IsTrusted)
                    {
                        _toggleTrusted.IsChecked = true;
                        _toggleVerified.IsChecked = false;
                    }
                    else if (c.IsVerified)
                    {
                        _toggleVerified.IsChecked = true;
                        _toggleTrusted.IsChecked = false;
                    }
                    else
                    {
                        _toggleVerified.IsChecked = false;
                        _toggleTrusted.IsChecked = false;
                    }
                    // No SelectedContact raise; bindings will update from property changes.
                }
                else
                {
                    _toggleVerified.IsChecked = false;
                    _toggleTrusted.IsChecked = false;
                }
            }
            finally { _updatingToggles = false; }
        }
        catch { }
        finally { _syncingDebugSwitches = false; }
    }
    private void OnVmPropertyChangedForDebugSwitches(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        try
        {
            if (e.PropertyName == nameof(MainWindowViewModel.SelectedContact))
            {
                if (!_syncingDebugSwitches && !_updatingToggles)
                {
                    SyncDebugSwitchesFromSelection();
                }
                TryRestoreFocusAfterContactsRefresh();
            }
        }
        catch { }
    }
#endif
    

    private void ShowSettingsOverlay(string? section = null)
    {
        try
        {
            var overlay = this.FindControl<Grid>("SettingsOverlay");
            var host = this.FindControl<ContentControl>("InlineSettingsHost");
            if (overlay == null || host == null) return;
            overlay.IsVisible = true;
            // Reset transient UI states on open: hide lingering toasts and banners
            try { SettingsProxy.ResetTransientUi(); } catch { }
            // Create view on demand to avoid early DataContext casts during window init
            if (host.Content is not SettingsView view)
            {
                view = new SettingsView();
                view.DataContext = SettingsProxy;
                host.Content = view;
            }
            // Only focus the view when it's newly shown and safe (no context menu / not frozen)
            bool safeToFocus = true;
            try
            {
                if (IsAnyContextMenuOpen()) safeToFocus = false;
                if (DataContext is MainWindowViewModel vm && vm.IsSelectionFrozen) safeToFocus = false;
            }
            catch { }
            if (safeToFocus)
            {
                view.Focus();
            }
            try { _settingsVm?.SyncThemeFromPersisted(); } catch { }
            try { _settingsVm?.SyncProfileFromPersisted(); } catch { }
            if (!string.IsNullOrWhiteSpace(section))
                view.SwitchToTab(section);
        }
        catch { }
    }

    private void HideSettingsOverlayIfConfirmed()
    {
        try
        {
            var overlay = this.FindControl<Grid>("SettingsOverlay");
            if (overlay == null || overlay.IsVisible == false) return;
            HideSettingsOverlayImmediate();
            try { SettingsProxy.ResetTransientUi(); } catch { }
        }
        catch { }
    }

    // Attempt to restore focus to a stable element after contacts refresh if focus is null and it's safe
    private void TryRestoreFocusAfterContactsRefresh()
    {
        try
        {
            var vm = this.DataContext as MainWindowViewModel;
            if (vm == null) return;
            if (vm.IsSelectionFrozen) return; // don't interfere during freeze
            if (IsAnyContextMenuOpen()) return;
            // Only restore focus if it was previously in the contacts list;
            // this prevents stealing focus from the message input and other fields.
            if (!_lastFocusWasContactsList) return;

            var focusedNow = this.FocusManager?.GetFocusedElement();
            if (focusedNow != null) return; // nothing to do

            // Defer slightly to allow virtualization to realize containers
            var sel = vm.SelectedContact;
            if (sel == null) return;
            var list = this.FindControl<ListBox>("ContactsList");
            if (list == null) return;
            var uid = sel.UID;
            var delay = TimeSpan.FromMilliseconds(150);
            _ = Avalonia.Threading.DispatcherTimer.RunOnce(() =>
            {
                try
                {
                    if (vm.IsSelectionFrozen || IsAnyContextMenuOpen()) return;
                    if (this.FocusManager?.GetFocusedElement() != null) return;
                    var container = list.ContainerFromItem(sel) as ListBoxItem;
                    if (container != null)
                    {
                        container.Focus();
                    }
                }
                catch { }
            }, delay);
        }
        catch { }
    }

    // Tracks whether the last GotFocus event originated from within the ContactsList
    private bool _lastFocusWasContactsList;

    private void HideSettingsOverlayImmediate()
    {
        try
        {
            var overlay = this.FindControl<Grid>("SettingsOverlay");
            if (overlay != null) overlay.IsVisible = false;
            // Dispose inline settings view to stop any bindings trying to cast MainWindowVM to SettingsVM
            try
            {
                var host = this.FindControl<ContentControl>("InlineSettingsHost");
                if (host != null) host.Content = null;
            }
            catch { }
        }
        catch { }
    }

    private void SettingsOverlay_Save_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            SettingsProxy.SaveCommand.Execute(null);
            // Stay on overlay as requested.
        }
        catch { }
    }
    private void SettingsOverlay_Close_Click(object? sender, RoutedEventArgs e)
    {
        HideSettingsOverlayIfConfirmed();
    }
    private void SettingsOverlay_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideSettingsOverlayIfConfirmed();
            e.Handled = true;
        }
    }

    private void PendingInvites_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var pend = AppServices.ContactRequests.PendingInboundRequests.ToList();
            // Placeholder notices collection (future: integrate with a NotificationsService)
            var notices = new System.Collections.Generic.List<string>();
            var win = new Window
            {
                // Notification Center (invites appear here for now)
                Title = $"Notifications ({pend.Count} invites)",
                // 16:9 size (e.g., 720x405) with reasonable minimums
                Width = 720,
                Height = 405,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ExtendClientAreaToDecorationsHint = true,
                ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome,
                ExtendClientAreaTitleBarHeightHint = 32
            };
            var invitesList = new StackPanel { Margin = new Thickness(8), Spacing = 6 };
            void RenderInvites()
            {
                    if (DataContext is MainWindowViewModel vm) vm.BeginSelectionFreeze();
                try
                {
                    invitesList.Children.Clear();
                    var invites = AppServices.ContactRequests.PendingInboundRequests.ToList();
                    foreach (var p in invites.OrderByDescending(x => x.ReceivedAt))
                    {
                        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Tag = p.Nonce };
                        row.Children.Add(new TextBlock { Text = $"{p.DisplayName} ({p.Uid[..Math.Min(10,p.Uid.Length)]}...)" });
                        var accept = new Button { Content = "Accept", Margin = new Thickness(4,0,0,0), Padding = new Thickness(6,2) };
                        var reject = new Button { Content = "Reject", Margin = new Thickness(4,0,0,0), Padding = new Thickness(6,2) };
                        Grid.SetColumn(accept, 1); Grid.SetColumn(reject, 2);
                        accept.Click += async (_, __) => { try { await AppServices.ContactRequests.AcceptPendingAsync(p.Nonce); RefreshPendingInviteFlag(); RenderInvites(); } catch { } };
                        reject.Click += async (_, __) => { try { await AppServices.ContactRequests.RejectPendingAsync(p.Nonce); RefreshPendingInviteFlag(); RenderInvites(); } catch { } };
                        row.Children.Add(accept); row.Children.Add(reject);
                        invitesList.Children.Add(new Border { Padding = new Thickness(6), Background = (IBrush?)Application.Current?.FindResource("App.Surface"), BorderBrush = (IBrush?)Application.Current?.FindResource("App.Border"), BorderThickness = new Thickness(1), Child = row });
                    }
                    if (invites.Count == 0)
                    {
                        invitesList.Children.Add(new TextBlock { Text = "No pending invites.", Opacity = 0.7 });
                    }
                }
                catch { }
            }
            var noticesList = new StackPanel { Margin = new Thickness(8), Spacing = 6 };
            void RenderNotices()
            {
                try
                {
                    noticesList.Children.Clear();
                    if (notices.Count == 0)
                    {
                        noticesList.Children.Add(new TextBlock { Text = "No notifications.", Opacity = 0.7 });
                        return;
                    }
                    foreach (var n in notices)
                    {
                        var row = new Border
                        {
                            Padding = new Thickness(6),
                            Background = (IBrush?)Application.Current?.FindResource("App.Surface"),
                            BorderBrush = (IBrush?)Application.Current?.FindResource("App.Border"),
                            BorderThickness = new Thickness(1),
                            Child = new TextBlock { Text = n, TextWrapping = Avalonia.Media.TextWrapping.Wrap }
                        };
                        noticesList.Children.Add(row);
                    }
                }
                catch { }
            }
            // Build custom header with actions and close button since we use custom chrome
            var header = new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(8, 6),
                BorderBrush = (IBrush?)Application.Current?.FindResource("App.Border"),
                Background = (IBrush?)Application.Current?.FindResource("App.Background")
            };
        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto") };
        headerGrid.Children.Add(new TextBlock { Text = win.Title, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var filterBtn = new Button();
            filterBtn.Classes.Add("icon-button");
            var filterGlyph = new TextBlock { Text = "\uE71C" }; // MDL2: Filter (funnel)
            filterGlyph.Classes.Add("icon-mdl2");
            filterBtn.Content = filterGlyph;
            bool showInvites = true;
            void UpdateFilterTip()
            {
                ToolTip.SetTip(filterBtn, showInvites ? "Filter: Showing Invites (click to show Notices)" : "Filter: Showing Notices (click to show Invites)");
            }
            UpdateFilterTip();
            // Clear All Notices (glyph + tooltip)
            var clearAllBtn = new Button();
            clearAllBtn.Classes.Add("icon-button");
            ToolTip.SetTip(clearAllBtn, "Clear All Notices");
            var clearAllGlyph = new TextBlock { Text = "\uE74D" }; // MDL2: Erase/Delete-like
            clearAllGlyph.Classes.Add("icon-mdl2");
            clearAllBtn.Content = clearAllGlyph;
            // Clear Invites (reuse prohibition glyph used in main header)
            var clearInvBtn = new Button();
            clearInvBtn.Classes.Add("icon-button");
            ToolTip.SetTip(clearInvBtn, "Clear Invites");
            var invGlyph = new Grid { Width = 16, Height = 16, IsHitTestVisible = false };
            invGlyph.Children.Add(new Avalonia.Controls.Shapes.Ellipse { Stroke = (IBrush?)Application.Current?.FindResource("App.ForegroundPrimary"), StrokeThickness = 1.6 });
            invGlyph.Children.Add(new Avalonia.Controls.Shapes.Path { Stroke = (IBrush?)Application.Current?.FindResource("App.ForegroundPrimary"), StrokeThickness = 1.6, Data = Avalonia.Media.Geometry.Parse("M3,13 L13,3") });
            clearInvBtn.Content = invGlyph;
            // Close button (MDL2 close glyph)
            var closeBtn = new Button();
            closeBtn.Classes.Add("icon-button");
            ToolTip.SetTip(closeBtn, "Close");
            var closeGlyph = new TextBlock { Text = "\uE8BB" };
            closeGlyph.Classes.Add("icon-mdl2");
            closeBtn.Content = closeGlyph;
            Grid.SetColumn(filterBtn, 1);
            Grid.SetColumn(clearAllBtn, 2);
            Grid.SetColumn(clearInvBtn, 3);
            Grid.SetColumn(closeBtn, 4);
            headerGrid.Children.Add(filterBtn);
            headerGrid.Children.Add(closeBtn);
            headerGrid.Children.Add(clearAllBtn);
            headerGrid.Children.Add(clearInvBtn);
            header.Child = headerGrid;

            // Root layout with header + scroll content
            var root = new Grid { RowDefinitions = new RowDefinitions("Auto, *") };
            Grid.SetRow(header, 0);
            root.Children.Add(header);
            // Content host switches between Invites and Notices
            var contentHost = new ContentControl();
            void Render()
            {
                if (showInvites)
                {
                    RenderInvites();
                    contentHost.Content = invitesList;
                }
                else
                {
                    RenderNotices();
                    contentHost.Content = noticesList;
                }
            }
            Render();
            var scroller = new ScrollViewer { Content = contentHost };
            Grid.SetRow(scroller, 1);
            root.Children.Add(scroller);

            filterBtn.Click += (_, __) => { showInvites = !showInvites; UpdateFilterTip(); Render(); };
            clearAllBtn.Click += (_, __) =>
            {
                try
                {
                    // Clear Notices only; do not touch invites
                    notices.Clear();
                    if (!showInvites) Render();
                }
                catch { }
            };
            clearInvBtn.Click += (_, __) =>
            {
                try
                {
                    AppServices.ContactRequests.ClearAllPendingInbound();
                    if (showInvites) Render();
                    RefreshPendingInviteFlag();
                }
                catch { }
            };
            closeBtn.Click += (_, __) => { try { win.Close(); } catch { } };
            win.Content = root;
            win.ShowDialog(this);
        }
        catch { }
    }

    private void RefreshPendingInviteFlag()
    {
        try
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.GetType().GetProperty("HasPendingInvites")?.SetValue(vm, AppServices.ContactRequests.PendingInboundRequests.Count > 0);
            }
        }
        catch { }
    }

    // ...existing code...
    private void ShowVerificationRequestPopup(string uid)
    {
        try
        {
            // If already showing for same uid, bring to front
            if (_verifyReqPopup != null && _verifyReqUid == uid)
            {
                _verifyReqPopup.Activate();
                return;
            }
            _verifyReqUid = uid;
            var contact = AppServices.Contacts.Contacts.FirstOrDefault(c => string.Equals(c.UID, uid, StringComparison.OrdinalIgnoreCase));
            var dn = contact?.DisplayName ?? uid;
            var win = new Window
            {
                Title = "Verification Request",
                // 16:9 modal sizing
                Width = 640,
                Height = 360,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ExtendClientAreaToDecorationsHint = true,
                ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome,
                ExtendClientAreaTitleBarHeightHint = 32
            };
            var root = new StackPanel { Margin = new Thickness(12), Spacing = 8 };
            root.Children.Add(new TextBlock { Text = $"{dn} ({uid}) wants to verify.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            var decline = new Button { Content = "Decline" };
            var start = new Button { Content = "Start", IsDefault = true };
            row.Children.Add(decline); row.Children.Add(start);
            root.Children.Add(row);
            win.Content = root;

            start.Click += async (_, __) =>
            {
                try
                {
                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
                    // Open local verification modal; do not resend 0xC4 (peer already asked)
                    var ok = await AppServices.Dialogs.ShowVerificationDialogAsync(dn, uid);
                    if (ok)
                    {
                        await AppServices.ContactRequests.RequestVerificationAsync(uid, cts.Token);
                    }
                    else
                    {
                        await AppServices.ContactRequests.CancelManualVerificationAsync(uid, cts.Token, AppServices.Identity.DisplayName ?? "You");
                    }
                }
                catch { }
                finally { win.Close(); _verifyReqPopup = null; _verifyReqUid = null; }
            };
            decline.Click += async (_, __) =>
            {
                try
                {
                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await AppServices.ContactRequests.CancelManualVerificationAsync(uid, cts.Token, AppServices.Identity.DisplayName ?? "You");
                }
                catch { }
                finally { win.Close(); _verifyReqPopup = null; _verifyReqUid = null; }
            };
            _verifyReqPopup = win;
            win.ShowDialog(this);
        }
        catch { }
    }

    private void DismissVerificationRequestPopup(string uid)
    {
        try
        {
            if (_verifyReqPopup != null && _verifyReqUid == uid)
            {
                _verifyReqPopup.Close();
                _verifyReqPopup = null; _verifyReqUid = null;
            }
        }
        catch { }
    }

    private async void StartVerification_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not MainWindowViewModel vm) return;
            var c = vm.SelectedContact;
            if (c == null) return;
            var uid = c.UID;
            var dn = c.DisplayName ?? uid;
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
            // Send a manual verification request (0xC4) so the peer sees a notification
            try { await AppServices.ContactRequests.StartManualVerificationAsync(uid, cts.Token); } catch { }
            // Open local modal
            var ok = await AppServices.Dialogs.ShowVerificationDialogAsync(dn, uid);
            if (ok)
            {
                // User clicked Verify: send verification intent (0xC3)
                try { await AppServices.ContactRequests.RequestVerificationAsync(uid, cts.Token); } catch { }
            }
            else
            {
                // User cancelled locally; inform peer and log
                try { await AppServices.ContactRequests.CancelManualVerificationAsync(uid, cts.Token, AppServices.Identity.DisplayName ?? "You"); } catch { }
            }
        }
        catch { }
    }

    // (method moved above)

    private void SetLeftVisibility(bool visible)
    {
        var grid = this.FindControl<Grid>("BodyGrid");
        if (grid?.ColumnDefinitions is { Count: >= 4 })
        {
            var leftRoot = this.FindControl<Grid>("LeftPanelRoot");
            grid.ColumnDefinitions[1].Width = visible ? new GridLength(280, GridUnitType.Pixel)
                                                      : new GridLength(0, GridUnitType.Pixel);
            if (leftRoot != null) leftRoot.IsVisible = visible;
        }
    }

    private void SetRightVisibility(bool visible)
    {
        var grid = this.FindControl<Grid>("BodyGrid");
        if (grid?.ColumnDefinitions is { Count: >= 4 })
        {
            grid.ColumnDefinitions[5].Width = visible ? new GridLength(300, GridUnitType.Pixel) : new GridLength(0, GridUnitType.Pixel);
            var rightRoot = this.FindControl<Border>("RightPanelRoot");
            if (rightRoot != null) rightRoot.IsVisible = visible;
        }
    }

    // Public API: Reset main UI to defaults (panel visibility + layout) and persist immediately.
    public void ResetMainLayoutToDefaults()
    {
        try
        {
            // Defaults: both panels visible; center is star
            SetLeftVisibility(true);
            SetRightVisibility(true);
            // Remove any legacy width persistence
            AppServices.Settings.Settings.MainLeftWidth = null;
            AppServices.Settings.Settings.MainRightWidth = null;
            _ = System.Threading.Tasks.Task.Run(() => AppServices.Settings.Save(AppServices.Passphrase));
        }
        catch { }
    }

    private sealed class ScrollAnimator : IDisposable
    {
        private readonly ScrollViewer _scrollViewer;
        private DispatcherTimer? _timer;
        private Stopwatch? _stopwatch;
        private double _startY;
        private double _targetY;
        private TimeSpan _duration;

        public ScrollAnimator(ScrollViewer scrollViewer)
        {
            _scrollViewer = scrollViewer;
        }

        public bool IsFor(ScrollViewer viewer) => ReferenceEquals(_scrollViewer, viewer);

        public bool MoveTo(double targetY)
        {
            try
            {
                targetY = Math.Max(0, targetY);
                var current = _scrollViewer.Offset;
                var startY = current.Y;
                var delta = targetY - startY;
                if (Math.Abs(delta) < 0.5)
                {
                    SetOffset(targetY);
                    Cancel();
                    return false;
                }

                _startY = startY;
                _targetY = targetY;
                var durationMs = Math.Clamp(110.0 + (Math.Abs(delta) / 3.5), 160.0, 420.0);
                _duration = TimeSpan.FromMilliseconds(durationMs);
                if (_duration <= TimeSpan.Zero)
                    _duration = TimeSpan.FromMilliseconds(120);

                EnsureTimer();
                _stopwatch ??= new Stopwatch();
                _stopwatch.Restart();
                return true;
            }
            catch
            {
                try { SetOffset(targetY); }
                catch { }
                Cancel();
                return false;
            }
        }

        public void Cancel()
        {
            StopTimer();
            _stopwatch?.Stop();
        }

        public void Dispose()
        {
            if (_timer != null)
            {
                _timer.Tick -= OnTick;
                _timer.Stop();
                _timer = null;
            }
            _stopwatch?.Stop();
        }

        private void EnsureTimer()
        {
            if (_timer == null)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(16)
                };
                _timer.Tick += OnTick;
            }

            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }
        }

        private void StopTimer()
        {
            if (_timer != null && _timer.IsEnabled)
            {
                _timer.Stop();
            }
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (_stopwatch == null)
            {
                Cancel();
                return;
            }

            var elapsed = _stopwatch.Elapsed;
            if (_duration <= TimeSpan.Zero)
            {
                SetOffset(_targetY);
                Cancel();
                return;
            }

            var progress = elapsed.TotalMilliseconds / _duration.TotalMilliseconds;
            if (progress >= 1)
            {
                SetOffset(_targetY);
                Cancel();
                return;
            }

            var eased = EaseOutCubic(Math.Clamp(progress, 0, 1));
            var y = _startY + ((_targetY - _startY) * eased);
            SetOffset(y);
        }

        private static double EaseOutCubic(double t)
        {
            var inv = 1 - t;
            return 1 - (inv * inv * inv);
        }

        private void SetOffset(double targetY)
        {
            try
            {
                var current = _scrollViewer.Offset;
                _scrollViewer.Offset = new Vector(current.X, targetY);
            }
            catch { }
        }
    }

    public void Dispose()
    {
        try
        {
            _uiPulseHandler = null;
        }
        catch { }
        try
        {
            _chatScrollAnimator?.Dispose();
            _chatScrollAnimator = null;
        }
        catch { }
        try
        {
            if (_messagesChangedHandler != null && DataContext is MainWindowViewModel vm)
            {
                vm.Messages.CollectionChanged -= _messagesChangedHandler;
            }
        }
        catch { }
        ClearMessageHandlers();
        GC.SuppressFinalize(this);
    }
}
