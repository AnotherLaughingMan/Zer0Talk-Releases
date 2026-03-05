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
    public partial class NotificationService
    {
        public NotificationItem AddOrUpdateMessageNotice(string title, string body, string? originUid, Guid messageId, bool incoming, DateTime? timestamp = null, bool isUnread = true, bool isPriority = false, bool isMention = false)
        {
            if (messageId == Guid.Empty) messageId = Guid.NewGuid();
            var trimmedOrigin = TrimUidPrefix(originUid ?? string.Empty);
            var messageTime = timestamp ?? DateTime.UtcNow;
            var formattedTime = messageTime.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
            var titleWithTime = $"{title} • {formattedTime}";

            // Determine presence-based notification behavior FIRST (before adding to notices)
            // Discord-like rules:
            //   Online     = Sound + toast + message center
            //   Away/Idle  = Sound + toast + message center
            //   DND        = No sound, no toast, message center (persistent, no auto-dismiss)
            //   Invisible  = Sound + toast + message center
            //   Offline    = No sound, no toast, message center (persistent)
            bool shouldShowToast = false;
            bool shouldPlayAudio = false;
            bool shouldAddToCenter = true;  // default: add to notification center
            bool makePersistent = false;    // DND messages don't auto-dismiss
            bool toastOnlyWhenAppInactive = false;
            string presenceMode = "Unknown";

            if (incoming)
            {
                try
                {
                    var settings = AppServices.Settings.Settings;
                    presenceMode = settings.Status.ToString();

                    switch (settings.Status)
                    {
                        case Models.PresenceStatus.Online:
                            shouldPlayAudio = true;
                            shouldShowToast = true;
                            shouldAddToCenter = true;
                            toastOnlyWhenAppInactive = true;
                            break;

                        case Models.PresenceStatus.Idle:
                            shouldPlayAudio = true;
                            shouldShowToast = true;
                            shouldAddToCenter = true;
                            toastOnlyWhenAppInactive = true;
                            break;

                        case Models.PresenceStatus.DoNotDisturb:
                            shouldPlayAudio = false;
                            shouldShowToast = false;
                            shouldAddToCenter = true;
                            makePersistent = true;
                            break;

                        case Models.PresenceStatus.Invisible:
                            shouldPlayAudio = true;
                            shouldShowToast = true;
                            shouldAddToCenter = true;
                            toastOnlyWhenAppInactive = true;
                            break;

                        case Models.PresenceStatus.Offline:
                            shouldPlayAudio = false;
                            shouldShowToast = false;
                            shouldAddToCenter = true;
                            makePersistent = true;
                            break;

                        default:
                            shouldPlayAudio = true;
                            shouldShowToast = true;
                            shouldAddToCenter = true;
                            break;
                    }

                    TryWriteUiVerboseLogThrottled("notices.presence.mode", PresenceDecisionLogInterval,
                        () => $"{DateTime.Now:O} [Notices] Presence mode: {presenceMode} → audio={shouldPlayAudio}, toast={shouldShowToast}, center={shouldAddToCenter}, persistent={makePersistent}\n");

                    // Quiet hours: suppress non-priority/non-mention interruptions while still keeping inbox entries.
                    var nowLocal = DateTime.Now;
                    if (IsWithinQuietHours(settings, nowLocal))
                    {
                        var allowPriority = settings.NotificationQuietHoursAllowPriority;
                        var allowMention = settings.NotificationQuietHoursAllowMentions;
                        var bypass = (isPriority && allowPriority) || (isMention && allowMention);
                        if (!bypass)
                        {
                            shouldPlayAudio = false;
                            shouldShowToast = false;
                            shouldAddToCenter = true;
                            makePersistent = true;
                            TryWriteUiLogThrottled("notices.quiet-hours.suppress", TimeSpan.FromMilliseconds(1500),
                                () => $"{DateTime.Now:O} [Notices] Quiet hours suppression active. priority={isPriority} mention={isMention}\n");
                        }
                    }
                }
                catch (Exception ex)
                {
                    shouldPlayAudio = true;
                    shouldShowToast = false;
                    shouldAddToCenter = false;
                    TryWriteUiVerboseLogThrottled("notices.presence.error", PresenceDecisionLogInterval,
                        () => $"{DateTime.Now:O} [Notices] Error checking presence: {ex.Message} → defaulting to Online behavior\n");
                }
            }

            // Build the notification item; only add to _notices if shouldAddToCenter or outgoing
            NotificationItem updated;
            bool notify = false;
            bool addedToCenter = !incoming || shouldAddToCenter;

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
                        ReadUtc = readUtc,
                        IsPersistent = makePersistent || existing.IsPersistent,
                        IsPriority = isPriority || existing.IsPriority,
                        IsMention = isMention || existing.IsMention
                    };
                    _notices[index] = updated;
                    notify = true;
                }
                else if (addedToCenter)
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
                        isUnread ? null : DateTime.UtcNow,
                        IsPersistent: makePersistent,
                        IsPriority: isPriority,
                        IsMention: isMention);
                    _notices.Add(updated);
                    notify = true;
                }
                else
                {
                    // Not adding to center — create a detached item for return value / audio purposes
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
                        isUnread ? null : DateTime.UtcNow,
                        IsPriority: isPriority,
                        IsMention: isMention);
                }
            }
            if (notify)
            {
                QueueNoticesChanged();
                TryWriteUiVerboseLogThrottled("notices.message.update", TimeSpan.FromMilliseconds(1500),
                    () => $"{DateTime.Now:O} [Notices] Message notice {(addedToCenter ? "added to center" : "skipped center")}: {updated.Title}\n");
            }
            if (incoming)
            {
                bool conversationFocused = false;
                bool appIsActive = false;
                try
                {
                    conversationFocused = IsConversationFocused(updated.OriginUid);
                    appIsActive = IsMainWindowActive();
                    TryWriteUiVerboseLogThrottled("notices.presence.decision", PresenceDecisionLogInterval,
                        () => $"{DateTime.Now:O} [Notices] Presence={presenceMode}, focused={conversationFocused}, appActive={appIsActive}, toast={shouldShowToast}, audio={shouldPlayAudio}, center={shouldAddToCenter}, origin={updated.OriginUid}\n");

                    // Show toast only for Away/Idle when conversation is not focused
                    if (shouldShowToast && !conversationFocused && (!toastOnlyWhenAppInactive || !appIsActive))
                    {
                        var toastBody = string.IsNullOrWhiteSpace(updated.FullBody) ? updated.Body : updated.FullBody;
                        ShowTransientToast(updated.Title, toastBody ?? string.Empty, Models.NotificationType.Information, updated.OriginUid, updated.MessageId);
                        try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Notices] Desktop toast shown: {updated.Title}\n"); } catch { }
                    }
                    else
                    {
                        string reason = !shouldShowToast
                            ? $"suppressed ({presenceMode} mode)"
                            : conversationFocused
                                ? "conversation is focused"
                                : (toastOnlyWhenAppInactive && appIsActive)
                                    ? "app is active"
                                    : "toast gate not met";
                        try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Notices] Toast skipped ({reason})\n"); } catch { }
                    }
                }
                catch { }

                // Play audio: Online/Idle/Invisible hear sound, DND/Offline are silent
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
                        try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.Audio, $"{DateTime.Now:O} [Audio] Audio skipped ({presenceMode} mode): {updated.Title}\n"); } catch { }
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
                    case Models.NotificationType.Update:
                        await AppServices.AudioNotifications.PlayCustomSoundAsync("smooth-notify-alert-toast-warn-274736.mp3", requestedAtUtc, "NotificationService.Toast.Update");
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

        private bool IsMainWindowActive()
        {
            try
            {
                return Dispatcher.UIThread.Invoke(() =>
                {
                    try
                    {
                        var desktop = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                        var mainWindow = desktop?.MainWindow;
                        if (mainWindow == null) return false;
                        if (!mainWindow.IsVisible) return false;
                        if (mainWindow.WindowState == Avalonia.Controls.WindowState.Minimized) return false;
                        return mainWindow.IsActive;
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

        /// <summary>Returns the count of unread message notices for a specific peer.</summary>
        public int GetUnreadCountForPeer(string originUid)
        {
            if (string.IsNullOrWhiteSpace(originUid)) return 0;
            var trimmed = TrimUidPrefix(originUid);
            lock (_notices)
            {
                int count = 0;
                for (int i = 0; i < _notices.Count; i++)
                {
                    var n = _notices[i];
                    if (n.IsMessage && n.IsUnread && string.Equals(n.OriginUid, trimmed, StringComparison.OrdinalIgnoreCase))
                        count++;
                }
                return count;
            }
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

        private void ShowTransientToast(string title, string text, Models.NotificationType? type = null, string? originUid = null, Guid? messageId = null)
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
                        win.Content = CreateToastContent(win, title, text, type, originUid, messageId);

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
