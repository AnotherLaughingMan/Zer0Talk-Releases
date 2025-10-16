using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using ZTalk.Utilities;

using System.Runtime.InteropServices;
using Avalonia;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;

namespace ZTalk.Services
{
    // Lightweight cross-platform app notification hub used by views and services.
    // - Stores in-app notices (for Notification Center in UI)
    // - Publishes OS notifications when available (Windows implementation here)
    public class NotificationService
    {
    public sealed record NotificationItem(Guid Id, string Title, string Body, string? OriginUid, DateTime Utc, string? FullBody = null, bool IsUnread = false, bool IsMessage = false, bool IsIncoming = false, Guid? MessageId = null, DateTime? ReadUtc = null);

    private readonly List<NotificationItem> _notices = new();
    private readonly object _removalLock = new();
    private readonly Queue<Guid> _messageRemovalQueue = new();
    private readonly HashSet<Guid> _messageRemovalPending = new();
    private bool _messageRemovalWorkerRunning;

    public IReadOnlyList<NotificationItem> Notices => _notices.AsReadOnly();

        public event Action? NoticesChanged;

        // Backwards-compatible convenience: post a simple notice with combined text
        public void PostNotice(string text)
        {
            PostNotice(title: string.Empty, body: text, originUid: null, fullBody: text);
        }

        // Structured notice post with optional origin UID (used for click-to-open)
        public void PostNotice(string title, string body, string? originUid = null, string? fullBody = null)
        {
            try
            {
                var item = new NotificationItem(Guid.NewGuid(), title ?? string.Empty, body ?? string.Empty, originUid, DateTime.UtcNow, fullBody);
                lock (_notices)
                {
                    _notices.Add(item);
                }

                // Notify in-app listeners on UI thread
                Dispatcher.UIThread.Post(() => { try { NoticesChanged?.Invoke(); } catch { } });
                try { if (Utilities.LoggingPaths.Enabled) ZTalk.Utilities.LoggingPaths.TryWrite(ZTalk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Notices] Posted: {item.Title} | {item.Body} origin={item.OriginUid}\n"); } catch { }

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
                            try { if (Utilities.LoggingPaths.Enabled) ZTalk.Utilities.LoggingPaths.TryWrite(ZTalk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Notices] Toast suppressed (DND): {item.Title} | {item.Body} origin={item.OriginUid}\n"); } catch { }
                        }
                    }
                    catch { }

                    if (shouldShowToast)
                    {
                        ShowTransientToast(item.Title, item.Body, item.OriginUid);
                        try { if (Utilities.LoggingPaths.Enabled) ZTalk.Utilities.LoggingPaths.TryWrite(ZTalk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Notices] Transient shown: {item.Title} | {item.Body} origin={item.OriginUid}\n"); } catch { }
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
                            try { if (Utilities.LoggingPaths.Enabled) ZTalk.Utilities.LoggingPaths.TryWrite(ZTalk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Notices] Audio suppressed (DND): {item.Title} | {item.Body}\n"); } catch { }
                        }
                    }
                    catch { }

                    if (shouldPlayAudio)
                    {
                        _ = System.Threading.Tasks.Task.Run(async () =>
                        {
                            try
                            {
                                await AppServices.AudioNotifications.PlaySoundAsync(AudioNotificationService.SoundType.NotificationGeneral);
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

        public NotificationItem AddOrUpdateMessageNotice(string title, string body, string? originUid, Guid messageId, bool incoming, bool isUnread = true)
        {
            if (messageId == Guid.Empty) messageId = Guid.NewGuid();
            var trimmedOrigin = TrimUidPrefix(originUid ?? string.Empty);
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
                        Title = string.IsNullOrWhiteSpace(title) ? existing.Title : title,
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
                        title ?? string.Empty,
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
                Dispatcher.UIThread.Post(() => { try { NoticesChanged?.Invoke(); } catch { } });
            }
            if (created && incoming)
            {
                try
                {
                    // Check if notifications should be suppressed in Do Not Disturb mode
                    bool shouldShowToast = true;
                    try
                    {
                        var settings = AppServices.Settings.Settings;
                        if (settings.SuppressNotificationsInDnd && settings.Status == Models.PresenceStatus.DoNotDisturb)
                        {
                            shouldShowToast = false;
                            try { if (Utilities.LoggingPaths.Enabled) ZTalk.Utilities.LoggingPaths.TryWrite(ZTalk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Notices] Message toast suppressed (DND): {updated.Title} | {updated.Body} origin={updated.OriginUid}\n"); } catch { }
                        }
                    }
                    catch { }

                    if (shouldShowToast)
                    {
                        var toastBody = string.IsNullOrWhiteSpace(updated.FullBody) ? updated.Body : updated.FullBody;
                        ShowTransientToast(updated.Title, toastBody ?? string.Empty, updated.OriginUid);
                    }
                }
                catch { }

                // Play incoming message sound (unless suppressed in DND mode)
                try
                {
                    bool shouldPlayAudio = true;
                    try
                    {
                        var settings = AppServices.Settings.Settings;
                        if (settings.SuppressNotificationsInDnd && settings.Status == Models.PresenceStatus.DoNotDisturb)
                        {
                            shouldPlayAudio = false;
                            try { if (Utilities.LoggingPaths.Enabled) ZTalk.Utilities.LoggingPaths.TryWrite(ZTalk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Notices] Message audio suppressed (DND): {updated.Title} | {updated.Body} origin={updated.OriginUid}\n"); } catch { }
                        }
                    }
                    catch { }

                    if (shouldPlayAudio)
                    {
                        _ = System.Threading.Tasks.Task.Run(async () =>
                        {
                            try
                            {
                                await AppServices.AudioNotifications.PlaySoundAsync(AudioNotificationService.SoundType.MessageIncoming);
                            }
                            catch (Exception ex)
                            {
                                Utilities.Logger.Log($"NotificationService: Incoming message audio failed: {ex.Message}");
                            }
                        });
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

        private void ShowTransientToast(string title, string text, string? originUid)
        {
            try
            {
                Dispatcher.UIThread.Post(async () =>
                {
                    try
                    {
                        var win = new Avalonia.Controls.Window
                        {
                            Width = 360,
                            Height = 80,
                            CanResize = false,
                            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.Manual,
                            Topmost = true,
                            ShowInTaskbar = false,
                            Content = new Avalonia.Controls.Border
                            {
                                Padding = new Avalonia.Thickness(8),
                                Background = (Avalonia.Media.IBrush?)Avalonia.Application.Current?.FindResource("App.Surface"),
                                Child = new Avalonia.Controls.StackPanel
                                {
                                    Spacing = 6,
                                    Children =
                                    {
                                        new Avalonia.Controls.TextBlock { Text = string.IsNullOrWhiteSpace(title) ? "ZTalk" : title, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                                        new Avalonia.Controls.TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap }
                                    }
                                }
                            }
                        };

                        // Compute top-right slide-in start/target positions
                        // Attempt to compute working area from main window's screens if available
                        Avalonia.PixelRect area;
                        try
                        {
                            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                            var mainWin = lifetime?.MainWindow;
                            if (mainWin != null && mainWin.Screens?.Primary != null) area = mainWin.Screens.Primary.WorkingArea; else area = new Avalonia.PixelRect(0, 0, 1280, 720);
                        }
                        catch
                        {
                            area = new Avalonia.PixelRect(0, 0, 1280, 720);
                        }
                        var margin = 12;
                        var targetLeft = area.Right - (int)win.Width - margin;
                        var targetTop = area.Y + margin;
                        var startLeft = area.Right + margin; // start off-screen to right

                        // Place window initially off-screen at top-right
                        try { win.Position = new Avalonia.PixelPoint((int)startLeft, (int)targetTop); } catch { }
                        win.Show();

                        // Slide-in animation (over ~300ms)
                        var durationMs = 320.0;
                        var start = DateTime.UtcNow;
                        var end = start.AddMilliseconds(durationMs);
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
                                try { win.Position = new Avalonia.PixelPoint((int)left, (int)targetTop); } catch { }
                                if (t >= 1.0)
                                {
                                    timer.Stop();
                                }
                            }
                            catch { }
                        };
                        timer.Start();

                        // Mouse/click handler: open conversation and dismiss
                        win.PointerReleased += (_, __) =>
                        {
                            try
                            {
                                if (!string.IsNullOrWhiteSpace(originUid))
                                {
                                    try { AppServices.Events.RaiseOpenConversationRequested(originUid); } catch { }
                                }
                                try { win.Close(); } catch { }
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
                                        Dispatcher.UIThread.Post(() => { try { win.Position = new Avalonia.PixelPoint((int)left, (int)targetTop); } catch { } });
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
