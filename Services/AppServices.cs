/*
    Simple service locator for singletons and shared state.
    - Holds global Passphrase set after unlock/account creation.
    - Provides access to Settings, Theme, Network, and other services.
*/
// TODO[ANCHOR]: AppServices - Global services and passphrase state
using System;
using ZTalk.Containers;
using System.Linq;
using Models = ZTalk.Models;
using ZTalk.Utilities;

namespace ZTalk.Services;

public static class AppServices
{
    // TODO: Integrate with LockService to set a real passphrase on unlock
    public static string Passphrase { get; set; } = "dev";
    public static SettingsService Settings { get; } = new(new P2EContainer());
    public static IdentityService Identity { get; } = new();
    public static NatTraversalService Nat { get; } = new();
    public static NetworkService Network { get; } = new(Identity, Nat);
    public static DialogService Dialogs { get; } = new();
    public static AccountManager Accounts { get; } = new();
    public static ContactManager Contacts { get; } = new();
    public static PeerManager Peers { get; } = new(Settings);
    public static PeerCrawler Crawler { get; } = new(Network, Settings, Identity);
    public static PeersStore PeersStore { get; } = new();
    public static ContactRequestsService ContactRequests { get; } = new(Identity, Network, Settings, Contacts, Dialogs);
    public static UpdateManager Updates { get; } = new();
    public static EventHub Events { get; } = new();
    public static ThemeService Theme { get; } = new();
    public static RegressionGuard Guard { get; } = new(Settings, Network, Nat);
    public static DiscoveryService Discovery { get; } = new(Settings, Network, Nat, Crawler);
    public static RetentionService Retention { get; } = new();
    public static PresenceRefreshService PresenceRefresh { get; } = new();
    public static LinkPreviewService LinkPreview { get; } = new();
    public static OutboxService Outbox { get; } = new();
    public static LogMaintenanceService LogMaintenance { get; } = new(Settings, Updates);
    // Centralized UI pulse key; interval can be adjusted via Settings if desired
    private const string UiPulseKey = "App.UI.Pulse";
    // Email verification removed in keypair-based identity model

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
            var mc = new ZTalk.Containers.MessageContainer();
            mc.UpdateMessage(peerUid, messageId, newContent, Passphrase);
            // UI: The bound list in MainWindow may not auto-refresh; rely on ViewModel listening to ChatMessageReceived for new only.
            // TODO: Consider raising an event for edits if we add an event channel.
        }
        catch { }
    }

    public static void MessagesDeleteFromRemote(string peerUid, System.Guid messageId)
    {
        try
        {
            var mc = new ZTalk.Containers.MessageContainer();
            mc.DeleteMessage(peerUid, messageId, Passphrase);
            try
            {
                if (ZTalk.Utilities.LoggingPaths.Enabled)
                {
                    var line = $"[RETENTION] {System.DateTime.Now:O}: Remote delete peer={peerUid} id={messageId}";
                    System.IO.File.AppendAllText(ZTalk.Utilities.LoggingPaths.Retention, line + System.Environment.NewLine);
                }
            }
            catch { }
            // TODO: Raise a UI event so active conversation can remove the message if visible.
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
                        if (!Network.HasEncryptedSession(uid)
                            && !string.Equals(status, "Invisible", System.StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(status, "Offline", System.StringComparison.OrdinalIgnoreCase))
                        {
                            var peer = Peers.Peers.FirstOrDefault(p => string.Equals(p.UID, uid, System.StringComparison.OrdinalIgnoreCase));
                            if (peer != null && !string.IsNullOrWhiteSpace(peer.Address) && peer.Port > 0)
                            {
                                await Network.ConnectWithRelayFallbackAsync(uid, peer.Address!, peer.Port, System.Threading.CancellationToken.None);
                            }
                        }
                        await Outbox.DrainAsync(uid, Passphrase, System.Threading.CancellationToken.None);
                    }
                    catch { }
                });
            };
        }
        catch { }
        // When we receive an ACK from a peer that they got our message, persist status even if chat isn't open
        try
        {
            Network.ChatMessageReceivedAcked += (uid, id) =>
            {
                try
                {
                    var mc = new ZTalk.Containers.MessageContainer();
                    var stamp = System.DateTime.UtcNow;
                    var ok = mc.UpdateDelivery(uid, id, "Sent", stamp, Passphrase);
                    if (!ok)
                    {
                        // Try alternate UID forms to tolerate stored key format differences
                        try
                        {
                            string alt = uid.StartsWith("usr-", StringComparison.OrdinalIgnoreCase) ? uid.Substring(4) : ("usr-" + uid);
                            ok = mc.UpdateDelivery(alt, id, "Sent", stamp, Passphrase);
                            if (ok) Logger.NetworkLog($"ACK-Rcvd: Fallback Updated delivery Sent using alt UID | peer={alt} | id={id}");
                        }
                        catch { }
                    }
                    if (ok)
                    {
                        Events.RaiseOutboundDeliveryUpdated(uid, id, "Sent", stamp);
                        Logger.NetworkLog($"ACK-Rcvd: Updated delivery Sent | peer={uid} | id={id}");
                    }
                    else
                    {
                        // Log if the message wasn't found so we can diagnose mismatched UIDs/IDs
                        Logger.NetworkLog($"ACK-Rcvd: message not found in store | peer={uid} | id={id}");
                    }
                }
                catch (System.Exception ex)
                {
                    Logger.NetworkLog($"ACK-Rcvd: handler exception | peer={uid} | id={id} | ex={ex.Message}");
                }
            };
        }
        catch { }
        try
        {
            Network.ChatMessageReadAcked += (uid, id) =>
            {
                try
                {
                    var mc = new ZTalk.Containers.MessageContainer();
                    var stamp = System.DateTime.UtcNow;
                    var ok = mc.UpdateDelivery(uid, id, "Read", null, Passphrase, readUtc: stamp);
                    if (!ok)
                    {
                        try
                        {
                            string alt = uid.StartsWith("usr-", StringComparison.OrdinalIgnoreCase) ? uid.Substring(4) : ("usr-" + uid);
                            ok = mc.UpdateDelivery(alt, id, "Read", null, Passphrase, readUtc: stamp);
                            if (ok) Logger.NetworkLog($"ACK-Read: Fallback Updated delivery Read using alt UID | peer={alt} | id={id}");
                        }
                        catch { }
                    }
                    if (ok)
                    {
                        Events.RaiseOutboundDeliveryUpdated(uid, id, "Read", stamp);
                        Logger.NetworkLog($"ACK-Read: Updated delivery Read | peer={uid} | id={id}");
                    }
                    else
                    {
                        Logger.NetworkLog($"ACK-Read: message not found in store | peer={uid} | id={id}");
                    }
                }
                catch (System.Exception ex)
                {
                    Logger.NetworkLog($"ACK-Read: handler exception | peer={uid} | id={id} | ex={ex.Message}");
                }
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
                System.Threading.Tasks.Task.Run(() => Outbox.DrainAsync(who, Passphrase, System.Threading.CancellationToken.None));
            }
        }; } catch { }

        // Start a lightweight, centralized UI pulse so views can stay current even when inactive/minimized.
        // This avoids per-window timers and ensures consistent refresh cadence.
        try
        {
            var interval = 500; // ms; could be bound to Settings if needed
            Updates.RegisterUiInterval(UiPulseKey, interval, () =>
            {
                try { Events.RaiseUiPulse(); } catch { }
                // Early app pulses can opportunistically trigger connection attempts when data arrives
                try { Network.RequestAutoConnectSweep(); } catch { }
            });
        }
        catch { }

        // Start regression guard background monitor (skip in SafeMode)
        try { if (!ZTalk.Utilities.RuntimeFlags.SafeMode) Guard.Start(); } catch { }
        // Start discovery orchestrator (non-invasive; coordinates existing services) (skip in SafeMode)
        try { if (!ZTalk.Utilities.RuntimeFlags.SafeMode) Discovery.Start(); } catch { }

#if DEBUG
    try { LogMaintenance.TryStart(); } catch { }
#endif

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

    // Centralized graceful shutdown. Invoked on MainWindow close / application exit.
    public static void Shutdown()
    {
        try { Updates.Shutdown(); } catch { }
        try { Discovery.Stop(); } catch { }
        try { Crawler.Stop(); } catch { }
        try { Guard?.GetType(); /* no explicit stop; timers cleared via Updates */ } catch { }
        try { Network.Stop(); } catch { }
        try { Nat?.UnmapAsync(); } catch { }
        try { PeersStore.Save(Peers.Peers, Passphrase); } catch { }
        try { Settings.Save(Passphrase); } catch { }
        try { LinkPreview.Dispose(); } catch { }
#if DEBUG
    try { LogMaintenance.Stop(); } catch { }
#endif
        // No hard process exit here; allow normal app shutdown.
    }

}
