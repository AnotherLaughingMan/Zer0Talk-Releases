/*
    Simple service locator for singletons and shared state.
    - Holds global Passphrase set after unlock/account creation.
    - Provides access to Settings, Theme, Network, and other services.
*/
// TODO[ANCHOR]: AppServices - Global services and passphrase state
using System;
using Zer0Talk.Containers;
using System.Linq;
using Models = Zer0Talk.Models;
using Zer0Talk.Utilities;

namespace Zer0Talk.Services;

public static partial class AppServices
{
    private static void LogGuardedFailure(string operation, Exception ex)
    {
        try
        {
            Logger.Warning($"{operation} failed: {ex.Message}", source: nameof(AppServices), categoryOverride: "app");
        }
        catch { }
    }

    // True only while the app is intentionally shutting down (for example, tray Exit)
    // so main-window close logic does not route into minimize-to-tray behavior.
    public static bool IsShutdownRequested { get; set; }

    // Passphrase is set by LockService.Unlock() after validation against the account
    public static string Passphrase { get; set; } = string.Empty;
    public static SettingsService Settings { get; } = new(new P2EContainer());
    public static IdentityService Identity { get; } = new();
    public static NatTraversalService Nat { get; } = new();
    public static NetworkService Network { get; } = new(Identity, Nat);
    public static WanDirectoryService WanDirectory { get; } = new(Settings, Identity, Network, Nat);
    public static DialogService Dialogs { get; } = new();
    public static AccountManager Accounts { get; } = new();
    public static ContactManager Contacts { get; } = new();
    public static PeerManager Peers { get; } = new(Settings);
    public static PeersStore PeersStore { get; } = new();
    public static ContactRequestsService ContactRequests { get; } = new(Identity, Network, Settings, Contacts, Dialogs);
    public static UpdateManager Updates { get; } = new();
    public static EventHub Events { get; } = new();
    public static ThemeService Theme { get; } = new();
    public static ThemeEngine ThemeEngine { get; } = new(Theme);
    public static RegressionGuard Guard { get; } = new(Settings, Network, Nat);
    public static DiscoveryService Discovery { get; } = new(Settings, Network, Nat);
    public static RetentionService Retention { get; } = new();
    public static PresenceRefreshService PresenceRefresh { get; } = new();
    public static LinkPreviewService LinkPreview { get; } = new();
    public static OutboxService Outbox { get; } = new();
    public static LogMaintenanceService LogMaintenance { get; } = new(Settings, Updates);
    public static TrayIconService TrayIcon { get; } = new();
    public static NotificationService Notifications { get; } = new();
    public static AudioNotificationService AudioNotifications => AudioNotificationService.Instance;
    public static IpBlockingService IpBlocking { get; } = new(Settings);
    public static LocalizationService Localization { get; } = new();
    public static AutoUpdateService AutoUpdate { get; } = new();
    // Centralized UI pulse key; interval can be adjusted via Settings if desired
    private const string UiPulseKey = "App.UI.Pulse";
    private static readonly object PresenceWorkGate = new();
    private static readonly System.Collections.Generic.HashSet<string> PresenceWorkInFlight = new(System.StringComparer.OrdinalIgnoreCase);
    private static readonly System.Collections.Generic.Dictionary<string, System.DateTime> PresenceLastProcessedUtc = new(System.StringComparer.OrdinalIgnoreCase);
    private static readonly System.Collections.Generic.Dictionary<string, System.DateTime> PresenceConnectLastAttemptUtc = new(System.StringComparer.OrdinalIgnoreCase);
    private static readonly System.Collections.Generic.Dictionary<string, System.DateTime> OutboxDrainLastAttemptUtc = new(System.StringComparer.OrdinalIgnoreCase);
    private static readonly System.Threading.SemaphoreSlim PresenceConnectLimiter = new(3, 3);
    private static readonly System.Threading.SemaphoreSlim OutboxDrainLimiter = new(2, 2);
    private static readonly System.TimeSpan PresenceEventDebounce = System.TimeSpan.FromSeconds(2);
    private static readonly System.TimeSpan PresenceConnectDebounce = System.TimeSpan.FromSeconds(5);
    private static readonly System.TimeSpan OutboxDrainDebounce = System.TimeSpan.FromSeconds(3);
    private static long _presenceEventsSeen;
    private static long _presenceEventsExecuted;
    private static long _presenceEventsCoalesced;
    private static long _presenceConnectAttempts;
    private static long _presenceOutboxQueued;
    private static long _presenceOutboxSkipped;
    // Email verification removed in keypair-based identity model

    public readonly record struct PresencePipelineSnapshot(
        long Seen,
        long Executed,
        long Coalesced,
        long ConnectAttempts,
        long OutboxQueued,
        long OutboxSkipped,
        int InFlight);

    public static PresencePipelineSnapshot GetPresencePipelineSnapshot()
    {
        int inFlight;
        lock (PresenceWorkGate) { inFlight = PresenceWorkInFlight.Count; }
        return new PresencePipelineSnapshot(
            Seen: System.Threading.Interlocked.Read(ref _presenceEventsSeen),
            Executed: System.Threading.Interlocked.Read(ref _presenceEventsExecuted),
            Coalesced: System.Threading.Interlocked.Read(ref _presenceEventsCoalesced),
            ConnectAttempts: System.Threading.Interlocked.Read(ref _presenceConnectAttempts),
            OutboxQueued: System.Threading.Interlocked.Read(ref _presenceOutboxQueued),
            OutboxSkipped: System.Threading.Interlocked.Read(ref _presenceOutboxSkipped),
            InFlight: inFlight);
    }

    private static string NormalizePeerUid(string uid)
    {
        if (string.IsNullOrWhiteSpace(uid)) return string.Empty;
        return uid.StartsWith("usr-", System.StringComparison.Ordinal) && uid.Length > 4 ? uid.Substring(4) : uid;
    }

    private static bool TryBeginPresenceWork(string uid)
    {
        var now = System.DateTime.UtcNow;
        lock (PresenceWorkGate)
        {
            if (PresenceWorkInFlight.Contains(uid))
            {
                System.Threading.Interlocked.Increment(ref _presenceEventsCoalesced);
                return false;
            }
            if (PresenceLastProcessedUtc.TryGetValue(uid, out var lastUtc) && (now - lastUtc) < PresenceEventDebounce)
            {
                System.Threading.Interlocked.Increment(ref _presenceEventsCoalesced);
                return false;
            }
            PresenceLastProcessedUtc[uid] = now;
            PresenceWorkInFlight.Add(uid);
            System.Threading.Interlocked.Increment(ref _presenceEventsExecuted);
            return true;
        }
    }

    private static void EndPresenceWork(string uid)
    {
        lock (PresenceWorkGate)
        {
            PresenceWorkInFlight.Remove(uid);
        }
    }

    private static bool ShouldAttemptPeerConnect(string uid)
    {
        var now = System.DateTime.UtcNow;
        lock (PresenceWorkGate)
        {
            if (PresenceConnectLastAttemptUtc.TryGetValue(uid, out var lastUtc) && (now - lastUtc) < PresenceConnectDebounce)
            {
                return false;
            }
            PresenceConnectLastAttemptUtc[uid] = now;
            return true;
        }
    }

    private static bool ShouldAttemptOutboxDrain(string uid)
    {
        var now = System.DateTime.UtcNow;
        lock (PresenceWorkGate)
        {
            if (OutboxDrainLastAttemptUtc.TryGetValue(uid, out var lastUtc) && (now - lastUtc) < OutboxDrainDebounce)
            {
                return false;
            }
            OutboxDrainLastAttemptUtc[uid] = now;
            return true;
        }
    }

    private static void QueuePeerOutboxDrain(string uid)
    {
        var normalizedUid = NormalizePeerUid(uid);
        if (string.IsNullOrWhiteSpace(normalizedUid)) return;
        if (!ShouldAttemptOutboxDrain(normalizedUid))
        {
            System.Threading.Interlocked.Increment(ref _presenceOutboxSkipped);
            return;
        }
        System.Threading.Interlocked.Increment(ref _presenceOutboxQueued);

        System.Threading.Tasks.Task.Run(async () =>
        {
            await OutboxDrainLimiter.WaitAsync().ConfigureAwait(false);
            try
            {
                await Outbox.DrainAsync(normalizedUid, Passphrase, System.Threading.CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogGuardedFailure($"Outbox.DrainAsync({normalizedUid})", ex);
            }
            finally
            {
                try { OutboxDrainLimiter.Release(); } catch { }
            }
        });
    }

    // Convenience helper for viewmodels to cancel queued messages by id
    public static void OutboxCancelIfQueued(string peerUid, System.Guid messageId, string passphrase)
    {
        try { Outbox.CancelQueued(peerUid, messageId, passphrase); }
        catch (Exception ex) { LogGuardedFailure($"Outbox.CancelQueued({peerUid}, {messageId})", ex); }
    }

    // Update queued message content if still pending
    public static void OutboxUpdateIfQueued(string peerUid, System.Guid messageId, string newContent, string passphrase)
    {
        try { Outbox.UpdateQueued(peerUid, messageId, newContent, passphrase); }
        catch (Exception ex) { LogGuardedFailure($"Outbox.UpdateQueued({peerUid}, {messageId})", ex); }
    }

    public static void OutboxQueueEdit(string peerUid, System.Guid messageId, string newContent, string passphrase)
    {
        try { Outbox.EnqueueEdit(peerUid, messageId, newContent, passphrase); }
        catch (Exception ex) { LogGuardedFailure($"Outbox.EnqueueEdit({peerUid}, {messageId})", ex); }
    }

    public static void PeersClearTransientStatuses()
    {
        try { Peers.ClearTransientStatuses(); }
        catch (Exception ex) { LogGuardedFailure("Peers.ClearTransientStatuses", ex); }
    }

    // Apply remote edit to local message store and notify UI where applicable
    public static void MessagesUpdateFromRemote(string peerUid, System.Guid messageId, string newContent)
    {
        try
        {
            var mc = new Zer0Talk.Containers.MessageContainer();
            mc.UpdateMessage(peerUid, messageId, newContent, Passphrase);
            // Raise event so UI can refresh if this conversation is currently visible
            Events.RaiseMessageEdited(peerUid, messageId, newContent);
        }
        catch (Exception ex)
        {
            LogGuardedFailure($"MessagesUpdateFromRemote({peerUid}, {messageId})", ex);
        }
    }

    public static void MessagesDeleteFromRemote(string peerUid, System.Guid messageId)
    {
        try
        {
            var mc = new Zer0Talk.Containers.MessageContainer();
            mc.DeleteMessage(peerUid, messageId, Passphrase);
            try
            {
                if (Zer0Talk.Utilities.LoggingPaths.Enabled)
                {
                    var line = $"[RETENTION] {System.DateTime.Now:O}: Remote delete peer={peerUid} id={messageId}";
                    System.IO.File.AppendAllText(Zer0Talk.Utilities.LoggingPaths.Retention, line + System.Environment.NewLine);
                }
            }
            catch { }
            // Raise event so UI can refresh if this conversation is currently visible
            Events.RaiseMessageDeleted(peerUid, messageId);
        }
        catch (Exception ex)
        {
            LogGuardedFailure($"MessagesDeleteFromRemote({peerUid}, {messageId})", ex);
        }
    }

}
