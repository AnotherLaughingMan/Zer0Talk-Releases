using System;
using Zer0Talk.Containers;
using System.Linq;
using Models = Zer0Talk.Models;
using Zer0Talk.Utilities;

namespace Zer0Talk.Services;

public static partial class AppServices
{
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
                        if (contact != null) contact.PeerVersion = theirVersion;
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
                            _ => Models.PresenceStatus.Offline
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
        try { AutoUpdate.Dispose(); } catch { }
        try { AudioNotifications.Dispose(); } catch { }
#if DEBUG
    try { LogMaintenance.Stop(); } catch { }
#endif
        // No hard process exit here; allow normal app shutdown.
    }

}
