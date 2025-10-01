/*
    RegressionGuard: monitors critical systems (Network, Encryption, Port config) and reverts to last-known-good behavior on anomalies.
    - Lightweight, scoped corrections only (no global patches).
    - Persists a checkpoint with discovery mode and port.
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

using ZTalk.Utilities;

namespace ZTalk.Services
{
    public sealed class RegressionGuard
    {
        private readonly SettingsService _settings;
        private readonly NetworkService _network;
        private readonly NatTraversalService _nat;
        private readonly object _gate = new();
        private DateTime _lastHealthy = DateTime.UtcNow;
        private int _consecutiveNetFailures;
        private int _consecutiveEncFailures;
        private int _consecutiveDiscFailures;
        private DateTime _discLastHealthy = DateTime.UtcNow;
        // Network stack extended monitors
        private int _noListenStrikes;
        private readonly Dictionary<int, (long In, long Out)> _lastPortTotals = new();
        private DateTime _lastTrafficChangeUtc = DateTime.UtcNow;
        private const string CheckpointFile = "checkpoint.json";
    private DateTime _lastRestartUtc = DateTime.MinValue; // [E] restart throttle marker
    // Tuning additions
    private readonly DateTime _startupUtc = DateTime.UtcNow; // startup baseline
    private DateTime _lastCheckpointRestoreUtc = DateTime.MinValue;
    private bool _restartAttemptedBeforeDiscoveryDowngrade;
    private const int StartupGraceSeconds = 120; // extended grace to suppress early noise
    private static readonly TimeSpan CheckpointRestoreCooldown = TimeSpan.FromMinutes(5);
    // Escalation dampening
    private DateTime _firstNetFailureUtc = DateTime.MinValue;
    private DateTime _postRestartSuppressUntil = DateTime.MinValue;
    private int _escalationsThisSession;
    private DateTime _lastNetDowngradeUtc = DateTime.MinValue;

        public RegressionGuard(SettingsService settings, NetworkService network, NatTraversalService nat)
        { _settings = settings; _network = network; _nat = nat; }

        public void Start()
        {
            try
            {
                // Start background monitor loop via UpdateManager
                AppServices.Updates.RegisterBgInterval("RegressionGuard", 4000, Evaluate);
            }
            catch { }
        }

        private string GetCheckpointPath()
        {
            var dir = ZTalk.Utilities.AppDataPaths.Root;
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, CheckpointFile);
        }

        private void Evaluate()
        {
            try
            {
                // 1) Encryption self-test (round-trip)
                var encOk = SelfTestEncryption();
                if (!encOk) _consecutiveEncFailures++; else _consecutiveEncFailures = 0;

                // 2) Network health from diagnostics and UDP loopback
                var netOk = SelfTestNetwork();
                if (!netOk)
                {
                    _consecutiveNetFailures++;
                    if (_firstNetFailureUtc == DateTime.MinValue) _firstNetFailureUtc = DateTime.UtcNow;
                }
                else
                {
                    _consecutiveNetFailures = 0;
                    _firstNetFailureUtc = DateTime.MinValue;
                }

                // 3) Discovery health (states/backoff)
                var discOk = EvaluateDiscovery();
                if (!discOk) _consecutiveDiscFailures++; else _consecutiveDiscFailures = 0;

                // 2.5) Extended network stack checks (stalled listener, port drift, zero-traffic with active sessions)
                var netStackOk = EvaluateNetworkStack();

                var inStartupGrace = (DateTime.UtcNow - _startupUtc) < TimeSpan.FromSeconds(StartupGraceSeconds);

                // Persist healthy checkpoint if stable for some time
                if (encOk && netOk && discOk && netStackOk)
                {
                    if ((DateTime.UtcNow - _lastHealthy) > TimeSpan.FromSeconds(30))
                    {
                        SaveCheckpoint();
                        _lastHealthy = DateTime.UtcNow;
                        _restartAttemptedBeforeDiscoveryDowngrade = false; // reset escalation ladder
                    }
                }
                else if (!inStartupGrace)
                {
                    // Scoped corrections
                    var netFailThreshold = GetNetFailureThreshold();
                    var obsWindow = GetNetObservationWindow();
                    var pastObservationWindow = _firstNetFailureUtc != DateTime.MinValue && (DateTime.UtcNow - _firstNetFailureUtc) >= obsWindow;
                    var postRestartSuppressed = DateTime.UtcNow < _postRestartSuppressUntil;
                    if (_consecutiveNetFailures >= netFailThreshold && pastObservationWindow && !postRestartSuppressed)
                    {
                        // First attempt a restart before downgrading discovery behavior
                        if (!_restartAttemptedBeforeDiscoveryDowngrade)
                        {
                            Logger.Log("RegressionGuard: network anomalies; attempting restart before discovery downgrade.");
                            TryRestartNetwork();
                            _restartAttemptedBeforeDiscoveryDowngrade = true;
                            _escalationsThisSession++;
                        }
                        else if (_network.DiscoveryBehavior != NetworkService.DiscoveryMode.BroadcastOnly)
                        {
                            Logger.Log("RegressionGuard: switching discovery to BroadcastOnly due to network anomalies.");
                            try { ZTalk.Utilities.ErrorLogger.LogException(new InvalidOperationException("[Regression] Discovery mode override → BroadcastOnly (network anomalies)"), source: "Regression.Network"); } catch { }
                            // Apply discovery mode in-place without restarting listener
                            _network.ApplyDiscoveryBehavior(NetworkService.DiscoveryMode.BroadcastOnly);
                            _lastNetDowngradeUtc = DateTime.UtcNow;
                            _escalationsThisSession++;
                        }
                        else
                        {
                            // Already in BroadcastOnly – do not keep restoring checkpoint; only log occasionally (every 30m)
                            if ((DateTime.UtcNow - _lastNetDowngradeUtc) > TimeSpan.FromMinutes(30))
                            {
                                Logger.Log("RegressionGuard: persistent network anomalies while already downgraded (checkpoint restore suppressed).");
                                _lastNetDowngradeUtc = DateTime.UtcNow;
                            }
                        }
                        _consecutiveNetFailures = 0; // prevent rapid cycling
                        _firstNetFailureUtc = DateTime.MinValue;
                    }
                    if (_consecutiveEncFailures >= 2)
                    {
                        Logger.Log("RegressionGuard: encryption self-test failed repeatedly. Check libsodium installation.");
                        try { ZTalk.Utilities.ErrorLogger.LogException(new InvalidOperationException("[Regression] Encryption self-test failed repeatedly"), source: "Regression.Crypto"); } catch { }
                        _consecutiveEncFailures = 0;
                    }
            var discFailThreshold = GetDiscoveryFailureThreshold();
            if (_consecutiveDiscFailures >= discFailThreshold)
                    {
                        // Discovery persistent problems: restart discovery service and consider restoring checkpoint
                        try { AppServices.Discovery.Restart(); Logger.Log("RegressionGuard: restarted DiscoveryService due to anomalies."); try { ZTalk.Utilities.ErrorLogger.LogException(new InvalidOperationException("[Regression] DiscoveryService restarted due to anomalies"), source: "Regression.Discovery"); } catch { } } catch { }
                        // If issues persist, restore network checkpoint to revert discovery mode/settings
            if (_consecutiveDiscFailures >= discFailThreshold * 2)
                        {
                MaybeRestoreCheckpoint();
                            var msg = "RegressionGuard: discovery persistent failure; restored last-known-good network/discovery mode.";
                            try { AppServices.Events.RaiseRegressionDetected(msg); } catch { }
                            try { ZTalk.Utilities.ErrorLogger.LogException(new InvalidOperationException("[Regression] Restored last-known-good checkpoint due to discovery failures"), source: "Regression.Discovery"); } catch { }
                            _consecutiveDiscFailures = 0; // avoid loops
                        }
                    }
                }
            }
            catch { }
        }

    private int GetNetFailureThreshold()
    {
#if DEBUG
        return 2; // aggressive in debug
#else
        return 5; // tolerant in release
#endif
    }

    private int GetDiscoveryFailureThreshold()
    {
#if DEBUG
        return 2;
#else
        return 4;
#endif
    }

        private void TryRestartNetwork()
        {
            try
            {
                // [E] Throttle restarts to avoid rapid cycling under flapping conditions
                var now = DateTime.UtcNow;
                if ((now - _lastRestartUtc) < TimeSpan.FromSeconds(10))
                {
                    Logger.Log("RegressionGuard: restart suppressed (throttled)");
                    return;
                }
                var s = _settings.Settings;
                _network.Stop();
                _network.StartIfMajorNode(s.Port, s.MajorNode);
                _lastRestartUtc = now;
                // Suppress further net escalation actions for a period to let network stabilize
                _postRestartSuppressUntil = now + GetPostRestartSuppressionWindow();
                // After a restart, treat beacon RX window as fresh to avoid immediate re-trigger due to counters
                try
                {
                    _firstNetFailureUtc = DateTime.MinValue;
                    _consecutiveNetFailures = 0;
                }
                catch { }
                try { ZTalk.Utilities.ErrorLogger.LogException(new InvalidOperationException("[Regression] Network restart to recover from anomalies"), source: "Regression.Network"); } catch { }
            }
            catch (Exception ex) { Logger.Log($"RegressionGuard restart failed: {ex.Message}"); }
        }

        private bool SelfTestEncryption()
        {
            try
            {
                // Use AeadTransport with in-memory stream to ensure round-trip works.
                using var ms = new System.IO.MemoryStream();
                var txKey = RandomNumberGenerator.GetBytes(32);
                var rxKey = (byte[])txKey.Clone();
                var txBase = RandomNumberGenerator.GetBytes(16);
                var rxBase = (byte[])txBase.Clone();
                using var t = new AeadTransport(ms, txKey, rxKey, txBase, rxBase);
                var plain = System.Text.Encoding.UTF8.GetBytes("rg-self-test");
                var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                // Write then reset stream for read
                t.WriteAsync(plain, cts.Token).GetAwaiter().GetResult();
                ms.Position = 0;
                var read = t.ReadAsync(cts.Token).GetAwaiter().GetResult();
                return System.Linq.Enumerable.SequenceEqual(plain, read);
            }
            catch { return false; }
        }

        private bool SelfTestNetwork()
        {
            try
            {
                var snap = _network.GetDiagnosticsSnapshot();
                // Tuned heuristics: require minimum data and longer beacon sample window to reduce false positives
                var totalHs = snap.HandshakeOk + snap.HandshakeFail;
                var enoughHandshakeData = totalHs >= 10 || snap.HandshakeFail >= 6;
                var manyFails = enoughHandshakeData && (snap.HandshakeFail > (snap.HandshakeOk * 3 + 8)); // slightly more tolerant
                var uptime = DateTime.UtcNow - _startupUtc;
                // Consider "no LAN beacon RX" unhealthy only under strong evidence conditions to avoid restarts on quiet LANs
                var noRx = false;
                try
                {
                    var ds = AppServices.Discovery.GetSnapshot();
                    var hasSeeds = (ds.Seeds?.Length ?? 0) > 0;
                    var hasPeers = ds.PeersCount > 0;
                    if (_network.DiscoveryBehavior == NetworkService.DiscoveryMode.Normal
                        && uptime > TimeSpan.FromMinutes(10)
                        && snap.UdpBeaconsRecv == 0
                        && snap.UdpBeaconsSent >= 30
                        && (hasSeeds || hasPeers))
                    {
                        noRx = true;
                    }
                }
                catch { }
                var unhealthy = manyFails || noRx;
                if (unhealthy)
                {
                    // Additional quick UDP loopback check for multicast when in Normal mode
                    if (_network.DiscoveryBehavior == NetworkService.DiscoveryMode.Normal)
                    {
                        if (!UdpMulticastLoopbackCheck()) return false;
                    }
                }
                return !unhealthy;
            }
            catch { return false; }
        }

    private TimeSpan GetNetObservationWindow()
    {
#if DEBUG
        return TimeSpan.FromSeconds(30);
#else
        return TimeSpan.FromSeconds(90);
#endif
    }

    private TimeSpan GetPostRestartSuppressionWindow()
    {
#if DEBUG
        return TimeSpan.FromSeconds(20);
#else
        return TimeSpan.FromSeconds(60);
#endif
    }

        private void MaybeRestoreCheckpoint()
        {
            try
            {
                var now = DateTime.UtcNow;
                if ((now - _lastCheckpointRestoreUtc) < CheckpointRestoreCooldown)
                {
                    Logger.Log("RegressionGuard: checkpoint restore suppressed (cooldown).");
                    return;
                }
                RestoreCheckpoint();
                _lastCheckpointRestoreUtc = now;
            }
            catch { }
        }

        private bool UdpMulticastLoopbackCheck()
        {
            try
            {
                using var udp = new System.Net.Sockets.UdpClient(System.Net.Sockets.AddressFamily.InterNetwork);
                udp.Client.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket, System.Net.Sockets.SocketOptionName.ReuseAddress, true);
                var port = 38384; // same as DiscoveryPort
                udp.Client.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Any, port));
                try { udp.JoinMulticastGroup(System.Net.IPAddress.Parse("239.255.42.42")); } catch { return false; }
                var payload = System.Text.Encoding.ASCII.GetBytes("rg-loop");
                var dest = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("239.255.42.42"), port);
                var recvTask = udp.ReceiveAsync();
                udp.Send(payload, payload.Length, dest);
                if (recvTask.Wait(300)) return true;
            }
            catch { }
            return false;
        }

        private void SaveCheckpoint()
        {
            try
            {
                // Snapshot discovery service state for potential restore
                ZTalk.Services.DiscoveryService.Snapshot? ds = null;
                try { ds = AppServices.Discovery.GetSnapshot(); } catch { }
                var cp = new Checkpoint
                {
                    Port = _settings.Settings.Port,
                    MajorNode = _settings.Settings.MajorNode,
                    Discovery = _network.DiscoveryBehavior,
                    AppVersion = GetAppVersion(),
                    DiscoveryState = ds?.StateValue ?? ZTalk.Services.DiscoveryService.State.Idle,
                    DiscoveryLastSuccessUtc = ds?.LastSuccessUtc,
                    LastListeningPort = _network.ListeningPort ?? 0,
                    LastUdpBeaconsRecv = _network.GetDiagnosticsSnapshot().UdpBeaconsRecv,
                    LastHandshakeOk = _network.GetDiagnosticsSnapshot().HandshakeOk,
                    LastHandshakeFail = _network.GetDiagnosticsSnapshot().HandshakeFail,
                    SavedUtc = DateTime.UtcNow
                };
                var json = JsonSerializer.Serialize(cp);
                File.WriteAllText(GetCheckpointPath(), json);
            }
            catch { }
        }

        private void RestoreCheckpoint()
        {
            try
            {
                var path = GetCheckpointPath();
                if (!File.Exists(path)) return;
                var cp = JsonSerializer.Deserialize<Checkpoint>(File.ReadAllText(path));
                if (cp == null) return;
                var changed = false;
                if (cp.Port != _settings.Settings.Port)
                { _settings.Settings.Port = cp.Port; changed = true; }
                if (cp.MajorNode != _settings.Settings.MajorNode)
                { _settings.Settings.MajorNode = cp.MajorNode; changed = true; }
                if (_network.DiscoveryBehavior != cp.Discovery)
                { _network.DiscoveryBehavior = cp.Discovery; changed = true; }
                if (changed)
                {
                    _settings.Save(AppServices.Passphrase);
                    var msg = "RegressionGuard: restored last-known-good network checkpoint.";
                    Logger.Log(msg);
                    try { AppServices.Events.RaiseRegressionDetected(msg); } catch { }
                    try { ZTalk.Utilities.ErrorLogger.LogException(new InvalidOperationException("[Regression] Checkpoint restore applied (port/major/discovery)"), source: "Regression.Network"); } catch { }
                    TryRestartNetwork();
                }
                // If checkpoint indicates discovery was healthy recently, try to nudge discovery back to healthy state
                try
                {
                    var snap = AppServices.Discovery.GetSnapshot();
                    if (cp.DiscoveryState == ZTalk.Services.DiscoveryService.State.Completed && snap.StateValue != ZTalk.Services.DiscoveryService.State.Completed)
                    {
                        AppServices.Discovery.Restart();
                        Logger.Log("RegressionGuard: nudged DiscoveryService to restart based on checkpoint.");
                    }
                }
                catch { }
            }
            catch { }
        }

        // Expose a public entry point to restore the last-known-good checkpoint (optional UI hook)
        public void RestoreLastCheckpoint()
        {
            try { RestoreCheckpoint(); } catch { }
        }

        // Expose a public entry point to immediately save a checkpoint (optional UI hook)
        public void SaveCurrentCheckpoint()
        {
            try
            {
                SaveCheckpoint();
                Logger.Log("RegressionGuard: checkpoint saved on request.");
                try { AppServices.Events.RaiseRegressionDetected("Checkpoint saved."); } catch { }
            }
            catch { }
        }

        private static string GetAppVersion()
        {
            try { return typeof(RegressionGuard).Assembly.GetName().Version?.ToString() ?? "unknown"; } catch { return "unknown"; }
        }

        private sealed class Checkpoint
        {
            public int Port { get; set; }
            public bool MajorNode { get; set; }
            public NetworkService.DiscoveryMode Discovery { get; set; }
            public string AppVersion { get; set; } = "unknown";
            public ZTalk.Services.DiscoveryService.State DiscoveryState { get; set; } = ZTalk.Services.DiscoveryService.State.Idle;
            public DateTime? DiscoveryLastSuccessUtc { get; set; }
            // Lightweight network health at checkpoint time
            public int LastListeningPort { get; set; }
            public long LastUdpBeaconsRecv { get; set; }
            public long LastHandshakeOk { get; set; }
            public long LastHandshakeFail { get; set; }
            public DateTime SavedUtc { get; set; }
        }

        private bool EvaluateDiscovery()
        {
            try
            {
                var snap = AppServices.Discovery.GetSnapshot();
                // Detect stuck in Discovering only after a longer grace period to avoid churn
                if (snap.StateValue == ZTalk.Services.DiscoveryService.State.Discovering && snap.LastAttemptUtc is DateTime la && (DateTime.UtcNow - la) > TimeSpan.FromSeconds(75))
                {
                    Logger.Log("RegressionGuard: DiscoveryService appears stuck in Discovering; restarting.");
                    try { AppServices.Discovery.Restart(); } catch { }
                    return false;
                }
                // If seeds exist but no peers discovered across multiple attempts, flag as failure
                var hasSeeds = (snap.Seeds?.Length ?? 0) > 0;
                if (hasSeeds && snap.PeersCount == 0 && snap.Attempts >= 2)
                {
                    Logger.Log("RegressionGuard: No peers discovered via seeds; major nodes may be unreachable.");
                    return false;
                }
                // Healthy signals
                if (snap.PeersCount > 0 || snap.UdpBeaconsRecv > 0 || snap.StateValue == ZTalk.Services.DiscoveryService.State.Completed)
                {
                    _discLastHealthy = DateTime.UtcNow;
                    return true;
                }
            }
            catch { }
            return true; // default neutral
        }

        private bool EvaluateNetworkStack()
        {
            try
            {
                // 1) Listener presence and desired port drift
                if (!_network.IsListening || _network.ListeningPort is null)
                {
                    _noListenStrikes++;
                    if (_noListenStrikes >= 3)
                    {
                        Logger.Log("RegressionGuard: network listener inactive; attempting restart.");
                        TryRestartNetwork();
                        _noListenStrikes = 0;
                        return false;
                    }
                }
                else
                {
                    _noListenStrikes = 0;
                    // If user configured a static port and Major Node is enabled, ensure we honor it
                    if (_settings.Settings.MajorNode && _settings.Settings.Port > 0 && _network.ListeningPort != _settings.Settings.Port)
                    {
                        Logger.Log($"RegressionGuard: listening on {_network.ListeningPort}, but configured port is {_settings.Settings.Port}; restarting to correct drift.");
                        TryRestartNetwork();
                        return false;
                    }
                }

                // 2) Stalled peer routing: sessions active but no traffic change for a while
                var diag = _network.GetDiagnosticsSnapshot();
                var stats = _network.GetPortStatsSnapshot();
                long sumIn = 0, sumOut = 0;
                foreach (var kv in stats)
                {
                    var p = kv.Key; var t = kv.Value;
                    if (!_lastPortTotals.TryGetValue(p, out var prev)) prev = (0, 0);
                    var din = t.TotalIn - prev.In; var dout = t.TotalOut - prev.Out;
                    sumIn += din; sumOut += dout; _lastPortTotals[p] = (t.TotalIn, t.TotalOut);
                }
                if (sumIn > 0 || sumOut > 0) _lastTrafficChangeUtc = DateTime.UtcNow;
                // Only consider a restart for truly stalled sessions after a long idle period
                if (diag.SessionsActive > 0 && (DateTime.UtcNow - _lastTrafficChangeUtc) > TimeSpan.FromMinutes(10))
                {
                    Logger.Log("RegressionGuard: sessions active but no traffic for 10 minutes; restarting network.");
                    TryRestartNetwork();
                    _lastTrafficChangeUtc = DateTime.UtcNow;
                    return false;
                }
            }
            catch { }
            return true;
        }
    }
}
