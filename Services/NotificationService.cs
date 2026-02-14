using System;
using System.Collections.Generic;
using System.Threading;
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
using Zer0Talk.ViewModels;

namespace Zer0Talk.Services
{
    // Lightweight cross-platform app notification hub used by views and services.
    // - Stores in-app notices (for Notification Center in UI)
    // - Publishes OS notifications when available (Windows implementation here)
    public class NotificationService
    {
    public sealed record NotificationItem(Guid Id, string Title, string Body, string? OriginUid, DateTime Utc, string? FullBody = null, bool IsUnread = false, bool IsMessage = false, bool IsIncoming = false, Guid? MessageId = null, DateTime? ReadUtc = null, bool IsPersistent = true, Models.NotificationType? Type = null);
    public sealed record SecurityEventItem(Guid Id, string AccountName, string PeerUid, string Summary, string Details, DateTime Utc, bool IsUnread = true);

    private readonly List<NotificationItem> _notices = new();
    private readonly List<SecurityEventItem> _securityEvents = new();
    private readonly IReadOnlyList<NotificationItem> _noticesReadOnly;
    private readonly IReadOnlyList<SecurityEventItem> _securityEventsReadOnly;
    private readonly object _removalLock = new();
    private readonly Queue<Guid> _messageRemovalQueue = new();
    private readonly HashSet<Guid> _messageRemovalPending = new();
    private bool _messageRemovalWorkerRunning;
    private readonly List<Window> _activeToastWindows = new();
    private readonly object _messageAudioDedupLock = new();
    private readonly object _uiLogThrottleLock = new();
    private readonly Dictionary<string, DateTime> _uiLogThrottleUtc = new();
    private string _lastMessageAudioFingerprint = string.Empty;
    private DateTime _lastMessageAudioUtc = DateTime.MinValue;
    private int _noticesChangedQueued;
    private int _securityEventsChangedQueued;

    private const int ToastMargin = 12;
    private const int ToastSpacing = 8;
    private const int ToastWidth = 420;
#if DEBUG
    private static readonly bool VerboseUiLogs = true;
#else
    private static readonly bool VerboseUiLogs = false;
#endif
    private static readonly TimeSpan MessageAudioDedupWindow = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ToastReflowLogInterval = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan ToastAnimationLogInterval = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan PresenceDecisionLogInterval = TimeSpan.FromMilliseconds(2000);
    private static readonly FontFamily SegoeFluentIconsFont = new FontFamily("Segoe Fluent Icons");
    private static readonly IBrush ToastSurfaceFallbackBrush = new SolidColorBrush(Color.FromArgb(255, 64, 64, 64));
    private static readonly IBrush ToastBorderErrorBrush = new SolidColorBrush(Color.FromArgb(255, 211, 47, 47));
    private static readonly IBrush ToastBorderWarningBrush = new SolidColorBrush(Color.FromArgb(255, 255, 152, 0));
    private static readonly IBrush ToastBorderMessageBrush = new SolidColorBrush(Color.FromArgb(255, 33, 150, 243));
    private static readonly IBrush ToastBorderInfoBrush = new SolidColorBrush(Color.FromArgb(255, 76, 175, 80));
    private static readonly IBrush ToastBorderFallbackBrush = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
    private static readonly IBrush ToastButtonLightBackgroundBrush = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
    private static readonly IBrush ToastButtonDarkForegroundBrush = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0));

    public NotificationService()
    {
        _noticesReadOnly = _notices.AsReadOnly();
        _securityEventsReadOnly = _securityEvents.AsReadOnly();
    }

    private void TryWriteUiLogThrottled(string key, TimeSpan minInterval, Func<string> messageFactory)
    {
        try
        {
            if (!Utilities.LoggingPaths.Enabled) return;
            var now = DateTime.UtcNow;
            lock (_uiLogThrottleLock)
            {
                if (_uiLogThrottleUtc.TryGetValue(key, out var lastUtc))
                {
                    if ((now - lastUtc) < minInterval) return;
                }
                _uiLogThrottleUtc[key] = now;
            }
            Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, messageFactory());
        }
        catch { }
    }

    private void TryWriteUiVerboseLogThrottled(string key, TimeSpan minInterval, Func<string> messageFactory)
    {
        if (!VerboseUiLogs) return;
        TryWriteUiLogThrottled(key, minInterval, messageFactory);
    }

    private void QueueNoticesChanged()
    {
        if (Interlocked.Exchange(ref _noticesChangedQueued, 1) == 1) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _noticesChangedQueued, 0);
            try { NoticesChanged?.Invoke(); } catch { }
        }, DispatcherPriority.Background);
    }

    private void QueueSecurityEventsChanged()
    {
        if (Interlocked.Exchange(ref _securityEventsChangedQueued, 1) == 1) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _securityEventsChangedQueued, 0);
            try { SecurityEventsChanged?.Invoke(); } catch { }
        }, DispatcherPriority.Background);
    }

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
                    TryWriteUiVerboseLogThrottled("toast.remove.ok", ToastReflowLogInterval,
                        () => $"{DateTime.Now:O} [Toast] Removed: {beforeCount} -> {_activeToastWindows.Count} active\n");
                    ReflowToastPositionsCore();
                }
                else
                {
                    TryWriteUiVerboseLogThrottled("toast.remove.miss", ToastReflowLogInterval,
                        () => $"{DateTime.Now:O} [Toast] Remove failed: not found in {beforeCount} active\n");
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
                TryWriteUiVerboseLogThrottled("toast.reflow.empty", ToastReflowLogInterval,
                    () => $"{DateTime.Now:O} [Toast] Reflow: No active windows\n");
                return;
            }

            var area = GetPrimaryWorkingArea();
            var targetLeft = area.Right - ToastWidth - ToastMargin;
            TryWriteUiVerboseLogThrottled("toast.reflow.summary", ToastReflowLogInterval,
                () => $"{DateTime.Now:O} [Toast] Reflow: {_activeToastWindows.Count} windows, area={area}, targetLeft={targetLeft}\n");

            var cumulativeTop = area.Y + ToastMargin;
            for (int i = 0; i < _activeToastWindows.Count; i++)
            {
                var toast = _activeToastWindows[i];
                if (toast == null || !toast.IsVisible) continue;

                var oldPos = toast.Position;
                try { toast.Position = new PixelPoint(targetLeft, cumulativeTop); } catch { }
                
                var toastHeight = (int)Math.Max(1, toast.Bounds.Height > 0 ? toast.Bounds.Height : toast.Height);
                TryWriteUiVerboseLogThrottled($"toast.reflow.item.{i}", ToastReflowLogInterval,
                    () => $"{DateTime.Now:O} [Toast] Reflow[{i}]: height={toastHeight}, {oldPos} -> ({targetLeft},{cumulativeTop})\n");
                
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

    private static string GetFluentNotificationGlyph(Models.NotificationType? type, bool isMessage)
    {
        if (isMessage) return "\uE8BD";
        return type switch
        {
            Models.NotificationType.Error => "\uE783",
            Models.NotificationType.Warning => "\uE7BA",
            Models.NotificationType.Success => "\uE73E",
            Models.NotificationType.Information => "\uE946",
            _ => "\uE946"
        };
    }

    private Control CreateToastContent(Window host, string? title, string text, Models.NotificationType? type = null, string? originUid = null)
    {
        var resolvedTitle = string.IsNullOrWhiteSpace(title) ? "Zer0Talk" : title;
        var resolvedBody = string.IsNullOrWhiteSpace(text) ? string.Empty : text;
        var hasOrigin = !string.IsNullOrWhiteSpace(originUid);
        
        // Determine toast border color based on notification type - use theme background
        IBrush backgroundColor = (IBrush?)Application.Current?.FindResource("App.Surface") ?? ToastSurfaceFallbackBrush;
        IBrush borderBrush;
        IBrush accentColor; // Used for title/icon color
        
        switch (type)
        {
            case Models.NotificationType.Error:
                borderBrush = ToastBorderErrorBrush; // Red
                accentColor = borderBrush;
                break;
            case Models.NotificationType.Warning:
                borderBrush = ToastBorderWarningBrush; // Orange
                accentColor = borderBrush;
                break;
            case Models.NotificationType.Information:
                borderBrush = hasOrigin
                    ? ToastBorderMessageBrush // Blue for message toasts
                    : ToastBorderInfoBrush; // Green for general info
                accentColor = borderBrush;
                break;
            case Models.NotificationType.Success:
                borderBrush = ToastBorderInfoBrush; // Green (same as Information)
                accentColor = borderBrush;
                break;
            default:
                // Messages or default case - blue for messages, gray for other
                if (hasOrigin)
                {
                    borderBrush = ToastBorderMessageBrush; // Blue
                    accentColor = borderBrush;
                }
                else
                {
                    borderBrush = (IBrush?)Application.Current?.FindResource("App.Border") ?? ToastBorderFallbackBrush;
                    accentColor = (IBrush?)Application.Current?.FindResource("App.ForegroundPrimary") ?? Brushes.White;
                }
                break;
        }

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            Margin = new Thickness(12, 10, 12, 12),
        };

        var iconBlock = new TextBlock
        {
            Text = GetFluentNotificationGlyph(type, hasOrigin),
            FontFamily = SegoeFluentIconsFont,
            FontSize = 16,
            Foreground = accentColor,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 8, 0)
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

        Grid.SetRow(iconBlock, 0);
        Grid.SetColumn(iconBlock, 0);
        Grid.SetRow(titleBlock, 0);
        Grid.SetColumn(titleBlock, 1);
        Grid.SetRow(closeButton, 0);
        Grid.SetColumn(closeButton, 2);
        Grid.SetRow(bodyBlock, 1);
        Grid.SetColumn(bodyBlock, 0);
        Grid.SetColumnSpan(bodyBlock, 3);

        grid.Children.Add(iconBlock);
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
                Background = ToastButtonLightBackgroundBrush,
                Foreground = ToastButtonDarkForegroundBrush,
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
            Grid.SetColumnSpan(goToChatButton, 3);
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

    public IReadOnlyList<NotificationItem> Notices => _noticesReadOnly;
    public IReadOnlyList<SecurityEventItem> SecurityEvents => _securityEventsReadOnly;

        public event Action? NoticesChanged;
        public event Action? SecurityEventsChanged;

        public void PostSecurityEvent(string? peerUid, string? accountName, string summary, string details)
        {
            try
            {
                var normalizedUid = TrimUidPrefix(peerUid ?? string.Empty);
                var unknownAccount = AppServices.Localization.GetString("Notifications.UnknownAccount", "Unknown");
                var defaultSummary = AppServices.Localization.GetString("Notifications.SecurityEventDetected", "Security event detected.");
                var resolvedAccount = string.IsNullOrWhiteSpace(accountName) ? (!string.IsNullOrWhiteSpace(normalizedUid) ? normalizedUid : unknownAccount) : accountName.Trim();
                var resolvedSummary = string.IsNullOrWhiteSpace(summary) ? defaultSummary : summary.Trim();
                var resolvedDetails = string.IsNullOrWhiteSpace(details) ? resolvedSummary : details.Trim();

                lock (_securityEvents)
                {
                    _securityEvents.Add(new SecurityEventItem(
                        Guid.NewGuid(),
                        resolvedAccount,
                        normalizedUid,
                        resolvedSummary,
                        resolvedDetails,
                        DateTime.UtcNow,
                        IsUnread: true));
                }

                QueueSecurityEventsChanged();

                var toastBody = string.IsNullOrWhiteSpace(normalizedUid)
                    ? $"{resolvedAccount}: {resolvedSummary}"
                    : $"{resolvedAccount} ({normalizedUid}): {resolvedSummary}";
                try { PostNotice(Models.NotificationType.Warning, toastBody, originUid: normalizedUid, fullBody: resolvedDetails, isPersistent: false); } catch { }
            }
            catch { }
        }

        public void RemoveSecurityEvent(Guid eventId)
        {
            if (eventId == Guid.Empty) return;
            try
            {
                lock (_securityEvents)
                {
                    _securityEvents.RemoveAll(e => e.Id == eventId);
                }
                QueueSecurityEventsChanged();
            }
            catch { }
        }

        public void ClearSecurityEvents()
        {
            try
            {
                lock (_securityEvents) { _securityEvents.Clear(); }
                QueueSecurityEventsChanged();
            }
            catch { }
        }

        public void RemoveNotice(Guid noticeId)
        {
            if (noticeId == Guid.Empty) return;
            try
            {
                lock (_notices)
                {
                    _notices.RemoveAll(n => n.Id == noticeId);
                }
                QueueNoticesChanged();
            }
            catch { }
        }

        public void ClearPersistentGeneralAlerts()
        {
            try
            {
                lock (_notices)
                {
                    _notices.RemoveAll(n => !n.IsMessage && string.IsNullOrWhiteSpace(n.OriginUid) && !n.Title.Contains("Invite", StringComparison.OrdinalIgnoreCase));
                }
                QueueNoticesChanged();
            }
            catch { }
        }

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
                
                // Add all notices to the in-app list so Alerts panel always reflects what user saw.
                lock (_notices)
                {
                    _notices.Add(item);
                }

                // Notify in-app listeners on UI thread
                QueueNoticesChanged();
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
                        var requestedAtUtc = DateTime.UtcNow;
                        _ = PlayToastAudioAsync(item, requestedAtUtc);
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
                QueueNoticesChanged();
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
                QueueNoticesChanged();
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
                QueueNoticesChanged();
                TryWriteUiVerboseLogThrottled("notices.message.update", TimeSpan.FromMilliseconds(1500),
                    () => $"{DateTime.Now:O} [Notices] Message notice added/updated for notification center: {updated.Title}\n");
            }
            if (created && incoming)
            {
                // Determine notification behavior based on presence status
                bool shouldShowToast = false;      // Default: no toast
                bool shouldPlayAudio = false;      // Default: no audio
                bool conversationFocused = false;
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
                        
                        TryWriteUiVerboseLogThrottled("notices.presence.mode", PresenceDecisionLogInterval,
                            () => $"{DateTime.Now:O} [Notices] Presence mode behavior: {presenceMode} → shouldPlayAudio={shouldPlayAudio}, shouldShowToast={shouldShowToast}\n");
                    }
                    catch (Exception ex)
                    {
                        // If we can't determine presence, default to allowing notifications
                        shouldPlayAudio = true;
                        shouldShowToast = true;
                        TryWriteUiVerboseLogThrottled("notices.presence.error", PresenceDecisionLogInterval,
                            () => $"{DateTime.Now:O} [Notices] Error checking presence status: {ex.Message} → defaulting to shouldPlayAudio=true, shouldShowToast=true\n");
                    }

                    conversationFocused = IsConversationFocused(updated.OriginUid);
                    TryWriteUiVerboseLogThrottled("notices.presence.decision", PresenceDecisionLogInterval,
                        () => $"{DateTime.Now:O} [Notices] Presence={presenceMode}, conversationFocused={conversationFocused}, shouldShowToast={shouldShowToast}, shouldPlayAudio={shouldPlayAudio}, origin={updated.OriginUid}\n");

                    // Show message toast only when the message is from a contact that is not currently focused.
                    if (shouldShowToast && !conversationFocused)
                    {
                        var toastBody = string.IsNullOrWhiteSpace(updated.FullBody) ? updated.Body : updated.FullBody;
                        ShowTransientToast(updated.Title, toastBody ?? string.Empty, Models.NotificationType.Information, updated.OriginUid);
                        try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Notices] Desktop toast shown: {updated.Title}\n"); } catch { }
                    }
                    else
                    {
                        string reason = !shouldShowToast ? "suppressed by presence mode" : "conversation is focused";
                        try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Notices] Desktop toast skipped ({reason}): shouldShowToast={shouldShowToast}, conversationFocused={conversationFocused}\n"); } catch { }
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
                        if (!ShouldPlayMessageAudio(updated))
                        {
                            try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.Audio, $"{DateTime.Now:O} [Audio] Message audio deduped: {updated.Title} origin={updated.OriginUid}\n"); } catch { }
                            return updated;
                        }

                        var requestedAtUtc = DateTime.UtcNow;
                        if (conversationFocused)
                        {
                            _ = PlayFocusedConversationMessageAudioAsync(updated, requestedAtUtc);
                        }
                        else
                        {
                            _ = PlayIncomingMessageAudioAsync(updated, requestedAtUtc);
                        }
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

        private bool ShouldPlayMessageAudio(NotificationItem item)
        {
            try
            {
                var now = DateTime.UtcNow;
                var normalizedOrigin = TrimUidPrefix(item.OriginUid ?? string.Empty);
                var messagePart = item.MessageId?.ToString() ?? string.Empty;
                var bodyPart = item.Body ?? string.Empty;
                if (bodyPart.Length > 64) bodyPart = bodyPart.Substring(0, 64);
                var fingerprint = $"{normalizedOrigin}|{messagePart}|{bodyPart}";

                lock (_messageAudioDedupLock)
                {
                    if (string.Equals(_lastMessageAudioFingerprint, fingerprint, StringComparison.Ordinal) &&
                        (now - _lastMessageAudioUtc) <= MessageAudioDedupWindow)
                    {
                        return false;
                    }

                    _lastMessageAudioFingerprint = fingerprint;
                    _lastMessageAudioUtc = now;
                }

                return true;
            }
            catch
            {
                return true;
            }
        }

        private async Task PlayToastAudioAsync(NotificationItem item, DateTime requestedAtUtc)
        {
            try
            {
                switch (item.Type)
                {
                    case Models.NotificationType.Warning:
                        await AppServices.AudioNotifications.PlayCustomSoundAsync("ui-10-smooth-warnnotify-sound-effect-365842.mp3", requestedAtUtc, "NotificationService.Toast.Warning");
                        break;
                    case Models.NotificationType.Information:
                        await AppServices.AudioNotifications.PlayCustomSoundAsync("smooth-notify-alert-toast-warn-274736.mp3", requestedAtUtc, "NotificationService.Toast.Information");
                        break;
                    case Models.NotificationType.Error:
                        await AppServices.AudioNotifications.PlayCustomSoundAsync("smooth-completed-notify-starting-alert-274739.mp3", requestedAtUtc, "NotificationService.Toast.Error");
                        break;
                    default:
                        await AppServices.AudioNotifications.PlaySoundAsync(AudioNotificationService.SoundType.NotificationGeneral, requestedAtUtc, "NotificationService.Toast.General");
                        break;
                }
            }
            catch (Exception ex)
            {
                Utilities.Logger.Log($"NotificationService: Audio notification failed: {ex.Message}");
            }
        }

        private async Task PlayIncomingMessageAudioAsync(NotificationItem updated, DateTime requestedAtUtc)
        {
            try
            {
                try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.Audio, $"{DateTime.Now:O} [Audio] Attempting to play MessageIncoming sound for: {updated.Title}\n"); } catch { }
                await AppServices.AudioNotifications.PlaySoundAsync(AudioNotificationService.SoundType.MessageIncoming, requestedAtUtc, "NotificationService.MessageIncoming.OutOfFocus");
                try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.Audio, $"{DateTime.Now:O} [Audio] Successfully played MessageIncoming sound for: {updated.Title}\n"); } catch { }
            }
            catch (Exception ex)
            {
                Utilities.Logger.Log($"NotificationService: Incoming message audio failed: {ex.Message}");
                try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.Audio, $"{DateTime.Now:O} [Audio] Failed to play MessageIncoming: {ex.Message}\n"); } catch { }
            }
        }

        private async Task PlayFocusedConversationMessageAudioAsync(NotificationItem updated, DateTime requestedAtUtc)
        {
            try
            {
                try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.Audio, $"{DateTime.Now:O} [Audio] Attempting focused-conversation pop sound for: {updated.Title}\n"); } catch { }
                await AppServices.AudioNotifications.PlayCustomSoundAsync("multi-pop-2-188167.mp3", requestedAtUtc, "NotificationService.MessageIncoming.Focused");
                try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.Audio, $"{DateTime.Now:O} [Audio] Successfully played focused-conversation pop sound for: {updated.Title}\n"); } catch { }
            }
            catch (Exception ex)
            {
                Utilities.Logger.Log($"NotificationService: Focused incoming message audio failed: {ex.Message}");
                try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.Audio, $"{DateTime.Now:O} [Audio] Failed focused-conversation pop sound: {ex.Message}\n"); } catch { }
            }
        }

        private bool IsConversationFocused(string? originUid)
        {
            try
            {
                var trimmedOrigin = TrimUidPrefix(originUid ?? string.Empty);
                if (string.IsNullOrWhiteSpace(trimmedOrigin)) return false;

                return Dispatcher.UIThread.Invoke(() =>
                {
                    try
                    {
                        var desktop = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                        var mainWindow = desktop?.MainWindow;
                        if (mainWindow == null) return false;
                        if (!mainWindow.IsVisible) return false;
                        if (mainWindow.WindowState == Avalonia.Controls.WindowState.Minimized) return false;
                        if (!mainWindow.IsActive) return false;

                        if (mainWindow.DataContext is MainWindowViewModel vm)
                        {
                            var selectedUid = vm.SelectedContact?.UID ?? string.Empty;
                            var trimmedSelected = TrimUidPrefix(selectedUid);
                            return string.Equals(trimmedSelected, trimmedOrigin, StringComparison.OrdinalIgnoreCase);
                        }

                        return false;
                    }
                    catch
                    {
                        return false;
                    }
                });
            }
            catch
            {
                return false;
            }
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
                QueueNoticesChanged();
            }
            if (scheduleRemoval) EnqueueMessageRemoval(messageId);
        }

        public void MarkConversationMessageNoticesRead(string originUid)
        {
            if (string.IsNullOrWhiteSpace(originUid)) return;
            var trimmed = TrimUidPrefix(originUid);
            var toRemove = new List<Guid>();
            bool notify = false;
            lock (_notices)
            {
                for (int i = 0; i < _notices.Count; i++)
                {
                    var n = _notices[i];
                    if (n.IsMessage && n.MessageId.HasValue && string.Equals(n.OriginUid, trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        toRemove.Add(n.MessageId.Value);
                        if (n.IsUnread || !n.ReadUtc.HasValue)
                        {
                            _notices[i] = n with { IsUnread = false, ReadUtc = DateTime.UtcNow };
                            notify = true;
                        }
                    }
                }
                if (toRemove.Count == 0) return;
            }
            if (notify)
            {
                QueueNoticesChanged();
            }
            foreach (var id in toRemove)
            {
                EnqueueMessageRemoval(id);
            }
        }

        public void MarkAllMessageNoticesRead()
        {
            var ids = new List<Guid>();
            bool notify = false;
            lock (_notices)
            {
                for (int i = 0; i < _notices.Count; i++)
                {
                    var n = _notices[i];
                    if (n.IsMessage && n.MessageId.HasValue)
                    {
                        ids.Add(n.MessageId.Value);
                        if (n.IsUnread || !n.ReadUtc.HasValue)
                        {
                            _notices[i] = n with { IsUnread = false, ReadUtc = DateTime.UtcNow };
                            notify = true;
                        }
                    }
                }
                if (ids.Count == 0) return;
            }
            if (notify)
            {
                QueueNoticesChanged();
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
                        QueueNoticesChanged();
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

                        TryWriteUiVerboseLogThrottled("toast.create", ToastAnimationLogInterval,
                            () => $"{DateTime.Now:O} [Toast] Creating: beforeCount={beforeCount}\n");

                        // Place window initially off-screen at top-right
                        try { win.Position = new Avalonia.PixelPoint((int)startLeft, (int)startTop); } catch { }
                        win.Show();
                        _activeToastWindows.Add(win);
                        win.Closed += (_, __) => RemoveToastWindow(win);
                        TryWriteUiVerboseLogThrottled("toast.added", ToastAnimationLogInterval,
                            () => $"{DateTime.Now:O} [Toast] Added to list: now {_activeToastWindows.Count} active\n");
                        
                        // Delay reflow slightly to allow window to measure with SizeToContent, then animate
                        Dispatcher.UIThread.Post(() => 
                        {
                            ReflowToastPositionsCore();
                            
                            // Get the target position after reflow
                            var targetPos = win.Position;
                            
                            // Start slide-in animation from off-screen
                            var durationMs = 320.0;
                            var start = DateTime.UtcNow;
                            TryWriteUiVerboseLogThrottled("toast.anim.start", ToastAnimationLogInterval,
                                () => $"{DateTime.Now:O} [Toast] Animation: start=({startLeft},{targetPos.Y}) -> target=({targetLeft},{targetPos.Y})\n");
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
                                        TryWriteUiVerboseLogThrottled("toast.anim.complete", ToastAnimationLogInterval,
                                            () => $"{DateTime.Now:O} [Toast] Animation complete: final=({(int)left},{targetPos.Y})\n");
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

                        // Auto close after timeout (with slide-out) - keep on UI thread to avoid extra dispatch hops.
                        var durationSeconds = 4.5; // Default fallback
                        try { durationSeconds = Math.Clamp(AppServices.Settings.Settings.NotificationDurationSeconds, 0.5, 30.0); } catch { }
                        DispatcherTimer.RunOnce(() =>
                        {
                            try
                            {
                                var outStart = DateTime.UtcNow;
                                var outDur = 260.0;
                                var currentY = win.Position.Y;
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
                                        win.Position = new Avalonia.PixelPoint((int)left, currentY);
                                        if (tt >= 1.0)
                                        {
                                            outTimer.Stop();
                                            try { win.Close(); } catch { }
                                        }
                                    }
                                    catch { }
                                };
                                outTimer.Start();
                            }
                            catch { }
                        }, TimeSpan.FromSeconds(durationSeconds));
                    }
                    catch { }
                }, Avalonia.Threading.DispatcherPriority.Normal);
            }
            catch { }
        }
    }
}

