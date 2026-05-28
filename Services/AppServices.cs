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
    public static ContactsBridgeService ContactsBridge { get; } = new(Contacts, Notifications);
    public static ContactsIpcEndpointService ContactsIpcEndpoint { get; } = new(ContactsBridge);
    public static UnreadStateService UnreadState { get; } = new(Notifications);
    public static UnreadBridgeService UnreadBridge { get; } = new(UnreadState, Events);
    public static UnreadIpcEndpointService UnreadIpcEndpoint { get; } = new(UnreadBridge, Events);
    public static MarkdownComposerStateService MarkdownComposerState { get; } = new();
    public static MarkdownIpcEndpointService MarkdownIpcEndpoint { get; } = new(MarkdownComposerState);
    public static HybridIpcHostService HybridIpcHost { get; } = new(ContactsIpcEndpoint, UnreadIpcEndpoint, MarkdownIpcEndpoint);
    public static HybridShellIpcClientService HybridShellIpcClient { get; } = new();
    public static HybridShellConsumerService HybridShellConsumer { get; } = new(HybridShellIpcClient);
    public static HybridShellAdapterService HybridShellAdapter { get; } = new(HybridShellConsumer);
    public static HybridShellIpcClientService HybridShellMarkdownIpcClient { get; } = new();
    public static HybridShellMarkdownAdapterService HybridShellMarkdownAdapter { get; } = new(HybridShellMarkdownIpcClient);
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
        return UidNormalization.TrimPrefix(uid ?? string.Empty);
    }

    public static void StartHybridIpcHostIfEnabled()
    {
        try
        {
            if (Settings.Settings.EnableHybridIpcHost)
            {
                HybridIpcHost.Start();
            }
            else
            {
                HybridIpcHost.Stop();
            }
        }
        catch { }
    }

    public static void StopHybridIpcHost()
    {
        try { HybridIpcHost.Stop(); } catch { }
    }

    public static void StartHybridShellAdapterIfEnabled()
    {
        try
        {
            var enableContacts = Settings.Settings.EnableHybridContactsShell;
            var enableUnread = Settings.Settings.EnableHybridUnreadShell;
            var enableMarkdown = Settings.Settings.EnableHybridMarkdownShell;
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await HybridShellAdapter.StartAsync(enableContacts, enableUnread).ConfigureAwait(false);
                }
                catch { }

                try
                {
                    await HybridShellMarkdownAdapter.StartAsync(enableMarkdown).ConfigureAwait(false);
                }
                catch { }
            });
        }
        catch { }
    }

    public static void StopHybridShellAdapter()
    {
        try { HybridShellMarkdownAdapter.StopAsync().GetAwaiter().GetResult(); } catch { }
        try { HybridShellAdapter.StopAsync().GetAwaiter().GetResult(); } catch { }
    }

    // Back-compat wrappers for existing call sites while migration is in progress.
    public static void StartHybridShellConsumerIfEnabled() => StartHybridShellAdapterIfEnabled();
    public static void StopHybridShellConsumer() => StopHybridShellAdapter();

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

    public static UnreadSnapshotDto GetUnreadSnapshotDto()
    {
        try
        {
            return UnreadBridge.GetSnapshot();
        }
        catch
        {
            return new UnreadSnapshotDto(UnreadBridgeService.SnapshotSchemaVersion, DateTime.UtcNow, Array.Empty<UnreadPeerCountDto>(), 0);
        }
    }

    public static string GetUnreadSnapshotJson()
    {
        try
        {
            return UnreadBridge.GetSnapshotJson();
        }
        catch
        {
            return "{\"schemaVersion\":1,\"generatedUtc\":null,\"peers\":[],\"totalUnread\":0}";
        }
    }

    public static bool TryHandleUnreadIpcRequest(string command, out string responseJson)
    {
        responseJson = string.Empty;
        try
        {
            return UnreadIpcEndpoint.TryHandleRequest(command, out responseJson);
        }
        catch
        {
            return false;
        }
    }

    public static ContactsSnapshotDto GetContactsSnapshotDto()
    {
        try
        {
            return ContactsBridge.GetSnapshot();
        }
        catch
        {
            return new ContactsSnapshotDto(ContactsBridgeService.SnapshotSchemaVersion, DateTime.UtcNow, Array.Empty<ContactListItemDto>(), 0, 0);
        }
    }

    public static string GetContactsSnapshotJson()
    {
        try
        {
            return ContactsBridge.GetSnapshotJson();
        }
        catch
        {
            return "{\"schemaVersion\":1,\"generatedUtc\":null,\"contacts\":[],\"totalContacts\":0,\"totalUnread\":0}";
        }
    }

    public static bool TryHandleContactsIpcRequest(string command, out string responseJson)
    {
        responseJson = string.Empty;
        try
        {
            return ContactsIpcEndpoint.TryHandleRequest(command, out responseJson);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryHandleMarkdownIpcRequest(string command, string requestJson, out string responseJson)
    {
        responseJson = string.Empty;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(requestJson)
                ? "{}"
                : requestJson);
            return MarkdownIpcEndpoint.TryHandleRequest(command, doc.RootElement, out responseJson);
        }
        catch
        {
            return false;
        }
    }

}
