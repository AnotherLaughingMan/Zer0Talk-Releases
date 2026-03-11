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
    public partial class NotificationService
    {
    public sealed record NotificationItem(Guid Id, string Title, string Body, string? OriginUid, DateTime Utc, string? FullBody = null, bool IsUnread = false, bool IsMessage = false, bool IsIncoming = false, Guid? MessageId = null, DateTime? ReadUtc = null, bool IsPersistent = true, Models.NotificationType? Type = null, bool IsPriority = false, bool IsMention = false);
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
    private static readonly FontFamily SegoeFluentIconsFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
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

    private void TryWriteNetworkLogThrottled(string key, TimeSpan minInterval, Func<string> messageFactory)
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
            Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.Network, messageFactory());
        }
        catch { }
    }

    private void TryWriteNetworkVerboseLogThrottled(string key, TimeSpan minInterval, Func<string> messageFactory)
    {
        if (!VerboseUiLogs) return;
        TryWriteNetworkLogThrottled(key, minInterval, messageFactory);
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
            Models.NotificationType.Update => "\uE895",
            Models.NotificationType.Success => "\uE73E",
            Models.NotificationType.Information => "\uE946",
            _ => "\uE946"
        };
    }

    private Control CreateToastContent(Window host, string? title, string text, Models.NotificationType? type = null, string? originUid = null, Guid? messageId = null)
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
            case Models.NotificationType.Update:
                borderBrush = ToastBorderMessageBrush; // Blue for update notices
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

        // Add quick actions for message toasts (but not for invites).
        var isInvite = resolvedTitle.Contains("Invite", StringComparison.OrdinalIgnoreCase);
        if (hasOrigin && !isInvite)
        {
            var actions = new WrapPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 8, 0, 0),
                ItemHeight = 30,
                ItemWidth = 0
            };

            var goToChatButton = new Button
            {
                Content = Services.AppServices.Localization.GetString("Notifications.GoToChat", "Go to Chat"),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
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

            var replyButton = new Button
            {
                Content = Services.AppServices.Localization.GetString("Notifications.Reply", "Reply"),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Padding = new Thickness(12, 6),
                Background = ToastButtonLightBackgroundBrush,
                Foreground = ToastButtonDarkForegroundBrush,
                BorderBrush = Brushes.Transparent,
                CornerRadius = new CornerRadius(4),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(6, 0, 0, 0)
            };
            replyButton.Click += (_, __) =>
            {
                try
                {
                    var fullUid = originUid?.StartsWith("usr-") == true ? originUid : $"usr-{originUid}";
                    AppServices.Events.RaiseOpenConversationRequested(fullUid);
                    try { ReplyRequested?.Invoke(fullUid, messageId); } catch { }
                    host.Close();
                }
                catch { }
            };

            var muteButton = new Button
            {
                Content = Services.AppServices.Localization.GetString("Notifications.Mute1h", "Mute 1h"),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Padding = new Thickness(12, 6),
                Background = ToastButtonLightBackgroundBrush,
                Foreground = ToastButtonDarkForegroundBrush,
                BorderBrush = Brushes.Transparent,
                CornerRadius = new CornerRadius(4),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(6, 0, 0, 0)
            };
            muteButton.Click += (_, __) =>
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(originUid))
                    {
                        MuteContactTemporarily(originUid, TimeSpan.FromHours(1));
                    }
                    host.Close();
                }
                catch { }
            };

            var markReadButton = new Button
            {
                Content = Services.AppServices.Localization.GetString("Notifications.MarkRead", "Mark read"),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Padding = new Thickness(12, 6),
                Background = ToastButtonLightBackgroundBrush,
                Foreground = ToastButtonDarkForegroundBrush,
                BorderBrush = Brushes.Transparent,
                CornerRadius = new CornerRadius(4),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(6, 0, 0, 0),
                IsVisible = messageId.HasValue
            };
            markReadButton.Click += (_, __) =>
            {
                try
                {
                    if (messageId.HasValue)
                    {
                        MarkMessageNoticeRead(messageId.Value);
                    }
                    host.Close();
                }
                catch { }
            };

            actions.Children.Add(goToChatButton);
            actions.Children.Add(replyButton);
            actions.Children.Add(muteButton);
            actions.Children.Add(markReadButton);

            Grid.SetRow(actions, 2);
            Grid.SetColumn(actions, 0);
            Grid.SetColumnSpan(actions, 3);
            grid.Children.Add(actions);
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
        public event Action<string, Guid?>? ReplyRequested;

        private static bool IsWithinQuietHours(Models.AppSettings settings, DateTime localNow)
        {
            if (!settings.NotificationQuietHoursEnabled) return false;
            var start = Math.Clamp(settings.NotificationQuietHoursStartHour, 0, 23);
            var end = Math.Clamp(settings.NotificationQuietHoursEndHour, 0, 23);
            var hour = localNow.Hour;

            if (start == end) return true;
            if (start < end)
            {
                return hour >= start && hour < end;
            }

            // Overnight window, e.g. 22 -> 07.
            return hour >= start || hour < end;
        }

        private static void MuteContactTemporarily(string originUid, TimeSpan duration)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(originUid)) return;
                var uid = originUid.StartsWith("usr-", StringComparison.OrdinalIgnoreCase) ? originUid : $"usr-{originUid}";
                if (!AppServices.Contacts.SetMuteNotifications(uid, true, AppServices.Passphrase)) return;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(duration).ConfigureAwait(false);
                        AppServices.Contacts.SetMuteNotifications(uid, false, AppServices.Passphrase);
                    }
                    catch { }
                });
            }
            catch { }
        }

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
                    _notices.RemoveAll(n => !n.IsMessage && !n.Title.Contains("Invite", StringComparison.OrdinalIgnoreCase));
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
                    Models.NotificationType.Update => AppServices.Localization.GetString("Notifications.Update", "Update"),
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
                        ShowTransientToast(item.Title, item.Body, item.Type, item.OriginUid, null);
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

    }
}
