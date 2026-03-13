using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Zer0Talk.RelayServer.Services;

/// <summary>
/// Manages federation between multiple relay servers for distributed load sharing.
/// Handles peer discovery, directory synchronization, and cross-relay session routing.
/// </summary>
public sealed class RelayFederationManager : IDisposable
{
    private readonly RelayConfig _config;
    private readonly ConcurrentDictionary<string, FederatedPeer> _peers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FederatedDirectoryEntry> _federatedDirectory = new(StringComparer.OrdinalIgnoreCase);
    // Tracks last reconnect attempt time per peer host:port to throttle reconnect retries.
    private readonly ConcurrentDictionary<string, DateTime> _reconnectThrottleByPeer = new(StringComparer.OrdinalIgnoreCase);
    // Persistent TCP connections to federation peers — reused across health checks and dir-syncs.
    private readonly ConcurrentDictionary<string, PersistentFederationConnection> _peerConnections = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan ReconnectThrottle = TimeSpan.FromSeconds(60);
    private CancellationTokenSource? _cts;
    private Task? _syncTask;
    private Task? _healthTask;
    private Task? _gossipTask;
    private static readonly TimeSpan MeshGossipInterval = TimeSpan.FromMinutes(5);
    // Rate-limiter for auto-connecting to mesh-discovered peers (max 1 new connection per 2 seconds)
    private readonly SemaphoreSlim _meshConnectGate = new(1, 1);
    private readonly ConcurrentDictionary<string, long> _lookupDiag = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastLookupDiagLogUtc = DateTime.MinValue;

    public event Action<string>? Log;
    public event Action<FederationStats>? StatsChanged;

    public bool Enabled => _config.EnableFederation;
    public int PeerCount => _peers.Count;

    private static readonly TimeSpan FederatedDirectoryCacheTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PeerHealthCheckInterval = TimeSpan.FromSeconds(30);
    private TimeSpan DirectorySyncInterval => TimeSpan.FromSeconds(Math.Max(10, _config.FederationSyncIntervalSeconds));
    private const int HealthFailuresBeforeUnhealthy = 3;
    private const int LookupFailuresBeforeUnhealthy = 6;
    private const int DisconnectNotifyAttemptsPerPeer = 3;

    public RelayFederationManager(RelayConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Start federation services: peer connection, health checks, directory sync.
    /// </summary>
    public void Start()
    {
        if (!_config.EnableFederation)
        {
            Log?.Invoke("Federation disabled in config");
            return;
        }

        _cts = new CancellationTokenSource();
        Log?.Invoke($"Federation enabled | mode={_config.FederationTrustMode} | peers={_config.PeerRelays.Count}");

        // Connect to configured peer relays
        foreach (var peerAddress in _config.PeerRelays)
        {
            _ = Task.Run(async () => await ConnectToPeerAsync(peerAddress, _cts.Token));
        }

        // Start background tasks
        _healthTask = Task.Run(async () => await HealthCheckLoopAsync(_cts.Token));
        _syncTask = Task.Run(async () => await DirectorySyncLoopAsync(_cts.Token));
        if (_config.MeshDiscoveryEnabled)
            _gossipTask = Task.Run(async () => await MeshGossipLoopAsync(_cts.Token));
    }

    /// <summary>
    /// Stop all federation activities and disconnect from peers.
    /// </summary>
    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }

        // Dispose all persistent peer connections.
        foreach (var conn in _peerConnections.Values)
        {
            try { conn.Dispose(); } catch { }
        }
        _peerConnections.Clear();

        _peers.Clear();
        Log?.Invoke("Federation stopped");
    }

    /// <summary>
    /// Look up a user across the federated relay network.
    /// Returns the user's host:port if found on any peer relay, or null if not found.
    /// Queries all healthy peers in parallel and returns on the first positive result.
    /// </summary>
    public async Task<(string? host, int? port)> LookupUserFederatedAsync(string uid, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(uid)) return (null, null);
        IncLookupDiag("lookup_attempt");

        var normalizedUid = NormalizeUid(uid);
        if (string.IsNullOrWhiteSpace(normalizedUid)) return (null, null);

        // Check federated directory cache first
        if (_federatedDirectory.TryGetValue(normalizedUid, out var cached))
        {
            if ((DateTime.UtcNow - cached.CachedAtUtc) < FederatedDirectoryCacheTtl)
            {
                IncLookupDiag("cache_hit");
                return (cached.Host, cached.Port);
            }

            _federatedDirectory.TryRemove(normalizedUid, out _);
            IncLookupDiag("cache_stale");
        }

        var peers = _peers.Values
            .OrderByDescending(p => p.IsHealthy)
            .ThenByDescending(p => p.LastSeenUtc)
            .ToList();

        if (peers.Count == 0)
        {
            IncLookupDiag("miss_no_peers");
            MaybeLogLookupDiagnostics();
            return (null, null);
        }

        // Fan out queries to all healthy peers simultaneously; return on first hit.
        var healthyPeers = peers.Where(p => p.IsHealthy).ToList();
        if (healthyPeers.Count > 0)
        {
            IncLookupDiag("peer_query_attempt");
            var result = await ParallelLookupAsync(healthyPeers, normalizedUid, ct);
            if (result.host != null && result.port != null)
            {
                _federatedDirectory[normalizedUid] = new FederatedDirectoryEntry(result.host, result.port.Value, DateTime.UtcNow);
                IncLookupDiag("peer_query_hit");
                return result;
            }
        }

        // Fallback: if no healthy peers found it, try unhealthy peers opportunistically.
        var unhealthyPeers = peers.Where(p => !p.IsHealthy).ToList();
        if (unhealthyPeers.Count > 0)
        {
            IncLookupDiag("fallback_unhealthy_scan");
            var result = await ParallelLookupAsync(unhealthyPeers, normalizedUid, ct);
            if (result.host != null && result.port != null)
            {
                _federatedDirectory[normalizedUid] = new FederatedDirectoryEntry(result.host, result.port.Value, DateTime.UtcNow);
                IncLookupDiag("peer_query_hit");
                return result;
            }
        }

        IncLookupDiag("lookup_miss");
        MaybeLogLookupDiagnostics();

        return (null, null);
    }

    /// <summary>
    /// Broadcast a directory update to all federated peers.
    /// </summary>
    public void BroadcastDirectoryUpdate(string uid, string host, int port)
    {
        if (!_config.EnableFederation) return;
        if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(host) || port <= 0 || port > 65535) return;
        _federatedDirectory[uid] = new FederatedDirectoryEntry(host, port, DateTime.UtcNow);
    }

    /// <summary>
    /// Notify federation that a user has disconnected.
    /// </summary>
    public void BroadcastUserDisconnect(string uid)
    {
        _ = Task.Run(async () => await BroadcastUserDisconnectAsync(uid, CancellationToken.None));
    }

    public async Task BroadcastUserDisconnectAsync(string uid, CancellationToken ct = default)
    {
        if (!_config.EnableFederation) return;
        var normalizedUid = NormalizeUid(uid);
        if (string.IsNullOrWhiteSpace(normalizedUid)) return;

        _federatedDirectory.TryRemove(normalizedUid, out _);

        var peers = _peers.Values.Where(p => p.IsHealthy).ToList();
        if (peers.Count == 0) return;

        var tasks = peers.Select(async peer =>
        {
            var delivered = false;
            for (var attempt = 1; attempt <= DisconnectNotifyAttemptsPerPeer && !ct.IsCancellationRequested; attempt++)
            {
                try
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    linked.CancelAfter(TimeSpan.FromSeconds(2));
                    var response = await SendCommandAsync(peer.Host, peer.Port, BuildFederationCommand($"RELAY-DISCONNECT {normalizedUid}"), TimeSpan.FromSeconds(2), linked.Token);
                    if (!string.IsNullOrWhiteSpace(response) &&
                        (response.StartsWith("OK", StringComparison.OrdinalIgnoreCase) || response.StartsWith("MISS", StringComparison.OrdinalIgnoreCase)))
                    {
                        delivered = true;
                        break;
                    }

                    if (attempt < DisconnectNotifyAttemptsPerPeer)
                    {
                        try { await Task.Delay(100, ct); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    if (attempt >= DisconnectNotifyAttemptsPerPeer)
                    {
                        Log?.Invoke($"Federation disconnect notify error (non-fatal) | peer={peer.Host}:{peer.Port} | attempts={attempt} | error={ex.Message}");
                    }
                    else
                    {
                        try { await Task.Delay(100, ct); } catch { }
                    }
                }
            }

            if (!delivered)
            {
                Log?.Invoke($"Federation disconnect notify failed (non-fatal) | peer={peer.Host}:{peer.Port} | attempts={DisconnectNotifyAttemptsPerPeer}");
            }
        }).ToArray();

        await Task.WhenAll(tasks);
    }

    public void RemoveCachedUser(string uid)
    {
        var normalizedUid = NormalizeUid(uid);
        if (string.IsNullOrWhiteSpace(normalizedUid)) return;
        _federatedDirectory.TryRemove(normalizedUid, out _);
    }

    private async Task ConnectToPeerAsync(string peerAddress, CancellationToken ct)
    {
        try
        {
            var parts = peerAddress.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return;

            var host = parts[0].Trim();
            if (!int.TryParse(parts[1], out var port)) return;

            Log?.Invoke($"Federation connecting to peer | host={host} | port={port}");

            var response = await SendCommandAsync(host, port, BuildFederationCommand($"RELAY-HELLO {_config.RelayAddressToken} {_config.Port} {_config.MaxSessions}"), TimeSpan.FromSeconds(10), ct);
            if (response == null || !response.StartsWith("OK-HELLO", StringComparison.OrdinalIgnoreCase))
            {
                Log?.Invoke($"Federation handshake failed | peer={host}:{port}");
                return;
            }

            // Parse peer info from response
            var responseParts = response.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var peerToken = responseParts.Length > 1 ? responseParts[1] : host;
            var peerCapacity = responseParts.Length > 2 && int.TryParse(responseParts[2], out var cap) ? cap : 512;

            var peer = new FederatedPeer
            {
                RelayToken = peerToken,
                Host = host,
                Port = port,
                MaxCapacity = peerCapacity,
                CurrentLoad = 0,
                LastSeenUtc = DateTime.UtcNow,
                IsHealthy = true
            };

            if (_peers.Count >= _config.MaxFederationPeers)
            {
                Log?.Invoke($"Federation peer cap reached ({_config.MaxFederationPeers}) | rejecting={host}:{port}");
                return;
            }

            _peers[peerToken] = peer;
            Log?.Invoke($"Federation peer connected | token={peerToken} | capacity={peerCapacity}");
            ReportStats();

            // Fetch mesh peers from newly connected relay and auto-connect to any unknown ones.
            if (_config.MeshDiscoveryEnabled && _cts != null)
                _ = Task.Run(() => FetchAndMergeMeshPeersFromAsync(host, port, _cts.Token));
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Federation connect error | peer={peerAddress} | error={ex.Message}");
        }
    }

    /// <summary>Returns "host:port" strings for all currently healthy federation peers.</summary>
    public IReadOnlyList<string> GetKnownPeerAddresses()
        => _peers.Values.Where(p => p.IsHealthy).Select(p => $"{p.Host}:{p.Port}").ToList();

    private async Task FetchAndMergeMeshPeersFromAsync(string host, int port, CancellationToken ct)
    {
        try
        {
            var response = await SendCommandAsync(host, port,
                BuildFederationCommand("RELAY-MESH-PEERS"),
                TimeSpan.FromSeconds(10), ct);

            if (string.IsNullOrWhiteSpace(response) ||
                !response.StartsWith("MESH-PEERS", StringComparison.OrdinalIgnoreCase)) return;

            var entries = response.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (entries.Length < 2) return;

            var newPeers = new List<string>();
            for (var i = 1; i < entries.Length; i++)
            {
                var addr = entries[i].Trim();
                if (string.IsNullOrWhiteSpace(addr) ||
                    string.Equals(addr, "NONE", StringComparison.OrdinalIgnoreCase)) continue;

                // Avoid reflexive connections (self) and already-known peers.
                if (!_peerConnections.ContainsKey(addr))
                    newPeers.Add(addr);
            }

            foreach (var peerAddr in newPeers)
            {
                if (_peers.Count >= _config.MaxFederationPeers) break;
                // Rate-limit: at most one new mesh-discovered connection at a time, 2s apart.
                await _meshConnectGate.WaitAsync(ct);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(2000, ct);
                        await ConnectToPeerAsync(peerAddr, ct);
                    }
                    finally { _meshConnectGate.Release(); }
                }, ct);
            }
        }
        catch { }
    }

    private async Task MeshGossipLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(MeshGossipInterval, ct);

                foreach (var peer in _peers.Values.Where(p => p.IsHealthy).ToList())
                {
                    try { await FetchAndMergeMeshPeersFromAsync(peer.Host, peer.Port, ct); }
                    catch { }
                }
            }
            catch { }
        }
    }

    private enum PeerLookupKind
    {
        Found,
        Miss,
        Error
    }

    private readonly record struct PeerLookupResult(PeerLookupKind Kind, string? Host, int? Port);

    /// <summary>
    /// Queries a list of peers in parallel. Returns the first positive result,
    /// cancelling all outstanding queries as soon as one succeeds.
    /// </summary>
    private async Task<(string? host, int? port)> ParallelLookupAsync(IReadOnlyList<FederatedPeer> peers, string normalizedUid, CancellationToken ct)
    {
        if (peers.Count == 0) return (null, null);
        if (peers.Count == 1)
        {
            try
            {
                var r = await QueryPeerForUserAsync(peers[0], normalizedUid, ct);
                if (r.Kind == PeerLookupKind.Found && r.Host != null && r.Port != null)
                {
                    peers[0].IsHealthy = true;
                    peers[0].ConsecutiveLookupFailures = 0;
                    peers[0].LastSeenUtc = DateTime.UtcNow;
                    return (r.Host, r.Port);
                }
            }
            catch { }
            return (null, null);
        }

        using var innerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tcs = new TaskCompletionSource<(string host, int port)>(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = peers.Select(async peer =>
        {
            try
            {
                var r = await QueryPeerForUserAsync(peer, normalizedUid, innerCts.Token);
                if (r.Kind == PeerLookupKind.Found && r.Host != null && r.Port != null)
                {
                    peer.IsHealthy = true;
                    peer.ConsecutiveLookupFailures = 0;
                    peer.LastSeenUtc = DateTime.UtcNow;
                    tcs.TrySetResult((r.Host, r.Port.Value));
                    innerCts.Cancel();
                }
                else if (r.Kind == PeerLookupKind.Miss)
                {
                    peer.ConsecutiveLookupFailures = 0;
                }
                else
                {
                    peer.ConsecutiveLookupFailures++;
                    if (peer.ConsecutiveLookupFailures >= LookupFailuresBeforeUnhealthy)
                        peer.IsHealthy = false;
                }
            }
            catch
            {
                peer.ConsecutiveLookupFailures++;
                if (peer.ConsecutiveLookupFailures >= LookupFailuresBeforeUnhealthy)
                    peer.IsHealthy = false;
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        if (tcs.Task.IsCompletedSuccessfully)
        {
            var result = await tcs.Task;
            return (result.host, result.port);
        }

        return (null, null);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Cross-relay session bridging (Phase 3)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Queries all healthy peer relays in parallel to find which relay holds the given pending session.
    /// Returns the host and port of the peer relay, or null if not found.
    /// </summary>
    public async Task<(string? host, int? port)> QuerySessionAsync(string sessionKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionKey)) return (null, null);

        var peers = _peers.Values.Where(p => p.IsHealthy).ToList();
        if (peers.Count == 0) return (null, null);

        using var innerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tcs = new TaskCompletionSource<(string host, int port)>(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = peers.Select(async peer =>
        {
            try
            {
                var response = await SendCommandAsync(peer.Host, peer.Port,
                    BuildFederationCommand($"RELAY-SESSION-QUERY {sessionKey}"),
                    TimeSpan.FromSeconds(4), innerCts.Token);

                if (response != null && response.StartsWith("HAS", StringComparison.OrdinalIgnoreCase))
                {
                    tcs.TrySetResult((peer.Host, peer.Port));
                    innerCts.Cancel();
                }
            }
            catch { }
        }).ToArray();

        await Task.WhenAll(tasks);

        if (tcs.Task.IsCompletedSuccessfully)
        {
            var result = await tcs.Task;
            Log?.Invoke($"Federation session found | key={sessionKey[..Math.Min(8, sessionKey.Length)]}... | peer={result.host}:{result.port}");
            return (result.host, result.port);
        }

        return (null, null);
    }

    /// <summary>
    /// Opens a cross-relay bridge to the target relay for the specified session.
    /// The returned TcpClient is connected with RELAY-BRIDGE handshake complete and ready for raw piping.
    /// Caller is responsible for closing the TcpClient when done.
    /// Returns null if the bridge could not be established.
    /// </summary>
    public async Task<TcpClient?> OpenBridgeAsync(string host, int port, string sessionKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionKey)) return null;

        TcpClient? client = null;
        try
        {
            client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            await client.ConnectAsync(host, port, cts.Token);
            client.NoDelay = true;
            var stream = client.GetStream();

            var cmd = BuildFederationCommand($"RELAY-BRIDGE {sessionKey}") + "\n";
            var bytes = Encoding.UTF8.GetBytes(cmd);
            await stream.WriteAsync(bytes, cts.Token);
            await stream.FlushAsync(cts.Token);

            var response = await ReadLineAsync(stream, TimeSpan.FromSeconds(5), cts.Token);
            if (response == null || !response.StartsWith("OK-BRIDGE", StringComparison.OrdinalIgnoreCase))
            {
                Log?.Invoke($"Federation bridge rejected | peer={host}:{port} | key={sessionKey[..Math.Min(8, sessionKey.Length)]}... | response={response ?? "(null)"}");
                try { client.Close(); } catch { }
                return null;
            }

            Log?.Invoke($"Federation bridge established | peer={host}:{port} | key={sessionKey[..Math.Min(8, sessionKey.Length)]}...");
            return client;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Federation bridge error | peer={host}:{port} | error={ex.Message}");
            try { client?.Close(); } catch { }
            return null;
        }
    }

    /// <summary>
    /// Forwards a rendezvous OFFER to a federated peer relay on behalf of a client.
    /// Used when the target UID is registered on a different relay than the source.
    /// </summary>
    public async Task<bool> ForwardOfferAsync(string host, int port, string targetUid, string sourceUid, string sessionKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(targetUid) || string.IsNullOrWhiteSpace(sourceUid) || string.IsNullOrWhiteSpace(sessionKey))
            return false;

        try
        {
            var cmd = BuildFederationCommand($"RELAY-OFFER {targetUid} {sourceUid} {sessionKey}");
            var response = await SendCommandAsync(host, port, cmd, TimeSpan.FromSeconds(5), ct);
            var ok = response != null && response.StartsWith("OK", StringComparison.OrdinalIgnoreCase);
            if (ok)
                Log?.Invoke($"Federation OFFER forwarded | peer={host}:{port} | src={sourceUid[..Math.Min(8, sourceUid.Length)]}... | dst={targetUid[..Math.Min(8, targetUid.Length)]}...");
            return ok;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Federation OFFER forward error | peer={host}:{port} | error={ex.Message}");
            return false;
        }
    }

    private async Task<PeerLookupResult> QueryPeerForUserAsync(FederatedPeer peer, string uid, CancellationToken ct)
    {
        var response = await SendCommandAsync(peer.Host, peer.Port, BuildFederationCommand($"RELAY-LOOKUP {uid}"), TimeSpan.FromSeconds(4), ct);
        if (string.IsNullOrWhiteSpace(response)) return new PeerLookupResult(PeerLookupKind.Error, null, null);

        if (string.Equals(response.Trim(), "MISS", StringComparison.OrdinalIgnoreCase))
        {
            return new PeerLookupResult(PeerLookupKind.Miss, null, null);
        }

        // Parse response: "PEER <uid> <host> <port> <age>"
        var parts = response.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 4 && string.Equals(parts[0], "PEER", StringComparison.OrdinalIgnoreCase))
        {
            var host = parts[2];
            if (int.TryParse(parts[3], out var port))
            {
                return new PeerLookupResult(PeerLookupKind.Found, host, port);
            }
        }

        return new PeerLookupResult(PeerLookupKind.Error, null, null);
    }

    private async Task HealthCheckLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PeerHealthCheckInterval, ct);

                foreach (var peer in _peers.Values)
                {
                    try
                    {
                        var conn = GetOrCreatePeerConnection(peer);
                        var response = await conn.SendAsync(BuildFederationCommand("RELAY-HEALTH"), TimeSpan.FromSeconds(5), ct);
                        if (response != null && response.StartsWith("HEALTH", StringComparison.OrdinalIgnoreCase))
                        {
                            peer.LastSeenUtc = DateTime.UtcNow;
                            peer.IsHealthy = true;
                            peer.ConsecutiveHealthFailures = 0;
                            peer.ConsecutiveLookupFailures = 0;
                            var parts = response.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 4)
                            {
                                if (int.TryParse(parts[3], out var active)) peer.CurrentLoad = active;
                            }
                        }
                        else
                        {
                            peer.ConsecutiveHealthFailures++;
                            if (peer.ConsecutiveHealthFailures >= HealthFailuresBeforeUnhealthy)
                            {
                                peer.IsHealthy = false;
                                TryScheduleReconnect(peer, ct);
                            }
                        }
                    }
                    catch
                    {
                        peer.ConsecutiveHealthFailures++;
                        if (peer.ConsecutiveHealthFailures >= HealthFailuresBeforeUnhealthy)
                        {
                            peer.IsHealthy = false;
                            TryScheduleReconnect(peer, ct);
                        }
                    }
                }

                ReportStats();
            }
            catch { }
        }
    }

    /// <summary>
    /// Schedules a reconnect attempt to an unhealthy peer, rate-limited to once per 60 seconds.
    /// </summary>
    private void TryScheduleReconnect(FederatedPeer peer, CancellationToken ct)
    {
        var peerKey = $"{peer.Host}:{peer.Port}";
        var now = DateTime.UtcNow;
        if (_reconnectThrottleByPeer.TryGetValue(peerKey, out var lastAttempt) &&
            (now - lastAttempt) < ReconnectThrottle)
        {
            return;
        }

        _reconnectThrottleByPeer[peerKey] = now;
        Log?.Invoke($"Federation scheduling reconnect | peer={peerKey} | failures={peer.ConsecutiveHealthFailures}");

        _ = Task.Run(async () =>
        {
            try
            {
                var peerAddress = $"{peer.Host}:{peer.Port}";
                await ConnectToPeerAsync(peerAddress, ct);
            }
            catch { }
        }, ct);
    }

    private async Task DirectorySyncLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(DirectorySyncInterval, ct);

                foreach (var peer in _peers.Values.Where(p => p.IsHealthy))
                {
                    try
                    {
                        await SyncDirectoryFromPeerAsync(peer, ct);
                    }
                    catch (Exception ex)
                    {
                        Log?.Invoke($"Federation directory sync error | peer={peer.Host}:{peer.Port} | error={ex.Message}");
                        peer.IsHealthy = false;
                    }
                }

                PruneFederatedDirectory();
                ReportStats();
            }
            catch { }
        }
    }

    private async Task SyncDirectoryFromPeerAsync(FederatedPeer peer, CancellationToken ct)
    {
        var conn = GetOrCreatePeerConnection(peer);
        var response = await conn.SendAsync(BuildFederationCommand("RELAY-DIR-DUMP"), TimeSpan.FromSeconds(6), ct);
        if (string.IsNullOrWhiteSpace(response))
        {
            return;
        }

        if (!response.StartsWith("DIRDUMP", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(response.Trim(), "DIRDUMP", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var payload = response.Length > 8 ? response[8..].Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        var imported = 0;
        var now = DateTime.UtcNow;
        var entries = payload.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var entry in entries)
        {
            var parts = entry.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 3) continue;

            var uid = NormalizeUid(parts[0]);
            var host = parts[1].Trim();
            if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(host)) continue;
            if (!int.TryParse(parts[2], out var port) || port < 1 || port > 65535) continue;

            _federatedDirectory[uid] = new FederatedDirectoryEntry(host, port, now);
            imported++;
        }

        if (imported > 0)
        {
            Log?.Invoke($"Federation directory sync | peer={peer.Host}:{peer.Port} | imported={imported}");
        }
    }

    private static string NormalizeUid(string uid)
    {
        var s = (uid ?? string.Empty).Trim();
        return s.StartsWith("usr-", StringComparison.Ordinal) && s.Length > 4 ? s[4..] : s;
    }

    private void PruneFederatedDirectory()
    {
        var cutoff = DateTime.UtcNow - FederatedDirectoryCacheTtl;
        foreach (var kv in _federatedDirectory)
        {
            if (kv.Value.CachedAtUtc < cutoff)
            {
                _federatedDirectory.TryRemove(kv.Key, out _);
            }
        }
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

                if (builder.Length > 4096) return null; // Prevent memory abuse
            }
        }
        catch
        {
            return null;
        }

        return builder.ToString();
    }

    private static async Task<string?> SendCommandAsync(string host, int port, string command, TimeSpan timeout, CancellationToken ct)
    {
        using var client = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        await client.ConnectAsync(host, port, cts.Token);
        client.NoDelay = true;
        using var stream = client.GetStream();

        var bytes = Encoding.UTF8.GetBytes(command + "\n");
        await stream.WriteAsync(bytes, cts.Token);
        await stream.FlushAsync(cts.Token);

        return await ReadLineAsync(stream, timeout, cts.Token);
    }

    private string BuildFederationCommand(string baseCommand)
    {
        var secret = (_config.FederationSharedSecret ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(secret)) return baseCommand;
        return baseCommand + " " + secret;
    }

    private void ReportStats()
    {
        var healthyPeers = _peers.Values.Count(p => p.IsHealthy);
        var totalPeers = _peers.Count;
        var cachedEntries = _federatedDirectory.Count;

        StatsChanged?.Invoke(new FederationStats(totalPeers, healthyPeers, cachedEntries));
    }

    private void IncLookupDiag(string key)
    {
        try { _lookupDiag.AddOrUpdate(key, 1, (_, current) => current + 1); } catch { }
    }

    private void MaybeLogLookupDiagnostics()
    {
        try
        {
            var now = DateTime.UtcNow;
            if ((now - _lastLookupDiagLogUtc) < TimeSpan.FromSeconds(30)) return;
            _lastLookupDiagLogUtc = now;

            var attempts = _lookupDiag.TryGetValue("lookup_attempt", out var a) ? a : 0;
            var cacheHit = _lookupDiag.TryGetValue("cache_hit", out var c) ? c : 0;
            var peerHit = _lookupDiag.TryGetValue("peer_query_hit", out var h) ? h : 0;
            var misses = _lookupDiag.TryGetValue("lookup_miss", out var m) ? m : 0;
            var qErr = _lookupDiag.TryGetValue("peer_query_error", out var qe) ? qe : 0;
            var qEx = _lookupDiag.TryGetValue("peer_query_exception", out var qx) ? qx : 0;

            Log?.Invoke($"Federation lookup diag | attempts={attempts} cacheHit={cacheHit} peerHit={peerHit} miss={misses} queryError={qErr} queryException={qEx}");
        }
        catch { }
    }

    private PersistentFederationConnection GetOrCreatePeerConnection(FederatedPeer peer)
    {
        var key = $"{peer.Host}:{peer.Port}";
        return _peerConnections.GetOrAdd(key, _ => new PersistentFederationConnection(peer.Host, peer.Port));
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Persistent federation connection (reused across commands)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// A long-lived TCP connection to a federation peer, protected by a semaphore.
    /// Automatically reconnects when the underlying socket is detected dead.
    /// Used for high-frequency background traffic (health checks, dir-syncs).
    /// </summary>
    private sealed class PersistentFederationConnection : IDisposable
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly string _host;
        private readonly int _port;
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

        public PersistentFederationConnection(string host, int port)
        {
            _host = host;
            _port = port;
        }

        public async Task<string?> SendAsync(string command, TimeSpan timeout, CancellationToken ct)
        {
            await _gate.WaitAsync(ct);
            try
            {
                await EnsureConnectedAsync(ct);
                if (_stream == null) return null;

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeout);

                var bytes = Encoding.UTF8.GetBytes(command + "\n");
                await _stream.WriteAsync(bytes, cts.Token);
                await _stream.FlushAsync(cts.Token);

                return await ReadLineInternalAsync(_stream, timeout, cts.Token);
            }
            catch
            {
                // Connection dead; tear down so next call reconnects.
                TearDown();
                return null;
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task EnsureConnectedAsync(CancellationToken ct)
        {
            if (_client != null && IsAlive(_client)) return;
            TearDown();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ConnectTimeout);

            var client = new TcpClient();
            await client.ConnectAsync(_host, _port, cts.Token);
            client.NoDelay = true;
            try
            {
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 30);
                client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 10);
                client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
            }
            catch { }

            _client = client;
            _stream = client.GetStream();
        }

        private void TearDown()
        {
            try { _client?.Close(); } catch { }
            _client = null;
            _stream = null;
        }

        private static bool IsAlive(TcpClient client)
        {
            try
            {
                if (client.Client == null || !client.Connected) return false;
                var closed = client.Client.Poll(0, SelectMode.SelectRead) && client.Client.Available == 0;
                return !closed;
            }
            catch { return false; }
        }

        private static async Task<string?> ReadLineInternalAsync(NetworkStream stream, TimeSpan timeout, CancellationToken ct)
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
                    if (builder.Length > 4096) return null;
                }
            }
            catch { return null; }

            return builder.ToString();
        }

        public void Dispose()
        {
            TearDown();
            _gate.Dispose();
        }
    }
}

/// <summary>
/// Represents a peer relay server in the federation.
/// </summary>
public sealed class FederatedPeer
{
    public required string RelayToken { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public int MaxCapacity { get; set; }
    public int CurrentLoad { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public bool IsHealthy { get; set; }
    public int ConsecutiveHealthFailures { get; set; }
    public int ConsecutiveLookupFailures { get; set; }
}

/// <summary>
/// Cached directory entry from a federated peer.
/// </summary>
public readonly record struct FederatedDirectoryEntry(string Host, int Port, DateTime CachedAtUtc);

/// <summary>
/// Federation statistics for monitoring.
/// </summary>
public readonly record struct FederationStats(int TotalPeers, int HealthyPeers, int CachedDirectoryEntries);
