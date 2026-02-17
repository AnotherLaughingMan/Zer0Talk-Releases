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

public static class AppServices
{
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
            catch { }
            finally
            {
                try { OutboxDrainLimiter.Release(); } catch { }
            }
        });
    }

    // Convenience helper for viewmodels to cancel queued messages by id
    public static void OutboxCancelIfQueued(string peerUid, System.Guid messageId, string passphrase)
    {
        try { Outbox.CancelQueued(peerUid, messageId, passphrase); } catch { }
    }

    // Update queued message content if still pending
    public static void OutboxUpdateIfQueued(string peerUid, System.Guid messageId, string newContent, string passphrase)
    {
        try { Outbox.UpdateQueued(peerUid, messageId, newContent, passphrase); } catch { }
    }

    public static void OutboxQueueEdit(string peerUid, System.Guid messageId, string newContent, string passphrase)
    {
        try { Outbox.EnqueueEdit(peerUid, messageId, newContent, passphrase); } catch { }
    }

    public static void PeersClearTransientStatuses()
    {
        try { Peers.ClearTransientStatuses(); } catch { }
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
        catch { }
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
        catch { }
    }

    static AppServices()
    {
        // Forward service events to centralized hub (no heavy work here)
        try { Nat.Changed += () => Events.RaiseNatChanged(); } catch { }
        try { Network.WarningRaised += msg => Events.RaiseFirewallPrompt(msg); } catch { }
        try
        {
            Network.VersionMismatchDetected += (peerUid, ourVersion, theirVersion) =>
            {
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var contact = Contacts.Contacts.FirstOrDefault(c => string.Equals(c.UID, peerUid, System.StringComparison.OrdinalIgnoreCase));
                        var peerDisplay = contact?.DisplayName ?? peerUid;
                        var message = $"Version compatibility warning:\n\n" +
                                    $"Peer: {peerDisplay}\n" +
                                    $"Their version: {theirVersion}\n" +
                                    $"Your version: {ourVersion}\n\n" +
                                    $"Communication may be unreliable due to version differences. " +
                                    $"Consider updating to the same version.";
                        
                        await Dialogs.ShowInfoAsync("Version Mismatch Detected", message, 8000);
                    }
                    catch (System.Exception ex)
                    {
                        Logger.NetworkLog($"Version mismatch notification error: {ex.Message}");
                    }
                });
            };
        }
        catch { }
        try
        {
            Network.ListeningChanged += (on, port) =>
            {
                try { Events.RaiseNetworkListeningChanged(on, port); } catch { }
                if (on)
                {
                    try { Network.RequestAutoConnectSweep(); } catch { }
                }
            };
        }
        catch { }
        try
        {
            Peers.Changed += () =>
            {
                try { Events.RaisePeersChanged(); } catch { }
                try { Network.RequestAutoConnectSweep(); } catch { }
            };
        }
        catch { }
        // When contacts change, clean up orphaned per-peer message and outbox files
        try
        {
            Contacts.Changed += () =>
            {
                try { Retention.CleanupOrphanMessageFiles(); } catch { }
                try { Retention.CleanupOrphanOutboxFiles(); } catch { }
                try { PresenceRefresh.RequestUnlockSweep(); } catch { }
                try { Network.RequestAutoConnectSweep(); } catch { }
            };
        }
        catch { }
    // When identity/profile changes, broadcast profile bits to active peers
    try { Identity.Changed += () => { try { Network.BroadcastAvatarToActiveSessions(); } catch { } try { Network.BroadcastBioToActiveSessions(); } catch { } }; } catch { }
        // When we receive presence from a peer, attempt connection if online and drain that peer's outbox
        try
        {
            Network.PresenceReceived += (uid, status) =>
            {
                if (string.IsNullOrWhiteSpace(uid)) return;
                var normalizedUid = NormalizePeerUid(uid);
                if (string.IsNullOrWhiteSpace(normalizedUid)) return;
                System.Threading.Interlocked.Increment(ref _presenceEventsSeen);
                if (!TryBeginPresenceWork(normalizedUid)) return;

                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        // Map textual status to enum and record signed presence with TTL
                        Models.PresenceStatus s = status switch
                        {
                            "Online" => Models.PresenceStatus.Online,
                            "Idle" => Models.PresenceStatus.Idle,
                            "Do Not Disturb" => Models.PresenceStatus.DoNotDisturb,
                            "Invisible" => Models.PresenceStatus.Invisible,
                            "Offline" => Models.PresenceStatus.Offline,
                            _ => Models.PresenceStatus.Online
                        };
                        try { Contacts.SetPresence(uid, s, System.TimeSpan.FromSeconds(60), Models.PresenceSource.Verified); } catch { }
                        // If not connected and peer appears reachable (not Invisible/Offline), try to connect
                        if (!Network.HasEncryptedSession(normalizedUid)
                            && !string.Equals(status, "Invisible", System.StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(status, "Offline", System.StringComparison.OrdinalIgnoreCase)
                            && ShouldAttemptPeerConnect(normalizedUid))
                        {
                            var peer = Peers.Peers.FirstOrDefault(p => string.Equals(p.UID, normalizedUid, System.StringComparison.OrdinalIgnoreCase) || string.Equals(p.UID, $"usr-{normalizedUid}", System.StringComparison.OrdinalIgnoreCase));
                            if (peer != null && !string.IsNullOrWhiteSpace(peer.Address) && peer.Port > 0)
                            {
                                System.Threading.Interlocked.Increment(ref _presenceConnectAttempts);
                                await PresenceConnectLimiter.WaitAsync().ConfigureAwait(false);
                                try
                                {
                                    await Network.ConnectWithRelayFallbackAsync(normalizedUid, peer.Address!, peer.Port, System.Threading.CancellationToken.None).ConfigureAwait(false);
                                }
                                finally
                                {
                                    try { PresenceConnectLimiter.Release(); } catch { }
                                }
                            }
                        }
                        // Retry any queued offline contact requests for this peer
                        try { ContactRequests.OnPeerOnline(normalizedUid); } catch { }
                        QueuePeerOutboxDrain(normalizedUid);
                    }
                    catch { }
                    finally
                    {
                        EndPresenceWork(normalizedUid);
                    }
                });
            };
        }
        catch { }
        try
        {
            Network.ChatMessageEditAcked += (uid, id) =>
            {
                try { Outbox.CancelQueued(uid, id, Passphrase); } catch { }
            };
        }
        catch { }
    // When a handshake completes, attempt to drain outbox for that peer
    try { Network.HandshakeCompleted += (ok, who, __) => {
            if (ok && !string.IsNullOrWhiteSpace(who))
            {
                try { Contacts.SetPresence(who, Models.PresenceStatus.Online, System.TimeSpan.FromMinutes(5), Models.PresenceSource.Session); } catch { }
                QueuePeerOutboxDrain(who);
            }
        }; } catch { }

        // Start a lightweight, centralized UI pulse so views can stay current even when inactive/minimized.
        // This avoids per-window timers and ensures consistent refresh cadence.
        try
        {
            var interval = 500; // ms; could be bound to Settings if needed
            var autoConnSweepTickCounter = 0;
            Updates.RegisterUiInterval(UiPulseKey, interval, () =>
            {
                try { Events.RaiseUiPulse(); } catch { }
                // Opportunistically trigger auto-connect sweeps at a lower cadence than the UI pulse.
                // Pulse is 500ms; sweep every 10 ticks (~5s) to reduce network/task churn.
                try
                {
                    autoConnSweepTickCounter++;
                    if (autoConnSweepTickCounter >= 10)
                    {
                        autoConnSweepTickCounter = 0;
                        Network.RequestAutoConnectSweep();
                    }
                }
                catch { }
            });
        }
        catch { }

        // Start regression guard background monitor (skip in SafeMode)
        try { if (!Zer0Talk.Utilities.RuntimeFlags.SafeMode) Guard.Start(); } catch { }
        // Start discovery orchestrator (non-invasive; coordinates existing services) (skip in SafeMode)
        try { if (!Zer0Talk.Utilities.RuntimeFlags.SafeMode) Discovery.Start(); } catch { }

#if DEBUG
    try { LogMaintenance.TryStart(); } catch { }
#endif

        // Initialize audio service with current settings (will be updated when settings are loaded)
        try
        {
            SyncAudioSettings();
        }
        catch { }

        // Opportunistic retention triggers
        try
        {
            // Presence expiry sweep: demote stale presences when TTL elapses and no session is active
            Updates.RegisterBgInterval("Presence.ExpirySweep", intervalMs: 5000, action: () =>
            {
                try { Contacts.ExpireStalePresences(); } catch { }
            });

            // After unlock: subscribe to a UI pulse and use early pulses for housekeeping/outbox draining
            int pulses = 0;
            Events.UiPulse += () =>
            {
                try
                {
                    if (pulses == 0)
                    {
                        try { Retention.CleanupOrphanMessageFiles(); } catch { }
                        try { Retention.CleanupOrphanOutboxFiles(); } catch { }
                    }

                    if (pulses < 5)
                    {
                        Outbox.DrainAllIfPossible(Passphrase);
                    }

                    pulses++;
                }
                catch { }
            };

            // Idle trigger: every 5 minutes in background drain any queued messages for connected peers
            Updates.RegisterBgInterval("Outbox.IdleCheck", intervalMs: 5 * 60 * 1000, action: () =>
            {
                try { Outbox.DrainAllIfPossible(Passphrase); } catch { }
            });
        }
        catch { }
    }

    // Sync audio service volumes with current settings
    public static void SyncAudioSettings()
    {
        try
        {
            var audio = AudioNotifications;
            var settings = Settings.Settings;
            audio.MainVolume = (float)Math.Clamp(settings.MainVolume, 0.0, 1.0);
            audio.NotificationVolume = (float)Math.Clamp(settings.NotificationVolume, 0.0, 1.0);
            audio.ChatVolume = (float)Math.Clamp(settings.ChatVolume, 0.0, 1.0);
        }
        catch { }
    }

    // Centralized graceful shutdown. Invoked on MainWindow close / application exit.
    public static void Shutdown()
    {
        try { Updates.Shutdown(); } catch { }
        try { Discovery.Stop(); } catch { }
        try { Guard?.GetType(); /* no explicit stop; timers cleared via Updates */ } catch { }
        try { Network.Stop(); } catch { }
        try { Nat?.UnmapAsync(); } catch { }
        try { PeersStore.Save(Peers.Peers, Passphrase); } catch { }
        try { Settings.Save(Passphrase); } catch { }
        try { LinkPreview.Dispose(); } catch { }
        try { TrayIcon.Dispose(); } catch { }
        try { AudioNotifications.Dispose(); } catch { }
#if DEBUG
    try { LogMaintenance.Stop(); } catch { }
#endif
        // No hard process exit here; allow normal app shutdown.
    }

}

