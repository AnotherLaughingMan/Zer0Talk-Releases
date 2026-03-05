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
    private CancellationTokenSource? _cts;
    private Task? _syncTask;
    private Task? _healthTask;
    private readonly ConcurrentDictionary<string, long> _lookupDiag = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastLookupDiagLogUtc = DateTime.MinValue;

    public event Action<string>? Log;
    public event Action<FederationStats>? StatsChanged;

    public bool Enabled => _config.EnableFederation;
    public int PeerCount => _peers.Count;

    private static readonly TimeSpan FederatedDirectoryCacheTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PeerHealthCheckInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DirectorySyncInterval = TimeSpan.FromSeconds(60);
    private const int HealthFailuresBeforeUnhealthy = 3;
    private const int LookupFailuresBeforeUnhealthy = 6;
    private const int LookupAttemptsPerPeer = 2;
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
    }

    /// <summary>
    /// Stop all federation activities and disconnect from peers.
    /// </summary>
    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }

        _peers.Clear();
        Log?.Invoke("Federation stopped");
    }

    /// <summary>
    /// Look up a user across the federated relay network.
    /// Returns the user's host:port if found on any peer relay, or null if not found.
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

        var queriedAny = false;

        // First pass: prefer healthy peers.
        foreach (var peer in peers.Where(p => p.IsHealthy))
        {
            queriedAny = true;
            try
            {
                for (var attempt = 1; attempt <= LookupAttemptsPerPeer; attempt++)
                {
                    IncLookupDiag("peer_query_attempt");
                    var result = await QueryPeerForUserAsync(peer, normalizedUid, ct);
                    if (result.Kind == PeerLookupKind.Found && result.Host != null && result.Port != null)
                    {
                        peer.ConsecutiveLookupFailures = 0;
                        peer.ConsecutiveHealthFailures = 0;
                        peer.IsHealthy = true;
                        peer.LastSeenUtc = DateTime.UtcNow;

                        _federatedDirectory[normalizedUid] = new FederatedDirectoryEntry(result.Host, result.Port.Value, DateTime.UtcNow);
                        IncLookupDiag("peer_query_hit");
                        return (result.Host, result.Port);
                    }

                    if (result.Kind == PeerLookupKind.Miss)
                    {
                        peer.ConsecutiveLookupFailures = 0;
                        IncLookupDiag("peer_query_miss");
                        break;
                    }

                    IncLookupDiag("peer_query_error");
                    if (attempt < LookupAttemptsPerPeer)
                    {
                        try { await Task.Delay(120, ct); } catch { }
                    }
                }

                peer.ConsecutiveLookupFailures++;
                if (peer.ConsecutiveLookupFailures >= LookupFailuresBeforeUnhealthy)
                {
                    peer.IsHealthy = false;
                    Log?.Invoke($"Federation peer degraded after lookup failures | peer={peer.Host}:{peer.Port} | failures={peer.ConsecutiveLookupFailures}");
                }
            }
            catch (Exception ex)
            {
                IncLookupDiag("peer_query_exception");
                peer.ConsecutiveLookupFailures++;
                if (peer.ConsecutiveLookupFailures >= LookupFailuresBeforeUnhealthy)
                {
                    peer.IsHealthy = false;
                }

                Log?.Invoke($"Federation lookup error | peer={peer.Host}:{peer.Port} | failures={peer.ConsecutiveLookupFailures} | error={ex.Message}");
            }
        }

        // Second pass: if no healthy peers responded, try unhealthy peers opportunistically.
        if (!queriedAny || peers.All(p => !p.IsHealthy))
        {
            IncLookupDiag("fallback_unhealthy_scan");
            foreach (var peer in peers)
            {
                try
                {
                    IncLookupDiag("peer_query_attempt");
                    var result = await QueryPeerForUserAsync(peer, normalizedUid, ct);
                    if (result.Kind == PeerLookupKind.Found && result.Host != null && result.Port != null)
                    {
                        peer.ConsecutiveLookupFailures = 0;
                        peer.ConsecutiveHealthFailures = 0;
                        peer.IsHealthy = true;
                        peer.LastSeenUtc = DateTime.UtcNow;

                        _federatedDirectory[normalizedUid] = new FederatedDirectoryEntry(result.Host, result.Port.Value, DateTime.UtcNow);
                        IncLookupDiag("peer_query_hit");
                        return (result.Host, result.Port);
                    }
                }
                catch { }
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

            _peers[peerToken] = peer;
            Log?.Invoke($"Federation peer connected | token={peerToken} | capacity={peerCapacity}");
            ReportStats();
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Federation connect error | peer={peerAddress} | error={ex.Message}");
        }
    }

    private enum PeerLookupKind
    {
        Found,
        Miss,
        Error
    }

    private readonly record struct PeerLookupResult(PeerLookupKind Kind, string? Host, int? Port);

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
                        var response = await SendCommandAsync(peer.Host, peer.Port, BuildFederationCommand("RELAY-HEALTH"), TimeSpan.FromSeconds(5), ct);
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
                            }
                        }
                    }
                    catch
                    {
                        peer.ConsecutiveHealthFailures++;
                        if (peer.ConsecutiveHealthFailures >= HealthFailuresBeforeUnhealthy)
                        {
                            peer.IsHealthy = false;
                        }
                    }
                }

                ReportStats();
            }
            catch { }
        }
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
        var response = await SendCommandAsync(peer.Host, peer.Port, BuildFederationCommand("RELAY-DIR-DUMP"), TimeSpan.FromSeconds(6), ct);
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

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
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
