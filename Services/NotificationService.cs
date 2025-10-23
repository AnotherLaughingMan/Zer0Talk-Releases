using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using Zer0Talk.Utilities;

using System.Runtime.InteropServices;
using Avalonia;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace Zer0Talk.Services
{
    // Lightweight cross-platform app notification hub used by views and services.
    // - Stores in-app notices (for Notification Center in UI)
    // - Publishes OS notifications when available (Windows implementation here)
    public class NotificationService
    {
    public sealed record NotificationItem(Guid Id, string Title, string Body, string? OriginUid, DateTime Utc, string? FullBody = null, bool IsUnread = false, bool IsMessage = false, bool IsIncoming = false, Guid? MessageId = null, DateTime? ReadUtc = null, bool IsPersistent = true, Models.NotificationType? Type = null);

    private readonly List<NotificationItem> _notices = new();
    private readonly object _removalLock = new();
    private readonly Queue<Guid> _messageRemovalQueue = new();
    private readonly HashSet<Guid> _messageRemovalPending = new();
    private bool _messageRemovalWorkerRunning;
    private readonly List<Window> _activeToastWindows = new();

    private const int ToastMargin = 12;
    private const int ToastSpacing = 8;
    private const int ToastWidth = 420;

    private void RemoveToastWindow(Window toast)
    {
        if (toast == null) return;

        void RemoveAndReflow()
        {
            try
            {
                var beforeCount = _activeToastWindows.Count;
                if (_activeToastWindows.Remove(toast))
                {
                    try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Toast] Removed: {beforeCount} -> {_activeToastWindows.Count} active\n"); } catch { }
                    ReflowToastPositionsCore();
                }
                else
                {
                    try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Toast] Remove failed: not found in {beforeCount} active\n"); } catch { }
                }
            }
            catch { }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            RemoveAndReflow();
        }
        else
        {
            Dispatcher.UIThread.Post(RemoveAndReflow);
        }
    }

    private void PruneToastWindows()
    {
        try
        {
            for (int i = _activeToastWindows.Count - 1; i >= 0; i--)
            {
                var win = _activeToastWindows[i];
                if (win == null || !win.IsVisible)
                {
                    _activeToastWindows.RemoveAt(i);
                }
            }
        }
        catch { }
    }

    private void ReflowToastPositions()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ReflowToastPositionsCore();
        }
        else
        {
            Dispatcher.UIThread.Post(ReflowToastPositionsCore);
        }
    }

    private void ReflowToastPositionsCore()
    {
        try
        {
            PruneToastWindows();
            if (_activeToastWindows.Count == 0) 
            {
                try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Toast] Reflow: No active windows\n"); } catch { }
                return;
            }

            var area = GetPrimaryWorkingArea();
            var targetLeft = area.Right - ToastWidth - ToastMargin;
            try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Toast] Reflow: {_activeToastWindows.Count} windows, area={area}, targetLeft={targetLeft}\n"); } catch { }

            var cumulativeTop = area.Y + ToastMargin;
            for (int i = 0; i < _activeToastWindows.Count; i++)
            {
                var toast = _activeToastWindows[i];
                if (toast == null || !toast.IsVisible) continue;

                var oldPos = toast.Position;
                try { toast.Position = new PixelPoint(targetLeft, cumulativeTop); } catch { }
                
                var toastHeight = (int)Math.Max(1, toast.Bounds.Height > 0 ? toast.Bounds.Height : toast.Height);
                try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Toast] Reflow[{i}]: height={toastHeight}, {oldPos} -> ({targetLeft},{cumulativeTop})\n"); } catch { }
                
                cumulativeTop += toastHeight + ToastSpacing;
            }
        }
        catch { }
    }

    private static PixelRect GetPrimaryWorkingArea()
    {
        try
        {
            var lifetime = Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var mainWin = lifetime?.MainWindow;
            if (mainWin?.Screens?.Primary != null)
            {
                return mainWin.Screens.Primary.WorkingArea;
            }
        }
        catch { }

        return new PixelRect(0, 0, 1280, 720);
    }

    private Control CreateToastContent(Window host, string? title, string text, Models.NotificationType? type = null, string? originUid = null)
    {
        var resolvedTitle = string.IsNullOrWhiteSpace(title) ? "Zer0Talk" : title;
        var resolvedBody = string.IsNullOrWhiteSpace(text) ? string.Empty : text;
        var hasOrigin = !string.IsNullOrWhiteSpace(originUid);
        
        // Determine toast border color based on notification type - use theme background
        IBrush backgroundColor = (IBrush?)Application.Current?.FindResource("App.Surface") ?? new SolidColorBrush(Color.FromArgb(255, 64, 64, 64));
        IBrush borderBrush;
        IBrush accentColor; // Used for title/icon color
        
        switch (type)
        {
            case Models.NotificationType.Error:
                borderBrush = new SolidColorBrush(Color.FromArgb(255, 211, 47, 47)); // Red
                accentColor = borderBrush;
                break;
            case Models.NotificationType.Warning:
                borderBrush = new SolidColorBrush(Color.FromArgb(255, 255, 152, 0)); // Orange
                accentColor = borderBrush;
                break;
            case Models.NotificationType.Information:
                borderBrush = new SolidColorBrush(Color.FromArgb(255, 76, 175, 80)); // Green
                accentColor = borderBrush;
                break;
            case Models.NotificationType.Success:
                borderBrush = new SolidColorBrush(Color.FromArgb(255, 76, 175, 80)); // Green (same as Information)
                accentColor = borderBrush;
                break;
            default:
                // Messages or default case - blue for messages, gray for other
                if (hasOrigin)
                {
                    borderBrush = new SolidColorBrush(Color.FromArgb(255, 33, 150, 243)); // Blue
                    accentColor = borderBrush;
                }
                else
                {
                    borderBrush = (IBrush?)Application.Current?.FindResource("App.Border") ?? new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
                    accentColor = (IBrush?)Application.Current?.FindResource("App.ForegroundPrimary") ?? Brushes.White;
                }
                break;
        }

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            Margin = new Thickness(12, 10, 12, 12),
        };

        var titleBlock = new TextBlock
        {
            Text = resolvedTitle,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var bodyBlock = new TextBlock
        {
            Text = resolvedBody,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            Margin = new Thickness(0, 6, 0, 0)
        };

        var closeButton = new Button
        {
            Content = "X",
            Width = 24,
            Height = 24,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = (IBrush?)Application.Current?.FindResource("App.ForegroundPrimary")
        };

        ToolTip.SetTip(closeButton, "Dismiss");

        closeButton.Click += (_, __) =>
        {
            try { host.Close(); } catch { }
        };
        closeButton.PointerPressed += (_, e) => { e.Handled = true; };

        Grid.SetRow(titleBlock, 0);
        Grid.SetColumn(titleBlock, 0);
        Grid.SetRow(closeButton, 0);
        Grid.SetColumn(closeButton, 1);
        Grid.SetRow(bodyBlock, 1);
        Grid.SetColumn(bodyBlock, 0);
        Grid.SetColumnSpan(bodyBlock, 2);

        grid.Children.Add(titleBlock);
        grid.Children.Add(closeButton);
        grid.Children.Add(bodyBlock);

        // Add "Go to Chat" button if this is a message notification (but not for invites)
        var isInvite = resolvedTitle.Contains("Invite", StringComparison.OrdinalIgnoreCase);
        if (hasOrigin && !isInvite)
        {
            var goToChatButton = new Button
            {
                Content = Services.AppServices.Localization.GetString("Notifications.GoToChat", "Go to Chat"),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(12, 6),
                Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0)),
                BorderBrush = Brushes.Transparent,
                CornerRadius = new CornerRadius(4),
                FontWeight = FontWeight.SemiBold
            };

            goToChatButton.Click += (_, __) =>
            {
                try
                {
                    // First, bring the main window to front and activate it
                    var desktop = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                    var mainWindow = desktop?.MainWindow;
                    if (mainWindow != null)
                    {
                        // Show window if hidden (from system tray)
                        if (!mainWindow.IsVisible)
                        {
                            mainWindow.Show();
                        }
                        
                        // Restore if minimized
                        if (mainWindow.WindowState == Avalonia.Controls.WindowState.Minimized)
                        {
                            mainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
                        }
                        
                        // Activate and bring to front
                        mainWindow.Activate();
                        mainWindow.Topmost = true;
                        mainWindow.Topmost = false; // Reset topmost to normal behavior
                        
                        try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Toast] Go to Chat: Main window activated and brought to front\n"); } catch { }
                    }
                    
                    // Then navigate to the chat with this user
                    var fullUid = originUid?.StartsWith("usr-") == true ? originUid : $"usr-{originUid}";
                    AppServices.Events.RaiseOpenConversationRequested(fullUid);
                    host.Close();
                }
                catch { }
            };

            Grid.SetRow(goToChatButton, 2);
            Grid.SetColumn(goToChatButton, 0);
            Grid.SetColumnSpan(goToChatButton, 2);
            grid.Children.Add(goToChatButton);
        }

        // Use accent color for title to match the border theme
        titleBlock.Foreground = accentColor;
        bodyBlock.Foreground = (IBrush?)Application.Current?.FindResource("App.ForegroundPrimary") ?? Brushes.White;
        closeButton.Foreground = (IBrush?)Application.Current?.FindResource("App.ForegroundPrimary") ?? Brushes.White;

        return new Border
        {
            Padding = new Thickness(8),
            Background = backgroundColor,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(6),
            Child = grid
        };
    }

    public IReadOnlyList<NotificationItem> Notices => _notices.AsReadOnly();

        public event Action? NoticesChanged;

        // Backwards-compatible convenience: post a simple notice with combined text
        public void PostNotice(string text, bool isPersistent = true)
        {
            PostNotice(Models.NotificationType.Information, text, originUid: null, fullBody: text, isPersistent: isPersistent);
        }

        // Structured notice post with optional origin UID (used for click-to-open)
        public void PostNotice(Models.NotificationType type, string body, string? originUid = null, string? fullBody = null, bool isPersistent = true)
        {
            try
            {
                // Localize the title based on notification type
                var title = type switch
                {
                    Models.NotificationType.Error => AppServices.Localization.GetString("Notifications.Error", "Error"),
                    Models.NotificationType.Warning => AppServices.Localization.GetString("Notifications.Warning", "Warning"),
                    Models.NotificationType.Success => AppServices.Localization.GetString("Notifications.Success", "Success"),
                    _ => AppServices.Localization.GetString("Notifications.Information", "Information")
                };

                var item = new NotificationItem(Guid.NewGuid(), title, body ?? string.Empty, originUid, DateTime.UtcNow, fullBody, IsPersistent: isPersistent, Type: type);
                
                // Only add to notice list if persistent (test toasts should not appear in notification center)
                if (isPersistent)
                {
                    lock (_notices)
                    {
                        _notices.Add(item);
                    }

                    // Notify in-app listeners on UI thread
                    Dispatcher.UIThread.Post(() => { try { NoticesChanged?.Invoke(); } catch { } });
                }
                try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Notices] Posted (persistent={isPersistent}): {item.Title} | {item.Body} origin={item.OriginUid}\n"); } catch { }

                // Show transient pop-up; attach origin so click may open conversation
                // Check if notifications should be suppressed in Do Not Disturb mode
                try
                {
                    bool shouldShowToast = true;
                    try
                    {
                        var settings = AppServices.Settings.Settings;
                        if (settings.SuppressNotificationsInDnd && settings.Status == Models.PresenceStatus.DoNotDisturb)
                        {
                            shouldShowToast = false;
                            try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Notices] Toast suppressed (DND): {item.Title} | {item.Body} origin={item.OriginUid}\n"); } catch { }
                        }
                    }
                    catch { }

                    if (shouldShowToast)
                    {
                        ShowTransientToast(item.Title, item.Body, item.Type, item.OriginUid);
                        try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Notices] Transient shown: {item.Title} | {item.Body} origin={item.OriginUid}\n"); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    Utilities.Logger.Log($"NotificationService: Transient toast failed: {ex.Message}");
                }

                // Play notification sound (unless suppressed in DND mode)
                try
                {
                    bool shouldPlayAudio = true;
                    try
                    {
                        var settings = AppServices.Settings.Settings;
                        if (settings.SuppressNotificationsInDnd && settings.Status == Models.PresenceStatus.DoNotDisturb)
                        {
                            shouldPlayAudio = false;
                            try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Notices] Audio suppressed (DND): {item.Title} | {item.Body}\n"); } catch { }
                        }
                    }
                    catch { }

                    if (shouldPlayAudio)
                    {
                        _ = System.Threading.Tasks.Task.Run(async () =>
                        {
                            try
                            {
                                // Play type-specific sounds based on notification type
                                switch (item.Type)
                                {
                                    case Models.NotificationType.Warning:
                                        await AppServices.AudioNotifications.PlayCustomSoundAsync("ui-10-smooth-warnnotify-sound-effect-365842.mp3");
                                        break;
                                    case Models.NotificationType.Information:
                                        await AppServices.AudioNotifications.PlayCustomSoundAsync("smooth-notify-alert-toast-warn-274736.mp3");
                                        break;
                                    case Models.NotificationType.Error:
                                        await AppServices.AudioNotifications.PlayCustomSoundAsync("smooth-completed-notify-starting-alert-274739.mp3");
                                        break;
                                    default:
                                        await AppServices.AudioNotifications.PlaySoundAsync(AudioNotificationService.SoundType.NotificationGeneral);
                                        break;
                                }
                            }
                            catch (Exception ex)
                            {
                                Utilities.Logger.Log($"NotificationService: Audio notification failed: {ex.Message}");
                            }
                        });
                    }
                }
                catch { }
            }
            catch { }
        }

        public void ClearNotices()
        {
            try
            {
                lock (_notices) { _notices.Clear(); }
                Dispatcher.UIThread.Post(() => { try { NoticesChanged?.Invoke(); } catch { } });
            }
            catch { }
        }

        // Remove notices whose OriginUid matches any of the supplied origins (UIDs).
        public void RemoveNoticesForOrigins(IEnumerable<string> origins)
        {
            try
            {
                if (origins == null) return;
                var set = new HashSet<string>(origins.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
                if (set.Count == 0) return;
                lock (_notices)
                {
                    _notices.RemoveAll(n => n.OriginUid != null && set.Contains(n.OriginUid));
                }
                Dispatcher.UIThread.Post(() => { try { NoticesChanged?.Invoke(); } catch { } });
            }
            catch { }
        }

        public NotificationItem AddOrUpdateMessageNotice(string title, string body, string? originUid, Guid messageId, bool incoming, DateTime? timestamp = null, bool isUnread = true)
        {
            if (messageId == Guid.Empty) messageId = Guid.NewGuid();
            var trimmedOrigin = TrimUidPrefix(originUid ?? string.Empty);
            var messageTime = timestamp ?? DateTime.UtcNow;
            var formattedTime = messageTime.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
            var titleWithTime = $"{title} • {formattedTime}";
            NotificationItem updated;
            bool notify = false;
            bool created = false;
            lock (_notices)
            {
                var index = _notices.FindIndex(n => n.IsMessage && n.MessageId == messageId);
                if (index >= 0)
                {
                    var existing = _notices[index];
                    var readUtc = isUnread ? existing.ReadUtc : existing.ReadUtc ?? DateTime.UtcNow;
                    updated = existing with
                    {
                        Title = string.IsNullOrWhiteSpace(title) ? existing.Title : titleWithTime,
                        Body = string.IsNullOrWhiteSpace(body) ? existing.Body : body,
                        FullBody = string.IsNullOrWhiteSpace(existing.FullBody) ? body : existing.FullBody,
                        OriginUid = string.IsNullOrWhiteSpace(trimmedOrigin) ? existing.OriginUid : trimmedOrigin,
                        IsUnread = isUnread,
                        IsMessage = true,
                        IsIncoming = incoming,
                        MessageId = messageId,
                        ReadUtc = readUtc
                    };
                    _notices[index] = updated;
                }
                else
                {
                    updated = new NotificationItem(
                        Guid.NewGuid(),
                        titleWithTime ?? string.Empty,
                        body ?? string.Empty,
                        string.IsNullOrWhiteSpace(trimmedOrigin) ? null : trimmedOrigin,
                        DateTime.UtcNow,
                        string.IsNullOrWhiteSpace(body) ? null : body,
                        isUnread,
                        true,
                        incoming,
                        messageId,
                        isUnread ? null : DateTime.UtcNow);
                    _notices.Add(updated);
                    created = true;
                }
                notify = true;
            }
            if (notify)
            {
                // Always notify UI of message notice changes (for notification center)
                Dispatcher.UIThread.Post(() => { try { NoticesChanged?.Invoke(); } catch { } });
                try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Notices] Message notice added/updated for notification center: {updated.Title}\n"); } catch { }
            }
            if (created && incoming)
            {
                // Determine notification behavior based on presence status
                bool shouldShowToast = false;      // Default: no toast
                bool shouldPlayAudio = false;      // Default: no audio
                string presenceMode = "Unknown";
                
                try
                {
                    try
                    {
                        var settings = AppServices.Settings.Settings;
                        presenceMode = settings.Status.ToString();
                        
                        // TESTING: Temporarily use normal logic to trace the actual execution path
                        switch (settings.Status)
                        {
                            case Models.PresenceStatus.Online:
                                shouldPlayAudio = true;   // NORMAL: Should play in Online mode
                                shouldShowToast = true;   // Online: Show toast (if window inactive)
                                break;
                                
                            case Models.PresenceStatus.Idle:
                                shouldPlayAudio = true;   // NORMAL: Should play in Idle mode
                                shouldShowToast = true;   // Idle: Show toast
                                break;
                                
                            case Models.PresenceStatus.DoNotDisturb:
                                shouldPlayAudio = false;  // NORMAL: Should NOT play in DND mode
                                shouldShowToast = false;  // DND: NO toast (silent notification to message center only)
                                break;
                                
                            case Models.PresenceStatus.Invisible:
                                shouldPlayAudio = true;   // NORMAL: Should play in Invisible mode
                                shouldShowToast = true;   // Invisible: Show toast
                                break;
                                
                            case Models.PresenceStatus.Offline:
                                shouldPlayAudio = false;  // NORMAL: Should NOT play when Offline
                                shouldShowToast = false;  // Offline: NO toast (user cannot interact)
                                break;
                                
                            default:
                                shouldPlayAudio = true;   // NORMAL: Default to playing sound
                                shouldShowToast = true;   // Unknown: Default to showing toast
                                break;
                        }
                        
                        try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Notices] Presence mode behavior: {presenceMode} → shouldPlayAudio={shouldPlayAudio}, shouldShowToast={shouldShowToast}\n"); } catch { }
                    }
                    catch (Exception ex)
                    {
                        // If we can't determine presence, default to allowing notifications
                        shouldPlayAudio = true;
                        shouldShowToast = true;
                        try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Notices] Error checking presence status: {ex.Message} → defaulting to shouldPlayAudio=true, shouldShowToast=true\n"); } catch { }
                    }

                    // Check if main window is active to determine desktop toast behavior
                    // IMPORTANT: Must run on UI thread to access window properties
                    bool mainWindowActive = false;
                    bool windowVisible = false;
                    string windowStateDebug = "unknown";
                    
                    // Use Dispatcher to safely access window properties from any thread
                    try
                    {
                        if (Dispatcher.UIThread.CheckAccess())
                        {
                            // Already on UI thread - direct access
                            var desktop = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                            var mainWindow = desktop?.MainWindow;
                            if (mainWindow != null)
                            {
                                windowVisible = mainWindow.IsVisible;
                                windowStateDebug = $"IsActive={mainWindow.IsActive}, WindowState={mainWindow.WindowState}, IsVisible={mainWindow.IsVisible}";
                                
                                // Enhanced window focus detection:
                                // Consider window "active" (suppress toasts) only if:
                                // 1. Window is active AND visible AND not minimized (primary check)
                                // 2. Window is visible AND not minimized (fallback, but must be visible!)
                                // Note: Removed WindowState==Normal check as it's true even when minimized to tray
                                mainWindowActive = (mainWindow.IsActive == true && 
                                                  mainWindow.WindowState != Avalonia.Controls.WindowState.Minimized &&
                                                  mainWindow.IsVisible) ||
                                                 (mainWindow.IsVisible && 
                                                  mainWindow.WindowState != Avalonia.Controls.WindowState.Minimized);
                            }
                            else
                            {
                                windowStateDebug = "MainWindow is null";
                            }
                        }
                        else
                        {
                            // Not on UI thread - use Invoke to marshal to UI thread
                            mainWindowActive = Dispatcher.UIThread.Invoke(() =>
                            {
                                try
                                {
                                    var desktop = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                                    var mainWindow = desktop?.MainWindow;
                                    if (mainWindow != null)
                                    {
                                        windowVisible = mainWindow.IsVisible;
                                        windowStateDebug = $"IsActive={mainWindow.IsActive}, WindowState={mainWindow.WindowState}, IsVisible={mainWindow.IsVisible}";
                                        
                                        return (mainWindow.IsActive == true && 
                                               mainWindow.WindowState != Avalonia.Controls.WindowState.Minimized &&
                                               mainWindow.IsVisible) ||
                                              (mainWindow.IsVisible && 
                                               mainWindow.WindowState != Avalonia.Controls.WindowState.Minimized);
                                    }
                                    else
                                    {
                                        windowStateDebug = "MainWindow is null";
                                        return false;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    windowStateDebug = $"Inner Exception: {ex.Message}";
                                    return false;
                                }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        windowStateDebug = $"Dispatcher Exception: {ex.Message}";
                        mainWindowActive = false;
                    }
                    
                    // Log window state and presence mode for debugging
                    try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Notices] Presence={presenceMode}, Window state: {windowStateDebug}, mainWindowActive={mainWindowActive}, shouldShowToast={shouldShowToast}, shouldPlayAudio={shouldPlayAudio}\n"); } catch { }
                    
                    // Show desktop toast when:
                    // 1. shouldShowToast is true (not suppressed by DND)
                    // 2. Main window is NOT active/visible (minimized, not focused, or system tray)
                    if (shouldShowToast && !mainWindowActive)
                    {
                        var toastBody = string.IsNullOrWhiteSpace(updated.FullBody) ? updated.Body : updated.FullBody;
                        ShowTransientToast(updated.Title, toastBody ?? string.Empty, null, updated.OriginUid);
                        try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Notices] Desktop toast shown: {updated.Title}\n"); } catch { }
                    }
                    else
                    {
                        string reason = !shouldShowToast ? "suppressed by presence mode" : "window is active";
                        try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Notices] Desktop toast skipped ({reason}): shouldShowToast={shouldShowToast}, mainWindowActive={mainWindowActive}\n"); } catch { }
                    }
                }
                catch { }

                // Play audio based on presence mode (suppressed only in DND)
                // Audio plays in all modes except DND: Online, Away, Idle, etc.
                try
                {
                    try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.Audio, $"{DateTime.Now:O} [Audio] Audio decision: shouldPlayAudio={shouldPlayAudio}, presenceMode={presenceMode}\n"); } catch { }

                    if (shouldPlayAudio)
                    {
                        _ = System.Threading.Tasks.Task.Run(async () =>
                        {
                            try
                            {
                                try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.Audio, $"{DateTime.Now:O} [Audio] Attempting to play MessageIncoming sound for: {updated.Title}\n"); } catch { }
                                await AppServices.AudioNotifications.PlaySoundAsync(AudioNotificationService.SoundType.MessageIncoming);
                                try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.Audio, $"{DateTime.Now:O} [Audio] Successfully played MessageIncoming sound for: {updated.Title}\n"); } catch { }
                            }
                            catch (Exception ex)
                            {
                                Utilities.Logger.Log($"NotificationService: Incoming message audio failed: {ex.Message}");
                                try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.Audio, $"{DateTime.Now:O} [Audio] Failed to play MessageIncoming: {ex.Message}\n"); } catch { }
                            }
                        });
                    }
                    else
                    {
                        try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.Audio, $"{DateTime.Now:O} [Audio] Audio playback skipped (presence mode: {presenceMode}): {updated.Title}\n"); } catch { }
                    }
                }
                catch { }
            }
            return updated;
        }

        public void MarkMessageNoticeRead(Guid messageId, bool scheduleRemoval = true)
        {
            if (messageId == Guid.Empty) return;
            bool notify = false;
            lock (_notices)
            {
                var index = _notices.FindIndex(n => n.IsMessage && n.MessageId == messageId);
                if (index < 0) return;
                var existing = _notices[index];
                if (!existing.IsUnread && existing.ReadUtc.HasValue && !scheduleRemoval)
                {
                    return;
                }
                var updated = existing with { IsUnread = false, ReadUtc = existing.ReadUtc ?? DateTime.UtcNow };
                _notices[index] = updated;
                notify = true;
            }
            if (notify)
            {
                Dispatcher.UIThread.Post(() => { try { NoticesChanged?.Invoke(); } catch { } });
            }
            if (scheduleRemoval) EnqueueMessageRemoval(messageId);
        }

        public void MarkConversationMessageNoticesRead(string originUid)
        {
            if (string.IsNullOrWhiteSpace(originUid)) return;
            var trimmed = TrimUidPrefix(originUid);
            List<Guid> toRemove;
            bool notify = false;
            lock (_notices)
            {
                toRemove = _notices
                    .Where(n => n.IsMessage && n.MessageId.HasValue && string.Equals(n.OriginUid, trimmed, StringComparison.OrdinalIgnoreCase))
                    .Select(n => n.MessageId!.Value)
                    .ToList();
                if (toRemove.Count == 0) return;
                for (int i = 0; i < _notices.Count; i++)
                {
                    var n = _notices[i];
                    if (n.IsMessage && n.MessageId.HasValue && string.Equals(n.OriginUid, trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        if (n.IsUnread || !n.ReadUtc.HasValue)
                        {
                            _notices[i] = n with { IsUnread = false, ReadUtc = DateTime.UtcNow };
                            notify = true;
                        }
                    }
                }
            }
            if (notify)
            {
                Dispatcher.UIThread.Post(() => { try { NoticesChanged?.Invoke(); } catch { } });
            }
            foreach (var id in toRemove)
            {
                EnqueueMessageRemoval(id);
            }
        }

        public void MarkAllMessageNoticesRead()
        {
            List<Guid> ids;
            bool notify = false;
            lock (_notices)
            {
                ids = _notices.Where(n => n.IsMessage && n.MessageId.HasValue).Select(n => n.MessageId!.Value).ToList();
                if (ids.Count == 0) return;
                for (int i = 0; i < _notices.Count; i++)
                {
                    var n = _notices[i];
                    if (n.IsMessage && n.MessageId.HasValue)
                    {
                        if (n.IsUnread || !n.ReadUtc.HasValue)
                        {
                            _notices[i] = n with { IsUnread = false, ReadUtc = DateTime.UtcNow };
                            notify = true;
                        }
                    }
                }
            }
            if (notify)
            {
                Dispatcher.UIThread.Post(() => { try { NoticesChanged?.Invoke(); } catch { } });
            }
            foreach (var id in ids)
            {
                EnqueueMessageRemoval(id);
            }
        }

        private void EnqueueMessageRemoval(Guid messageId)
        {
            if (messageId == Guid.Empty) return;
            bool shouldStart = false;
            lock (_removalLock)
            {
                if (!_messageRemovalPending.Add(messageId)) return;
                _messageRemovalQueue.Enqueue(messageId);
                if (!_messageRemovalWorkerRunning)
                {
                    _messageRemovalWorkerRunning = true;
                    shouldStart = true;
                }
            }
            if (!shouldStart) return;

            _ = Task.Run(async () =>
            {
                while (true)
                {
                    Guid next;
                    lock (_removalLock)
                    {
                        if (_messageRemovalQueue.Count == 0)
                        {
                            _messageRemovalWorkerRunning = false;
                            return;
                        }
                        next = _messageRemovalQueue.Dequeue();
                    }

                    await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

                    var removed = false;
                    lock (_notices)
                    {
                        for (int i = _notices.Count - 1; i >= 0; i--)
                        {
                            if (_notices[i].IsMessage && _notices[i].MessageId == next)
                            {
                                _notices.RemoveAt(i);
                                removed = true;
                            }
                        }
                    }
                    if (removed)
                    {
                        Dispatcher.UIThread.Post(() => { try { NoticesChanged?.Invoke(); } catch { } });
                    }
                    lock (_removalLock)
                    {
                        _messageRemovalPending.Remove(next);
                    }
                }
            });
        }

        private static string TrimUidPrefix(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return string.Empty;
            return uid.StartsWith("usr-", StringComparison.Ordinal) && uid.Length > 4 ? uid[4..] : uid;
        }

        private void ShowTransientToast(string title, string text, Models.NotificationType? type = null, string? originUid = null)
        {
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var win = new Avalonia.Controls.Window
                        {
                            Width = ToastWidth,
                            SizeToContent = Avalonia.Controls.SizeToContent.Height,
                            CanResize = false,
                            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.Manual,
                            Topmost = true,
                            ShowInTaskbar = false,
                            ExtendClientAreaToDecorationsHint = true,
                            ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome,
                            SystemDecorations = Avalonia.Controls.SystemDecorations.None
                        };
                        win.Content = CreateToastContent(win, title, text, type, originUid);

                        PruneToastWindows();
                        var beforeCount = _activeToastWindows.Count;

                        // Compute working area and initial positions
                        var area = GetPrimaryWorkingArea();
                        var targetLeft = area.Right - ToastWidth - ToastMargin;
                        var startLeft = area.Right + ToastMargin; // start off-screen to right
                        var startTop = area.Y + ToastMargin; // temporary position

                        try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Toast] Creating: beforeCount={beforeCount}\n"); } catch { }

                        // Place window initially off-screen at top-right
                        try { win.Position = new Avalonia.PixelPoint((int)startLeft, (int)startTop); } catch { }
                        win.Show();
                        _activeToastWindows.Add(win);
                        win.Closed += (_, __) => RemoveToastWindow(win);
                        try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Toast] Added to list: now {_activeToastWindows.Count} active\n"); } catch { }
                        
                        // Delay reflow slightly to allow window to measure with SizeToContent, then animate
                        Dispatcher.UIThread.Post(() => 
                        {
                            ReflowToastPositionsCore();
                            
                            // Get the target position after reflow
                            var targetPos = win.Position;
                            
                            // Start slide-in animation from off-screen
                            var durationMs = 320.0;
                            var start = DateTime.UtcNow;
                            try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Toast] Animation: start=({startLeft},{targetPos.Y}) -> target=({targetLeft},{targetPos.Y})\n"); } catch { }
                            var timer = new Avalonia.Threading.DispatcherTimer()
                            {
                                Interval = TimeSpan.FromMilliseconds(16)
                            };
                            timer.Tick += (_, __) =>
                            {
                                try
                                {
                                    var now = DateTime.UtcNow;
                                    var total = (now - start).TotalMilliseconds;
                                    var t = Math.Clamp(total / durationMs, 0.0, 1.0);
                                    // Ease-out cubic
                                    var eased = 1 - Math.Pow(1 - t, 3);
                                    var left = startLeft - ((startLeft - targetLeft) * eased);
                                    try { win.Position = new Avalonia.PixelPoint((int)left, targetPos.Y); } catch { }
                                    if (t >= 1.0)
                                    {
                                        timer.Stop();
                                        try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Toast] Animation complete: final=({(int)left},{targetPos.Y})\n"); } catch { }
                                    }
                                }
                                catch { }
                            };
                            timer.Start();
                        }, Avalonia.Threading.DispatcherPriority.Loaded);

                        // Mouse/click handler: open conversation and dismiss
                        win.PointerReleased += (_, e) =>
                        {
                            try
                            {
                                if (e.Source is Button)
                                {
                                    return;
                                }

                                if (!string.IsNullOrWhiteSpace(originUid))
                                {
                                    try { AppServices.Events.RaiseOpenConversationRequested(originUid); } catch { }
                                }
                                try { win.Close(); } catch { }
                                e.Handled = true;
                            }
                            catch { }
                        };

                        // Auto close after timeout (with slide-out)
                        _ = System.Threading.Tasks.Task.Run(async () =>
                        {
                            try
                            {
                                // Use user-configurable notification duration
                                var durationSeconds = 4.5; // Default fallback
                                try { durationSeconds = Math.Clamp(AppServices.Settings.Settings.NotificationDurationSeconds, 0.5, 30.0); } catch { }
                                await System.Threading.Tasks.Task.Delay((int)(durationSeconds * 1000));
                                // Slide-out animation (reverse)
                                var outStart = DateTime.UtcNow;
                                var outDur = 260.0;
                                var currentY = 0;
                                Dispatcher.UIThread.Post(() => { try { currentY = win.Position.Y; } catch { } });
                                var outTimer = new Avalonia.Threading.DispatcherTimer() { Interval = TimeSpan.FromMilliseconds(16) };
                                outTimer.Tick += (_, __) =>
                                {
                                    try
                                    {
                                        var now = DateTime.UtcNow;
                                        var elapsed = (now - outStart).TotalMilliseconds;
                                        var tt = Math.Clamp(elapsed / outDur, 0.0, 1.0);
                                        var eased = Math.Pow(tt, 3); // ease-in
                                        var left = targetLeft + ((startLeft - targetLeft) * eased);
                                        Dispatcher.UIThread.Post(() => { try { win.Position = new Avalonia.PixelPoint((int)left, currentY); } catch { } });
                                        if (tt >= 1.0)
                                        {
                                            outTimer.Stop();
                                            Dispatcher.UIThread.Post(() => { try { win.Close(); } catch { } });
                                        }
                                    }
                                    catch { }
                                };
                                outTimer.Start();
                            }
                            catch { }
                        });
                    }
                    catch { }
                }, Avalonia.Threading.DispatcherPriority.Normal);
            }
            catch { }
        }
    }
}

