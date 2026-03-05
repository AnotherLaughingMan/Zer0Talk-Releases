/*
    Networking core: ECDH P-256 handshake, HKDF key derivation, AEAD transport frames.
    - Integrates NAT traversal and peer management.
*/
/*
    Core networking: sockets, handshake (ECDH P-256 + HKDF-SHA256), and encrypted transport frames (AEAD).
    - Integrates with NatTraversalService and PeerManager.
*/
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Zer0Talk.Utilities;
using Zer0Talk.Models;
using Models = Zer0Talk.Models;

namespace Zer0Talk.Services
{
    public class NetworkService : IDisposable
    {
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private readonly IdentityService _identity;
        private readonly NatTraversalService _nat;
        private readonly ConcurrentDictionary<string, TcpClient> _peers = new();
        private readonly ConcurrentDictionary<int, PortTraffic> _traffic = new();
        private UdpClient? _udp;
        private Task? _udpTask;
    private readonly ConcurrentDictionary<string, Utilities.AeadTransport> _sessions = new(StringComparer.OrdinalIgnoreCase);
    // Track per-peer connection mode (direct vs relay) for UI display
    private readonly ConcurrentDictionary<string, Models.ConnectionMode> _sessionModes = new(StringComparer.OrdinalIgnoreCase);
    // Track peer's ephemeral ECDH public key (DER SPKI) per transport to verify identity binding
    private readonly ConcurrentDictionary<Utilities.AeadTransport, byte[]> _handshakePeerKeys = new();
    // Tracks expected peer UID for outbound connections until handshake completes (keyed by remote endpoint string)
    private readonly ConcurrentDictionary<string, PendingOutboundExpectation> _pendingOutboundExpectations = new(StringComparer.OrdinalIgnoreCase);
    // Track remote endpoint string for a given transport so we can attribute identity binding/rotation correctly.
    private readonly ConcurrentDictionary<Utilities.AeadTransport, string> _transportEndpoints = new();
        private readonly NetworkDiagnostics _diag = new();
        public NetworkDiagnostics.Snapshot GetDiagnosticsSnapshot() => _diag.GetSnapshot();
    // Track last derived UID per remote endpoint to detect rotating identities
    private readonly ConcurrentDictionary<string, string> _endpointLastUid = new(StringComparer.OrdinalIgnoreCase);
    // Presence liveness tracking
    private readonly ConcurrentDictionary<string, DateTime> _presenceLastSeenUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _presenceLastSentUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DiscoveredRelay> _discoveredRelays = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RelayHealthEntry> _relayHealth = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _outsideLanDirectSuccessByHost = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan OutsideLanDirectEvidenceTtl = TimeSpan.FromMinutes(20);
    private System.Threading.Timer? _presenceTimeoutTimer;
    private static readonly TimeSpan PresenceSweepInterval = TimeSpan.FromSeconds(5);
    private readonly object _timeoutTuningGate = new();
    private double _directSessionWaitMsEwma = 6000;
    private double _relayAckWaitMsEwma = 5000;
    private double _relayPairWaitMsEwma = 45000;

    private static TimeSpan GetPresenceTimeout()
    {
        try
        {
            var seconds = AppServices.Settings?.Settings?.RelayPresenceTimeoutSeconds ?? 45;
            return TimeSpan.FromSeconds(Math.Clamp(seconds, 10, 300));
        }
        catch
        {
            return TimeSpan.FromSeconds(45);
        }
    }

    private static TimeSpan GetRelayDiscoveryTtl()
    {
        try
        {
            var minutes = AppServices.Settings?.Settings?.RelayDiscoveryTtlMinutes ?? 3;
            return TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 60));
        }
        catch
        {
            return TimeSpan.FromMinutes(3);
        }
    }

    // [AUTO-CONNECT] Throttled sweep to proactively establish sessions without user action
    private readonly ConcurrentDictionary<string, DateTime> _autoConnLastAttempt = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _relaySessionConnectInFlight = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan AutoConnectBackoff = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan AutoConnectSweepMinInterval = TimeSpan.FromSeconds(3);
    private volatile int _autoConnSweepRunning;
    private DateTime _lastAutoConnSweepUtc = DateTime.MinValue;

    // [VERSION-CONTROL] Version tracking and mismatch detection
    private readonly ConcurrentDictionary<string, string> _peerVersions = new(StringComparer.OrdinalIgnoreCase);
    public event Action<string, string, string>? VersionMismatchDetected; // (peerUid, ourVersion, theirVersion)

    private sealed class PendingOutboundExpectation
    {
        public required string ExpectedUid { get; init; }
        public DateTime CreatedUtc { get; init; }
    }

    // Avatar receive throttling (avoid network/disk churn)
    private readonly ConcurrentDictionary<string, DateTime> _avatarLastAcceptedUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _avatarLastSignature = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _inboundReplayWindow = new(StringComparer.Ordinal);
    private static readonly TimeSpan InboundReplayTtl = TimeSpan.FromMinutes(30);
    private readonly ConcurrentQueue<DateTime> _avatarGlobalAccepts = new();
    private static readonly TimeSpan AvatarMinIntervalPerPeer = TimeSpan.FromSeconds(10);
    private static readonly int AvatarMaxBytes = 256 * 1024; // 256 KB
    private static readonly TimeSpan AvatarGlobalWindow = TimeSpan.FromSeconds(10);
    private static readonly int AvatarGlobalMaxPerWindow = 15; // max avatars accepted across all peers per window
    // Inbound payload caps (defensive limits)
    private const int MaxChatBytes = 16 * 1024; // 16 KB per message
    private const int MaxReactionEmojiBytes = 16; // enough for emoji + variation selector
    private const int MaxDisplayNameBytes = 128; // reasonable UI cap for display names
    private const int MaxPresenceToken = 8; // 'on','idle','dnd','inv'
    private const int MaxBioBytes = 512; // max UTF-8 bytes for bio payload
    private const byte SecurityAlertFrameType = 0xE0;
    private const byte SecurityReasonKeyMismatch = 0x01;

    // [RATE-LIMITING] Connection attempt tracking per IP address
    private class ConnectionAttemptTracker
    {
        public int Count { get; set; }
        public DateTime WindowStart { get; set; }
        public DateTime LastAttempt { get; set; }
    }
    private readonly ConcurrentDictionary<string, ConnectionAttemptTracker> _connectionAttempts = new();
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(5);
    private static readonly int MaxConnectionAttemptsPerWindow = 15; // Max 15 connections per 5 minutes per IP
    private System.Threading.Timer? _rateLimitCleanupTimer;

        // Discovery behavior toggle for regression guard
        public enum DiscoveryMode { Normal, BroadcastOnly }
        public DiscoveryMode DiscoveryBehavior { get; set; } = DiscoveryMode.Normal;
        // Apply discovery behavior changes in-place without restarting TCP listener
        public void ApplyDiscoveryBehavior(DiscoveryMode mode)
        {
            try
            {
                if (DiscoveryBehavior == mode) return;
                DiscoveryBehavior = mode;
                // Toggle multicast membership in-place if UDP discovery is active
                var udp = _udp;
                if (udp != null)
                {
                    try
                    {
                        if (mode == DiscoveryMode.Normal)
                        {
                            try { udp.JoinMulticastGroup(MulticastGroup); Logger.Log($"UDP multicast joined {MulticastGroup}"); } catch (Exception ex) { Logger.Log($"UDP multicast join failed: {ex.Message}"); }
                        }
                        else
                        {
                            try { udp.DropMulticastGroup(MulticastGroup); Logger.Log($"UDP multicast dropped {MulticastGroup}"); } catch (Exception ex) { Logger.Log($"UDP multicast drop failed: {ex.Message}"); }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        // [DISCOVERY] LAN discovery constants (fixed UDP port + multicast group)
        // Using a well-known local multicast group and a fixed UDP discovery port ensures
        // all peers can hear each other regardless of their individual TCP listening ports.
        private const int DiscoveryPort = 38384; // LAN discovery UDP port (does not expose chat port)
        private static readonly IPAddress MulticastGroup = IPAddress.Parse("239.255.42.42"); // Organization-local multicast

        public bool IsListening { get; private set; }
        public int? ListeningPort { get; private set; }
        public int? LastAutoClientPort { get; private set; }
        public IPAddress PreferredBindAddress { get; private set; } = IPAddress.Any;
        // For UI/monitor only
        public IPAddress UdpBoundAddress { get; private set; } = IPAddress.Any;
        public int? UdpBoundPort { get; private set; }

        public event Action<string>? WarningRaised;
        public event Action<bool, int?>? ListeningChanged; // (isListening, port)
                                                           // (success, peerOrEndpoint, reason) — peerOrEndpoint is UID on success, remote endpoint string on failure
        public event Action<bool, string, string?>? HandshakeCompleted;
    // Raised when a signed chat message is received: (peerUid, messageId, content)
    public event Action<string, Guid, string>? ChatMessageReceived;
    public event Action<string, Guid, string>? ChatMessageEdited; // (peerUid, messageId, newContent)
    public event Action<string, Guid>? ChatMessageDeleted; // (peerUid, messageId)
    public event Action<string, Guid, string, bool>? ChatMessageReactionReceived; // (peerUid, messageId, emoji, isAdd)
    public event Action<string, Guid>? ChatMessageEditAcked; // (peerUid, messageId)
    public event Action<string, Guid>? ChatMessageDeleteAcked; // (peerUid, messageId)
    public event Action<string, Guid>? ChatMessageDeliveryAcked; // (peerUid, messageId)
    // Raised when presence is received from a peer (uid, status)
    public event Action<string, string>? PresenceReceived;

        private sealed class DiscoveredRelay
        {
            public string Token { get; init; } = string.Empty;
            public string Host { get; init; } = string.Empty;
            public int Port { get; init; }
            public DateTime LastSeenUtc { get; set; }
        }

        private sealed class RelayHealthEntry
        {
            public int SuccessCount { get; set; }
            public int FailureCount { get; set; }
            public double EwmaLatencyMs { get; set; } = 1500;
            public DateTime LastSuccessUtc { get; set; } = DateTime.MinValue;
            public DateTime LastFailureUtc { get; set; } = DateTime.MinValue;
        }

        public NetworkService(IdentityService identity, NatTraversalService nat)
        {
            _identity = identity;
            _nat = nat;
        }

        // Port readiness / firewall warmup gating
        private DateTime _portReadyAfterUtc = DateTime.MinValue;
        private volatile bool _portReady;
        private readonly object _readyGate = new();
        public bool IsPortReady => _portReady;
        private const int FirewallWarmupSeconds = 6; // allow time for firewall prompt/allow & UPnP verification

        private async Task WaitForPortReadyAsync(CancellationToken ct)
        {
            // If already ready or not listening just return
            if (_portReady || !IsListening) return;
            try
            {
                var start = DateTime.UtcNow;
                while (!_portReady && !ct.IsCancellationRequested)
                {
                    if (DateTime.UtcNow >= _portReadyAfterUtc) _portReady = true; // time based readiness
                    // Early readiness if mapping succeeded (major node) or we are a peer (no mapping needed)
                    if (!_majorNodeActive) _portReady = true;
                    else if (_nat.MappedTcpPort == ListeningPort && _nat.ExternalIPAddress != null) _portReady = true;
                    if (_portReady) break;
                    if ((DateTime.UtcNow - start) > TimeSpan.FromSeconds(20)) { _portReady = true; break; } // hard ceiling
                    await Task.Delay(250, ct);
                }
            }
            catch { }
        }

    // Delay outbound/handshake attempts slightly after readiness (firewall UX) to reduce early failures.
    private static readonly TimeSpan OutboundConnectDelay = TimeSpan.FromMilliseconds(750);
    // Bound readiness gating for outbound dialing so NAT mapping lag does not stall first-message sends.
    private static readonly TimeSpan MaxOutboundReadyWait = TimeSpan.FromSeconds(2);
    private DateTime _readyMarkedUtc;

        private bool _majorNodeActive;
        // [DISCOVERY] Major Node now only influences WAN reachability (UPnP/NAT) and logging; LAN discovery works regardless.
        public void StartIfMajorNode(int port, bool majorNode)
        {
            // Global kill-switch for diagnostics mode: no binds, no UDP, no UPnP.
            if (Zer0Talk.Utilities.RuntimeFlags.SafeMode)
            {
                try { Logger.Log("NetworkService.StartIfMajorNode suppressed due to SafeMode"); } catch { }
                return;
            }
            // Idempotent and delta-based behavior:
            // - If already listening on the desired port and only MajorNode changed, adjust NAT without restart.
            // - If already listening on matching config, no-op.
            // - If port must change or not listening, (re)start and map if needed.

            var desiredPort = port;
            var currentlyListening = IsListening;
            var currentPort = ListeningPort;

            // Case 1: Already listening and port matches desired (or desired <= 0 meaning auto-any), only MajorNode state differs
            if (currentlyListening && ((desiredPort <= 0 && currentPort.HasValue) || (desiredPort > 0 && currentPort == desiredPort)))
            {
                if (_majorNodeActive != majorNode)
                {
                    _majorNodeActive = majorNode;
                    if (majorNode)
                    {
                        // Enable mapping without restarting listener
                        _ = Task.Run(async () => { try { await _nat.TryMapPortsAsync(currentPort ?? 0, currentPort ?? 0); } catch { } });
                        Logger.Log("Major Node enabled: attempting UPnP mapping without restart");
                    }
                    else
                    {
                        // Disable mapping without stopping listener
                        try { _ = _nat.UnmapAsync(); } catch { }
                        Logger.Log("Major Node disabled: UPnP unmapped without restart");
                    }
                }
                return; // No restart necessary
            }

            // Case 2: Already listening and config is effectively identical
            if (currentlyListening && _majorNodeActive == majorNode && ((desiredPort <= 0 && currentPort.HasValue) || currentPort == desiredPort))
            {
                return; // No changes needed
            }

            // Otherwise: (re)start listener
            Stop();
            _cts = new CancellationTokenSource();
            var when = DateTime.UtcNow.ToString("o");
            var uid = _identity.UID;
            int boundPort = 0;
            // [PORT] Prefer a stable default for non-major peers to avoid random port churn.
            // If MajorNode is off and no explicit port is set, prefer 26264; fall back to requested or ephemeral.
            const int PreferredPeerPort = 26264;
            var requestedPort = port;
            if (!majorNode && requestedPort <= 0) requestedPort = PreferredPeerPort;
            if (requestedPort > 0)
            {
                try
                {
                    var bindIp = SelectPreferredBindAddress();
                    PreferredBindAddress = bindIp;
                    Logger.Log($"[{when}] Attempting bind on {bindIp}:{requestedPort} (requested). UID={uid}");
                    _listener = new TcpListener(bindIp, requestedPort);
                    _listener.Start();
                    boundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[{when}] Bind failed on requested port {requestedPort}: {ex.Message}. Falling back to auto-negotiation.");
                    if (!majorNode && requestedPort == PreferredPeerPort)
                    {
                        // Notify UI that the preferred port is busy so the user can override in Network settings.
                        WarningRaised?.Invoke($"Preferred port {PreferredPeerPort} is in use. Using a temporary port. You can adjust this in Network settings.");
                    }
                }
            }
            if (boundPort == 0)
            {
                var bindIp = SelectPreferredBindAddress();
                PreferredBindAddress = bindIp;
                boundPort = TryBindEphemeral(bindIp, out _listener);
                if (boundPort == 0)
                {
                    Logger.Log($"[{when}] ERROR: Could not bind to any port in 49152-65535. UID={uid}");
                    IsListening = false; ListeningPort = null;
                    WarningRaised?.Invoke("Failed to bind to any local port. Try a different port or enable auto-negotiation.");
                    return;
                }
                Logger.Log($"[{DateTime.UtcNow:o}] Auto-negotiated port {boundPort}. UID={uid}");
                if (!majorNode && requestedPort == PreferredPeerPort)
                {
                    WarningRaised?.Invoke($"Using temporary port {boundPort}. Preferred {PreferredPeerPort} was unavailable.");
                }
            }
            ListeningPort = boundPort;
            var boundAddr = (_listener?.LocalEndpoint as IPEndPoint)?.Address ?? IPAddress.Any;
            if (majorNode)
            {
                Logger.Log($"Listening on {boundAddr}:{boundPort} (Major Node mode)");
                WarningRaised?.Invoke("If Windows Firewall prompts, allow Zer0Talk for inbound connections on the chosen port.");
                _ = Task.Run(async () => { try { await _nat.TryMapPortsAsync(boundPort, boundPort); } catch { } });
                try { _nat.ConfigureDesiredPorts(boundPort, boundPort); _nat.EnableAutoMapping(true, forceKick: true); } catch { }
            }
            else
            {
                Logger.Log($"Listening on {boundAddr}:{boundPort} (Peer mode; Major Node disabled)");
                // Even when not a major node, keep trying to obtain a mapping opportunistically to improve reachability
                try { _nat.ConfigureDesiredPorts(boundPort, boundPort); _nat.EnableAutoMapping(true, forceKick: true); } catch { }
            }
            _ = AcceptLoop(_cts.Token);
            IsListening = true;
            _majorNodeActive = majorNode;
            try { Zer0Talk.Services.AppServices.Events.RaiseNetworkListeningChanged(true, ListeningPort); } catch { }
            try { ListeningChanged?.Invoke(true, ListeningPort); } catch { }
            // [DISCOVERY] Always start LAN discovery; advertising is no longer gated by Major Node.
            StartLanDiscovery(boundPort);
            _portReady = false;
            _portReadyAfterUtc = DateTime.UtcNow + TimeSpan.FromSeconds(FirewallWarmupSeconds);
            // Fire and forget warmup task (no await needed)
            _ = Task.Run(async () => {
                try
                {
                    await WaitForPortReadyAsync(CancellationToken.None);
                    _readyMarkedUtc = DateTime.UtcNow;
                    Logger.Log("NetworkService: port readiness gating complete");
                    // Prompt a NAT verification pass (includes hairpin test when possible)
                    try { await _nat.RetryVerificationAsync(); } catch { }
                }
                catch { }
            });

            // Start presence liveness timer
            try { _presenceTimeoutTimer?.Change(Timeout.Infinite, Timeout.Infinite); _presenceTimeoutTimer?.Dispose(); } catch { }
            _presenceTimeoutTimer = new System.Threading.Timer(_ => { try { SweepPresenceTimeouts(); } catch { } }, null, PresenceSweepInterval, PresenceSweepInterval);

            // [TIER-1-BLOCKING] Start rate limit cleanup timer (runs every 10 minutes)
            try { _rateLimitCleanupTimer?.Change(Timeout.Infinite, Timeout.Infinite); _rateLimitCleanupTimer?.Dispose(); } catch { }
            _rateLimitCleanupTimer = new System.Threading.Timer(_ => { try { CleanupRateLimitTracking(); } catch { } }, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
        }

        public void ForceRestart()
        {
            var s = AppServices.Settings.Settings;
            Logger.Log("NetworkService.ForceRestart: stopping and restarting listener");
            Stop();
            StartIfMajorNode(s.Port, s.MajorNode);
        }

        public void Stop()
        {
            // Best-effort: announce going invisible before tearing down sessions (force-send 'inv')
            try
            {
                var payload = System.Text.Encoding.UTF8.GetBytes("inv");
                var frame = new byte[2 + payload.Length];
                frame[0] = 0xD0; frame[1] = (byte)payload.Length; Buffer.BlockCopy(payload, 0, frame, 2, payload.Length);
                var peers = _sessions.Keys.ToArray();
                foreach (var uid in peers)
                {
                    _ = TrySendEncryptedAsync(uid, frame, CancellationToken.None);
                }
                try { SafeNetLog($"shutdown announce-inv | peers={peers.Length}"); } catch { }
            }
            catch { }
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            try { _udp?.Close(); } catch { }
            try { _ = _nat.UnmapAsync(); } catch { }
            try { _presenceTimeoutTimer?.Change(Timeout.Infinite, Timeout.Infinite); _presenceTimeoutTimer?.Dispose(); } catch { }
            _udp = null; _udpTask = null;
            _cts = null; _listener = null; IsListening = false; ListeningPort = null;
            UdpBoundPort = null; UdpBoundAddress = IPAddress.Any;
            try { Zer0Talk.Services.AppServices.Events.RaiseNetworkListeningChanged(false, null); } catch { }
            try { ListeningChanged?.Invoke(false, null); } catch { }
        }

        private async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient? client = null;
                try
                {
                    if (_listener == null) { await Task.Delay(100, ct); continue; }
                    client = await _listener.AcceptTcpClientAsync(ct);
                    try { _diag.IncAccepted(); } catch { }
                    try { ApplySocketOptions(client.Client); } catch { }
                    Logger.Log($"Accepted from {(client.Client.RemoteEndPoint as IPEndPoint)?.ToString()}");
                    _ = HandleClient(client, false, ct);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Logger.Log($"Accept error: {ex.Message}"); client?.Close(); }
            }
        }

        public async Task<TcpClient?> ConnectWithNatFallbackAsync(string hostOrIp, int port, CancellationToken ct)
        {
            try
            {
                if (!_portReady && IsListening)
                {
                    using var readyCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    readyCts.CancelAfter(MaxOutboundReadyWait);
                    try { await WaitForPortReadyAsync(readyCts.Token); } catch { }
                }
                // Enforce outbound delay to allow firewall prompt acceptance
                if (_readyMarkedUtc != DateTime.MinValue)
                {
                    var since = DateTime.UtcNow - _readyMarkedUtc;
                    if (since < OutboundConnectDelay)
                    {
                        var remaining = OutboundConnectDelay - since;
                        if (remaining > TimeSpan.Zero)
                        {
                            try { await Task.Delay(remaining, ct); } catch { }
                        }
                    }
                }
                return await ConnectAsync(hostOrIp, port, ct);
            }
            catch (Exception ex)
            {
                Logger.Log($"Direct TCP connect failed: {ex.Message}");
            }
            // If we are unmapped and have desired ports configured, opportunistically attempt a fast UPnP try before punching
            try
            {
                if (!_nat.MappedTcpPort.HasValue && ListeningPort is int lp && lp > 0)
                {
                    _nat.ConfigureDesiredPorts(lp, lp);
                    // Fire-and-forget quick attempt (do not block dialing for long)
                    _ = Task.Run(async () =>
                    {
                        try { await _nat.TryMapPortsAsync(lp, lp).ConfigureAwait(false); } catch { }
                    }, CancellationToken.None);
                }
            }
            catch { }
            try
            {
                if (IPAddress.TryParse(hostOrIp, out var ip))
                {
                    // [B] Choose a sensible local UDP bind port for punching: prefer mapped UDP, then listening port, else ephemeral (0).
                    // Avoid using discovery UDP port here because it is typically already bound by LAN discovery.
                    int localUdp = _nat.MappedUdpPort ?? (ListeningPort ?? 0);
                    var ok = await _nat.TryUdpHolePunchAsync(new IPEndPoint(ip, port), localUdp, ct);
                    if (ok)
                    {
                        try { _diag.IncNatSuccess(); } catch { }
                        return await ConnectAsync(hostOrIp, port, ct);
                    }
                    else
                    {
                        try { _diag.IncNatFail(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"NAT fallback error: {ex.Message}");
            }
            return null;
        }

        // Connect to a peer with direct -> NAT punch -> relay fallback sequence.
        // On success, returns true once an encrypted session is established and registered in _sessions.
        public async Task<bool> ConnectWithRelayFallbackAsync(string peerUid, string hostOrIp, int port, CancellationToken ct)
        {
            // Block check: Refuse to connect to blocked peers
            var expectedKey = Trim(peerUid);
            if (AppServices.Settings.Settings.BlockList?.Contains(expectedKey) == true)
            {
                Logger.Log($"Refusing connection attempt to blocked peer: {expectedKey}");
                return false;
            }
            
            // First try the existing direct/NAT path
            try
            {
                var beforeKeys = _sessions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var client = await ConnectWithNatFallbackAsync(hostOrIp, port, ct);
                if (client != null)
                {
                    try
                    {
                        var ep = client.Client.RemoteEndPoint?.ToString();
                        RegisterPendingOutboundExpectation(ep, expectedKey);
                    }
                    catch { }
                    // Wait for handshake to register expected session or detect mismatch
                    var directWaitTimeout = GetDirectSessionWaitTimeout();
                    var deadline = DateTime.UtcNow + directWaitTimeout;
                    var directWaitStart = DateTime.UtcNow;
                    bool reportedMismatch = false;
                    while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
                    {
                        if (_sessions.ContainsKey(expectedKey))
                        {
                            ObserveDirectSessionWait(DateTime.UtcNow - directWaitStart);
                            MarkOutsideLanDirectSuccess(hostOrIp);
                            try { _diag.IncDirectSuccess(); } catch { }
                            SafeNetLog($"connect direct/nat success | peer={peerUid} | endpoint={hostOrIp}:{port}");
                            return true;
                        }
                        // Only flag UID mismatch if the specific endpoint we connected to completed
                        // a handshake with a different UID. Ignore unrelated concurrent sessions.
                        var ep2 = client.Client.RemoteEndPoint?.ToString();
                        if (ep2 != null && _endpointLastUid.TryGetValue(ep2, out var epUid)
                            && !string.Equals(Trim(epUid), expectedKey, StringComparison.OrdinalIgnoreCase))
                        {
                            SafeNetLog($"connect direct uid-mismatch | peer={peerUid} | got={epUid}");
                            Logger.Log($"Direct/NAT handshake UID mismatch: expected={peerUid} got={epUid} from {hostOrIp}:{port}");
                            try { _diag.IncUidMismatch(); } catch { }
                            try { AppServices.Events.RaiseFirewallPrompt($"Peer identity mismatch: expected {expectedKey}, got {epUid} from {hostOrIp}:{port}. Your contact may be stale."); } catch { }
                            reportedMismatch = true;
                            break;
                        }
                        try { await Task.Delay(75, ct); } catch { }
                    }
                    if (!reportedMismatch)
                    {
                        try { _diag.IncDirectFail(); } catch { }
                        SafeNetLog($"connect direct timeout-wait-session | peer={peerUid}");
                        Logger.Log($"Direct connect: timeout waiting for session {expectedKey}");
                    }
                    else
                    {
                        try { _diag.IncDirectFail(); } catch { }
                    }
                    return false;
                }
            }
            catch { try { _diag.IncDirectFail(); } catch { } }

            // Quick second attempt in case auto-mapping succeeded moments later
            try
            {
                await Task.Delay(400, ct);
                var client2 = await ConnectWithNatFallbackAsync(hostOrIp, port, ct);
                if (client2 != null)
                {
                    try
                    {
                        var ep2 = client2.Client.RemoteEndPoint?.ToString();
                        RegisterPendingOutboundExpectation(ep2, Trim(peerUid));
                    }
                    catch { }
                    var okSession = await WaitForEncryptedSessionAsync(Trim(peerUid), GetDirectRetrySessionWaitTimeout(), ct);
                    if (okSession)
                    {
                        MarkOutsideLanDirectSuccess(hostOrIp);
                        SafeNetLog($"connect direct retry success | peer={peerUid}");
                        return true;
                    }
                }
            }
            catch { }

            // If disabled or no server configured, stop here
            var s = AppServices.Settings.Settings;
            if (!ShouldAttemptRelayFallback(hostOrIp))
            {
                SafeNetLog($"connect relay skipped | peer={peerUid} | reason=outside-lan-direct-available");
                return false;
            }
            if (!s.RelayFallbackEnabled)
            {
                SafeNetLog($"connect relay skipped | peer={peerUid} | reason=disabled");
                return false;
            }

            var relayCandidates = BuildRelayCandidates(s);
            if (relayCandidates.Count == 0)
            {
                SafeNetLog($"connect relay skipped | peer={peerUid} | reason=invalid-endpoint");
                Logger.Log("Relay candidates unavailable (no valid explicit/saved/seed endpoints)");
                return false;
            }

            // Ensure we're registered before relay coordination (needed for OFFER auth).
            try { await AppServices.WanDirectory.TryRegisterSelfAsync(ct); } catch { }

            // Best-effort rendezvous wakeup for the remote peer so both sides join the same relay session key.
            var offerDelivered = false;
            try
            {
                var localUid = Trim(_identity.UID);
                var sessionKey = BuildRelaySessionKey(localUid, Trim(peerUid));
                offerDelivered = await AppServices.WanDirectory.TryOfferRendezvousAsync(Trim(peerUid), localUid, sessionKey, ct);
                SafeNetLog($"connect relay offer | peer={peerUid} | delivered={offerDelivered}");
            }
            catch { }

            // Give the remote peer time to poll the invite before we start queuing on the relay.
            if (offerDelivered)
            {
                try { await Task.Delay(2000, ct); } catch { }
            }

            // Retry relay attempts with wider spacing to allow remote poll interval to pick up the invite.
            const int relayRounds = 3;
            for (var round = 0; round < relayRounds; round++)
            {
                // Re-send OFFER each round in case the previous invite expired or was pruned.
                if (round > 0)
                {
                    try
                    {
                        var localUid2 = Trim(_identity.UID);
                        var sessionKey2 = BuildRelaySessionKey(localUid2, Trim(peerUid));
                        await AppServices.WanDirectory.TryOfferRendezvousAsync(Trim(peerUid), localUid2, sessionKey2, ct);
                    }
                    catch { }
                }

                foreach (var relay in relayCandidates)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var ok = await TryConnectViaRelayEndpointAsync(peerUid, relay.Host, relay.Port, relay.Display, ct);
                    sw.Stop();
                    RecordRelayAttemptResult(relay.Host, relay.Port, ok, sw.Elapsed.TotalMilliseconds);
                    if (ok) return true;
                }

                if (round < relayRounds - 1)
                {
                    try { await Task.Delay(3000, ct); } catch { }
                }
            }

            return false;
        }

        private async Task<bool> TryConnectViaRelayEndpointAsync(string peerUid, string relayHost, int relayPort, string relayDisplay, CancellationToken ct)
        {
            var localUid = Trim(_identity.UID);
            var sessionKey = BuildRelaySessionKey(localUid, Trim(peerUid));
            return await TryConnectViaRelaySessionAsync(peerUid, sessionKey, relayHost, relayPort, relayDisplay, ct);
        }

        private static bool IsRelayInitiator(string localUid, string peerUid)
        {
            var local = Trim(localUid);
            var peer = Trim(peerUid);
            if (string.IsNullOrWhiteSpace(local) || string.IsNullOrWhiteSpace(peer))
            {
                return true;
            }
            return string.Compare(local, peer, StringComparison.OrdinalIgnoreCase) < 0;
        }

        public async Task<bool> TryConnectViaRelayInviteAsync(string sourceUid, string sessionKey, CancellationToken ct)
        {
            try
            {
                var normalizedSource = Trim(sourceUid);
                if (string.IsNullOrWhiteSpace(normalizedSource) || string.IsNullOrWhiteSpace(sessionKey)) return false;
                if (AppServices.Settings.Settings.BlockList?.Contains(normalizedSource) == true) return false;

                var s = AppServices.Settings.Settings;
                if (!s.RelayFallbackEnabled) return false;

                var relayCandidates = BuildRelayCandidates(s);
                if (relayCandidates.Count == 0) return false;

                foreach (var relay in relayCandidates)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var ok = await TryConnectViaRelaySessionAsync(normalizedSource, sessionKey, relay.Host, relay.Port, relay.Display, ct);
                    sw.Stop();
                    RecordRelayAttemptResult(relay.Host, relay.Port, ok, sw.Elapsed.TotalMilliseconds);
                    if (ok) return true;
                }
            }
            catch { }

            return false;
        }

        private async Task<bool> TryConnectViaRelaySessionAsync(string peerUid, string sessionKey, string relayHost, int relayPort, string relayDisplay, CancellationToken ct)
        {
            var normalizedSessionKey = (sessionKey ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedSessionKey)) return false;

            if (!_relaySessionConnectInFlight.TryAdd(normalizedSessionKey, 0))
            {
                SafeNetLog($"connect relay skipped in-flight | peer={peerUid} | session={normalizedSessionKey}");
                return false;
            }

            try
            {
                var localUid = Trim(_identity.UID);
                var relayClient = await _nat.TryRelayAsync(relayHost, relayPort, localUid, Trim(peerUid), normalizedSessionKey, ct);
                if (relayClient == null) { SafeNetLog($"connect relay fail | peer={peerUid} | server={relayDisplay}"); return false; }
                var relayStream = relayClient.GetStream();

                // Wait for relay acknowledgment (QUEUED or PAIRED)
                string? response = null;
                var ackTimeout = GetRelayAckTimeout();
                var ackStart = DateTime.UtcNow;
                using var ackCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                ackCts.CancelAfter(ackTimeout);
                try
                {
                    response = await ReadLineAsync(relayStream, ackTimeout, ackCts.Token);
                }
                catch (OperationCanceledException)
                {
                    Logger.Log("Relay acknowledgment timeout");
                    try { _diag.IncRelayFail(); } catch { }
                    SafeNetLog($"connect relay ack-timeout | peer={peerUid}");
                    try { relayClient.Close(); } catch { }
                    return false;
                }

                if (string.IsNullOrEmpty(response))
                {
                    Logger.Log("Relay acknowledgment missing");
                    try { _diag.IncRelayFail(); } catch { }
                    SafeNetLog($"connect relay ack-missing | peer={peerUid}");
                    try { relayClient.Close(); } catch { }
                    return false;
                }

                if (response.StartsWith("QUEUED", StringComparison.OrdinalIgnoreCase))
                {
                    ObserveRelayAckWait(DateTime.UtcNow - ackStart);
                    Logger.Log($"Relay session queued, waiting for peer to connect...");
                    SafeNetLog($"connect relay queued | peer={peerUid}");
                    
                    // Wait for PAIRED message (peer needs to connect)
                    // Adaptive timeout tuned from observed peer-arrival latency.
                    var pairTimeout = GetRelayPairWaitTimeout();
                    var pairStart = DateTime.UtcNow;
                    using var pairCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    pairCts.CancelAfter(pairTimeout);
                    try
                    {
                        response = await ReadLineAsync(relayStream, pairTimeout, pairCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        Logger.Log("Relay pairing timeout - peer did not connect");
                        try { _diag.IncRelayFail(); } catch { }
                        SafeNetLog($"connect relay pair-timeout | peer={peerUid}");
                        try { relayClient.Close(); } catch { }
                        return false;
                    }

                    if (!string.IsNullOrWhiteSpace(response) && response.StartsWith("PAIRED", StringComparison.OrdinalIgnoreCase))
                    {
                        ObserveRelayPairWait(DateTime.UtcNow - pairStart);
                    }
                }
                else
                {
                    ObserveRelayAckWait(DateTime.UtcNow - ackStart);
                }

                if (string.IsNullOrEmpty(response) || !response.StartsWith("PAIRED", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Log($"Relay pairing failed: {response ?? "null"}");
                    try { _diag.IncRelayFail(); } catch { }
                    SafeNetLog($"connect relay pair-fail | peer={peerUid} | response={response}");
                    try { relayClient.Close(); } catch { }
                    return false;
                }

                Logger.Log("Relay confirmed pairing, starting ECDH handshake...");
                SafeNetLog($"connect relay paired | peer={peerUid}");

                // NOW we can start ECDH handshake - both clients are ready!
                using var dh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
                var pub = dh.PublicKey.ExportSubjectPublicKeyInfo();
                await WriteFrame(relayStream, pub, ct);
                byte[] peerPub;
                try
                {
                    using var hcts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    hcts.CancelAfter(TimeSpan.FromSeconds(5));
                    var (ok, payload, expected, actual, reason) = await TryReadHandshakeFrame(relayStream, hcts.Token);
                    if (!ok || payload == null)
                    {
                        Logger.Log($"Relay handshake read error: {reason} (got {actual} / expected {expected} bytes)");
                        try { _diag.IncHandshakeFail(reason); } catch { }
                        SafeNetLog($"connect relay handshake-fail | peer={peerUid} | reason={reason}");
                        return false;
                    }
                    peerPub = payload;
                }
                catch (OperationCanceledException)
                {
                    Logger.Log("Relay handshake read error: timeout");
                    try { _diag.IncHandshakeFail("timeout"); } catch { }
                    SafeNetLog($"connect relay handshake-timeout | peer={peerUid}");
                    return false;
                }
                catch (Exception ex)
                {
                    Logger.Log($"Relay handshake read error: {ex.Message}");
                    try { _diag.IncHandshakeFail(ex.Message); } catch { }
                    SafeNetLog($"connect relay handshake-ex | peer={peerUid} | {ex.Message}");
                    return false;
                }

                using var peerKey = ECDiffieHellman.Create();
                peerKey.ImportSubjectPublicKeyInfo(peerPub, out _);
                var derivedUid = IdentityService.ComputeUidFromPublicKey(peerPub);
                if (!string.Equals(Trim(peerUid), Trim(derivedUid), StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Log($"Relay handshake UID mismatch: claimed={peerUid} derived={derivedUid}");
                    try { _diag.IncHandshakeFail("uid-mismatch"); } catch { }
                    SafeNetLog($"connect relay uid-mismatch | peer={peerUid} | derived={derivedUid}");
                    return false;
                }

                var secret = dh.DeriveKeyMaterial(peerKey.PublicKey);
                var info = Encoding.UTF8.GetBytes("Zer0Talk-session");
                var prk = Hkdf.DeriveKey(secret, salt: Array.Empty<byte>(), info: info, length: 32 + 32 + 16 + 16);
                CryptographicOperations.ZeroMemory(secret);
                var txKey = new byte[32]; var rxKey = new byte[32]; var txBase = new byte[16]; var rxBase = new byte[16];
                var relayInitiator = true;
                var ourPub = dh.PublicKey.ExportSubjectPublicKeyInfo();
                var comparison = CompareBytes(ourPub, peerPub);
                if (comparison > 0)
                {
                    relayInitiator = false;
                }
                if (relayInitiator)
                {
                    Buffer.BlockCopy(prk, 0, txKey, 0, 32); Buffer.BlockCopy(prk, 32, rxKey, 0, 32);
                    Buffer.BlockCopy(prk, 64, txBase, 0, 16); Buffer.BlockCopy(prk, 80, rxBase, 0, 16);
                }
                else
                {
                    Buffer.BlockCopy(prk, 0, rxKey, 0, 32); Buffer.BlockCopy(prk, 32, txKey, 0, 32);
                    Buffer.BlockCopy(prk, 64, rxBase, 0, 16); Buffer.BlockCopy(prk, 80, txBase, 0, 16);
                }
                var transport = new Utilities.AeadTransport(relayStream, txKey, rxKey, txBase, rxBase);
                try { _handshakePeerKeys[transport] = peerPub; } catch { }

                var normUid = Trim(peerUid);
                _sessions[normUid] = transport;
                _sessionModes[normUid] = Models.ConnectionMode.Relay;
                try
                {
                    Logger.Log($"[sess] add | mode=relay | peer={normUid} | total={_sessions.Count} | ts={DateTime.UtcNow:o}");
                    SafeNetLog($"session add relay | key={normUid} | total={_sessions.Count}");
                }
                catch { }
                RaiseSessionCountChanged();
                try { AppServices.Peers.SetObservedPublicKey(Trim(peerUid), peerPub); } catch { }
                try { _diag.IncHandshakeOk(); _diag.IncSessionsActive(); } catch { }
                try { HandshakeCompleted?.Invoke(true, Trim(peerUid), null); } catch { }
                try { Zer0Talk.Services.AppServices.Contacts.SetLastKnownEncrypted(Trim(peerUid), true, Zer0Talk.Services.AppServices.Passphrase); } catch { }
                try { _diag.IncRelaySuccess(); } catch { }
                SafeNetLog($"connect relay success | peer={peerUid} | server={relayDisplay}");

                // Create a session-lifetime cancellation token independent of connection timeout
                var sessionCts = new CancellationTokenSource();
                var sessionCt = sessionCts.Token;

                _ = SendIdentityAnnounceAsync(transport, pub, sessionCt);

                // Start relay session read loop
                _ = Task.Run(async () =>
                {
                    try
                    {
                        while (!sessionCt.IsCancellationRequested)
                        {
                            var data = await transport.ReadAsync(sessionCt);
                            if (data.Length > 0)
                            {
                                await HandleInboundFrameAsync(Trim(peerUid), transport, data, sessionCt);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Relay session error: {ex.Message}");
                        try { SafeNetLog($"relay session error | peer={Trim(peerUid)} | {ex.GetType().Name}:{ex.Message}"); } catch { }
                    }
                    finally
                    {
                        var removed = _sessions.TryRemove(Trim(peerUid), out _);
                        try { _sessionModes.TryRemove(Trim(peerUid), out _); } catch { }
                        try { if (removed) Logger.Log($"[sess] remove | mode=relay | peer={Trim(peerUid)} | reason=reader-exit | ts={DateTime.UtcNow:o}"); } catch { }
                        if (removed) RaiseSessionCountChanged();
                        try { _diag.DecSessionsActive(); } catch { }
                        try { _handshakePeerKeys.TryRemove(transport, out _); } catch { }
                        try { AppServices.Contacts.SetLastKnownEncrypted(Trim(peerUid), false, AppServices.Passphrase); } catch { }
                        // On session close: mark peer Offline immediately
                        try { AppServices.Peers.SetPeerStatus(Trim(peerUid), "Offline"); } catch { }
                        try { sessionCts.Dispose(); } catch { }
                        try { relayClient.Close(); } catch { }
                    }
                }, CancellationToken.None);

                // Start keepalive task to detect dead connections early
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var keepalivePayload = Array.Empty<byte>();
                        while (!sessionCt.IsCancellationRequested)
                        {
                            try { await Task.Delay(30000, sessionCt); } catch { break; }
                            if (sessionCt.IsCancellationRequested) break;
                            
                            try
                            {
                                await transport.WriteAsync(keepalivePayload, sessionCt);
                            }
                            catch (Exception ex)
                            {
                                Logger.Log($"Relay keepalive failed for {Trim(peerUid)}: {ex.Message}");
                                try { sessionCts.Cancel(); } catch { }
                                break;
                            }
                        }
                    }
                    catch { }
                }, CancellationToken.None);

                try { _ = SendPresenceAsync(Trim(peerUid), AppServices.Settings.Settings.Status, sessionCt); } catch { }
                try
                {
                    if (_identity.ShareAvatar && _identity.AvatarBytes != null && _identity.AvatarBytes.Length > 0)
                    {
                        var payload = BuildAvatarFrame(_identity.AvatarBytes);
                        await transport.WriteAsync(payload, sessionCt);
                    }
                }
                catch (Exception ex) { Logger.Log($"Relay avatar send error: {ex.Message}"); }
                try { var bioFrame = BuildBioFrame(_identity.Bio); await transport.WriteAsync(bioFrame, sessionCt); } catch { }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"Relay connect/handshake exception: {ex.Message}");
                SafeNetLog($"connect relay exception | peer={peerUid} | {ex.Message}");
                return false;
            }
            finally
            {
                _relaySessionConnectInFlight.TryRemove(normalizedSessionKey, out _);
            }
        }

        private Task HandleInboundFrameAsync(string peerUid, Utilities.AeadTransport transport, byte[] data, CancellationToken ct)
        {
            // Block check: Reject all incoming frames from blocked peers
            var normalizedUid = Trim(peerUid);
            if (AppServices.Settings.Settings.BlockList?.Contains(normalizedUid) == true)
            {
                Logger.Log($"Frame from blocked peer rejected: {normalizedUid}");
                return Task.CompletedTask;
            }

            if (TryParseSecurityAlert(data, out var secReason, out var secMsg))
            {
                var details = string.IsNullOrWhiteSpace(secMsg)
                    ? "Remote reported a key mismatch. Conversation was stopped."
                    : secMsg;
                NotifyKeyMismatch(peerUid, details);
                EnforceKeyMismatchAndTerminate(peerUid, transport, details, notifyRemote: false);
                return Task.CompletedTask;
            }
            
            if (data[0] == 0xA1)
            {
                try
                {
                    int idx = 1;
                    if (data.Length < idx + 1) return Task.CompletedTask;
                    int pubLen = data[idx++];
                    if (pubLen != 32 || data.Length < idx + pubLen + 1) return Task.CompletedTask;
                    var pub = new byte[pubLen];
                    Buffer.BlockCopy(data, idx, pub, 0, pubLen); idx += pubLen;
                    int sigLen = data[idx++];
                    if (sigLen != 64 || data.Length < idx + sigLen) return Task.CompletedTask;
                    var sig = new byte[sigLen];
                    Buffer.BlockCopy(data, idx, sig, 0, sigLen); idx += sigLen;
                    
                    // Parse version information (optional for backward compatibility)
                    string? peerVersion = null;
                    if (data.Length > idx)
                    {
                        int versionLen = data[idx++];
                        if (versionLen > 0 && data.Length >= idx + versionLen)
                        {
                            var versionBytes = new byte[versionLen];
                            Buffer.BlockCopy(data, idx, versionBytes, 0, versionLen);
                            peerVersion = Encoding.UTF8.GetString(versionBytes);
                        }
                    }
                    
                    if (!_handshakePeerKeys.TryGetValue(transport, out var peerSpki) || peerSpki == null || peerSpki.Length == 0)
                    {
                        Logger.Log("Identity announce received but missing handshake key; ignoring");
                        return Task.CompletedTask;
                    }
                    if (!IdentityService.Verify(peerSpki, sig, pub))
                    {
                        Logger.Log("Identity announce verification failed; dropping");
                        return Task.CompletedTask;
                    }
                    var claimedUid = IdentityService.ComputeUidFromPublicKey(pub);
                    var normClaimed = Trim(claimedUid);
                    var normSession = Trim(peerUid);

                    if (TryGetExpectedKeyMismatch(normClaimed, pub, out var expectedHex, out var observedHex))
                    {
                        var details = $"Expected key {expectedHex}, observed {observedHex}.";
                        EnforceKeyMismatchAndTerminate(normClaimed, transport, details, "Key mismatch detected. Conversation cannot continue.");
                        return Task.CompletedTask;
                    }
                    
                    // Store peer version and check compatibility
                    if (!string.IsNullOrEmpty(peerVersion))
                    {
                        _peerVersions[normClaimed] = peerVersion;
                        
                        // Check for version mismatch
                        if (!AppInfo.IsVersionCompatible(AppInfo.Version, peerVersion))
                        {
                            Logger.Log($"Version mismatch detected: peer {normClaimed} version {peerVersion}, our version {AppInfo.Version}");
                            VersionMismatchDetected?.Invoke(normClaimed, AppInfo.Version, peerVersion);
                        }
                    }
                    
                    if (!string.Equals(normClaimed, normSession, StringComparison.OrdinalIgnoreCase))
                    {
                        var details = $"Identity announce UID mismatch: session {normSession}, claimed {normClaimed}.";
                        EnforceKeyMismatchAndTerminate(normSession, transport, details, "Identity/key mismatch detected. Conversation cannot continue.");
                        return Task.CompletedTask;
                    }
                    try { AppServices.Peers.SetObservedPublicKey(normClaimed, pub); } catch { }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Identity announce handle error: {ex.Message}");
                }
                return Task.CompletedTask;
            }
            if (data[0] == 0xA2 && data.Length >= 5)
            {
                var len = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(1, 4));
                if (len > 0 && data.Length >= 5 + len)
                {
                    var avatar = new byte[len];
                    Buffer.BlockCopy(data, 5, avatar, 0, (int)len);
                    if (ShouldAcceptAvatar(Trim(peerUid), (int)len, avatar)) SavePeerAvatarToCache(peerUid, avatar);
                }
            }
            else if (data[0] == 0xD1)
            {
                int idx = 1; if (data.Length < idx + 2) return Task.CompletedTask; ushort blen = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(idx, 2)); idx += 2;
                if (blen > MaxBioBytes || data.Length < idx + blen) return Task.CompletedTask;
                var bytes = blen > 0 ? data.AsSpan(idx, blen).ToArray() : Array.Empty<byte>();
                var bio = bytes.Length == 0 ? string.Empty : Encoding.UTF8.GetString(bytes);
                try { AppServices.Contacts.SetBio(Trim(peerUid), bio, AppServices.Passphrase); } catch { }
            }
            else if (data[0] == 0xB0)
            {
                // Chat message (signed): [0xB0][msgId(16)][len(2)][utf8 content][pubLen(1)=32][pub(32)][sigLen(1)=64][sig(64)]
                int idx = 1;
                if (data.Length < idx + 16 + 2 + 1 + 32 + 1 + 64) return Task.CompletedTask;
                var guidBytes = new byte[16]; Buffer.BlockCopy(data, idx, guidBytes, 0, 16); idx += 16; var msgId = new Guid(guidBytes);
                if (!TryAcceptInboundFrameId(peerUid, 0xB0, msgId))
                {
                    Logger.Log($"Duplicate/replay chat message dropped from {Trim(peerUid)} id={msgId}");
                    return Task.CompletedTask;
                }
                ushort len = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(idx, 2)); idx += 2; if (len > MaxChatBytes) return Task.CompletedTask;
                if (len < 0 || data.Length < idx + len + 1 + 32 + 1 + 64) return Task.CompletedTask;
                var txtBytes = data.AsSpan(idx, len).ToArray(); idx += len;
                int pubLen = data[idx++]; if (pubLen != 32 || data.Length < idx + pubLen + 1 + 64) return Task.CompletedTask;
                var pub = new byte[32]; Buffer.BlockCopy(data, idx, pub, 0, 32); idx += 32;
                int sigLen = data[idx++]; if (sigLen != 64 || data.Length < idx + sigLen) return Task.CompletedTask;
                var sig = new byte[64]; Buffer.BlockCopy(data, idx, sig, 0, 64);
                var idxAfterSig = idx + 64;
                // Skip any optional trailing data (legacy retention field or future extensions)
                try
                {
                    if (data.Length > idxAfterSig)
                    {
                        var remaining = data.Length - idxAfterSig;
                        if (remaining > 0)
                        {
                            // First byte indicates optional payload length; ensure it's within bounds
                            var optLen = data[idxAfterSig];
                            if (remaining >= 1 + optLen)
                            {
                                idxAfterSig += 1 + optLen;
                            }
                        }
                    }
                }
                catch { }
                var payloadToSign = new byte[16 + 2 + txtBytes.Length];
                Buffer.BlockCopy(guidBytes, 0, payloadToSign, 0, 16);
                BinaryPrimitives.WriteUInt16BigEndian(payloadToSign.AsSpan(16, 2), (ushort)txtBytes.Length);
                Buffer.BlockCopy(txtBytes, 0, payloadToSign, 18, txtBytes.Length);
                var claimedUid = IdentityService.ComputeUidFromPublicKey(pub);
                if (!string.Equals(Trim(claimedUid), Trim(peerUid), StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Log($"Spoofed message rejected: claimed {claimedUid} != session {peerUid}");
                    try { AppServices.Peers.Block(Trim(claimedUid)); } catch { }
                    EnforceKeyMismatchAndTerminate(peerUid, transport, $"Message signer UID mismatch: claimed {Trim(claimedUid)} != session {Trim(peerUid)}.", "Message key mismatch detected. Conversation cannot continue.");
                    return Task.CompletedTask;
                }
                if (!IdentityService.Verify(payloadToSign, sig, pub))
                {
                    Logger.Log("Invalid signature; message dropped");
                    return Task.CompletedTask;
                }
                var content = Encoding.UTF8.GetString(txtBytes);
                Logger.Log($"Msg from {Trim(peerUid)} len={content.Length} id={msgId}");
                try { ChatMessageReceived?.Invoke(peerUid, msgId, content); } catch { }
                // Send Chat-Received ACK: [0xB5][msgId]
                var ack = new byte[1 + 16]; ack[0] = 0xB5; Buffer.BlockCopy(guidBytes, 0, ack, 1, 16);
                try { _ = TrySendEncryptedAsync(Trim(peerUid), ack, CancellationToken.None); } catch { }
            }
            else if (data[0] == 0xB1)
            {
                // Edit message (signed): [0xB1][msgId(16)][len(2)][utf8 content][pubLen(1)=32][pub(32)][sigLen(1)=64][sig(64)]
                int idx = 1;
                if (data.Length < idx + 16 + 2 + 1 + 32 + 1 + 64) return Task.CompletedTask;
                var guidBytes = new byte[16]; Buffer.BlockCopy(data, idx, guidBytes, 0, 16); idx += 16; var msgId = new Guid(guidBytes);
                if (!TryAcceptInboundFrameId(peerUid, 0xB1, msgId))
                {
                    Logger.Log($"Duplicate/replay edit dropped from {Trim(peerUid)} id={msgId}");
                    return Task.CompletedTask;
                }
                ushort len = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(idx, 2)); idx += 2; if (len > MaxChatBytes) return Task.CompletedTask;
                if (len <= 0 || data.Length < idx + len + 1 + 32 + 1 + 64) return Task.CompletedTask;
                var txtBytes = data.AsSpan(idx, len).ToArray(); idx += len;
                int pubLen = data[idx++]; if (pubLen != 32 || data.Length < idx + pubLen + 1 + 64) return Task.CompletedTask;
                var pub = new byte[32]; Buffer.BlockCopy(data, idx, pub, 0, 32); idx += 32;
                int sigLen = data[idx++]; if (sigLen != 64 || data.Length < idx + sigLen) return Task.CompletedTask;
                var sig = new byte[64]; Buffer.BlockCopy(data, idx, sig, 0, 64);
                var payloadToSign = new byte[16 + 2 + txtBytes.Length];
                Buffer.BlockCopy(guidBytes, 0, payloadToSign, 0, 16);
                BinaryPrimitives.WriteUInt16BigEndian(payloadToSign.AsSpan(16, 2), (ushort)txtBytes.Length);
                Buffer.BlockCopy(txtBytes, 0, payloadToSign, 18, txtBytes.Length);
                var claimed = IdentityService.ComputeUidFromPublicKey(pub);
                if (!string.Equals(Trim(claimed), Trim(peerUid), StringComparison.OrdinalIgnoreCase)) { Logger.Log("Edit spoof rejected: UID mismatch"); EnforceKeyMismatchAndTerminate(peerUid, transport, $"Edit signer UID mismatch: claimed {Trim(claimed)} != session {Trim(peerUid)}.", "Edit key mismatch detected. Conversation cannot continue."); return Task.CompletedTask; }
                if (!IdentityService.Verify(payloadToSign, sig, pub)) { Logger.Log("Edit bad signature"); return Task.CompletedTask; }
                var txt = Encoding.UTF8.GetString(txtBytes);
                try { AppServices.MessagesUpdateFromRemote(Trim(peerUid), msgId, txt); } catch { }
                try { ChatMessageEdited?.Invoke(Trim(peerUid), msgId, txt); } catch { }
                // Send ACK: [0xB3][msgId]
                var ack = new byte[1 + 16]; ack[0] = 0xB3; Buffer.BlockCopy(guidBytes, 0, ack, 1, 16);
                try { _ = TrySendEncryptedAsync(Trim(peerUid), ack, CancellationToken.None); } catch { }
            }
            else if (data[0] == 0xB2)
            {
                // Delete message (signed): [0xB2][msgId(16)][pubLen(1)=32][pub(32)][sigLen(1)=64][sig(64)], sig over msgId
                int idx = 1; if (data.Length < idx + 16 + 1 + 32 + 1 + 64) return Task.CompletedTask;
                var guidBytes = new byte[16]; Buffer.BlockCopy(data, idx, guidBytes, 0, 16); idx += 16;
                var msgId = new Guid(guidBytes);
                if (!TryAcceptInboundFrameId(peerUid, 0xB2, msgId))
                {
                    Logger.Log($"Duplicate/replay delete dropped from {Trim(peerUid)} id={msgId}");
                    return Task.CompletedTask;
                }
                int pubLen = data[idx++]; if (pubLen != 32 || data.Length < idx + pubLen + 1 + 64) return Task.CompletedTask;
                var pub = new byte[32]; Buffer.BlockCopy(data, idx, pub, 0, 32); idx += 32;
                int sigLen = data[idx++]; if (sigLen != 64 || data.Length < idx + sigLen) return Task.CompletedTask;
                var sig = new byte[64]; Buffer.BlockCopy(data, idx, sig, 0, 64);
                var claimed = IdentityService.ComputeUidFromPublicKey(pub);
                if (!string.Equals(Trim(claimed), Trim(peerUid), StringComparison.OrdinalIgnoreCase)) { Logger.Log("Delete spoof rejected: UID mismatch"); EnforceKeyMismatchAndTerminate(peerUid, transport, $"Delete signer UID mismatch: claimed {Trim(claimed)} != session {Trim(peerUid)}.", "Delete key mismatch detected. Conversation cannot continue."); return Task.CompletedTask; }
                if (!IdentityService.Verify(guidBytes, sig, pub)) { Logger.Log("Delete bad signature"); return Task.CompletedTask; }
                try { AppServices.MessagesDeleteFromRemote(Trim(peerUid), msgId); } catch { }
                try { ChatMessageDeleted?.Invoke(Trim(peerUid), msgId); } catch { }
                // Send ACK: [0xB4][msgId]
                var ack = new byte[1 + 16]; ack[0] = 0xB4; Buffer.BlockCopy(guidBytes, 0, ack, 1, 16);
                try { _ = TrySendEncryptedAsync(Trim(peerUid), ack, CancellationToken.None); } catch { }
            }
            else if (data[0] == 0xB3)
            {
                // Edit ACK: [0xB3][msgId(16)]
                int idx = 1; if (data.Length < idx + 16) return Task.CompletedTask;
                var guidBytes = new byte[16]; Buffer.BlockCopy(data, idx, guidBytes, 0, 16);
                var msgId = new Guid(guidBytes);
                try { ChatMessageEditAcked?.Invoke(Trim(peerUid), msgId); } catch { }
            }
            else if (data[0] == 0xB4)
            {
                // Delete ACK: [0xB4][msgId(16)]
                int idx = 1; if (data.Length < idx + 16) return Task.CompletedTask;
                var guidBytes = new byte[16]; Buffer.BlockCopy(data, idx, guidBytes, 0, 16);
                var msgId = new Guid(guidBytes);
                try { ChatMessageDeleteAcked?.Invoke(Trim(peerUid), msgId); } catch { }
            }
            else if (data[0] == 0xB5)
            {
                // Chat Received ACK: [0xB5][msgId(16)] - peer confirmed delivery
                int idx = 1; if (data.Length < idx + 16) return Task.CompletedTask;
                var guidBytes5 = new byte[16]; Buffer.BlockCopy(data, idx, guidBytes5, 0, 16);
                var msgId5 = new Guid(guidBytes5);
                Logger.Log($"Delivery ACK from {Trim(peerUid)} id={msgId5}");
                try { ChatMessageDeliveryAcked?.Invoke(Trim(peerUid), msgId5); } catch { }
            }
            else if (data[0] == 0xB6)
            {
                // Reaction event (signed): [0xB6][eventId(16)][msgId(16)][op(1:add,0:remove)][emojiLen(1)][emojiUtf8][pubLen(1)=32][pub(32)][sigLen(1)=64][sig(64)]
                int idx = 1;
                if (data.Length < idx + 16 + 16 + 1 + 1 + 1 + 32 + 1 + 64) return Task.CompletedTask;

                var eventIdBytes = new byte[16]; Buffer.BlockCopy(data, idx, eventIdBytes, 0, 16); idx += 16;
                var eventId = new Guid(eventIdBytes);
                if (!TryAcceptInboundFrameId(peerUid, 0xB6, eventId))
                {
                    Logger.Log($"Duplicate/replay reaction dropped from {Trim(peerUid)} event={eventId}");
                    return Task.CompletedTask;
                }

                var messageIdBytes = new byte[16]; Buffer.BlockCopy(data, idx, messageIdBytes, 0, 16); idx += 16;
                var messageId = new Guid(messageIdBytes);
                var op = data[idx++];
                var isAdd = op == 1;
                var emojiLen = data[idx++];
                if (emojiLen <= 0 || emojiLen > MaxReactionEmojiBytes || data.Length < idx + emojiLen + 1 + 32 + 1 + 64) return Task.CompletedTask;
                var emojiBytes = data.AsSpan(idx, emojiLen).ToArray(); idx += emojiLen;
                var emoji = Encoding.UTF8.GetString(emojiBytes).Trim();
                if (string.IsNullOrWhiteSpace(emoji)) return Task.CompletedTask;
                int pubLen = data[idx++]; if (pubLen != 32 || data.Length < idx + pubLen + 1 + 64) return Task.CompletedTask;
                var pub = new byte[32]; Buffer.BlockCopy(data, idx, pub, 0, 32); idx += 32;
                int sigLen = data[idx++]; if (sigLen != 64 || data.Length < idx + sigLen) return Task.CompletedTask;
                var sig = new byte[64]; Buffer.BlockCopy(data, idx, sig, 0, 64);

                var payloadToSign = new byte[16 + 16 + 1 + 1 + emojiBytes.Length];
                int p = 0;
                Buffer.BlockCopy(eventIdBytes, 0, payloadToSign, p, 16); p += 16;
                Buffer.BlockCopy(messageIdBytes, 0, payloadToSign, p, 16); p += 16;
                payloadToSign[p++] = op;
                payloadToSign[p++] = (byte)emojiBytes.Length;
                Buffer.BlockCopy(emojiBytes, 0, payloadToSign, p, emojiBytes.Length);

                var claimed = IdentityService.ComputeUidFromPublicKey(pub);
                if (!string.Equals(Trim(claimed), Trim(peerUid), StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Log("Reaction spoof rejected: UID mismatch");
                    EnforceKeyMismatchAndTerminate(peerUid, transport, $"Reaction signer UID mismatch: claimed {Trim(claimed)} != session {Trim(peerUid)}.", "Reaction key mismatch detected. Conversation cannot continue.");
                    return Task.CompletedTask;
                }
                if (!IdentityService.Verify(payloadToSign, sig, pub))
                {
                    Logger.Log("Reaction bad signature");
                    return Task.CompletedTask;
                }

                try { ChatMessageReactionReceived?.Invoke(Trim(peerUid), messageId, emoji, isAdd); } catch { }
            }
            else if (data[0] == 0xC0)
            {
                int idx = 1; int nlen = data[idx++]; if (data.Length < idx + nlen + 32 + 64 + 1) return Task.CompletedTask;
                var nonce = Encoding.UTF8.GetString(data, idx, nlen); idx += nlen;
                var pub = new byte[32]; Buffer.BlockCopy(data, idx, pub, 0, 32); idx += 32;
                var sig = new byte[64]; Buffer.BlockCopy(data, idx, sig, 0, 64); idx += 64;
                int dnLen = data[idx++]; if (dnLen < 0 || dnLen > MaxDisplayNameBytes) return Task.CompletedTask; var dn = dnLen > 0 ? Encoding.UTF8.GetString(data, idx, Math.Min(dnLen, data.Length - idx)) : string.Empty;
                var claimed = IdentityService.ComputeUidFromPublicKey(pub);
                if (!string.Equals(Trim(claimed), Trim(peerUid), StringComparison.OrdinalIgnoreCase)) { Logger.Log("Contact req spoofed"); return Task.CompletedTask; }
                var payload = Encoding.UTF8.GetBytes(nonce);
                if (!IdentityService.Verify(payload, sig, pub)) { Logger.Log("Contact req bad sig"); return Task.CompletedTask; }
                try { SafeNetLog($"recv C0 contact-request | peer={Trim(peerUid)} | nonce={nonce} | dnLen={dnLen}"); } catch { }
                _ = AppServices.ContactRequests.OnInboundRequestAsync(Trim(peerUid), nonce, dn ?? string.Empty);
            }
            else if (data[0] == 0xC1)
            {
                int idx = 1;
                int nlen = data[idx++];
                if (data.Length < idx + nlen) return Task.CompletedTask;
                var nonce = Encoding.UTF8.GetString(data, idx, nlen);
                idx += nlen;
                // Parse display name if present (new protocol)
                string? displayName = null;
                if (data.Length > idx)
                {
                    int dnLen = data[idx++];
                    if (data.Length >= idx + dnLen && dnLen > 0)
                    {
                        displayName = Encoding.UTF8.GetString(data, idx, dnLen);
                    }
                }
                try { SafeNetLog($"recv C1 contact-accept | peer={Trim(peerUid)} | nonce={nonce} | dnLen={displayName?.Length ?? 0}"); } catch { }
                AppServices.ContactRequests.OnInboundAccept(nonce, Trim(peerUid), displayName);
            }
            else if (data[0] == 0xC2)
            {
                int idx = 1; int nlen = data[idx++]; if (data.Length < idx + nlen) return Task.CompletedTask; var nonce = Encoding.UTF8.GetString(data, idx, nlen);
                try { SafeNetLog($"recv C2 contact-cancel | peer={Trim(peerUid)} | nonce={nonce}"); } catch { }
                AppServices.ContactRequests.OnInboundCancel(nonce);
            }
            else if (data[0] == 0xC3)
            {
                // Verification intent (no payload besides opcode)
                try { AppServices.ContactRequests.OnInboundVerifyIntent(Trim(peerUid)); } catch { }
            }
            else if (data[0] == 0xC4)
            {
                // Verification request (no payload). Receiver should show notification with Start/Decline.
                try { AppServices.ContactRequests.OnInboundVerifyRequest(Trim(peerUid)); } catch { }
            }
            else if (data[0] == 0xC5)
            {
                // Verification cancel (no payload). Receiver should dismiss pending and show info.
                try { AppServices.ContactRequests.OnInboundVerifyCancel(Trim(peerUid)); } catch { }
            }
            else if (data[0] == 0xC6)
            {
                // Verification complete notification from peer - they have verified us
                try { AppServices.ContactRequests.OnInboundVerifyComplete(Trim(peerUid)); } catch { }
            }
            else if (data[0] == 0xD0)
            {
                int idx = 1; if (data.Length <= idx) return Task.CompletedTask; int n = data[idx++]; if (n < 0 || n > MaxPresenceToken || data.Length < idx + n) return Task.CompletedTask;
                var tok = System.Text.Encoding.UTF8.GetString(data, idx, n).Trim().ToLowerInvariant();
                string status = tok switch { "on" => "Online", "idle" => "Idle", "dnd" => "Do Not Disturb", "inv" => "Invisible", "off" => "Offline", _ => "Offline" };
                try { AppServices.Peers.SetPeerStatus(Trim(peerUid), status); } catch { }
                try { _presenceLastSeenUtc[Trim(peerUid)] = DateTime.UtcNow; } catch { }
                try { PresenceReceived?.Invoke(Trim(peerUid), status); } catch { }
                try
                {
                    var myStatus = AppServices.Settings.Settings.Status;
                    if (myStatus != Models.PresenceStatus.Invisible)
                    {
                        var key = Trim(peerUid);
                        var now = DateTime.UtcNow;
                        if (!_presenceLastSentUtc.TryGetValue(key, out var last) || (now - last) > TimeSpan.FromSeconds(10))
                        {
                            _ = SendPresenceAsync(key, myStatus, CancellationToken.None);
                        }
                    }
                }
                catch { }
            }
            return Task.CompletedTask;
        }

        private static void SafeNetLog(string line)
        {
            try
            {
                if (!Utilities.LoggingPaths.Enabled) return;
                var path = Utilities.LoggingPaths.Network;
                System.IO.File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}\n");
            }
            catch { }
        }

        // [DISCOVERY] Start LAN discovery (multicast + broadcast). Parameter 'port' is the TCP listen port advertised to peers.
        private void StartLanDiscovery(int port)
        {
            try
            {
                var bindIp = SelectPreferredBindAddress();
                PreferredBindAddress = bindIp;
                // Bind a reusable UDP socket on the fixed discovery port for both multicast and broadcast.
                try
                {
                    _udp = new UdpClient(AddressFamily.InterNetwork);
                    _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    _udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                    _udp.EnableBroadcast = true;
                    if (DiscoveryBehavior == DiscoveryMode.Normal)
                    {
                        try { _udp.JoinMulticastGroup(MulticastGroup); } catch (Exception exJoin) { Logger.Log($"UDP multicast join failed: {exJoin.Message}"); }
                        try { _udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1); } catch { }
                    }
                    Logger.Log($"UDP discovery bound on 0.0.0.0:{DiscoveryPort}; multicast={MulticastGroup}");
                    try { AppServices.Discovery.NoteExternalTrigger("lan-discovery-start", $"port={port}"); } catch { }
                    UdpBoundAddress = IPAddress.Any; UdpBoundPort = DiscoveryPort;
                }
                catch (Exception ex)
                {
                    Logger.Log($"UDP discovery bind failed on {DiscoveryPort}: {ex.Message}");
                    UdpBoundPort = null; UdpBoundAddress = IPAddress.Any;
                }
                // [A] If UDP bind failed, skip starting the discovery loop to avoid null dereferences and noisy logs.
                if (_udp == null)
                {
                    Logger.Log("UDP discovery not started because socket bind failed.");
                    return;
                }
                _udpTask = Task.Run(async () =>
                {
                    var token = _cts?.Token ?? CancellationToken.None;
                    var announceInterval = TimeSpan.FromSeconds(5);
                    var lastAnnounce = DateTime.MinValue;
                    // [DISCOVERY] Always advertise on LAN to enable true peer-to-peer discovery; no identifiers are persisted externally.
                    var advertise = true;
                    while (!token.IsCancellationRequested)
                    {
                        // Snapshot UDP reference to avoid races with Stop() nulling the field
                        var udp = _udp;
                        if (udp == null) break;
                        try
                        {
                            if (advertise && (DateTime.UtcNow - lastAnnounce) > announceInterval)
                            {
                                lastAnnounce = DateTime.UtcNow;
                                var payload = BuildBeacon(port);
                                int sent = 0;
                                // [DISCOVERY] Multicast announce first (best-effort, stays within LAN segment)
                                if (DiscoveryBehavior == DiscoveryMode.Normal)
                                {
                                    try
                                    {
                                        await udp.SendAsync(payload, payload.Length, new IPEndPoint(MulticastGroup, DiscoveryPort));
                                        try { _diag.IncUdpBeaconSent(); } catch { }
                                        sent++;
                                        Logger.Log($"UDP beacon (multicast) -> {MulticastGroup}:{DiscoveryPort} | uid={_identity.UID} | dn='{_identity.DisplayName}' | ts={DateTime.UtcNow:o}");
                                    }
                                    catch (Exception exMc)
                                    {
                                        Logger.Log($"UDP multicast beacon failed: {exMc.Message}");
                                    }
                                }
                                foreach (var bcast in GetBroadcastAddressesPreferredFirst(bindIp))
                                {
                                    try
                                    {
                                        await udp.SendAsync(payload, payload.Length, new IPEndPoint(bcast, DiscoveryPort));
                                        try { _diag.IncUdpBeaconSent(); } catch { }
                                        sent++;
                                        Logger.Log($"UDP beacon (broadcast) -> {bcast}:{DiscoveryPort} | uid={_identity.UID} | dn='{_identity.DisplayName}' | ts={DateTime.UtcNow:o}");
                                    }
                                    catch (Exception exSend)
                                    {
                                        Logger.Log($"UDP beacon send failed to {bcast}:{DiscoveryPort}: {exSend.Message}");
                                    }
                                }
                                if (sent == 0 && advertise)
                                {
                                    try
                                    {
                                        await udp.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
                                        try { _diag.IncUdpBeaconSent(); } catch { }
                                        Logger.Log($"UDP beacon (broadcast-fallback) -> 255.255.255.255:{DiscoveryPort}");
                                    }
                                    catch (Exception exSend2)
                                    {
                                        Logger.Log($"UDP beacon fallback send failed: {exSend2.Message}");
                                    }
                                }
                            }
                            // Refresh snapshot in case Stop() ran while sending
                            udp = _udp;
                            if (udp == null) break;
                            if (udp.Available > 0)
                            {
                                var result = await udp.ReceiveAsync(token);
                                try { _diag.IncUdpBeaconRecv(); } catch { }
                                Logger.Log($"UDP beacon <- {result.RemoteEndPoint.Address}:{result.RemoteEndPoint.Port} | ts={DateTime.UtcNow:o}");
                                HandleBeacon(result.Buffer, result.RemoteEndPoint);
                            }
                            else
                            {
                                await Task.Delay(100, token);
                            }
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception ex) { Logger.Log($"UDP discovery error: {ex.Message}"); await Task.Delay(500, token); }
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"LAN discovery start failed: {ex.Message}");
            }
        }

        // [DISCOVERY] Build a LAN beacon announcing our UID, TCP port, public key, display name, and presence.
        private byte[] BuildBeacon(int port)
        {
            var uid = _identity.UID;
            var b64 = Convert.ToBase64String(_identity.PublicKey);
            var dn = _identity.DisplayName?.Replace("|", "/") ?? string.Empty;
            // Presence: compact tokens to keep payload small
            var presence = AppServices.Settings.Settings.Status switch
            {
                Models.PresenceStatus.Online => "on",
                Models.PresenceStatus.Idle => "idle",
                Models.PresenceStatus.DoNotDisturb => "dnd",
                Models.PresenceStatus.Invisible => "inv",
                _ => "on"
            };
            var s = $"{uid}|{port}|{b64}|{dn}|{presence}";
            return Encoding.UTF8.GetBytes(s);
        }

        // [DISCOVERY] Handle incoming LAN beacons on DiscoveryPort; extract the peer's TCP port for potential connection.
        private void HandleBeacon(byte[] bytes, IPEndPoint remote)
        {
            try
            {
                var text = Encoding.UTF8.GetString(bytes);
                if (TryHandleRelayBeacon(text, remote)) return;
                var parts = text.Split('|');
                if (parts.Length < 3) return;
                var uid = parts[0];
                if (string.Equals(uid, _identity.UID, StringComparison.Ordinal)) return; // self
                if (!int.TryParse(parts[1], out var port)) return;
                var pub = Convert.FromBase64String(parts[2]);
                var dn = parts.Length >= 4 ? parts[3] : string.Empty;
                string? presence = null;
                if (parts.Length >= 5)
                {
                    var tok = parts[4].Trim().ToLowerInvariant();
                    presence = tok switch
                    {
                        "on" => "Online",
                        "idle" => "Idle",
                        "dnd" => "Do Not Disturb",
                        "inv" => "Invisible",
                        "off" => "Offline",
                        _ => null
                    };
                }
                Logger.Log($"UDP beacon parsed from {remote.Address}: uid={uid} port={port} dn='{dn}' ts={DateTime.UtcNow:o}");
                try { AppServices.Discovery.NoteExternalTrigger("beacon-rx", $"uid={uid} dn='{dn}'"); } catch { }
                // Note: We intentionally only store UID/IP/TCP Port; no trust or profile metadata is broadcasted.
                var normUidDisc = uid.StartsWith("usr-", StringComparison.Ordinal) && uid.Length > 4 ? uid.Substring(4) : uid;
                var peer = new Models.Peer { UID = normUidDisc, Address = remote.Address.ToString(), Port = port, Status = presence ?? "Discovered" };
                var list = new System.Collections.Generic.List<Models.Peer>(AppServices.Peers.Peers) { peer };
                AppServices.Peers.SetDiscovered(list);
                try { _presenceLastSeenUtc[normUidDisc] = DateTime.UtcNow; } catch { }
                // Proactively attempt to connect to known peers after discovery/beacon
                try { RequestAutoConnectSweep(); } catch { }

                // Opportunistically apply remote display name to existing contact when safe (only if user hasn't customized).
                try
                {
                    if (!string.IsNullOrWhiteSpace(dn))
                    {
                        var contacts = AppServices.Contacts.Contacts;
                        var c = contacts.FirstOrDefault(x => string.Equals(Trim(x.UID), normUidDisc, StringComparison.OrdinalIgnoreCase));
                        if (c != null)
                        {
                            var currentDn = c.DisplayName ?? string.Empty;
                            // Treat as user-customized if DisplayName is not empty and not equal to UID.
                            var isCustomized = !string.IsNullOrWhiteSpace(currentDn) && !string.Equals(currentDn, c.UID, StringComparison.Ordinal);
                            if (!isCustomized && !string.Equals(currentDn, dn, StringComparison.Ordinal))
                            {
                                AppServices.Contacts.UpdateDisplayName(normUidDisc, dn, AppServices.Passphrase);
                            }
                        }
                    }
                }
                catch { }
            }
            catch { }
        }

        private bool TryHandleRelayBeacon(string text, IPEndPoint remote)
        {
            try
            {
                if (!text.StartsWith("RLY|", StringComparison.Ordinal)) return false;
                var parts = text.Split('|');
                if (parts.Length < 3) return true;
                var token = parts[1].Trim();
                if (string.IsNullOrWhiteSpace(token)) return true;
                if (!int.TryParse(parts[2], out var relayPort) || relayPort < 1 || relayPort > 65535) return true;

                var endpointKey = $"{remote.Address}:{relayPort}";
                _discoveredRelays[endpointKey] = new DiscoveredRelay
                {
                    Token = token,
                    Host = remote.Address.ToString(),
                    Port = relayPort,
                    LastSeenUtc = DateTime.UtcNow
                };
                PruneDiscoveredRelays();
                Logger.Log($"Relay beacon parsed from {remote.Address}: token={token} port={relayPort}");
                try { AppServices.Discovery.NoteExternalTrigger("relay-beacon-rx", $"token={token} host={remote.Address}:{relayPort}"); } catch { }
                return true;
            }
            catch
            {
                return true;
            }
        }

        private void PruneDiscoveredRelays()
        {
            try
            {
                var cutoff = DateTime.UtcNow - GetRelayDiscoveryTtl();
                foreach (var kv in _discoveredRelays.ToArray())
                {
                    if (kv.Value.LastSeenUtc < cutoff)
                    {
                        _discoveredRelays.TryRemove(kv.Key, out _);
                    }
                }
            }
            catch { }
        }

        // [AUTO-CONNECT] Attempt to connect to known peers with endpoints to establish sessions for identity/verification
        public void RequestAutoConnectSweep()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastAutoConnSweepUtc) < AutoConnectSweepMinInterval) return;
            _lastAutoConnSweepUtc = now;
            if (System.Threading.Interlocked.Exchange(ref _autoConnSweepRunning, 1) == 1) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    // Only auto-connect to contacts — peers without a contact entry
                    // are transient discoveries and should not trigger relay OFFERs.
                    var uidSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        foreach (var c in AppServices.Contacts.Contacts)
                        {
                            if (c == null) continue;
                            var cu = Trim(c.UID ?? string.Empty);
                            if (!string.IsNullOrWhiteSpace(cu)) uidSet.Add(cu);
                        }
                    }
                    catch { }

                    var snapshot = uidSet.ToList();
                    var now = DateTime.UtcNow;
                    foreach (var uid in snapshot)
                    {
                        try
                        {
                            if (string.IsNullOrWhiteSpace(uid)) continue;
                            if (HasEncryptedSession(uid)) continue;
                            if (_autoConnLastAttempt.TryGetValue(uid, out var last) && (now - last) < AutoConnectBackoff) continue;
                            _autoConnLastAttempt[uid] = now;
                            try {
                                Logger.Log($"[AutoConnect] Attempting connection to {uid}");
                                var connected = await ConnectPeerBestEffortAsync(uid, CancellationToken.None);
                                Logger.Log($"[AutoConnect] Connection to {uid} result: {connected}");
                            } catch (Exception ex) { 
                                Logger.Log($"[AutoConnect] Connection to {uid} failed: {ex.Message}");
                            }
                        }
                        catch { }
                    }
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref _autoConnSweepRunning, 0);
                }
            });
        }

        // Establish an encrypted session to a peer using UID-based best-effort strategy
        // (known endpoint -> WAN lookup -> relay-only rendezvous path).
        public async Task<bool> ConnectPeerByUidAsync(string peerUid, CancellationToken ct)
        {
            return await ConnectPeerBestEffortAsync(peerUid, ct).ConfigureAwait(false);
        }

        // Establish an encrypted session using optional endpoint hints first, then UID fallback strategy.
        public async Task<bool> ConnectPeerWithHintsAsync(string peerUid, string? host, int? port, CancellationToken ct)
        {
            var uid = Trim(peerUid);
            if (string.IsNullOrWhiteSpace(uid)) return false;
            if (HasEncryptedSession(uid)) return true;

            if (!string.IsNullOrWhiteSpace(host) && port is > 0 and <= 65535)
            {
                var okHinted = await ConnectWithRelayFallbackAsync(uid, host!, port.Value, ct).ConfigureAwait(false);
                if (okHinted) return true;
            }

            return await ConnectPeerBestEffortAsync(uid, ct).ConfigureAwait(false);
        }

        private async Task<bool> ConnectPeerBestEffortAsync(string peerUid, CancellationToken ct)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var mode = "none";
            var outcome = "fail";
            var uidForLog = Trim(peerUid);
            try { SafeNetLog($"connect attempt begin | peer={uidForLog}"); } catch { }
            try
            {
                var uid = Trim(peerUid);
                uidForLog = uid;
                if (string.IsNullOrWhiteSpace(uid)) return false;
                if (HasEncryptedSession(uid))
                {
                    mode = "existing-session";
                    outcome = "ok";
                    try { SafeNetLog($"autoconn skip has-session | peer={uid}"); } catch { }
                    return true;
                }

                var peer = AppServices.Peers.Peers.FirstOrDefault(p => string.Equals(Trim(p.UID), uid, StringComparison.OrdinalIgnoreCase));
                if (peer != null && !string.IsNullOrWhiteSpace(peer.Address) && peer.Port > 0)
                {
                    mode = "direct+relay-fallback";
                    try { SafeNetLog($"autoconn path=direct+relay-fallback | peer={uid} | endpoint={peer.Address}:{peer.Port}"); } catch { }
                    var ok = await ConnectWithRelayFallbackAsync(uid, peer.Address!, peer.Port, ct);
                    outcome = ok ? "ok" : "fail";
                    try { SafeNetLog($"autoconn result | peer={uid} | mode=direct+relay-fallback | ok={ok}"); } catch { }
                    return ok;
                }

                // WAN bootstrap: resolve UID via directory when no endpoint is known locally.
                try
                {
                    mode = "wan-lookup+relay-fallback";
                    using var lookupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    lookupCts.CancelAfter(TimeSpan.FromSeconds(5));
                    var lookup = await AppServices.WanDirectory.LookupPeerAsync(uid, lookupCts.Token).ConfigureAwait(false);
                    if (lookup != null && !string.IsNullOrWhiteSpace(lookup.Host) && lookup.Port > 0)
                    {
                        try { SafeNetLog($"autoconn wan-lookup hit | peer={uid} | endpoint={lookup.Host}:{lookup.Port} | src={lookup.Source}"); } catch { }

                        try
                        {
                            var merged = new List<Models.Peer>(AppServices.Peers.Peers)
                            {
                                new Models.Peer { UID = uid, Address = lookup.Host, Port = lookup.Port, Status = "Discovered" }
                            };
                            AppServices.Peers.SetDiscovered(merged);
                        }
                        catch { }

                        var ok = await ConnectWithRelayFallbackAsync(uid, lookup.Host, lookup.Port, ct).ConfigureAwait(false);
                        outcome = ok ? "ok" : "fail";
                        try { SafeNetLog($"autoconn result | peer={uid} | mode=wan-lookup+relay-fallback | ok={ok}"); } catch { }
                        return ok;
                    }
                    try { SafeNetLog($"autoconn wan-lookup miss | peer={uid}"); } catch { }
                }
                catch { }

                var s = AppServices.Settings.Settings;
                mode = "relay-only";
                try { SafeNetLog($"autoconn path=relay-only | peer={uid} | reason=no-peer-endpoint"); } catch { }
                if (!ShouldAttemptRelayFallback(peer?.Address))
                {
                    outcome = "skipped-outside-lan-direct-available";
                    try { SafeNetLog($"autoconn relay-only skipped | peer={uid} | reason=outside-lan-direct-available"); } catch { }
                    return false;
                }
                if (!s.RelayFallbackEnabled)
                {
                    outcome = "skipped-relay-disabled";
                    try { SafeNetLog($"autoconn relay-only skipped | peer={uid} | reason=disabled"); } catch { }
                    return false;
                }
                var relayCandidates = BuildRelayCandidates(s);
                if (relayCandidates.Count == 0)
                {
                    outcome = "skipped-invalid-relay-endpoint";
                    try { SafeNetLog($"autoconn relay-only skipped | peer={uid} | reason=invalid-endpoint"); } catch { }
                    return false;
                }
                foreach (var relay in relayCandidates)
                {
                    try { SafeNetLog($"autoconn relay-only try | peer={uid} | server={relay.Display}"); } catch { }
                    if (await TryConnectViaRelayEndpointAsync(uid, relay.Host, relay.Port, relay.Display, ct))
                    {
                        outcome = "ok";
                        try { SafeNetLog($"autoconn result | peer={uid} | mode=relay-only | ok=true | server={relay.Display}"); } catch { }
                        return true;
                    }
                }
                outcome = "fail";
                try { SafeNetLog($"autoconn result | peer={uid} | mode=relay-only | ok=false"); } catch { }
            }
            catch (Exception ex)
            {
                outcome = $"ex:{ex.GetType().Name}";
            }
            finally
            {
                try { SafeNetLog($"connect attempt done | peer={uidForLog} | mode={mode} | outcome={outcome} | ms={sw.ElapsedMilliseconds}"); } catch { }
            }
            return false;
        }

        private static int TryBindEphemeral(IPAddress bindIp, out TcpListener? listener)
        {
            listener = null;
            var rand = new Random();
            const int start = 49152; const int end = 65535;
            var tried = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < 200; i++)
            {
                var p = rand.Next(start, end + 1);
                if (tried.Contains(p)) { i--; continue; }
                tried.Add(p);
                try
                {
                    var l = new TcpListener(bindIp, p);
                    l.Start();
                    listener = l;
                    return p;
                }
                catch { }
            }
            for (int p = start; p <= end; p++)
            {
                if (tried.Contains(p)) continue;
                try
                {
                    var l = new TcpListener(bindIp, p);
                    l.Start();
                    listener = l;
                    return p;
                }
                catch { }
            }
            return 0;
        }

        private static IPAddress SelectPreferredBindAddress()
        {
            try
            {
                var order = AppServices.Settings.Settings.AdapterPriorityIds ?? new System.Collections.Generic.List<string>();
                var nics = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                foreach (var id in order)
                {
                    var ni = System.Linq.Enumerable.FirstOrDefault(nics, n => n.Id == id);
                    if (ni == null) continue;
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    var ipProps = ni.GetIPProperties();
                    foreach (var ua in ipProps.UnicastAddresses)
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                            return ua.Address;
                }
                foreach (var ni in nics)
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    var ipProps = ni.GetIPProperties();
                    foreach (var ua in ipProps.UnicastAddresses)
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                            return ua.Address;
                }
            }
            catch { }
            return IPAddress.Any;
        }

        private static System.Collections.Generic.IEnumerable<IPAddress> GetBroadcastAddressesPreferredFirst(IPAddress preferred)
        {
            var primary = new System.Collections.Generic.List<IPAddress>();
            var others = new System.Collections.Generic.List<IPAddress>();
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                    var ipProps = ni.GetIPProperties();
                    foreach (var ua in ipProps.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var ip = ua.Address.GetAddressBytes();
                        var mask = ua.IPv4Mask?.GetAddressBytes();
                        if (mask == null) continue;
                        var bcast = new byte[4];
                        for (int i = 0; i < 4; i++) bcast[i] = (byte)(ip[i] | (mask[i] ^ 255));
                        var addr = new IPAddress(bcast);
                        if (!preferred.Equals(IPAddress.Any) && ua.Address.Equals(preferred)) primary.Add(addr);
                        else others.Add(addr);
                    }
                }
            }
            catch { }
            if (primary.Count == 0 && others.Count == 0) return new[] { IPAddress.Broadcast };
            foreach (var o in others) primary.Add(o);
            return primary;
        }

        private async Task HandleClient(TcpClient client, bool isInitiator, CancellationToken ct)
        {
            using var dh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var localPort = (client.Client.LocalEndPoint as IPEndPoint)?.Port ?? 0;
            var remoteEp = (client.Client.RemoteEndPoint as IPEndPoint)?.ToString() ?? "unknown";
            
            // [TIER-1-BLOCKING] IP-based blocking and rate limiting
            var remoteIp = ExtractIpFromEndpoint(remoteEp);
            if (!string.IsNullOrEmpty(remoteIp))
            {
                // Check hardcoded IP range blocklist (highest priority)
                if (SecurityBlocklistService.IsIpInBlockedRange(remoteIp))
                {
                    Logger.Log($"[SECURITY] Connection blocked - IP in hardcoded blocklist range: {remoteIp}");
                    try { client.Close(); } catch { }
                    return;
                }

                // Check geo-blocking
                var countryCode = SecurityBlocklistService.DeriveCountryCodeFromIp(remoteIp);
                if (!string.IsNullOrEmpty(countryCode) && 
                    SecurityBlocklistService.IsCountryBlocked(countryCode, AppServices.Settings.Settings))
                {
                    // Already logged inside IsCountryBlocked
                    try { client.Close(); } catch { }
                    return;
                }

                // Check user-configured IP blocklist
                if (IsIpBlocked(remoteIp))
                {
                    Logger.Log($"[BLOCK-IP] Blocked IP attempted connection: {remoteIp}");
                    try { client.Close(); } catch { }
                    return;
                }

                // Check rate limiting
                if (IsRateLimited(remoteIp))
                {
                    Logger.Log($"[RATE-LIMIT] Connection rejected from {remoteIp} - too many attempts");
                    try { client.Close(); } catch { }
                    return;
                }
            }
            
            var baseStream = client.GetStream();
            var ns = new CountingStream(baseStream,
                onRead: n => ReportBytes(localPort, inbound: n, outbound: 0),
                onWrite: n => ReportBytes(localPort, inbound: 0, outbound: n));
            var hsWatch = System.Diagnostics.Stopwatch.StartNew();
            var pub = dh.PublicKey.ExportSubjectPublicKeyInfo();
            await WriteFrame(ns, pub, ct);
            // Defensive handshake read: tolerate prematurely closed sockets and log actual vs expected bytes
            byte[] peerPub;
            try
            {
                using var hcts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                hcts.CancelAfter(TimeSpan.FromSeconds(5)); // bound handshake wait to avoid hanging connections
                var (ok, payload, expected, actual, reason) = await TryReadHandshakeFrame(ns, hcts.Token);
                if (!ok || payload == null)
                {
                    Logger.Log($"Handshake read error from {remoteEp}: {reason} (got {actual} / expected {expected} bytes)");
                    try { SafeNetLog($"handshake ecdh-fail | ep={remoteEp} | ms={hsWatch.ElapsedMilliseconds} | reason={reason}"); } catch { }
                    try { _diag.IncHandshakeFail(reason); } catch { }
                    try { HandshakeCompleted?.Invoke(false, remoteEp, reason); } catch { }
                    client.Close();
                    return;
                }
                try { SafeNetLog($"handshake ecdh-ok | ep={remoteEp} | ms={hsWatch.ElapsedMilliseconds} | initiator={isInitiator}"); } catch { }
                peerPub = payload;
            }
            catch (OperationCanceledException)
            {
                Logger.Log($"Handshake read error from {remoteEp}: timed out waiting for peer");
                try { SafeNetLog($"handshake ecdh-timeout | ep={remoteEp} | ms={hsWatch.ElapsedMilliseconds}"); } catch { }
                try { _diag.IncHandshakeFail("timeout"); } catch { }
                try { HandshakeCompleted?.Invoke(false, remoteEp, "timeout"); } catch { }
                client.Close(); return;
            }
            catch (Exception ex)
            {
                Logger.Log($"Handshake read error from {remoteEp}: {ex.Message}");
                try { SafeNetLog($"handshake ecdh-ex | ep={remoteEp} | ms={hsWatch.ElapsedMilliseconds} | {ex.GetType().Name}:{ex.Message}"); } catch { }
                try { _diag.IncHandshakeFail(ex.Message); } catch { }
                try { HandshakeCompleted?.Invoke(false, remoteEp, ex.Message); } catch { }
                client.Close(); return;
            }
            using var peerKey = ECDiffieHellman.Create();
            peerKey.ImportSubjectPublicKeyInfo(peerPub, out _);
            // Do not treat ECDH-derived UID as identity; defer identity decisions until 0xA1 announce.
            var epNow = (client.Client.RemoteEndPoint as IPEndPoint)?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(epNow)) epNow = remoteEp;
            // Note: Block check by UID happens after identity announcement (0xA1), not at ECDH stage
            var secret = dh.DeriveKeyMaterial(peerKey.PublicKey);
            var info = Encoding.UTF8.GetBytes("Zer0Talk-session");
            var prk = Hkdf.DeriveKey(secret, salt: Array.Empty<byte>(), info: info, length: 32 + 32 + 16 + 16);
            CryptographicOperations.ZeroMemory(secret);
            var txKey = new byte[32]; var rxKey = new byte[32]; var txBase = new byte[16]; var rxBase = new byte[16];
            
            // [COLLISION-DETECTION] For simultaneous connections, determine initiator role deterministically 
            // by comparing ECDH public keys (prevents both sides thinking they're initiator)
            var ourPub = dh.PublicKey.ExportSubjectPublicKeyInfo();
            var actualIsInitiator = isInitiator;
            if (isInitiator)
            {
                // If we initiated but their public key is lexicographically smaller, we should act as responder
                var comparison = CompareBytes(ourPub, peerPub);
                if (comparison > 0)
                {
                    actualIsInitiator = false;
                    Logger.Log($"[COLLISION] Outbound connection demoted to responder role due to key comparison: {remoteEp}");
                }
            }
            
            if (actualIsInitiator)
            {
                Buffer.BlockCopy(prk, 0, txKey, 0, 32); Buffer.BlockCopy(prk, 32, rxKey, 0, 32);
                Buffer.BlockCopy(prk, 64, txBase, 0, 16); Buffer.BlockCopy(prk, 80, rxBase, 0, 16);
            }
            else
            {
                Buffer.BlockCopy(prk, 0, rxKey, 0, 32); Buffer.BlockCopy(prk, 32, txKey, 0, 32);
                Buffer.BlockCopy(prk, 64, rxBase, 0, 16); Buffer.BlockCopy(prk, 80, txBase, 0, 16);
            }
            var transport = new Utilities.AeadTransport(ns, txKey, rxKey, txBase, rxBase);
            Logger.Log($"Session cipher established with {(client.Client.RemoteEndPoint as IPEndPoint)?.ToString()}");
            // Track peer's handshake ECDH SPKI and endpoint for identity binding and rotation checks
            try { _handshakePeerKeys[transport] = peerPub; } catch { }
            try { _transportEndpoints[transport] = epNow; } catch { }
            try { _ = SendIdentityAnnounceAsync(transport, pub, ct); } catch { }
            string? boundUid = null;
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var data = await transport.ReadAsync(ct);
                        if (data.Length > 0)
                        {
                            if (TryParseSecurityAlert(data, out var secReason2, out var secMsg2))
                            {
                                var details = string.IsNullOrWhiteSpace(secMsg2)
                                    ? "Remote reported a key mismatch. Conversation was stopped."
                                    : secMsg2;
                                NotifyKeyMismatch(boundUid ?? "unknown", details);
                                break;
                            }

                            if (data[0] == 0xA2 && data.Length >= 5)
                            {
                                var len = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(1, 4));
                                if (len > 0 && data.Length >= 5 + len)
                                {
                                    var avatar = new byte[len];
                                    Buffer.BlockCopy(data, 5, avatar, 0, (int)len);
                                    if (!string.IsNullOrEmpty(boundUid) && ShouldAcceptAvatar(boundUid, (int)len, avatar)) SavePeerAvatarToCache(boundUid, avatar);
                                }
                            }
                            else if (data[0] == 0xD1)
                            {
                                // Bio frame: [0xD1][len(2)][utf8]
                                int idx = 1; if (data.Length < idx + 2) continue; ushort blen = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(idx, 2)); idx += 2;
                                if (blen > MaxBioBytes || data.Length < idx + blen) continue;
                                var bytes = blen > 0 ? data.AsSpan(idx, blen).ToArray() : Array.Empty<byte>();
                                var bio = bytes.Length == 0 ? string.Empty : Encoding.UTF8.GetString(bytes);
                                if (!string.IsNullOrEmpty(boundUid))
                                {
                                    try { AppServices.Contacts.SetBio(boundUid, bio, AppServices.Passphrase); } catch { }
                                }
                            }
                            else if (data[0] == 0xA1)
                            {
                                try
                                {
                                    int idx = 1;
                                    if (data.Length < idx + 1) continue;
                                    int pubLen = data[idx++];
                                    if (pubLen != 32 || data.Length < idx + pubLen + 1) continue;
                                    var pub2 = new byte[pubLen];
                                    Buffer.BlockCopy(data, idx, pub2, 0, pubLen); idx += pubLen;
                                    int sigLen = data[idx++];
                                    if (sigLen != 64 || data.Length < idx + sigLen) continue;
                                    var sig2 = new byte[sigLen];
                                    Buffer.BlockCopy(data, idx, sig2, 0, sigLen);
                                    if (!_handshakePeerKeys.TryGetValue(transport, out var peerSpki2) || peerSpki2 == null || peerSpki2.Length == 0) continue;
                                    if (!IdentityService.Verify(peerSpki2, sig2, pub2)) continue;
                                    var claimed2 = IdentityService.ComputeUidFromPublicKey(pub2);
                                    var normClaimed2 = Trim(claimed2);

                                    if (TryGetExpectedKeyMismatch(normClaimed2, pub2, out var expectedHex2, out var observedHex2))
                                    {
                                        var details = $"Expected key {expectedHex2}, observed {observedHex2}.";
                                        EnforceKeyMismatchAndTerminate(normClaimed2, transport, details, "Key mismatch detected. Conversation cannot continue.", client: client);
                                        break;
                                    }

                                    // If we initiated, verify expected UID now (not during ECDH stage)
                                    try
                                    {
                                        if (isInitiator && _transportEndpoints.TryGetValue(transport, out var epStr) && !string.IsNullOrEmpty(epStr))
                                        {
                                            if (_pendingOutboundExpectations.TryRemove(epStr, out var expectation) &&
                                                !string.Equals(Trim(expectation.ExpectedUid), normClaimed2, StringComparison.OrdinalIgnoreCase))
                                            {
                                                SafeNetLog($"handshake direct uid-mismatch | expected={expectation.ExpectedUid} | got={normClaimed2}");
                                                Logger.Log($"Direct handshake UID mismatch after identity announce: expected={expectation.ExpectedUid} got={normClaimed2}");
                                                EnforceKeyMismatchAndTerminate(normClaimed2, transport, $"Expected UID {Trim(expectation.ExpectedUid)}, observed {normClaimed2}.", "Identity/key mismatch detected. Conversation cannot continue.", client: client);
                                                break;
                                            }
                                        }
                                    }
                                    catch { }
                                    // [TIER-1-BLOCKING] Check hardcoded security blocklist (highest priority)
                                    try
                                    {
                                        var fingerprint = ComputePublicKeyFingerprint(pub2);
                                        if (!string.IsNullOrEmpty(fingerprint) && 
                                            SecurityBlocklistService.IsPublicKeyOnHardcodedBlocklist(fingerprint))
                                        {
                                            Logger.Log($"[SECURITY] CRITICAL: Hardcoded blocklist match! Closing connection to {normClaimed2}");
                                            try { AppServices.Events.RaiseFirewallPrompt($"SECURITY ALERT: Connection from known hostile actor blocked ({normClaimed2})"); } catch { }
                                            try { client.Close(); } catch { }
                                            break;
                                        }
                                    }
                                    catch { }
                                    // [TIER-1-BLOCKING] Check if public key fingerprint is blocked (user-configured)
                                    try
                                    {
                                        if (IsPublicKeyBlocked(pub2))
                                        {
                                            Logger.Log($"[BLOCK-PUBKEY] Blocked public key fingerprint attempted connection: {normClaimed2}");
                                            try { client.Close(); } catch { }
                                            break;
                                        }
                                    }
                                    catch { }
                                    // Block if this identity is on the block list
                                    try
                                    {
                                        if (AppServices.Settings.Settings.BlockList?.Contains(normClaimed2) == true)
                                        {
                                            Logger.Log($"[BLOCK-UID] Blocked peer attempted connection: {normClaimed2}");
                                            try { client.Close(); } catch { }
                                            break;
                                        }
                                    }
                                    catch { }
                                    // Register session under stable identity if not already
                                    if (!_sessions.ContainsKey(normClaimed2))
                                    {
                                        _sessions[normClaimed2] = transport;
                                        _sessionModes[normClaimed2] = Models.ConnectionMode.Direct;
                                        try
                                        {
                                            Logger.Log($"[sess] add | mode=direct | peer={normClaimed2} | total={_sessions.Count} | ts={DateTime.UtcNow:o}");
                                            SafeNetLog($"session add direct | key={normClaimed2} | total={_sessions.Count}");
                                        }
                                        catch { }
                                        RaiseSessionCountChanged();
                                        // Endpoint-based rotation detection now based on stable identity
                                        try
                                        {
                                            if (_transportEndpoints.TryGetValue(transport, out var epStr) && !string.IsNullOrEmpty(epStr))
                                            {
                                                if (_endpointLastUid.TryGetValue(epStr, out var last) && !string.Equals(last, normClaimed2, StringComparison.OrdinalIgnoreCase))
                                                {
                                                    SafeNetLog($"identity rotate detected | endpoint={epStr} | prev={last} | now={normClaimed2}");
                                                    Logger.Log($"Identity rotation detected on endpoint {epStr}: {last} -> {normClaimed2}");
                                                    try { AppServices.Events.RaiseFirewallPrompt($"Identity rotation detected for {epStr}: {last} → {normClaimed2}. Remote may have a new account; your contact might be stale."); } catch { }
                                                }
                                                _endpointLastUid[epStr] = normClaimed2;
                                            }
                                        }
                                        catch { }
                                        // Observed public key is the Ed25519 identity key
                                        try { AppServices.Peers.SetObservedPublicKey(normClaimed2, pub2); } catch { }
                                        try { _diag.IncHandshakeOk(); _diag.IncSessionsActive(); } catch { }
                                        try { SafeNetLog($"handshake bind-ok | peer={normClaimed2} | ms={hsWatch.ElapsedMilliseconds} | ep={epNow}"); } catch { }
                                        try { HandshakeCompleted?.Invoke(true, normClaimed2, null); } catch { }
                                        // Send current presence and avatar after binding
                                        try { _ = SendPresenceAsync(normClaimed2, AppServices.Settings.Settings.Status, ct); } catch { }
                                        try
                                        {
                                            if (_identity.ShareAvatar && _identity.AvatarBytes != null && _identity.AvatarBytes.Length > 0)
                                            {
                                                var payload = BuildAvatarFrame(_identity.AvatarBytes);
                                                await transport.WriteAsync(payload, ct);
                                            }
                                        }
                                        catch (Exception ex) { Logger.Log($"Avatar send error: {ex.Message}"); }
                                        // Send our bio as part of profile sync
                                        try { var bioFrame = BuildBioFrame(_identity.Bio); await transport.WriteAsync(bioFrame, ct); } catch { }
                                        // Mark identity as bound
                                        boundUid = normClaimed2;
                                    }
                                }
                                catch { }
                                continue;
                            }
                            else if (data[0] == 0xB0)
                            {
                                // Chat message (signed): [0xB0][msgId(16)][len(2)][utf8 content][pubLen(1)=32][pub(32)][sigLen(1)=64][sig(64)]
                                int idx = 1;
                                if (data.Length < idx + 16 + 2 + 1 + 32 + 1 + 64) continue;
                                if (string.IsNullOrEmpty(boundUid)) continue;
                                var gid = new byte[16]; Buffer.BlockCopy(data, idx, gid, 0, 16); idx += 16; var mid = new Guid(gid);
                                if (!TryAcceptInboundFrameId(boundUid, 0xB0, mid))
                                {
                                    Logger.Log($"Duplicate/replay chat message dropped from {boundUid} id={mid}");
                                    continue;
                                }
                                ushort len = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(idx, 2)); idx += 2; if (len > MaxChatBytes) continue;
                                if (len < 0 || data.Length < idx + len + 1 + 32 + 1 + 64) continue;
                                var txtb = data.AsSpan(idx, len).ToArray(); idx += len;
                                int pl = data[idx++]; if (pl != 32 || data.Length < idx + pl + 1 + 64) continue;
                                var pub = new byte[32]; Buffer.BlockCopy(data, idx, pub, 0, 32); idx += 32;
                                int sl = data[idx++]; if (sl != 64 || data.Length < idx + sl) continue;
                                var sig = new byte[64]; Buffer.BlockCopy(data, idx, sig, 0, 64);
                                var idxAfterSig = idx + 64;
                                try
                                {
                                    if (data.Length > idxAfterSig)
                                    {
                                        var remaining = data.Length - idxAfterSig;
                                        if (remaining > 0)
                                        {
                                            var optLen = data[idxAfterSig];
                                            if (remaining >= 1 + optLen)
                                            {
                                                idxAfterSig += 1 + optLen;
                                            }
                                        }
                                    }
                                }
                                catch { }
                                var claimedUid = IdentityService.ComputeUidFromPublicKey(pub);
                                if (!string.Equals(Trim(claimedUid), boundUid, StringComparison.OrdinalIgnoreCase))
                                {
                                    Logger.Log($"Spoofed message rejected: claimed {claimedUid} != session {boundUid}");
                                    try { AppServices.Peers.Block(Trim(claimedUid)); } catch { }
                                    EnforceKeyMismatchAndTerminate(boundUid, transport, $"Message signer UID mismatch: claimed {Trim(claimedUid)} != session {boundUid}.", "Message key mismatch detected. Conversation cannot continue.", client: client);
                                    break;
                                }
                                var payloadToSign = new byte[16 + 2 + txtb.Length];
                                Buffer.BlockCopy(gid, 0, payloadToSign, 0, 16);
                                BinaryPrimitives.WriteUInt16BigEndian(payloadToSign.AsSpan(16, 2), (ushort)txtb.Length);
                                Buffer.BlockCopy(txtb, 0, payloadToSign, 18, txtb.Length);
                                if (!IdentityService.Verify(payloadToSign, sig, pub))
                                {
                                    Logger.Log("Invalid signature; message dropped");
                                    continue;
                                }
                                var content = Encoding.UTF8.GetString(txtb);
                                Logger.Log($"Msg from {boundUid} len={content.Length} id={mid}");
                                try { ChatMessageReceived?.Invoke(boundUid, mid, content); } catch { }
                            }
                            else if (data[0] == 0xB1)
                            {
                                // Signed edit in direct session
                                int idx2 = 1;
                                if (data.Length < idx2 + 16 + 2 + 1 + 32 + 1 + 64) continue;
                                if (string.IsNullOrEmpty(boundUid)) continue;
                                var gid = new byte[16]; Buffer.BlockCopy(data, idx2, gid, 0, 16); idx2 += 16; var mid = new Guid(gid);
                                if (!TryAcceptInboundFrameId(boundUid, 0xB1, mid))
                                {
                                    Logger.Log($"Duplicate/replay edit dropped from {boundUid} id={mid}");
                                    continue;
                                }
                                ushort len2 = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(idx2, 2)); idx2 += 2; if (len2 > MaxChatBytes) continue;
                                if (len2 <= 0 || data.Length < idx2 + len2 + 1 + 32 + 1 + 64) continue;
                                var txtb = data.AsSpan(idx2, len2).ToArray(); idx2 += len2;
                                int pl = data[idx2++]; if (pl != 32 || data.Length < idx2 + pl + 1 + 64) continue;
                                var pub = new byte[32]; Buffer.BlockCopy(data, idx2, pub, 0, 32); idx2 += 32;
                                int sl = data[idx2++]; if (sl != 64 || data.Length < idx2 + sl) continue;
                                var sig = new byte[64]; Buffer.BlockCopy(data, idx2, sig, 0, 64);
                                var claimed = IdentityService.ComputeUidFromPublicKey(pub);
                                if (!string.Equals(Trim(claimed), boundUid, StringComparison.OrdinalIgnoreCase)) { Logger.Log("Edit spoof rejected: UID mismatch"); EnforceKeyMismatchAndTerminate(boundUid, transport, $"Edit signer UID mismatch: claimed {Trim(claimed)} != session {boundUid}.", "Edit key mismatch detected. Conversation cannot continue.", client: client); break; }
                                var payloadToSign = new byte[16 + 2 + txtb.Length];
                                Buffer.BlockCopy(gid, 0, payloadToSign, 0, 16);
                                BinaryPrimitives.WriteUInt16BigEndian(payloadToSign.AsSpan(16, 2), (ushort)txtb.Length);
                                Buffer.BlockCopy(txtb, 0, payloadToSign, 18, txtb.Length);
                                if (!IdentityService.Verify(payloadToSign, sig, pub)) { Logger.Log("Edit bad signature"); continue; }
                                var txt2 = Encoding.UTF8.GetString(txtb);
                                try { AppServices.MessagesUpdateFromRemote(boundUid, mid, txt2); } catch { }
                                try { ChatMessageEdited?.Invoke(boundUid, mid, txt2); } catch { }
                                var ack = new byte[1 + 16]; ack[0] = 0xB3; Buffer.BlockCopy(gid, 0, ack, 1, 16);
                                try { _ = TrySendEncryptedAsync(boundUid, ack, CancellationToken.None); } catch { }
                            }
                            else if (data[0] == 0xB2)
                            {
                                // Signed delete in direct session
                                int idx2 = 1; if (data.Length < idx2 + 16 + 1 + 32 + 1 + 64) continue;
                                if (string.IsNullOrEmpty(boundUid)) continue;
                                var gid = new byte[16]; Buffer.BlockCopy(data, idx2, gid, 0, 16); idx2 += 16;
                                var mid = new Guid(gid);
                                if (!TryAcceptInboundFrameId(boundUid, 0xB2, mid))
                                {
                                    Logger.Log($"Duplicate/replay delete dropped from {boundUid} id={mid}");
                                    continue;
                                }
                                int pl = data[idx2++]; if (pl != 32 || data.Length < idx2 + pl + 1 + 64) continue;
                                var pub = new byte[32]; Buffer.BlockCopy(data, idx2, pub, 0, 32); idx2 += 32;
                                int sl = data[idx2++]; if (sl != 64 || data.Length < idx2 + sl) continue;
                                var sig = new byte[64]; Buffer.BlockCopy(data, idx2, sig, 0, 64);
                                var claimed = IdentityService.ComputeUidFromPublicKey(pub);
                                if (!string.Equals(Trim(claimed), boundUid, StringComparison.OrdinalIgnoreCase)) { Logger.Log("Delete spoof rejected: UID mismatch"); EnforceKeyMismatchAndTerminate(boundUid, transport, $"Delete signer UID mismatch: claimed {Trim(claimed)} != session {boundUid}.", "Delete key mismatch detected. Conversation cannot continue.", client: client); break; }
                                if (!IdentityService.Verify(gid, sig, pub)) { Logger.Log("Delete bad signature"); continue; }
                                try { AppServices.MessagesDeleteFromRemote(boundUid, mid); } catch { }
                                try { ChatMessageDeleted?.Invoke(boundUid, mid); } catch { }
                                var ack = new byte[1 + 16]; ack[0] = 0xB4; Buffer.BlockCopy(gid, 0, ack, 1, 16);
                                try { _ = TrySendEncryptedAsync(boundUid, ack, CancellationToken.None); } catch { }
                            }
                            else if (data[0] == 0xB3)
                            {
                                int idx2 = 1; if (data.Length < idx2 + 16) continue;
                                var gid = new byte[16]; Buffer.BlockCopy(data, idx2, gid, 0, 16);
                                var mid = new Guid(gid);
                                if (!string.IsNullOrEmpty(boundUid)) { try { ChatMessageEditAcked?.Invoke(boundUid, mid); } catch { } }
                            }
                            else if (data[0] == 0xB4)
                            {
                                int idx2 = 1; if (data.Length < idx2 + 16) continue;
                                var gid = new byte[16]; Buffer.BlockCopy(data, idx2, gid, 0, 16);
                                var mid = new Guid(gid);
                                if (!string.IsNullOrEmpty(boundUid)) { try { ChatMessageDeleteAcked?.Invoke(boundUid, mid); } catch { } }
                            }
                            else if (data[0] == 0xB6)
                            {
                                int idx2 = 1;
                                if (data.Length < idx2 + 16 + 16 + 1 + 1 + 1 + 32 + 1 + 64) continue;
                                if (string.IsNullOrEmpty(boundUid)) continue;

                                var eventIdBytes = new byte[16]; Buffer.BlockCopy(data, idx2, eventIdBytes, 0, 16); idx2 += 16;
                                var eventId = new Guid(eventIdBytes);
                                if (!TryAcceptInboundFrameId(boundUid, 0xB6, eventId))
                                {
                                    Logger.Log($"Duplicate/replay reaction dropped from {boundUid} event={eventId}");
                                    continue;
                                }

                                var messageIdBytes = new byte[16]; Buffer.BlockCopy(data, idx2, messageIdBytes, 0, 16); idx2 += 16;
                                var messageId = new Guid(messageIdBytes);
                                var op = data[idx2++];
                                var isAdd = op == 1;
                                var emojiLen = data[idx2++];
                                if (emojiLen <= 0 || emojiLen > MaxReactionEmojiBytes || data.Length < idx2 + emojiLen + 1 + 32 + 1 + 64) continue;
                                var emojiBytes = data.AsSpan(idx2, emojiLen).ToArray(); idx2 += emojiLen;
                                var emoji = Encoding.UTF8.GetString(emojiBytes).Trim();
                                if (string.IsNullOrWhiteSpace(emoji)) continue;

                                int pl = data[idx2++]; if (pl != 32 || data.Length < idx2 + pl + 1 + 64) continue;
                                var pub = new byte[32]; Buffer.BlockCopy(data, idx2, pub, 0, 32); idx2 += 32;
                                int sl = data[idx2++]; if (sl != 64 || data.Length < idx2 + sl) continue;
                                var sig = new byte[64]; Buffer.BlockCopy(data, idx2, sig, 0, 64);

                                var payloadToSign = new byte[16 + 16 + 1 + 1 + emojiBytes.Length];
                                int p = 0;
                                Buffer.BlockCopy(eventIdBytes, 0, payloadToSign, p, 16); p += 16;
                                Buffer.BlockCopy(messageIdBytes, 0, payloadToSign, p, 16); p += 16;
                                payloadToSign[p++] = op;
                                payloadToSign[p++] = (byte)emojiBytes.Length;
                                Buffer.BlockCopy(emojiBytes, 0, payloadToSign, p, emojiBytes.Length);

                                var claimed = IdentityService.ComputeUidFromPublicKey(pub);
                                if (!string.Equals(Trim(claimed), boundUid, StringComparison.OrdinalIgnoreCase))
                                {
                                    Logger.Log("Reaction spoof rejected: UID mismatch");
                                    EnforceKeyMismatchAndTerminate(boundUid, transport, $"Reaction signer UID mismatch: claimed {Trim(claimed)} != session {boundUid}.", "Reaction key mismatch detected. Conversation cannot continue.", client: client);
                                    break;
                                }
                                if (!IdentityService.Verify(payloadToSign, sig, pub))
                                {
                                    Logger.Log("Reaction bad signature");
                                    continue;
                                }

                                try { ChatMessageReactionReceived?.Invoke(boundUid, messageId, emoji, isAdd); } catch { }
                            }
                            else if (data[0] == 0xC0)
                            {
                                int idx = 1; int nlen = data[idx++]; if (data.Length < idx + nlen + 32 + 64 + 1) continue;
                                var nonce = Encoding.UTF8.GetString(data, idx, nlen); idx += nlen;
                                var pub = new byte[32]; Buffer.BlockCopy(data, idx, pub, 0, 32); idx += 32;
                                var sig = new byte[64]; Buffer.BlockCopy(data, idx, sig, 0, 64); idx += 64;
                                int dnLen = data[idx++]; if (dnLen < 0 || dnLen > MaxDisplayNameBytes) continue; var dn = dnLen > 0 ? Encoding.UTF8.GetString(data, idx, Math.Min(dnLen, data.Length - idx)) : string.Empty;
                                var claimed = IdentityService.ComputeUidFromPublicKey(pub);
                                if (string.IsNullOrEmpty(boundUid)) continue;
                                if (!string.Equals(Trim(claimed), boundUid, StringComparison.OrdinalIgnoreCase)) { Logger.Log("Contact req spoofed"); continue; }
                                var payload = Encoding.UTF8.GetBytes(nonce);
                                if (!IdentityService.Verify(payload, sig, pub)) { Logger.Log("Contact req bad sig"); continue; }
                                try { SafeNetLog($"recv C0 contact-request | peer={boundUid} | nonce={nonce} | dnLen={dnLen}"); } catch { }
                                _ = AppServices.ContactRequests.OnInboundRequestAsync(boundUid, nonce, dn ?? string.Empty);
                            }
                            else if (data[0] == 0xC1)
                            {
                                int idx = 1;
                                int nlen = data[idx++];
                                if (data.Length < idx + nlen) continue;
                                var nonce = Encoding.UTF8.GetString(data, idx, nlen);
                                idx += nlen;
                                // Parse display name if present (new protocol)
                                string? displayName = null;
                                if (data.Length > idx)
                                {
                                    int dnLen = data[idx++];
                                    if (data.Length >= idx + dnLen && dnLen > 0)
                                    {
                                        displayName = Encoding.UTF8.GetString(data, idx, dnLen);
                                    }
                                }
                                try { SafeNetLog($"recv C1 contact-accept | peer={boundUid} | nonce={nonce} | dnLen={displayName?.Length ?? 0}"); } catch { }
                                if (!string.IsNullOrEmpty(boundUid))
                                {
                                    AppServices.ContactRequests.OnInboundAccept(nonce, boundUid, displayName);
                                }
                            }
                            else if (data[0] == 0xC2)
                            {
                                int idx = 1; int nlen = data[idx++]; if (data.Length < idx + nlen) continue; var nonce = Encoding.UTF8.GetString(data, idx, nlen);
                                try { SafeNetLog($"recv C2 contact-cancel | peer={boundUid} | nonce={nonce}"); } catch { }
                                AppServices.ContactRequests.OnInboundCancel(nonce);
                            }
                            else if (data[0] == 0xC3)
                            {
                                // Verification intent
                                if (!string.IsNullOrEmpty(boundUid)) { try { AppServices.ContactRequests.OnInboundVerifyIntent(boundUid); } catch { } }
                            }
                            else if (data[0] == 0xC4)
                            {
                                // Verification request
                                if (!string.IsNullOrEmpty(boundUid)) { try { AppServices.ContactRequests.OnInboundVerifyRequest(boundUid); } catch { } }
                            }
                            else if (data[0] == 0xC5)
                            {
                                // Verification cancel
                                if (!string.IsNullOrEmpty(boundUid)) { try { AppServices.ContactRequests.OnInboundVerifyCancel(boundUid); } catch { } }
                            }
                            else if (data[0] == 0xC6)
                            {
                                // Verification complete notification
                                if (!string.IsNullOrEmpty(boundUid)) { try { AppServices.ContactRequests.OnInboundVerifyComplete(boundUid); } catch { } }
                            }
                            else if (data[0] == 0xD0)
                            {
                                int idx = 1; if (data.Length <= idx) continue; int n = data[idx++]; if (n < 0 || n > MaxPresenceToken || data.Length < idx + n) continue;
                                var tok = System.Text.Encoding.UTF8.GetString(data, idx, n).Trim().ToLowerInvariant();
                                string status = tok switch { "on" => "Online", "idle" => "Idle", "dnd" => "Do Not Disturb", "inv" => "Invisible", "off" => "Offline", _ => "Offline" };
                                if (!string.IsNullOrEmpty(boundUid)) { try { AppServices.Peers.SetPeerStatus(boundUid, status); } catch { } try { _presenceLastSeenUtc[boundUid] = DateTime.UtcNow; } catch { } try { PresenceReceived?.Invoke(boundUid, status); } catch { } }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Do not block peers for generic session errors; networks flap and peers may disconnect normally.
                    // Only explicit spoofing/protocol violations trigger blocking elsewhere.
                    Logger.Log($"Session error: {ex.Message}");
                    try { SafeNetLog($"session error | uid={boundUid ?? "?"} | {ex.GetType().Name}:{ex.Message}"); } catch { }
                }
                finally
                {
                    if (!string.IsNullOrEmpty(boundUid))
                    {
                        var removed = _sessions.TryRemove(boundUid, out _);
                        try { _sessionModes.TryRemove(boundUid, out _); } catch { }
                        try { if (removed) Logger.Log($"[sess] remove | mode=direct | peer={boundUid} | reason=reader-exit | ts={DateTime.UtcNow:o}"); } catch { }
                        if (removed) RaiseSessionCountChanged();
                    }
                    try { if (!string.IsNullOrEmpty(boundUid)) Zer0Talk.Services.AppServices.Contacts.SetLastKnownEncrypted(boundUid, false, Zer0Talk.Services.AppServices.Passphrase); } catch { }
                    try { _handshakePeerKeys.TryRemove(transport, out _); } catch { }
                    try { _transportEndpoints.TryRemove(transport, out _); } catch { }
                    try { _diag.DecSessionsActive(); } catch { }
                    // On session close: mark peer Offline immediately; liveness timers will maintain thereafter
                    if (!string.IsNullOrEmpty(boundUid)) { try { AppServices.Peers.SetPeerStatus(boundUid, "Offline"); } catch { } }
                    try { ns.Dispose(); } catch { }
                    try { client.Close(); client.Dispose(); } catch { }
                }
            }, ct);

            // [PHASE-3-KEEPALIVE] Send heartbeat frames every 30s to detect dead connections early
            _ = Task.Run(async () =>
            {
                try
                {
                    var keepalivePayload = Array.Empty<byte>();
                    while (!ct.IsCancellationRequested)
                    {
                        await Task.Delay(30_000, ct);
                        if (ct.IsCancellationRequested) break;

                        try
                        {
                            await transport.WriteAsync(keepalivePayload, ct);
                        }
                        catch
                        {
                            // Write failed - connection is dead, cancel main session
                            Logger.Log($"Direct keepalive write failed to {boundUid ?? "unknown"}, canceling session");
                            try { client.Close(); } catch { }
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Logger.Log($"Direct keepalive task error: {ex.Message}");
                }
            }, CancellationToken.None);
        }

        private static void SavePeerAvatarToCache(string uid, byte[] bytes)
        {
            try
            {
                // Always normalize UID to match what the UI/contacts use
                var normalized = uid;
                if (!string.IsNullOrWhiteSpace(normalized) && normalized.StartsWith("usr-", StringComparison.Ordinal) && normalized.Length > 4)
                    normalized = normalized.Substring(4);
                AvatarCache.Save(normalized, bytes);
            }
            catch { }
        }

        private static byte[] BuildAvatarFrame(byte[] avatarBytes)
        {
            var buf = new byte[1 + 4 + avatarBytes.Length];
            buf[0] = 0xA2;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(1, 4), (uint)avatarBytes.Length);
            Buffer.BlockCopy(avatarBytes, 0, buf, 5, avatarBytes.Length);
            return buf;
        }

        private static byte[] BuildBioFrame(string? bio)
        {
            var text = string.IsNullOrWhiteSpace(bio) ? string.Empty : bio!;
            if (text.Length > 280) text = text.Substring(0, 280);
            var bytes = Encoding.UTF8.GetBytes(text);
            var buf = new byte[1 + 2 + bytes.Length];
            buf[0] = 0xD1; // profile bio frame
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(1, 2), (ushort)bytes.Length);
            if (bytes.Length > 0) Buffer.BlockCopy(bytes, 0, buf, 3, bytes.Length);
            return buf;
        }

        private static byte[] BuildSecurityAlertFrame(byte reasonCode, string message)
        {
            var text = string.IsNullOrWhiteSpace(message) ? "Security key mismatch detected." : message.Trim();
            var body = Encoding.UTF8.GetBytes(text);
            var len = Math.Min(body.Length, 240);
            var frame = new byte[1 + 1 + 1 + len];
            frame[0] = SecurityAlertFrameType;
            frame[1] = reasonCode;
            frame[2] = (byte)len;
            if (len > 0) Buffer.BlockCopy(body, 0, frame, 3, len);
            return frame;
        }

        private static bool TryParseSecurityAlert(byte[] data, out byte reasonCode, out string message)
        {
            reasonCode = 0;
            message = string.Empty;
            try
            {
                if (data == null || data.Length < 3) return false;
                if (data[0] != SecurityAlertFrameType) return false;
                reasonCode = data[1];
                var len = data[2];
                if (data.Length < 3 + len) return false;
                message = len > 0 ? Encoding.UTF8.GetString(data, 3, len) : string.Empty;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeHexForCompare(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return value.Trim().ToLowerInvariant().Replace("-", string.Empty).Replace(":", string.Empty).Replace(" ", string.Empty);
        }

        private static bool TryGetExpectedKeyMismatch(string uid, byte[] observedPublicKey, out string expectedHex, out string observedHex)
        {
            expectedHex = string.Empty;
            observedHex = string.Empty;
            try
            {
                var normalizedUid = Trim(uid);
                var contact = AppServices.Contacts.Contacts.FirstOrDefault(c => string.Equals(Trim(c.UID), normalizedUid, StringComparison.OrdinalIgnoreCase));
                if (contact == null) return false;

                expectedHex = NormalizeHexForCompare(contact.ExpectedPublicKeyHex);
                if (string.IsNullOrWhiteSpace(expectedHex)) return false;

                observedHex = Convert.ToHexStringLower(observedPublicKey ?? Array.Empty<byte>());
                if (string.IsNullOrWhiteSpace(observedHex)) return true;
                return !string.Equals(expectedHex, observedHex, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private void NotifyKeyMismatch(string peerUid, string details)
        {
            try
            {
                var normalizedUid = Trim(peerUid);
                var accountName = ResolvePeerAccountName(normalizedUid);
                var msg = $"Key mismatch with {accountName} ({normalizedUid}). Conversation blocked. {details}";
                var securitySummary = AppServices.Localization.GetString("Notifications.KeyMismatchBlocked", "Key mismatch detected. Conversation blocked.");
                Logger.Log(msg);
                try { AppServices.Events.RaiseFirewallPrompt(msg); } catch { }
                try { AppServices.Notifications.PostSecurityEvent(normalizedUid, accountName, securitySummary, details); } catch { }
                try { SafeNetLog($"security key-mismatch | peer={normalizedUid} | {details}"); } catch { }
            }
            catch { }
        }

        private static string ResolvePeerAccountName(string peerUid)
        {
            try
            {
                var normalizedUid = Trim(peerUid);
                if (string.IsNullOrWhiteSpace(normalizedUid)) return "Unknown";
                var contact = AppServices.Contacts.Contacts.FirstOrDefault(c => string.Equals(Trim(c.UID), normalizedUid, StringComparison.OrdinalIgnoreCase));
                if (contact != null && !string.IsNullOrWhiteSpace(contact.DisplayName))
                {
                    return contact.DisplayName.Trim();
                }
                return normalizedUid;
            }
            catch
            {
                return string.IsNullOrWhiteSpace(peerUid) ? "Unknown" : Trim(peerUid);
            }
        }

        private void EnforceKeyMismatchAndTerminate(string peerUid, Utilities.AeadTransport transport, string localDetails, string? remoteDetails = null, TcpClient? client = null, bool notifyRemote = true)
        {
            var normalizedUid = Trim(peerUid);
            NotifyKeyMismatch(normalizedUid, localDetails);

            _ = Task.Run(async () =>
            {
                try
                {
                    if (notifyRemote)
                    {
                        var remoteMsg = string.IsNullOrWhiteSpace(remoteDetails)
                            ? "Key mismatch detected. Conversation cannot continue."
                            : remoteDetails!;
                        var alert = BuildSecurityAlertFrame(SecurityReasonKeyMismatch, remoteMsg);
                        try { await transport.WriteAsync(alert, CancellationToken.None); } catch { }
                    }

                    var removed2 = false;
                    try { removed2 = _sessions.TryRemove(normalizedUid, out _); } catch { }
                    try { _sessionModes.TryRemove(normalizedUid, out _); } catch { }
                    if (removed2) RaiseSessionCountChanged();
                    try { _handshakePeerKeys.TryRemove(transport, out _); } catch { }
                    try { _transportEndpoints.TryRemove(transport, out _); } catch { }
                    try { transport.Dispose(); } catch { }
                    try { client?.Close(); } catch { }
                    try { AppServices.Contacts.SetLastKnownEncrypted(normalizedUid, false, AppServices.Passphrase); } catch { }
                    try { AppServices.Peers.SetPeerStatus(normalizedUid, "Offline"); } catch { }
                }
                catch { }
            });
        }

        private bool ShouldAcceptAvatar(string uid, int len, byte[] data)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(uid)) return false;
                if (len <= 0 || len > AvatarMaxBytes) return false;

                // Per-peer interval
                var now = DateTime.UtcNow;
                if (_avatarLastAcceptedUtc.TryGetValue(uid, out var last) && (now - last) < AvatarMinIntervalPerPeer)
                {
                    return false;
                }

                // Lightweight signature (length + sparse hash) to avoid re-writing same avatar
                unchecked
                {
                    int hash = 17;
                    int step = Math.Max(1, len / 64);
                    for (int i = 0; i < len; i += step) hash = hash * 31 + data[i];
                    var sig = $"{len}:{hash}";
                    if (_avatarLastSignature.TryGetValue(uid, out var prev) && string.Equals(prev, sig, StringComparison.Ordinal))
                    {
                        // Duplicate content; skip
                        return false;
                    }
                    // Global windowed cap
                    while (_avatarGlobalAccepts.TryPeek(out var ts) && (now - ts) > AvatarGlobalWindow)
                    {
                        _avatarGlobalAccepts.TryDequeue(out _);
                    }
                    if (_avatarGlobalAccepts.Count >= AvatarGlobalMaxPerWindow)
                    {
                        return false;
                    }
                    // Accept: record state
                    _avatarLastAcceptedUtc[uid] = now;
                    _avatarLastSignature[uid] = sig;
                    _avatarGlobalAccepts.Enqueue(now);
                }
                return true;
            }
            catch { return false; }
        }

        private static async Task WriteFrame(System.IO.Stream ns, byte[] payload, CancellationToken ct)
        {
            var lenBuf = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(lenBuf, (uint)payload.Length);
            await ns.WriteAsync(lenBuf.AsMemory(0, 4), ct);
            await ns.WriteAsync(payload.AsMemory(0, payload.Length), ct);
            await ns.FlushAsync(ct);
        }

        private static string BuildRelaySessionKey(string uid1, string uid2)
        {
            if (string.Compare(uid1, uid2, StringComparison.OrdinalIgnoreCase) <= 0)
            {
                return $"{uid1}:{uid2}";
            }
            return $"{uid2}:{uid1}";
        }

        private void RegisterPendingOutboundExpectation(string? endpoint, string expectedUid)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(endpoint)) return;
                if (string.IsNullOrWhiteSpace(expectedUid)) return;

                var ep = endpoint.Trim();
                var uid = Trim(expectedUid);
                if (string.IsNullOrWhiteSpace(ep) || string.IsNullOrWhiteSpace(uid)) return;

                var expectation = new PendingOutboundExpectation
                {
                    ExpectedUid = uid,
                    CreatedUtc = DateTime.UtcNow
                };

                _pendingOutboundExpectations[ep] = expectation;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                        if (_pendingOutboundExpectations.TryGetValue(ep, out var current) && object.ReferenceEquals(current, expectation))
                        {
                            _pendingOutboundExpectations.TryRemove(ep, out _);
                        }
                    }
                    catch { }
                });
            }
            catch { }
        }

        private static bool TryParseRelayEndpoint(string input, out string host, out int port)
        {
            host = string.Empty;
            port = 443;
            if (string.IsNullOrWhiteSpace(input)) return false;

            var text = input.Trim();
            if (text.StartsWith("[", StringComparison.Ordinal))
            {
                var end = text.IndexOf(']');
                if (end <= 1) return false;
                host = text.Substring(1, end - 1);
                if (end + 1 < text.Length)
                {
                    if (text[end + 1] != ':') return false;
                    if (!int.TryParse(text.Substring(end + 2), out port)) return false;
                }
            }
            else
            {
                var idx = text.LastIndexOf(':');
                if (idx > 0 && idx < text.Length - 1)
                {
                    host = text.Substring(0, idx);
                    if (!int.TryParse(text.Substring(idx + 1), out port)) return false;
                }
                else
                {
                    host = text;
                }
            }

            if (port <= 0 || port > 65535) return false;

            if (System.Net.IPAddress.TryParse(host, out _)) return true;
            var hostType = Uri.CheckHostName(host);
            return hostType == UriHostNameType.Dns;
        }

        private bool TryResolveRelayEndpoint(string input, out string host, out int port)
        {
            if (TryParseRelayEndpoint(input, out host, out port)) return true;

            host = string.Empty;
            port = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;

            var token = input.Trim();
            var now = DateTime.UtcNow;
            var relayTtl = GetRelayDiscoveryTtl();
            var matches = _discoveredRelays.Values
                .Where(r => string.Equals(r.Token, token, StringComparison.Ordinal) && (now - r.LastSeenUtc) <= relayTtl)
                .OrderBy(r => RelayDistanceScore(r.Host))
                .ThenByDescending(r => r.LastSeenUtc)
                .ToList();

            if (matches.Count == 0)
            {
                return false;
            }

            host = matches[0].Host;
            port = matches[0].Port;
            Logger.Log($"Resolved relay token '{token}' to {host}:{port}");
            return true;
        }

        private int RelayDistanceScore(string host)
        {
            try
            {
                if (!IPAddress.TryParse(host, out var relayIp)) return 50;
                if (PreferredBindAddress != null && PreferredBindAddress.AddressFamily == AddressFamily.InterNetwork && relayIp.AddressFamily == AddressFamily.InterNetwork)
                {
                    var localBytes = PreferredBindAddress.GetAddressBytes();
                    var relayBytes = relayIp.GetAddressBytes();
                    if (localBytes[0] == relayBytes[0] && localBytes[1] == relayBytes[1] && localBytes[2] == relayBytes[2]) return 0;
                    if (IsPrivateV4(relayBytes)) return 1;
                    return 2;
                }
            }
            catch { }

            return 10;
        }

        private static string BuildRelayHealthKey(string host, int port)
            => $"{host}:{port}";

        /// <summary>Returns a snapshot of relay candidate health for monitoring.</summary>
        public IReadOnlyList<RelayHealthSnapshot> GetRelayHealthSnapshots()
        {
            try
            {
                var s = AppServices.Settings?.Settings;
                if (s == null) return Array.Empty<RelayHealthSnapshot>();
                var candidates = BuildRelayCandidates(s);
                var result = new List<RelayHealthSnapshot>(candidates.Count);
                foreach (var c in candidates)
                {
                    var key = BuildRelayHealthKey(c.Host, c.Port);
                    var h = _relayHealth.GetOrAdd(key, _ => new RelayHealthEntry());
                    lock (h)
                    {
                        result.Add(new RelayHealthSnapshot
                        {
                            Endpoint = c.Display,
                            SuccessCount = h.SuccessCount,
                            FailureCount = h.FailureCount,
                            LatencyMs = h.EwmaLatencyMs,
                            LastSuccessUtc = h.LastSuccessUtc,
                            LastFailureUtc = h.LastFailureUtc,
                            Score = GetRelayHealthScore(c.Host, c.Port),
                        });
                    }
                }
                return result;
            }
            catch { return Array.Empty<RelayHealthSnapshot>(); }
        }

        public sealed class RelayHealthSnapshot
        {
            public string Endpoint { get; init; } = "";
            public int SuccessCount { get; init; }
            public int FailureCount { get; init; }
            public double LatencyMs { get; init; }
            public DateTime LastSuccessUtc { get; init; }
            public DateTime LastFailureUtc { get; init; }
            public double Score { get; init; }
        }

        private double GetRelayHealthScore(string host, int port)
        {
            try
            {
                var key = BuildRelayHealthKey(host, port);
                if (!_relayHealth.TryGetValue(key, out var h)) return 0;

                lock (h)
                {
                    var score = 0.0;
                    score += h.SuccessCount * 2.0;
                    score -= h.FailureCount * 1.25;
                    if (h.LastSuccessUtc > DateTime.UtcNow.AddMinutes(-10)) score += 3.0;
                    if (h.LastFailureUtc > DateTime.UtcNow.AddMinutes(-2)) score -= 2.0;

                    // Lower latency should increase score.
                    var latencyPenalty = Math.Clamp((h.EwmaLatencyMs - 500.0) / 300.0, -2.0, 4.0);
                    score -= latencyPenalty;
                    return score;
                }
            }
            catch { return 0; }
        }

        private void RecordRelayAttemptResult(string host, int port, bool success, double elapsedMs)
        {
            try
            {
                var key = BuildRelayHealthKey(host, port);
                var entry = _relayHealth.GetOrAdd(key, _ => new RelayHealthEntry());
                lock (entry)
                {
                    if (success)
                    {
                        entry.SuccessCount++;
                        entry.LastSuccessUtc = DateTime.UtcNow;
                        entry.EwmaLatencyMs = (entry.EwmaLatencyMs * 0.75) + (Math.Max(1.0, elapsedMs) * 0.25);
                    }
                    else
                    {
                        entry.FailureCount++;
                        entry.LastFailureUtc = DateTime.UtcNow;
                        // Penalize failures by nudging latency up a bit.
                        entry.EwmaLatencyMs = Math.Min(10000, entry.EwmaLatencyMs + 250);
                    }
                }
            }
            catch { }
        }

        private List<(string Host, int Port, string Display)> BuildRelayCandidates(AppSettings s)
        {
            var candidates = new List<(string Host, int Port, string Display)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddCandidate(string? endpoint)
            {
                if (string.IsNullOrWhiteSpace(endpoint)) return;
                if (!TryResolveRelayEndpoint(endpoint, out var host, out var port)) return;
                var key = BuildRelayHealthKey(host, port);
                if (!seen.Add(key)) return;
                candidates.Add((host, port, endpoint.Trim()));

                // If the user did not specify a port, also try 8443 as an alternate relay port.
                // This helps on networks that expect TLS semantics on 443 and block plaintext relay traffic.
                if (!HasExplicitPort(endpoint) && port != 8443)
                {
                    var altKey = BuildRelayHealthKey(host, 8443);
                    if (seen.Add(altKey))
                    {
                        candidates.Add((host, 8443, $"{endpoint.Trim()}:8443"));
                    }
                }
            }

            AddCandidate(s.RelayServer);
            foreach (var relay in s.SavedRelayServers ?? new List<string>())
            {
                AddCandidate(relay);
            }
            // Include seed relays as fallback candidates, especially useful for first-run WAN users.
            foreach (var seed in s.WanSeedNodes ?? new List<string>())
            {
                AddCandidate(seed);
            }

            if (candidates.Count > 1)
            {
                candidates = candidates
                    .OrderByDescending(c => GetRelayHealthScore(c.Host, c.Port))
                    .ThenBy(c => RelayDistanceScore(c.Host))
                    .ToList();
            }

            return candidates;
        }

        private static bool HasExplicitPort(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint)) return false;
            var text = endpoint.Trim();
            if (text.StartsWith("[", StringComparison.Ordinal))
            {
                var end = text.IndexOf(']');
                return end > 0 && end + 1 < text.Length && text[end + 1] == ':';
            }

            var firstColon = text.IndexOf(':');
            var lastColon = text.LastIndexOf(':');
            return firstColon == lastColon && firstColon > 0 && firstColon < text.Length - 1;
        }

        private TimeSpan GetDirectSessionWaitTimeout()
        {
            lock (_timeoutTuningGate)
            {
                var ms = Math.Clamp(_directSessionWaitMsEwma * 1.75, 4000, 15000);
                return TimeSpan.FromMilliseconds(ms);
            }
        }

        private TimeSpan GetDirectRetrySessionWaitTimeout()
        {
            lock (_timeoutTuningGate)
            {
                var ms = Math.Clamp(_directSessionWaitMsEwma * 1.1, 3500, 9000);
                return TimeSpan.FromMilliseconds(ms);
            }
        }

        private TimeSpan GetRelayAckTimeout()
        {
            lock (_timeoutTuningGate)
            {
                var ms = Math.Clamp(_relayAckWaitMsEwma * 2.0, 3000, 10000);
                return TimeSpan.FromMilliseconds(ms);
            }
        }

        private TimeSpan GetRelayPairWaitTimeout()
        {
            lock (_timeoutTuningGate)
            {
                var ms = Math.Clamp(_relayPairWaitMsEwma * 1.6, 15000, 60000);
                return TimeSpan.FromMilliseconds(ms);
            }
        }

        private void ObserveDirectSessionWait(TimeSpan elapsed)
        {
            var measured = Math.Clamp(elapsed.TotalMilliseconds, 250, 30000);
            lock (_timeoutTuningGate)
            {
                _directSessionWaitMsEwma = (_directSessionWaitMsEwma * 0.8) + (measured * 0.2);
            }
        }

        private void ObserveRelayAckWait(TimeSpan elapsed)
        {
            var measured = Math.Clamp(elapsed.TotalMilliseconds, 150, 15000);
            lock (_timeoutTuningGate)
            {
                _relayAckWaitMsEwma = (_relayAckWaitMsEwma * 0.8) + (measured * 0.2);
            }
        }

        private void ObserveRelayPairWait(TimeSpan elapsed)
        {
            var measured = Math.Clamp(elapsed.TotalMilliseconds, 1000, 90000);
            lock (_timeoutTuningGate)
            {
                _relayPairWaitMsEwma = (_relayPairWaitMsEwma * 0.85) + (measured * 0.15);
            }
        }

        private static bool IsPrivateV4(byte[] octets)
        {
            if (octets.Length != 4) return false;
            if (octets[0] == 10) return true;
            if (octets[0] == 172 && octets[1] >= 16 && octets[1] <= 31) return true;
            if (octets[0] == 192 && octets[1] == 168) return true;
            return false;
        }

        private bool ShouldAttemptRelayFallback(string? hostOrIp)
        {
            // Reliability-first policy: once direct/NAT has already failed for this attempt,
            // always allow relay fallback. Prior direct success evidence can become stale quickly
            // (NAT rebinding, roaming, endpoint drift) and should not block relay recovery.
            return true;
        }

        private void MarkOutsideLanDirectSuccess(string? hostOrIp)
        {
            try
            {
                if (IsOutsideLanHost(hostOrIp))
                {
                    var hostKey = hostOrIp?.Trim();
                    if (!string.IsNullOrWhiteSpace(hostKey))
                    {
                        _outsideLanDirectSuccessByHost[hostKey] = DateTime.UtcNow;
                    }
                }
            }
            catch { }
        }

        private static bool IsOutsideLanHost(string? hostOrIp)
        {
            if (string.IsNullOrWhiteSpace(hostOrIp)) return false;
            var host = hostOrIp.Trim();

            if (IPAddress.TryParse(host, out var ip))
            {
                if (IPAddress.IsLoopback(ip)) return false;
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    var octets = ip.GetAddressBytes();
                    if (IsPrivateV4(octets)) return false;
                    if (octets[0] == 169 && octets[1] == 254) return false; // link-local
                    return true;
                }

                // IPv6: treat unique-local/link-local/loopback as local, others as outside-LAN.
                if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return false;
                var bytes = ip.GetAddressBytes();
                if (bytes.Length == 16 && (bytes[0] & 0xFE) == 0xFC) return false; // fc00::/7
                return true;
            }

            // Hostname/domain with no explicit local marker is treated as outside-LAN.
            if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        // Periodic presence liveness sweep: mark peers Offline when not seen recently (no beacon/presence)
        private void SweepPresenceTimeouts()
        {
            var now = DateTime.UtcNow;
            var presenceTimeout = GetPresenceTimeout();
            try
            {
                foreach (var kv in _presenceLastSeenUtc.ToArray())
                {
                    var uid = kv.Key; var last = kv.Value;
                    if (now - last > presenceTimeout)
                    {
                        try { AppServices.Peers.SetPeerStatus(uid, "Offline"); } catch { }
                    }
                }
            }
            catch { }
        }

        // Defensive, handshake-only frame reader that logs expected vs actual bytes and bounds payload sizes.
        // Keeps wire format unchanged; validates plausibility of the payload length to detect protocol mismatches early.
        private static async Task<(bool ok, byte[]? payload, uint expected, int actual, string reason)> TryReadHandshakeFrame(Stream ns, CancellationToken ct)
        {
            const int HeaderSize = 4;
            const int HandshakeMaxFrameLen = 512; // P-256 SPKI is ~90-120 bytes; 512 is a safe upper bound

            // Read length header with a manual loop to avoid throwing on short reads.
            var lenBuf = new byte[HeaderSize];
            int read = 0;
            while (read < HeaderSize)
            {
                int n = await ns.ReadAsync(lenBuf.AsMemory(read, HeaderSize - read), ct);
                if (n == 0)
                {
                    return (false, null, HeaderSize, read, "stream closed while reading length");
                }
                read += n;
            }
            var len = BinaryPrimitives.ReadUInt32BigEndian(lenBuf);
            if (len == 0)
            {
                return (false, null, 0, 0, "empty handshake frame");
            }
            if (len > HandshakeMaxFrameLen)
            {
                return (false, null, len, 0, "handshake frame too large (protocol mismatch?)");
            }

            var payload = new byte[len];
            int got = 0;
            while (got < len)
            {
                int n = await ns.ReadAsync(payload.AsMemory(got, (int)len - got), ct);
                if (n == 0)
                {
                    // Truncated payload
                    return (false, null, len, got, "truncated handshake payload");
                }
                got += n;
            }

            // Plausibility check for ECDH P-256 SPKI, typical range ~80-200 bytes; reject extremely small frames early
            if (got < 60)
            {
                return (false, null, len, got, "handshake payload too small");
            }
            // Basic DER SPKI validation: should start with 0x30 (SEQUENCE). If not, likely protocol mismatch/noise.
            if (payload[0] != 0x30)
            {
                return (false, null, len, got, "unexpected handshake payload (not DER SPKI)");
            }

            return (true, payload, len, got, "");
        }

        private void ReportBytes(int localPort, long inbound, long outbound)
        {
            if (localPort <= 0) return;
            var t = _traffic.GetOrAdd(localPort, _ => new PortTraffic());
            if (inbound != 0) Interlocked.Add(ref t.TotalIn, inbound);
            if (outbound != 0) Interlocked.Add(ref t.TotalOut, outbound);
        }

        public System.Collections.Generic.Dictionary<int, (long TotalIn, long TotalOut)> GetPortStatsSnapshot()
        {
            var dict = new System.Collections.Generic.Dictionary<int, (long, long)>();
            foreach (var kv in _traffic)
            {
                var t = kv.Value;
                var tin = Interlocked.Read(ref t.TotalIn);
                var tout = Interlocked.Read(ref t.TotalOut);
                dict[kv.Key] = (tin, tout);
            }
            if (ListeningPort is int lp && !dict.ContainsKey(lp)) dict[lp] = (0, 0);
            if (LastAutoClientPort is int ap && !dict.ContainsKey(ap)) dict[ap] = (0, 0);
            if (UdpBoundPort is int up && !dict.ContainsKey(up)) dict[up] = (0, 0);
            return dict;
        }

        private sealed class PortTraffic
        {
            public long TotalIn;
            public long TotalOut;
        }

    public async Task<TcpClient> ConnectAsync(string host, int port, CancellationToken ct)
        {
            TcpClient client;
            var bindIp = SelectPreferredBindAddress();
            if (!bindIp.Equals(IPAddress.Any))
            {
                client = new TcpClient(bindIp.AddressFamily);
                try { client.Client.Bind(new IPEndPoint(bindIp, 0)); }
                catch (Exception ex) { Logger.Log($"Local bind {bindIp}:0 failed, continuing without: {ex.Message}"); }
            }
            else
            {
                client = new TcpClient();
            }
            await client.ConnectAsync(host, port, ct);
            try { _diag.IncConnects(); } catch { }
            try { ApplySocketOptions(client.Client); } catch { }
            LastAutoClientPort = (client.Client.LocalEndPoint as IPEndPoint)?.Port;
            Logger.Log($"Connected to {host}:{port} (local {LastAutoClientPort})");
            _ = HandleClient(client, true, ct);
            return client;
        }

        // Identity announce: bind our Ed25519 identity to this session by signing our ephemeral ECDH public key
        private async Task SendIdentityAnnounceAsync(Utilities.AeadTransport transport, byte[] myDhSpki, CancellationToken ct)
        {
            try
            {
                var pub = _identity.PublicKey; // 32 bytes Ed25519
                var sig = _identity.Sign(myDhSpki); // sign our ECDH SPKI bytes
                var versionBytes = Encoding.UTF8.GetBytes(AppInfo.Version);
                
                // Frame: [0xA1][pub_len][pub][sig_len][sig][version_len][version]
                var frame = new byte[1 + 1 + pub.Length + 1 + sig.Length + 1 + versionBytes.Length];
                int i = 0; 
                frame[i++] = 0xA1; 
                frame[i++] = (byte)pub.Length; 
                Buffer.BlockCopy(pub, 0, frame, i, pub.Length); 
                i += pub.Length; 
                frame[i++] = (byte)sig.Length; 
                Buffer.BlockCopy(sig, 0, frame, i, sig.Length);
                i += sig.Length;
                frame[i++] = (byte)versionBytes.Length;
                Buffer.BlockCopy(versionBytes, 0, frame, i, versionBytes.Length);
                
                await transport.WriteAsync(frame, ct);
            }
            catch (Exception ex)
            {
                Logger.Log($"Identity announce send error: {ex.Message}");
            }
        }

        public async Task SendSignedAsync(TcpClient client, string message, CancellationToken ct)
        {
            using var ns = client.GetStream();
            var payload = Encoding.UTF8.GetBytes(message);
            var sig = _identity.Sign(payload);
            var frame = new byte[1 + 1 + 32 + 1 + 64 + payload.Length];
            int i = 0; frame[i++] = 0xB0; frame[i++] = 32; Buffer.BlockCopy(_identity.PublicKey, 0, frame, i, 32); i += 32; frame[i++] = 64; Buffer.BlockCopy(sig, 0, frame, i, 64); i += 64; Buffer.BlockCopy(payload, 0, frame, i, payload.Length);
            await ns.WriteAsync(frame.AsMemory(0, frame.Length), ct);
            await ns.FlushAsync(ct);
        }

        // Sends a signed chat message over an established encrypted session to a known peer UID
        public Task<bool> SendChatAsync(string peerUid, Guid messageId, string content, CancellationToken ct)
        {
            EncChatLog($"SendChatAsync: peerUid={peerUid}, msgId={messageId}, contentLength=lorem ipsum dolor sit amet");
            
            var bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
            EncChatLog($"SendChatAsync: Content encoded to consectetur adipiscing elit UTF-8 bytes");
            
            var idb = messageId.ToByteArray();
            var payloadToSign = new byte[16 + 2 + bytes.Length];
            Buffer.BlockCopy(idb, 0, payloadToSign, 0, 16);
            BinaryPrimitives.WriteUInt16BigEndian(payloadToSign.AsSpan(16, 2), (ushort)bytes.Length);
            Buffer.BlockCopy(bytes, 0, payloadToSign, 18, bytes.Length);
            
            EncChatLog($"SendChatAsync: Signing payload sed do eiusmod tempor incididunt");
            var sig = _identity.Sign(payloadToSign);
            EncChatLog($"SendChatAsync: Signature generated ut labore et dolore magna aliqua");
            
            var frame = new byte[1 + 16 + 2 + bytes.Length + 1 + 32 + 1 + 64];
            int i = 0; frame[i++] = 0xB0; Buffer.BlockCopy(idb, 0, frame, i, 16); i += 16; BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(i, 2), (ushort)bytes.Length); i += 2; Buffer.BlockCopy(bytes, 0, frame, i, bytes.Length); i += bytes.Length; frame[i++] = 32; Buffer.BlockCopy(_identity.PublicKey, 0, frame, i, 32); i += 32; frame[i++] = 64; Buffer.BlockCopy(sig, 0, frame, i, 64);
            EncChatLog($"SendChatAsync: Frame constructed enim ad minim veniam quis nostrud exercitation");
            EncChatLog($"SendChatAsync: Frame header ullamco laboris nisi ut aliquip ex ea commodo");
            
            return TrySendEncryptedAsync(peerUid, frame, ct);
        }

        // Edit message: signed propagation to recipient
        public Task<bool> SendEditMessageAsync(string peerUid, Guid messageId, string newContent, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(newContent ?? string.Empty);
            var idb = messageId.ToByteArray();
            var payloadToSign = new byte[16 + 2 + bytes.Length];
            Buffer.BlockCopy(idb, 0, payloadToSign, 0, 16);
            BinaryPrimitives.WriteUInt16BigEndian(payloadToSign.AsSpan(16, 2), (ushort)bytes.Length);
            Buffer.BlockCopy(bytes, 0, payloadToSign, 18, bytes.Length);
            var sig = _identity.Sign(payloadToSign);
            var frame = new byte[1 + 16 + 2 + bytes.Length + 1 + 32 + 1 + 64];
            int i = 0; frame[i++] = 0xB1; Buffer.BlockCopy(idb, 0, frame, i, 16); i += 16; BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(i, 2), (ushort)bytes.Length); i += 2; Buffer.BlockCopy(bytes, 0, frame, i, bytes.Length); i += bytes.Length; frame[i++] = 32; Buffer.BlockCopy(_identity.PublicKey, 0, frame, i, 32); i += 32; frame[i++] = 64; Buffer.BlockCopy(sig, 0, frame, i, 64);
            return TrySendEncryptedAsync(peerUid, frame, ct);
        }

        // Delete message: signed propagation to recipient
        public Task<bool> SendDeleteMessageAsync(string peerUid, Guid messageId, CancellationToken ct)
        {
            var idb = messageId.ToByteArray();
            var sig = _identity.Sign(idb);
            var frame = new byte[1 + 16 + 1 + 32 + 1 + 64];
            int i = 0; frame[i++] = 0xB2; Buffer.BlockCopy(idb, 0, frame, i, 16); i += 16; frame[i++] = 32; Buffer.BlockCopy(_identity.PublicKey, 0, frame, i, 32); i += 32; frame[i++] = 64; Buffer.BlockCopy(sig, 0, frame, i, 64);
            return TrySendEncryptedAsync(peerUid, frame, ct);
        }

        // Reaction event: signed toggle to recipient
        public Task<bool> SendMessageReactionAsync(string peerUid, Guid messageId, string emoji, bool isAdd, CancellationToken ct)
        {
            var reaction = (emoji ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(reaction)) return Task.FromResult(false);

            var emojiBytes = Encoding.UTF8.GetBytes(reaction);
            if (emojiBytes.Length == 0 || emojiBytes.Length > MaxReactionEmojiBytes) return Task.FromResult(false);

            var eventIdBytes = Guid.NewGuid().ToByteArray();
            var messageIdBytes = messageId.ToByteArray();
            var op = isAdd ? (byte)1 : (byte)0;

            var payloadToSign = new byte[16 + 16 + 1 + 1 + emojiBytes.Length];
            int p = 0;
            Buffer.BlockCopy(eventIdBytes, 0, payloadToSign, p, 16); p += 16;
            Buffer.BlockCopy(messageIdBytes, 0, payloadToSign, p, 16); p += 16;
            payloadToSign[p++] = op;
            payloadToSign[p++] = (byte)emojiBytes.Length;
            Buffer.BlockCopy(emojiBytes, 0, payloadToSign, p, emojiBytes.Length);

            var sig = _identity.Sign(payloadToSign);
            var frame = new byte[1 + 16 + 16 + 1 + 1 + emojiBytes.Length + 1 + 32 + 1 + 64];
            int i = 0;
            frame[i++] = 0xB6;
            Buffer.BlockCopy(eventIdBytes, 0, frame, i, 16); i += 16;
            Buffer.BlockCopy(messageIdBytes, 0, frame, i, 16); i += 16;
            frame[i++] = op;
            frame[i++] = (byte)emojiBytes.Length;
            Buffer.BlockCopy(emojiBytes, 0, frame, i, emojiBytes.Length); i += emojiBytes.Length;
            frame[i++] = 32;
            Buffer.BlockCopy(_identity.PublicKey, 0, frame, i, 32); i += 32;
            frame[i++] = 64;
            Buffer.BlockCopy(sig, 0, frame, i, 64);

            return TrySendEncryptedAsync(peerUid, frame, ct);
        }

    private async Task<bool> TrySendEncryptedAsync(string peerUid, byte[] frame, CancellationToken ct)
        {
            // Block check: Refuse to send messages to blocked peers
            var key = Trim(peerUid);
            if (AppServices.Settings.Settings.BlockList?.Contains(key) == true)
            {
                Logger.Log($"Refusing to send message to blocked peer: {key}");
                return false;
            }
            
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            Utilities.AeadTransport? tr = null;
            var start = DateTime.UtcNow;
            // Kick off a best-effort connection attempt in the background if we don't already have a session
            var connectStarted = false;
            Task? connectTask = null;
            while (true)
            {
                if (_sessions.TryGetValue(key, out tr)) break;
                // Fallback: try opposite form (prefixed) if not already
                if (!key.StartsWith("usr-", StringComparison.Ordinal))
                {
                    if (_sessions.TryGetValue("usr-" + key, out tr)) break;
                }
                else
                {
                    var alt = key.StartsWith("usr-", StringComparison.Ordinal) && key.Length > 4 ? key.Substring(4) : key;
                    if (_sessions.TryGetValue(alt, out tr)) break;
                }

                // If no session yet, start a one-shot connect attempt using discovered peer info
                if (!connectStarted)
                {
                    try
                    {
                        connectStarted = true;
                        connectTask = Task.Run(async () =>
                        {
                            try { await ConnectPeerBestEffortAsync(key, ct); }
                            catch { }
                        }, ct);
                    }
                    catch { }
                }
                if (DateTime.UtcNow > deadline)
                {
                    try
                    {
                        var keys = string.Join(',', _sessions.Keys);
                        Logger.Log($"TrySend timeout; no session for {key}; sessions=[{keys}]");
                        SafeNetLog($"send timeout | peer={key} | sessions={keys}");
                    }
                    catch { }
                    return false;
                }
                await Task.Delay(50, ct);
            }
            EncChatLog($"TrySendEncryptedAsync: Session found consequat duis aute irure dolor in reprehenderit");
            EncChatLog($"TrySendEncryptedAsync: Frame type voluptate velit esse cillum dolore eu fugiat");
            
            await tr!.WriteAsync(frame, ct);
            
            EncChatLog($"TrySendEncryptedAsync: Frame encrypted nulla pariatur excepteur sint occaecat");
            
            try
            {
                var waitedMs = (DateTime.UtcNow - start).TotalMilliseconds;
                if (waitedMs > 250)
                {
                    Logger.Log($"[sess-send] waited {waitedMs:F0}ms for session key={key}; keys={string.Join(',', _sessions.Keys)}");
                    SafeNetLog($"session send-wait {waitedMs:F0}ms | key={key}");
                }
            }
            catch { }
            return true;
        }

        /// <summary>Number of active encrypted sessions (connected peers).</summary>
        public int ActiveSessionCount => _sessions.Count;

        /// <summary>Returns the connection mode (Direct/Relay/None) for a peer.</summary>
        public Models.ConnectionMode GetConnectionMode(string peerUid)
        {
            var key = Trim(peerUid);
            return _sessionModes.TryGetValue(key, out var mode) ? mode : Models.ConnectionMode.None;
        }

        public string? GetPeerVersion(string peerUid)
        {
            var key = Trim(peerUid);
            return _peerVersions.TryGetValue(key, out var ver) ? ver : null;
        }

        /// <summary>Raised when the active session count changes (session added or removed).</summary>
        public event Action<int>? SessionCountChanged;

        private void RaiseSessionCountChanged()
        {
            try { SessionCountChanged?.Invoke(_sessions.Count); } catch { }
        }

        // Public helper: check if an encrypted session exists (post-handshake)
        public bool HasEncryptedSession(string peerUid)
        {
            if (string.IsNullOrWhiteSpace(peerUid)) return false;
            var key = Trim(peerUid);
            if (_sessions.ContainsKey(key)) return true;
            if (!key.StartsWith("usr-", StringComparison.Ordinal) && _sessions.ContainsKey("usr-" + key)) return true;
            if (key.StartsWith("usr-", StringComparison.Ordinal) && key.Length > 4)
            {
                var alt = key.Substring(4);
                if (_sessions.ContainsKey(alt)) return true;
            }
            return false;
        }

        // Await an encrypted session for the given peer UID up to timeout.
        public async Task<bool> WaitForEncryptedSessionAsync(string peerUid, TimeSpan timeout, CancellationToken ct)
        {
            var end = DateTime.UtcNow + timeout;
            var logged = false;
            while (DateTime.UtcNow < end && !ct.IsCancellationRequested)
            {
                if (HasEncryptedSession(peerUid)) return true;
                if (!logged)
                {
                    try { Logger.Log($"Waiting for encrypted session with {peerUid}..."); } catch { }
                    logged = true;
                }
                try { await Task.Delay(75, ct); } catch { }
            }
            var ok = HasEncryptedSession(peerUid);
            if (!ok)
            {
                try
                {
                    var keys = string.Join(',', _sessions.Keys);
                    Logger.Log($"Encrypted session wait timeout for {peerUid}; existing keys=[{keys}]");
                    SafeNetLog($"session wait-timeout | peer={peerUid} | keys={keys}");
                }
                catch { }
            }
            return ok;
        }

        // Broadcast our presence status to a specific peer over an established encrypted session
        // Frame: 0xD0 | 1-byte token length | ASCII token (on|idle|dnd|inv)
        public async Task<bool> SendPresenceAsync(string peerUid, Models.PresenceStatus status, CancellationToken ct)
        {
            // [PRIVACY] Do not actively broadcast presence when Invisible
            if (status == Models.PresenceStatus.Invisible)
            {
                return true;
            }
            var tok = status switch
            {
                Models.PresenceStatus.Online => "on",
                Models.PresenceStatus.Idle => "idle",
                Models.PresenceStatus.DoNotDisturb => "dnd",
                _ => "on"
            };
            var payload = System.Text.Encoding.UTF8.GetBytes(tok);
            var frame = new byte[2 + payload.Length];
            frame[0] = 0xD0; frame[1] = (byte)payload.Length; Buffer.BlockCopy(payload, 0, frame, 2, payload.Length);
            var ok = await TrySendEncryptedAsync(peerUid, frame, ct);
            if (ok)
            {
                try { _presenceLastSentUtc[Trim(peerUid)] = DateTime.UtcNow; } catch { }
            }
            return ok;
        }

        // Fire-and-forget broadcast to all currently active sessions
        public void BroadcastPresenceToActiveSessions(Models.PresenceStatus status)
        {
            try
            {
                // [PRIVACY] Suppress broadcasts when Invisible; rely on TTL expiry on peers
                if (status == Models.PresenceStatus.Invisible) return;
                var peers = _sessions.Keys.ToArray();
                foreach (var uid in peers)
                {
                    _ = SendPresenceAsync(uid, status, CancellationToken.None);
                }
            }
            catch { }
        }

        // Disconnect an active session with a specific peer (used when blocking)
        public void DisconnectPeer(string peerUid)
        {
            try
            {
                var key = Trim(peerUid);
                if (_sessions.TryRemove(key, out var transport))
                {
                    try { _sessionModes.TryRemove(key, out _); } catch { }
                    try { transport?.Dispose(); } catch { }
                    Logger.Log($"Disconnected session with blocked peer: {key}");
                    SafeNetLog($"session disconnect | peer={key} | reason=blocked");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error disconnecting peer {peerUid}: {ex.Message}");
            }
        }

        // Broadcast current avatar to all active sessions when it changes or sharing toggles on
        public void BroadcastAvatarToActiveSessions()
        {
            try
            {
                var bytes = _identity.AvatarBytes;
                if (!_identity.ShareAvatar || bytes == null || bytes.Length == 0) return;
                var frame = BuildAvatarFrame(bytes);
                var peers = _sessions.Keys.ToArray();
                foreach (var uid in peers)
                {
                    try { _ = TrySendEncryptedAsync(uid, frame, CancellationToken.None); } catch { }
                }
            }
            catch { }
        }

        // Broadcast current bio to all active sessions
        public void BroadcastBioToActiveSessions()
        {
            try
            {
                var frame = BuildBioFrame(_identity.Bio);
                var peers = _sessions.Keys.ToArray();
                foreach (var uid in peers)
                {
                    try { _ = TrySendEncryptedAsync(uid, frame, CancellationToken.None); } catch { }
                }
            }
            catch { }
        }

        public async Task<bool> SendContactRequestAsync(string peerUid, string nonce, string displayName, CancellationToken ct)
        {
            var payload = Encoding.UTF8.GetBytes(nonce);
            var sig = _identity.Sign(payload);
            var dn = Encoding.UTF8.GetBytes(displayName ?? string.Empty);
            var frame = new byte[1 + 1 + payload.Length + 32 + 64 + 1 + dn.Length];
            int i = 0; frame[i++] = 0xC0; frame[i++] = (byte)payload.Length; Buffer.BlockCopy(payload, 0, frame, i, payload.Length); i += payload.Length; Buffer.BlockCopy(_identity.PublicKey, 0, frame, i, 32); i += 32; Buffer.BlockCopy(sig, 0, frame, i, 64); i += 64; frame[i++] = (byte)dn.Length; Buffer.BlockCopy(dn, 0, frame, i, dn.Length);
            try { SafeNetLog($"send C0 contact-request | peer={Trim(peerUid)} | nonce={nonce} | dnLen={dn.Length}"); } catch { }
            return await TrySendEncryptedAsync(peerUid, frame, ct);
        }

        public Task<bool> SendContactAcceptAsync(string peerUid, string nonce, CancellationToken ct)
        {
            var payload = Encoding.UTF8.GetBytes(nonce);
            var dn = Encoding.UTF8.GetBytes(_identity.DisplayName ?? string.Empty);
            // Frame: [0xC1][nonce_len][nonce][dn_len][display_name]
            var frame = new byte[1 + 1 + payload.Length + 1 + dn.Length];
            int i = 0;
            frame[i++] = 0xC1;
            frame[i++] = (byte)payload.Length;
            Buffer.BlockCopy(payload, 0, frame, i, payload.Length);
            i += payload.Length;
            frame[i++] = (byte)dn.Length;
            Buffer.BlockCopy(dn, 0, frame, i, dn.Length);
            try { SafeNetLog($"send C1 contact-accept | peer={Trim(peerUid)} | nonce={nonce} | dnLen={dn.Length}"); } catch { }
            return TrySendEncryptedAsync(peerUid, frame, ct);
        }

        public Task<bool> SendContactCancelAsync(string peerUid, string nonce, CancellationToken ct)
        {
            var payload = Encoding.UTF8.GetBytes(nonce);
            var frame = new byte[1 + 1 + payload.Length]; int i = 0; frame[i++] = 0xC2; frame[i++] = (byte)payload.Length; Buffer.BlockCopy(payload, 0, frame, i, payload.Length);
            try { SafeNetLog($"send C2 contact-cancel | peer={Trim(peerUid)} | nonce={nonce}"); } catch { }
            return TrySendEncryptedAsync(peerUid, frame, ct);
        }

        public async Task SendContactRequestAsync(TcpClient client, string nonce, string displayName, CancellationToken ct)
        {
            var payload = Encoding.UTF8.GetBytes(nonce);
            var sig = _identity.Sign(payload);
            var dn = Encoding.UTF8.GetBytes(displayName ?? string.Empty);
            var frame = new byte[1 + 1 + payload.Length + 32 + 64 + 1 + dn.Length];
            int i = 0; frame[i++] = 0xC0; frame[i++] = (byte)payload.Length; Buffer.BlockCopy(payload, 0, frame, i, payload.Length); i += payload.Length; Buffer.BlockCopy(_identity.PublicKey, 0, frame, i, 32); i += 32; Buffer.BlockCopy(sig, 0, frame, i, 64); i += 64; frame[i++] = (byte)dn.Length; Buffer.BlockCopy(dn, 0, frame, i, dn.Length);
            using var ns = client.GetStream();
            await ns.WriteAsync(frame.AsMemory(0, frame.Length), ct);
            await ns.FlushAsync(ct);
        }

        public async Task SendContactAcceptAsync(TcpClient client, string nonce, CancellationToken ct)
        {
            var payload = Encoding.UTF8.GetBytes(nonce);
            var frame = new byte[1 + 1 + payload.Length]; int i = 0; frame[i++] = 0xC1; frame[i++] = (byte)payload.Length; Buffer.BlockCopy(payload, 0, frame, i, payload.Length);
            using var ns = client.GetStream();
            await ns.WriteAsync(frame.AsMemory(0, frame.Length), ct);
            await ns.FlushAsync(ct);
        }

        public async Task SendContactCancelAsync(TcpClient client, string nonce, CancellationToken ct)
        {
            var payload = Encoding.UTF8.GetBytes(nonce);
            var frame = new byte[1 + 1 + payload.Length]; int i = 0; frame[i++] = 0xC2; frame[i++] = (byte)payload.Length; Buffer.BlockCopy(payload, 0, frame, i, payload.Length);
            using var ns = client.GetStream();
            await ns.WriteAsync(frame.AsMemory(0, frame.Length), ct);
            await ns.FlushAsync(ct);
        }

        // 0xC3: Verification intent (no payload). Returns false if session is not established within deadline.
        public Task<bool> SendVerifyIntentAsync(string peerUid, CancellationToken ct)
        {
            var frame = new byte[] { 0xC3 };
            return TrySendEncryptedAsync(Trim(peerUid), frame, ct);
        }

        // 0xC4: Verification request (no payload) – ask peer to open verification dialog.
        public Task<bool> SendVerifyRequestAsync(string peerUid, CancellationToken ct)
        {
            var frame = new byte[] { 0xC4 };
            return TrySendEncryptedAsync(Trim(peerUid), frame, ct);
        }

        // 0xC5: Verification cancel (no payload) – notify peer to dismiss request/dialog.
        public Task<bool> SendVerifyCancelAsync(string peerUid, CancellationToken ct)
        {
            var frame = new byte[] { 0xC5 };
            return TrySendEncryptedAsync(Trim(peerUid), frame, ct);
        }

        // 0xC6: Verification complete (no payload) – notify peer that we have verified them.
        // This allows bidirectional verification updates so both parties see the green shield immediately.
        public Task<bool> SendVerifyCompleteAsync(string peerUid, CancellationToken ct)
        {
            var frame = new byte[] { 0xC6 };
            return TrySendEncryptedAsync(Trim(peerUid), frame, ct);
        }

        private static string Trim(string uid) => uid.StartsWith("usr-", StringComparison.Ordinal) && uid.Length > 4 ? uid.Substring(4) : uid;

        private bool TryAcceptInboundFrameId(string peerUid, byte frameType, Guid messageId)
        {
            if (messageId == Guid.Empty)
            {
                return false;
            }

            var now = DateTime.UtcNow;
            var key = $"{Trim(peerUid)}:{frameType:X2}:{messageId:D}";
            if (_inboundReplayWindow.TryGetValue(key, out var seenAt) && (now - seenAt) <= InboundReplayTtl)
            {
                return false;
            }

            _inboundReplayWindow[key] = now;

            if (_inboundReplayWindow.Count > 5000)
            {
                var cutoff = now - InboundReplayTtl;
                foreach (var kv in _inboundReplayWindow.ToArray())
                {
                    if (kv.Value < cutoff)
                    {
                        _inboundReplayWindow.TryRemove(kv.Key, out _);
                    }
                }
            }

            return true;
        }

        private static async Task<string?> ReadLineAsync(NetworkStream stream, TimeSpan timeout, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            var buffer = new byte[1];
            var builder = new StringBuilder();
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(0, 1), cts.Token);
                    if (read <= 0) return null;
                    var ch = (char)buffer[0];
                    if (ch == '\n') break;
                    if (ch != '\r') builder.Append(ch);
                    if (builder.Length > 512) return null; // Prevent abuse
                }
            }
            catch
            {
                return null;
            }
            return builder.ToString();
        }

        // [TIER-1-BLOCKING] Helper methods for enhanced blocking mechanisms

        // Extract IP address from endpoint string (format: "ip:port")
        private static string? ExtractIpFromEndpoint(string endpoint)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(endpoint)) return null;
                var parts = endpoint.Split(':');
                if (parts.Length < 2) return null;
                // Handle IPv6 addresses in brackets [::1]:port
                var ipPart = parts[0].TrimStart('[').TrimEnd(']');
                return System.Net.IPAddress.TryParse(ipPart, out _) ? ipPart : null;
            }
            catch { return null; }
        }

        // Compute SHA256 fingerprint of public key for blocking
        private static string ComputePublicKeyFingerprint(byte[] publicKey)
        {
            try
            {
                var hash = System.Security.Cryptography.SHA256.HashData(publicKey);
                return Convert.ToBase64String(hash);
            }
            catch { return string.Empty; }
        }

        // Check if IP address is blocked
        private bool IsIpBlocked(string? ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress)) return false;
            try
            {
                return AppServices.Settings.Settings.BlockedIpAddresses?.Contains(ipAddress) == true;
            }
            catch { return false; }
        }

        // Check if public key fingerprint is blocked
        private bool IsPublicKeyBlocked(byte[] publicKey)
        {
            if (publicKey == null || publicKey.Length == 0) return false;
            try
            {
                var fingerprint = ComputePublicKeyFingerprint(publicKey);
                return !string.IsNullOrEmpty(fingerprint) &&
                       AppServices.Settings.Settings.BlockedPublicKeyFingerprints?.Contains(fingerprint) == true;
            }
            catch { return false; }
        }

        // Rate limiting check: returns true if IP should be blocked
        private bool IsRateLimited(string? ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress)) return false;
            
            try
            {
                var now = DateTime.UtcNow;
                var tracker = _connectionAttempts.GetOrAdd(ipAddress, _ => new ConnectionAttemptTracker
                {
                    Count = 0,
                    WindowStart = now,
                    LastAttempt = now
                });

                // Reset window if expired
                if ((now - tracker.WindowStart) > RateLimitWindow)
                {
                    tracker.Count = 1;
                    tracker.WindowStart = now;
                    tracker.LastAttempt = now;
                    return false;
                }

                // Increment and check limit
                tracker.Count++;
                tracker.LastAttempt = now;

                if (tracker.Count > MaxConnectionAttemptsPerWindow)
                {
                    Logger.Log($"[RATE-LIMIT] IP {ipAddress} exceeded connection limit: {tracker.Count} attempts in {(now - tracker.WindowStart).TotalSeconds:F1}s");
                    return true;
                }

                return false;
            }
            catch { return false; }
        }

        // Periodic cleanup of old rate limit tracking entries
        private void CleanupRateLimitTracking()
        {
            try
            {
                var now = DateTime.UtcNow;
                var staleThreshold = now - (RateLimitWindow * 2); // Clean up entries older than 2x window

                var staleKeys = _connectionAttempts
                    .Where(kvp => kvp.Value.LastAttempt < staleThreshold)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in staleKeys)
                {
                    _connectionAttempts.TryRemove(key, out _);
                }

                if (staleKeys.Count > 0)
                {
                    Logger.Log($"[RATE-LIMIT] Cleaned up {staleKeys.Count} stale tracking entries");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[RATE-LIMIT] Cleanup error: {ex.Message}");
            }
        }

        // Expose diagnostics snapshot for UI/monitoring.

        // Apply low-latency, resilient socket options suitable for chat traffic.
        private static void ApplySocketOptions(Socket s)
        {
            try { s.NoDelay = true; } catch { }
            try { s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true); } catch { }
        }

        // Lexicographic comparison of byte arrays for deterministic collision resolution
        private static int CompareBytes(byte[] a, byte[] b)
        {
            var minLen = Math.Min(a.Length, b.Length);
            for (int i = 0; i < minLen; i++)
            {
                var result = a[i].CompareTo(b[i]);
                if (result != 0) return result;
            }
            return a.Length.CompareTo(b.Length);
        }

        /// <summary>
        /// Logs encrypted chat message flow to enc_chat.log
        /// </summary>
        private static void EncChatLog(string message)
        {
            if (!LoggingPaths.Enabled) return;
            
            try
            {
                var line = $"[ENC_CHAT] {DateTime.Now:O}: {message}{Environment.NewLine}";
                LoggingPaths.TryWrite(LoggingPaths.EncryptedChat, line);
            }
            catch
            {
                // Best-effort logging, don't throw
            }
        }

        public void Dispose()
        {
            try { Stop(); } catch { }
            // Dispose timers
            try { _presenceTimeoutTimer?.Dispose(); } catch { }
            try { _rateLimitCleanupTimer?.Dispose(); } catch { }
            // Dispose peer connections
            try
            {
                foreach (var kv in _peers)
                {
                    try { kv.Value.Close(); kv.Value.Dispose(); } catch { }
                }
            }
            catch { }
            try { _cts?.Dispose(); } catch { }
            try
            {
                // AeadTransport has no IDisposable; just clear references
                foreach (var s in _sessions.Values) { /* no dispose */ }
                _sessions.Clear();
            }
            catch { }
            System.GC.SuppressFinalize(this);
        }
    }
}

