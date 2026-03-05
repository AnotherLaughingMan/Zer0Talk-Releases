using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Zer0Talk.RelayServer.Services;

/// <summary>
/// Relay server host — rebuilt for reliability.
/// Handles: directory registration (REG/LOOKUP), rendezvous coordination (OFFER/POLL/WAITPOLL/ACK),
/// session pairing (RELAY), federation (RELAY-*), and LAN discovery (UDP multicast).
/// </summary>
public sealed class RelayHost
{
    // ── Dependencies ──
    private readonly RelayConfig _config;
    private readonly RelaySessionManager _sessions;
    private readonly RelayForwarder _forwarder;
    private readonly RelayRateLimiter _rateLimiter;
    private readonly RelayFederationManager? _federation;

    // ── Directory: uid → registration entry ──
    private readonly ConcurrentDictionary<string, DirectoryEntry> _directory = new(StringComparer.OrdinalIgnoreCase);

    // ── Rendezvous: targetUid → list of pending invites ──
    private readonly Dictionary<string, List<RendezvousInvite>> _invitesByTarget = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _inviteGate = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _inviteSignals = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _probeAuditLogs = new();
    private readonly ConcurrentDictionary<string, DateTime> _blockedUids = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _blockedIps = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FirstArrivalState> _firstArrivalBySession = new(StringComparer.OrdinalIgnoreCase);

    // ── Network listeners ──
    private TcpListener? _listener;
    private TcpListener? _federationListener;
    private Task? _federationAcceptTask;
    private bool _federationUsesMainPort = true;
    private UdpClient? _discoverySocket;
    private Task? _discoveryTask;
    private CancellationTokenSource? _cts;

    // ── Counters ──
    private long _totalConnections;
    private long _offerCommands;
    private long _pollCommands;
    private long _waitPollCommands;
    private long _ackCommands;
    private long _ackMisses;
    private bool _paused;

    // ── Tuning constants ──
    private const int DefaultDiscoveryPort = 38384;
    private static readonly IPAddress DiscoveryMulticastGroup = IPAddress.Parse("239.255.42.42");

    /// <summary>Directory entries live for 5 minutes (was 2 min — too aggressive).</summary>
    private static readonly TimeSpan DirectoryEntryTtl = TimeSpan.FromMinutes(5);

    /// <summary>Invites expire after 45 seconds.</summary>
    private static readonly TimeSpan InviteTtl = TimeSpan.FromSeconds(45);

    /// <summary>Don't redeliver same invite within 6 seconds.</summary>
    private static readonly TimeSpan InviteRedeliveryDelay = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan FirstArrivalStreakWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FirstArrivalStateTtl = TimeSpan.FromMinutes(10);
    private const int ProbeAuditLimit = 2000;
    private const int HandleFingerprintLength = 12;

    public RelayHost(RelayConfig config)
    {
        _config = config;
        _sessions = new RelaySessionManager(config);
        _forwarder = new RelayForwarder(config.BufferSize);
        _rateLimiter = new RelayRateLimiter(config.MaxConnectionsPerMinute, config.BanSeconds);
        if (config.EnableFederation)
        {
            _federation = new RelayFederationManager(config);
            _federation.Log += msg => Log?.Invoke($"[Federation] {msg}");
            _federation.StatsChanged += stats => Log?.Invoke($"[Federation] peers={stats.HealthyPeers}/{stats.TotalPeers} cache={stats.CachedDirectoryEntries}");
        }
    }

    // ── Public state ──
    public bool IsRunning { get; private set; }
    public bool IsPaused => _paused;
    public event Action<string>? Log;
    public event Action<string>? ProbeAuditLogged;
    public event Action<RelayStats>? StatsChanged;
    public event Action<IReadOnlyList<RelaySessionInfo>>? SessionsChanged;
    public event Action<IReadOnlyList<RelayClientInfo>>? ClientsChanged;

    public IReadOnlyList<string> GetProbeAuditSnapshot()
    {
        return _probeAuditLogs.ToList();
    }

    public void ClearProbeAuditLogs()
    {
        while (_probeAuditLogs.TryDequeue(out _)) { }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Lifecycle
    // ═══════════════════════════════════════════════════════════════

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, _config.Port);
        _listener.Start();
        _federationUsesMainPort = true;
        if (_config.EnableFederation)
        {
            var fedPort = _config.FederationPort > 0 ? _config.FederationPort : _config.Port;
            if (fedPort != _config.Port)
            {
                try
                {
                    _federationListener = new TcpListener(IPAddress.Any, fedPort);
                    _federationListener.Start();
                    _federationUsesMainPort = false;
                    Log?.Invoke($"Relay federation listening on port {fedPort}");
                    _federationAcceptTask = AcceptFederationLoopAsync(_cts.Token);
                }
                catch (Exception ex)
                {
                    _federationUsesMainPort = true;
                    Log?.Invoke($"Relay federation listener failed on {fedPort}; falling back to main port. Error: {ex.Message}");
                }
            }
            else
            {
                Log?.Invoke($"Relay federation using main relay port {_config.Port}");
            }
        }

        IsRunning = true;
        _paused = false;
        Log?.Invoke($"Relay listening on port {_config.Port}");

        if (_config.DiscoveryEnabled)
        {
            StartRelayDiscovery();
            Log?.Invoke($"Relay token: {_config.RelayAddressToken}");
        }

        try { _federation?.Start(); } catch (Exception ex) { Log?.Invoke($"Federation start failed: {ex.Message}"); }
        _ = AcceptLoopAsync(_cts.Token);
        _ = CleanupLoopAsync(_cts.Token);
        ReportStats();
        ReportSessions();
        ReportClients();
    }

    public void Stop()
    {
        if (!IsRunning) return;
        try { _federation?.Stop(); } catch { }
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { _federationListener?.Stop(); } catch { }
        try { _discoverySocket?.Close(); } catch { }
        _discoverySocket = null;
        _discoveryTask = null;
        _federationListener = null;
        _federationAcceptTask = null;
        _federationUsesMainPort = true;
        IsRunning = false;
        _paused = false;
        Log?.Invoke("Relay stopped");
        ReportStats();
        ReportSessions();
        ReportClients();
    }

    public void Pause()  { if (!IsRunning) return; _paused = true;  Log?.Invoke("Relay paused");  ReportStats(); }
    public void Resume() { if (!IsRunning) return; _paused = false; Log?.Invoke("Relay resumed"); ReportStats(); }

    public bool DisconnectSession(string sessionKey)
    {
        var ok = _sessions.DisconnectSession(sessionKey);
        if (ok) { Log?.Invoke($"Relay disconnected session {sessionKey}"); ReportSessions(); }
        return ok;
    }

    public IReadOnlyList<RelayClientInfo> GetRegisteredClientsSnapshot()
    {
        PruneBlockedEntries();
        return _directory
            .Select(kv => BuildClientInfo(kv.Key, kv.Value))
            .OrderBy(client => client.ModerationHandle, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool BlockClientByHandle(string moderationHandle)
    {
        if (string.IsNullOrWhiteSpace(moderationHandle)) return false;

        PruneDirectory();
        PruneBlockedEntries();

        string? matchedUid = null;
        DirectoryEntry matchedEntry = default;
        foreach (var entry in _directory)
        {
            var handle = BuildModerationHandle(entry.Key);
            if (!string.Equals(handle, moderationHandle.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            matchedUid = entry.Key;
            matchedEntry = entry.Value;
            break;
        }

        if (string.IsNullOrWhiteSpace(matchedUid))
        {
            return false;
        }

        var blockFor = TimeSpan.FromSeconds(Math.Max(30, _config.OperatorBlockSeconds));
        var untilUtc = DateTime.UtcNow.Add(blockFor);

        _blockedUids[matchedUid] = untilUtc;
        if (!string.IsNullOrWhiteSpace(matchedEntry.Host))
        {
            _blockedIps[matchedEntry.Host] = untilUtc;
        }

        _directory.TryRemove(matchedUid, out _);
        PurgeInvitesForUid(matchedUid);
        var disconnected = _sessions.DisconnectSessionsForUid(matchedUid);

        Log?.Invoke($"Operator block applied handle={moderationHandle} duration={blockFor.TotalSeconds:F0}s disconnected={disconnected}");
        ReportClients();
        ReportSessions();
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Accept loops
    // ═══════════════════════════════════════════════════════════════

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                if (_paused) { await Task.Delay(200, ct); continue; }
                client = await _listener!.AcceptTcpClientAsync(ct);
                if (!AllowClient(client)) { SafeClose(client); continue; }
                Interlocked.Increment(ref _totalConnections);
                _ = HandleClientAsync(client, ct);
            }
            catch
            {
                SafeClose(client);
                if (!IsRunning) break;
            }
            ReportStats();
        }
    }

    private async Task AcceptFederationLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await _federationListener!.AcceptTcpClientAsync(ct);
                Interlocked.Increment(ref _totalConnections);
                _ = HandleFederationClientAsync(client, ct);
            }
            catch
            {
                SafeClose(client);
                if (!IsRunning) break;
            }
            ReportStats();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Client dispatch
    // ═══════════════════════════════════════════════════════════════

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        ConfigureSocket(client);
        var stream = client.GetStream();
        var remoteEp = client.Client.RemoteEndPoint as IPEndPoint;
        var line = await ReadLineAsync(stream, TimeSpan.FromSeconds(5), ct);
        if (line == null)
        {
            RecordProbeAudit(remoteEp, "Unknown", "(none)", "Failure", "Handshake timeout (no command within 5s)");
            Log?.Invoke("Relay handshake timeout (no command received within 5s)");
            SafeClose(client);
            return;
        }

        var command = GetCommandToken(line);
        var sourceType = ClassifySourceType(command);
        RecordProbeAudit(remoteEp, sourceType, command, "Attempt", "Inbound probe/request received");

        // Detect non-protocol traffic (TLS handshakes, binary probes, scanners)
        if (LooksLikeBinaryOrProbe(line))
        {
            var ip = remoteEp?.Address.ToString() ?? "?";
            RecordProbeAudit(remoteEp, "Unknown", command, "Failure", "Rejected non-protocol probe (TLS/HTTP/Binary)");
            Log?.Invoke($"Rejected non-protocol probe from {ip} (TLS/scanner traffic on plaintext port)");
            SafeClose(client);
            return;
        }

        // Try directory/rendezvous commands first (REG, LOOKUP, OFFER, POLL, WAITPOLL, ACK)
        if (await TryHandleDirectoryCommandAsync(line, client, stream, ct)) return;

        // Federation commands on main port (if shared)
        if (!_federationUsesMainPort && line.StartsWith("RELAY-", StringComparison.OrdinalIgnoreCase))
        {
            RecordProbeAudit(remoteEp, "Zer0Talk Relay", command, "Failure", "Federation command sent to wrong port");
            await WriteLineAsync(stream, "ERR use-federation-port", ct);
            SafeClose(client);
            return;
        }
        if (_federationUsesMainPort && await TryHandleFederationCommandAsync(line, client, stream, ct)) return;

        // RELAY session join
        if (!RelayRequest.TryParse(line, out var request))
        {
            var ip = remoteEp?.Address.ToString() ?? "?";
            RecordProbeAudit(remoteEp, sourceType, command, "Failure", "Rejected non-Zer0Talk/invalid command");
            Log?.Invoke($"Relay received invalid command from {ip}: {(line.Length > 40 ? line[..40] + "..." : line)}");
            await WriteLineAsync(stream, "ERR non-zer0talk-command", ct);
            SafeClose(client);
            return;
        }

        RecordProbeAudit(remoteEp, "Zer0Talk Client", command, "Accepted", "Recognized RELAY session request");

        await HandleRelaySessionAsync(request, client, stream, ct);
    }

    private async Task HandleFederationClientAsync(TcpClient client, CancellationToken ct)
    {
        ConfigureSocket(client);
        var stream = client.GetStream();
        var remoteEp = client.Client.RemoteEndPoint as IPEndPoint;
        var line = await ReadLineAsync(stream, TimeSpan.FromSeconds(5), ct);
        if (line == null)
        {
            RecordProbeAudit(remoteEp, "Zer0Talk Relay", "(none)", "Failure", "Federation handshake timeout");
            SafeClose(client);
            return;
        }

        var command = GetCommandToken(line);
        RecordProbeAudit(remoteEp, ClassifySourceType(command), command, "Attempt", "Inbound federation request received");
        if (await TryHandleFederationCommandAsync(line, client, stream, ct)) return;
        RecordProbeAudit(remoteEp, ClassifySourceType(command), command, "Failure", "Rejected non-federation command on federation port");
        await WriteLineAsync(stream, "ERR federation-command-required", ct);
        SafeClose(client);
    }

    // ═══════════════════════════════════════════════════════════════
    //  RELAY session handling
    // ═══════════════════════════════════════════════════════════════

    private async Task HandleRelaySessionAsync(RelayRequest request, TcpClient client, NetworkStream stream, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.Uid) && IsUidBlocked(request.Uid))
        {
            await WriteLineAsync(stream, "ERR blocked", ct);
            SafeClose(client);
            return;
        }

        var outcome = _sessions.TryPairOrQueue(request, client, stream, out var session);

        switch (outcome)
        {
            case RelaySessionManager.PairOutcome.Queued:
                var queueState = TrackFirstArrival(request);
                await WriteLineAsync(stream, "QUEUED", ct);
                Log?.Invoke($"Relay queued pending session {request.SessionKey} first={queueState.Handle} role={queueState.Role} streak={queueState.Streak}");
                if (queueState.ShouldWarn)
                {
                    Log?.Invoke($"Relay queue imbalance {request.SessionKey}: same side arrived first {queueState.Streak}x over {queueState.Window.TotalSeconds:F0}s; waiting for counterpart");
                }
                ReportStats();
                ReportSessions();
                return;

            case RelaySessionManager.PairOutcome.RejectedAlreadyQueued:
                await WriteLineAsync(stream, "ERR already-queued", ct);
                Log?.Invoke($"Relay duplicate pending ignored {request.SessionKey}: existing first side still waiting");
                ReportStats();
                return;

            case RelaySessionManager.PairOutcome.RejectedCapacity:
                Log?.Invoke($"Relay rejected {request.SessionKey}: capacity ({_sessions.ActiveCount}/{_config.MaxSessions})");
                await WriteLineAsync(stream, "ERR capacity", ct);
                SafeClose(client);
                ReportStats();
                return;

            case RelaySessionManager.PairOutcome.RejectedAlreadyActive:
                Log?.Invoke($"Relay rejected {request.SessionKey}: already active");
                await WriteLineAsync(stream, "ERR already-active", ct);
                SafeClose(client);
                ReportStats();
                return;

            case RelaySessionManager.PairOutcome.RejectedCooldown:
                Log?.Invoke($"Relay rejected {request.SessionKey}: cooldown");
                await WriteLineAsync(stream, "ERR cooldown", ct);
                SafeClose(client);
                ReportStats();
                return;

            case RelaySessionManager.PairOutcome.RejectedIncompatible:
                Log?.Invoke($"Relay rejected {request.SessionKey}: incompatible roles");
                await WriteLineAsync(stream, "ERR incompatible", ct);
                SafeClose(client);
                ReportStats();
                return;

            case RelaySessionManager.PairOutcome.Paired:
                _firstArrivalBySession.TryRemove(request.SessionKey, out _);
                break; // Continue below

            default:
                SafeClose(client);
                return;
        }

        if (session == null) { SafeClose(client); return; }

        // Send PAIRED to both sides
        try
        {
            await WriteLineAsync(stream, "PAIRED", ct);
            await WriteLineAsync(session.GetOtherStream(stream), "PAIRED", ct);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Relay failed to send PAIRED for {session.SessionKey}: {ex.Message}");
            _sessions.RemoveSession(session, out _);
            return;
        }

        Log?.Invoke($"Relay paired session {session.SessionKey}");
        ReportStats();
        ReportSessions();

        // Forward data until one side disconnects
        try
        {
            await session.RunAsync(_forwarder, ct);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Relay session {session.SessionKey} error: {ex.Message}");
        }
        finally
        {
            _sessions.RemoveSession(session, out var lifetime);
            var ms = (int)lifetime.TotalMilliseconds;
            Log?.Invoke(ms < 2000
                ? $"Relay session {session.SessionKey} ended prematurely ({ms}ms)"
                : $"Relay session {session.SessionKey} ended ({ms}ms)");
            ReportStats();
            ReportSessions();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Directory & rendezvous commands
    // ═══════════════════════════════════════════════════════════════

    private async Task<bool> TryHandleDirectoryCommandAsync(string line, TcpClient client, NetworkStream stream, CancellationToken ct)
    {
        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        var cmd = parts[0].ToUpperInvariant();
        var remoteEp = client.Client.RemoteEndPoint as IPEndPoint;

        var authRateKey = TryBuildAuthRateKey(cmd, parts);
        if (!string.IsNullOrWhiteSpace(authRateKey) && !_rateLimiter.ShouldAllowAuthenticated(authRateKey, out var authRetry))
        {
            var retryText = authRetry.HasValue ? $" retry={authRetry.Value.TotalSeconds:F0}s" : string.Empty;
            RecordProbeAudit(remoteEp, "Zer0Talk Client", cmd, "Failure", $"Authenticated rate limit blocked{retryText}");
            await WriteLineAsync(stream, "ERR rate-limit", ct);
            SafeClose(client);
            return true;
        }

        try
        {
            switch (cmd)
            {
                case "REG":
                case "LOOKUP":
                case "OFFER":
                case "POLL":
                case "WAITPOLL":
                case "ACK":
                case "UNREG":
                    RecordProbeAudit(remoteEp, "Zer0Talk Client", cmd, "Accepted", "Recognized directory/rendezvous command");
                    break;
                default:         return false;
            }

            return cmd switch
            {
                "REG" => await HandleRegAsync(parts, client, stream, ct),
                "LOOKUP" => await HandleLookupAsync(parts, client, stream, ct),
                "OFFER" => await HandleOfferAsync(parts, client, stream, ct),
                "POLL" => await HandlePollAsync(parts, client, stream, ct),
                "WAITPOLL" => await HandleWaitPollAsync(parts, client, stream, ct),
                "ACK" => await HandleAckAsync(parts, client, stream, ct),
                "UNREG" => await HandleUnregAsync(parts, client, stream, ct),
                _ => false
            };
        }
        catch
        {
            RecordProbeAudit(remoteEp, "Zer0Talk Client", cmd, "Failure", "Command handling exception");
            SafeClose(client);
            return true;
        }
    }

    // ── REG <uid> <port> [publicKey] ──
    private async Task<bool> HandleRegAsync(string[] parts, TcpClient client, NetworkStream stream, CancellationToken ct)
    {
        if (parts.Length < 3)
        {
            await WriteLineAsync(stream, "ERR bad-reg", ct);
            SafeClose(client);
            return true;
        }

        var uid = NormalizeUid(parts[1]);
        if (string.IsNullOrWhiteSpace(uid) || !int.TryParse(parts[2], out var port) || port < 1 || port > 65535)
        {
            await WriteLineAsync(stream, "ERR bad-reg", ct);
            SafeClose(client);
            return true;
        }

        if (IsUidBlocked(uid))
        {
            await WriteLineAsync(stream, "ERR blocked", ct);
            SafeClose(client);
            return true;
        }

        var host = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(host))
        {
            await WriteLineAsync(stream, "ERR bad-endpoint", ct);
            SafeClose(client);
            return true;
        }

        if (IsIpBlocked(host))
        {
            await WriteLineAsync(stream, "ERR blocked", ct);
            SafeClose(client);
            return true;
        }

        var publicKey = parts.Length >= 4 ? NormalizePublicKey(parts[3]) : string.Empty;

        // Reuse existing token if same host+port (keeps client's stored token valid)
        var authToken = GenerateAuthToken();
        if (_directory.TryGetValue(uid, out var existing) &&
            string.Equals(existing.Host, host, StringComparison.OrdinalIgnoreCase) &&
            existing.Port == port)
        {
            authToken = existing.AuthToken;
            if (string.IsNullOrWhiteSpace(publicKey))
            {
                publicKey = existing.PublicKey;
            }
        }

        _directory[uid] = new DirectoryEntry(host, port, DateTime.UtcNow, authToken, publicKey);
        PruneDirectory();
        try { _federation?.BroadcastDirectoryUpdate(uid, host, port); } catch { }
        ReportClients();

        await WriteLineAsync(stream, $"OK {authToken}", ct);
        SafeClose(client);
        return true;
    }

    // ── LOOKUP <uid> ──
    private async Task<bool> HandleLookupAsync(string[] parts, TcpClient client, NetworkStream stream, CancellationToken ct)
    {
        if (parts.Length < 2)
        {
            await WriteLineAsync(stream, "ERR bad-lookup", ct);
            SafeClose(client);
            return true;
        }

        var uid = NormalizeUid(parts[1]);
        PruneDirectory();

        if (_directory.TryGetValue(uid, out var entry))
        {
            var age = Math.Max(0, (int)(DateTime.UtcNow - entry.LastSeenUtc).TotalSeconds);
            await WriteLineAsync(stream, $"PEER {uid} {entry.Host} {entry.Port} {age}", ct);
        }
        else
        {
            // Try federated lookup
            string? fedHost = null;
            int? fedPort = null;
            try
            {
                if (_federation != null)
                {
                    var result = await _federation.LookupUserFederatedAsync(uid, ct);
                    fedHost = result.host;
                    fedPort = result.port;
                }
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(fedHost) && fedPort is > 0 and <= 65535)
                await WriteLineAsync(stream, $"PEER {uid} {fedHost} {fedPort.Value} 0", ct);
            else
                await WriteLineAsync(stream, "MISS", ct);
        }

        SafeClose(client);
        return true;
    }

    // ── OFFER <targetUid> <sourceUid> <sessionKey> [token] ──
    private async Task<bool> HandleOfferAsync(string[] parts, TcpClient client, NetworkStream stream, CancellationToken ct)
    {
        if (parts.Length < 4)
        {
            await WriteLineAsync(stream, "ERR bad-offer", ct);
            SafeClose(client);
            return true;
        }

        var targetUid = NormalizeUid(parts[1]);
        var sourceUid = NormalizeUid(parts[2]);
        var sessionKey = parts[3].Trim();
        var sourceToken = parts.Length >= 5 ? parts[4].Trim() : string.Empty;

        if (string.IsNullOrWhiteSpace(targetUid) || string.IsNullOrWhiteSpace(sourceUid) || string.IsNullOrWhiteSpace(sessionKey))
        {
            await WriteLineAsync(stream, "ERR bad-offer", ct);
            SafeClose(client);
            return true;
        }

        if (IsUidBlocked(sourceUid) || IsUidBlocked(targetUid))
        {
            await WriteLineAsync(stream, "ERR blocked", ct);
            SafeClose(client);
            return true;
        }

        Interlocked.Increment(ref _offerCommands);

        // Auth check: source must be registered with a valid token
        if (!IsUidAuthorized(sourceUid, sourceToken))
        {
            // Attempt implicit auth via IP match (same client, expired token)
            var clientIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? string.Empty;
            if (!TryImplicitReRegister(sourceUid, clientIp))
            {
                Log?.Invoke("OFFER rejected: unauthorized source (not registered or token mismatch)");
                await WriteLineAsync(stream, "ERR unauthorized", ct);
                SafeClose(client);
                return true;
            }
        }

        // Deduplicate: don't store duplicate offers for same source+session
        var inviteId = StoreInvite(targetUid, sourceUid, sessionKey);

        var sourceHandle = BuildModerationHandle(sourceUid);
        var targetHandle = BuildModerationHandle(targetUid);
        var sessionFingerprint = BuildFingerprint(sessionKey, 8);
        Log?.Invoke($"OFFER stored: invite={inviteId[..Math.Min(8, inviteId.Length)]}... src={sourceHandle} dst={targetHandle} session={sessionFingerprint}");
        await WriteLineAsync(stream, $"OK {inviteId}", ct);
        SafeClose(client);
        return true;
    }

    // ── POLL <uid> [token] ──
    private async Task<bool> HandlePollAsync(string[] parts, TcpClient client, NetworkStream stream, CancellationToken ct)
    {
        if (parts.Length < 2)
        {
            await WriteLineAsync(stream, "ERR bad-poll", ct);
            SafeClose(client);
            return true;
        }

        var uid = NormalizeUid(parts[1]);
        var uidToken = parts.Length >= 3 ? parts[2].Trim() : string.Empty;

        if (string.IsNullOrWhiteSpace(uid))
        {
            await WriteLineAsync(stream, "ERR bad-poll", ct);
            SafeClose(client);
            return true;
        }

        if (IsUidBlocked(uid))
        {
            await WriteLineAsync(stream, "ERR blocked", ct);
            SafeClose(client);
            return true;
        }

        Interlocked.Increment(ref _pollCommands);

        if (!IsUidAuthorized(uid, uidToken))
        {
            var clientIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? string.Empty;
            if (!TryImplicitReRegister(uid, clientIp))
            {
                Log?.Invoke($"POLL rejected: uid={uid} unauthorized (re-register required)");
                await WriteLineAsync(stream, "ERR unauthorized", ct);
                SafeClose(client);
                return true;
            }
            else
            {
                Log?.Invoke($"POLL: uid={uid} implicitly re-authorized via IP match");
            }
        }

        PruneInvites();
        var invites = GetPendingInvites(uid, 8);
        await WriteLineAsync(stream, BuildInviteResponse(invites), ct);

        SafeClose(client);
        return true;
    }

    // ── WAITPOLL <uid> <waitMs> [token] ──
    private async Task<bool> HandleWaitPollAsync(string[] parts, TcpClient client, NetworkStream stream, CancellationToken ct)
    {
        if (parts.Length < 3)
        {
            await WriteLineAsync(stream, "ERR bad-waitpoll", ct);
            SafeClose(client);
            return true;
        }

        var uid = NormalizeUid(parts[1]);
        if (!int.TryParse(parts[2], out var waitMs)) waitMs = 10000;
        var uidToken = parts.Length >= 4 ? parts[3].Trim() : string.Empty;
        waitMs = Math.Clamp(waitMs, 500, 15000);

        if (string.IsNullOrWhiteSpace(uid))
        {
            await WriteLineAsync(stream, "ERR bad-waitpoll", ct);
            SafeClose(client);
            return true;
        }

        if (IsUidBlocked(uid))
        {
            await WriteLineAsync(stream, "ERR blocked", ct);
            SafeClose(client);
            return true;
        }

        Interlocked.Increment(ref _waitPollCommands);

        // If token auth fails, try IP-based implicit re-authorization.
        if (!IsUidAuthorized(uid, uidToken))
        {
            var clientIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? string.Empty;
            if (!TryImplicitReRegister(uid, clientIp))
            {
                Log?.Invoke($"WAITPOLL rejected: uid={uid} unauthorized (re-register required)");
                await WriteLineAsync(stream, "ERR unauthorized", ct);
                SafeClose(client);
                return true;
            }
            else
            {
                Log?.Invoke($"WAITPOLL: uid={uid} implicitly re-authorized via IP match");
            }
        }

        // Check for existing invites immediately
        PruneInvites();
        var invites = GetPendingInvites(uid, 8);

        if (invites.Count == 0)
        {
            // Long-poll: wait for an invite to arrive
            var signal = _inviteSignals.GetOrAdd(uid, _ => new SemaphoreSlim(0, int.MaxValue));
            try { await signal.WaitAsync(waitMs, ct); } catch { }
            PruneInvites();
            invites = GetPendingInvites(uid, 8);
        }

        await WriteLineAsync(stream, BuildInviteResponse(invites), ct);

        SafeClose(client);
        return true;
    }

    // ── ACK <uid> <inviteId> [token] ──
    private async Task<bool> HandleAckAsync(string[] parts, TcpClient client, NetworkStream stream, CancellationToken ct)
    {
        if (parts.Length < 3)
        {
            await WriteLineAsync(stream, "ERR bad-ack", ct);
            SafeClose(client);
            return true;
        }

        var uid = NormalizeUid(parts[1]);
        var inviteId = parts[2].Trim();
        var uidToken = parts.Length >= 4 ? parts[3].Trim() : string.Empty;

        if (IsUidBlocked(uid))
        {
            await WriteLineAsync(stream, "ERR blocked", ct);
            SafeClose(client);
            return true;
        }

        Interlocked.Increment(ref _ackCommands);

        if (!IsUidAuthorized(uid, uidToken))
        {
            // ACK is best-effort — allow if IP matches or the invite exists
            var clientIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? string.Empty;
            if (!TryImplicitReRegister(uid, clientIp) && !HasInviteForUid(uid, inviteId))
            {
                await WriteLineAsync(stream, "ERR unauthorized", ct);
                SafeClose(client);
                return true;
            }
        }

        var acked = AckInvite(uid, inviteId);
        if (!acked) Interlocked.Increment(ref _ackMisses);
        await WriteLineAsync(stream, acked ? "OK" : "MISS", ct);
        SafeClose(client);
        return true;
    }

    // ── UNREG <uid> <token> ──
    private async Task<bool> HandleUnregAsync(string[] parts, TcpClient client, NetworkStream stream, CancellationToken ct)
    {
        if (parts.Length < 3)
        {
            await WriteLineAsync(stream, "ERR bad-unreg", ct);
            SafeClose(client);
            return true;
        }

        var uid = NormalizeUid(parts[1]);
        var token = parts[2].Trim();

        if (string.IsNullOrWhiteSpace(uid))
        {
            await WriteLineAsync(stream, "ERR bad-unreg", ct);
            SafeClose(client);
            return true;
        }

        if (IsUidBlocked(uid))
        {
            await WriteLineAsync(stream, "ERR blocked", ct);
            SafeClose(client);
            return true;
        }

        // Auth: require valid token OR matching IP (same as implicit re-auth)
        if (!IsUidAuthorized(uid, token))
        {
            var clientIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? string.Empty;
            if (!_directory.TryGetValue(uid, out var entry) ||
                !string.Equals(entry.Host, clientIp, StringComparison.OrdinalIgnoreCase))
            {
                Log?.Invoke($"UNREG rejected: uid={uid} unauthorized");
                await WriteLineAsync(stream, "ERR unauthorized", ct);
                SafeClose(client);
                return true;
            }
        }

        var removed = _directory.TryRemove(uid, out _);
        if (removed)
        {
            try
            {
                if (_federation != null)
                {
                    using var notifyCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await _federation.BroadcastUserDisconnectAsync(uid, notifyCts.Token);
                }
            }
            catch { }
        }

        // Also purge any pending invites targeting this UID
        lock (_inviteGate)
        {
            _invitesByTarget.Remove(uid);
        }

        Log?.Invoke($"UNREG: uid={uid} removed={removed}");
        ReportClients();
        await WriteLineAsync(stream, removed ? "OK" : "MISS", ct);
        SafeClose(client);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Directory auth
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Check if a UID+token pair is authorized.
    /// Returns true if directory entry exists, isn't expired, and token matches.
    /// </summary>
    private bool IsUidAuthorized(string uid, string? token)
    {
        if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(token)) return false;
        if (!_directory.TryGetValue(uid, out var entry)) return false;
        if ((DateTime.UtcNow - entry.LastSeenUtc) > DirectoryEntryTtl) return false;
        return string.Equals(entry.AuthToken, token.Trim(), StringComparison.Ordinal);
    }

    /// <summary>
    /// If a command fails token auth, try to implicitly re-authorize the UID
    /// by checking if the requesting IP matches the registered IP.
    /// This handles the case where the client's cached token expired but it's
    /// clearly the same machine. Refreshes the entry timestamp on match.
    /// </summary>
    private bool TryImplicitReRegister(string uid, string clientIp)
    {
        if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(clientIp)) return false;

        if (_directory.TryGetValue(uid, out var entry))
        {
            if (string.Equals(entry.Host, clientIp, StringComparison.OrdinalIgnoreCase))
            {
                // Same IP = same client, refresh timestamp to prevent re-expiry
                _directory[uid] = new DirectoryEntry(entry.Host, entry.Port, DateTime.UtcNow, entry.AuthToken, entry.PublicKey);
                return true;
            }
        }

        return false;
    }

    private static string GenerateAuthToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(18);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private void PruneDirectory()
    {
        var removedAny = false;
        var removedUids = new List<string>();
        var cutoff = DateTime.UtcNow - DirectoryEntryTtl;
        foreach (var kv in _directory)
        {
            if (kv.Value.LastSeenUtc < cutoff && _directory.TryRemove(kv.Key, out _))
            {
                removedAny = true;
                removedUids.Add(kv.Key);
            }
        }

        if (removedUids.Count > 0)
        {
            foreach (var uid in removedUids)
            {
                try { _federation?.BroadcastUserDisconnect(uid); } catch { }
            }
        }

        if (removedAny)
        {
            ReportClients();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Invite storage
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Store an invite, deduplicating by source+session combination.
    /// Returns the invite ID (existing if deduplicated, new otherwise).
    /// </summary>
    private string StoreInvite(string targetUid, string sourceUid, string sessionKey)
    {
        var inviteId = Guid.NewGuid().ToString("N");

        lock (_inviteGate)
        {
            if (!_invitesByTarget.TryGetValue(targetUid, out var list))
            {
                list = new List<RendezvousInvite>();
                _invitesByTarget[targetUid] = list;
            }

            // Deduplicate: if same source+session already pending, refresh it
            for (var i = 0; i < list.Count; i++)
            {
                var existing = list[i];
                if (!existing.Acked &&
                    string.Equals(existing.SourceUid, sourceUid, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.SessionKey, sessionKey, StringComparison.OrdinalIgnoreCase))
                {
                    existing.CreatedUtc = DateTime.UtcNow;
                    existing.DeliveredUtc = null; // Allow immediate redelivery
                    list[i] = existing;
                    inviteId = existing.InviteId;

                    // Signal waiting long-polls
                    try { _inviteSignals.GetOrAdd(targetUid, _ => new SemaphoreSlim(0, int.MaxValue)).Release(); } catch { }
                    return inviteId;
                }
            }

            // New invite
            list.Add(new RendezvousInvite(inviteId, sourceUid, sessionKey, DateTime.UtcNow));
        }

        // Signal waiting long-polls
        try { _inviteSignals.GetOrAdd(targetUid, _ => new SemaphoreSlim(0, int.MaxValue)).Release(); } catch { }

        PruneInvites();
        return inviteId;
    }

    private List<RendezvousInvite> GetPendingInvites(string targetUid, int maxCount)
    {
        var results = new List<RendezvousInvite>();
        if (maxCount <= 0) return results;

        lock (_inviteGate)
        {
            if (!_invitesByTarget.TryGetValue(targetUid, out var list) || list.Count == 0) return results;

            var now = DateTime.UtcNow;
            for (var i = list.Count - 1; i >= 0; i--)
            {
                var invite = list[i];
                if (invite.Acked) continue;
                if ((now - invite.CreatedUtc) > InviteTtl) continue;
                if (invite.DeliveredUtc.HasValue && (now - invite.DeliveredUtc.Value) < InviteRedeliveryDelay) continue;

                invite.DeliveredUtc = now;
                invite.Deliveries++;
                list[i] = invite;
                results.Add(invite);
                if (results.Count >= maxCount) break;
            }
        }

        return results;
    }

    private static string BuildInviteResponse(IReadOnlyList<RendezvousInvite> invites)
    {
        if (invites == null || invites.Count == 0) return "NONE";
        if (invites.Count == 1)
        {
            var invite = invites[0];
            return $"INVITE {invite.InviteId} {invite.SourceUid} {invite.SessionKey}";
        }

        var payload = string.Join('|', invites.Select(i => $"{i.InviteId}:{i.SourceUid}:{i.SessionKey}"));
        return $"INVITES {payload}";
    }

    private bool AckInvite(string targetUid, string inviteId)
    {
        if (string.IsNullOrWhiteSpace(targetUid) || string.IsNullOrWhiteSpace(inviteId)) return false;
        lock (_inviteGate)
        {
            if (!_invitesByTarget.TryGetValue(targetUid, out var list)) return false;
            for (var i = 0; i < list.Count; i++)
            {
                if (!string.Equals(list[i].InviteId, inviteId, StringComparison.Ordinal)) continue;
                var invite = list[i];
                invite.Acked = true;
                list[i] = invite;
                return true;
            }
        }
        return false;
    }

    private bool HasInviteForUid(string targetUid, string inviteId)
    {
        lock (_inviteGate)
        {
            if (!_invitesByTarget.TryGetValue(targetUid, out var list)) return false;
            return list.Any(x => string.Equals(x.InviteId, inviteId, StringComparison.Ordinal));
        }
    }

    private void PruneInvites()
    {
        var cutoff = DateTime.UtcNow - InviteTtl;
        lock (_inviteGate)
        {
            foreach (var key in _invitesByTarget.Keys.ToList())
            {
                var list = _invitesByTarget[key];
                list.RemoveAll(x => x.Acked || x.CreatedUtc < cutoff);
                if (list.Count == 0) _invitesByTarget.Remove(key);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Federation commands
    // ═══════════════════════════════════════════════════════════════

    private async Task<bool> TryHandleFederationCommandAsync(string line, TcpClient client, NetworkStream stream, CancellationToken ct)
    {
        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        var cmd = parts[0];
        if (!cmd.StartsWith("RELAY-", StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            if (!_config.EnableFederation)
            {
                RecordProbeAudit(client.Client.RemoteEndPoint as IPEndPoint, "Zer0Talk Relay", cmd, "Failure", "Federation disabled");
                await WriteLineAsync(stream, "ERR federation-disabled", ct);
                SafeClose(client);
                return true;
            }

            if (!IsFederationClientAllowed(client))
            {
                RecordProbeAudit(client.Client.RemoteEndPoint as IPEndPoint, "Zer0Talk Relay", cmd, "Failure", "Federation peer not allow-listed");
                await WriteLineAsync(stream, "ERR unauthorized", ct);
                SafeClose(client);
                return true;
            }

            if (string.Equals(cmd, "RELAY-HELLO", StringComparison.OrdinalIgnoreCase))
            {
                var secret = parts.Length >= 2 ? parts[^1].Trim() : string.Empty;
                if (!IsFederationSecretOk(secret))
                {
                    RecordProbeAudit(client.Client.RemoteEndPoint as IPEndPoint, "Zer0Talk Relay", cmd, "Failure", "Invalid federation shared secret");
                    await WriteLineAsync(stream, "ERR unauthorized", ct);
                    SafeClose(client);
                    return true;
                }
                RecordProbeAudit(client.Client.RemoteEndPoint as IPEndPoint, "Zer0Talk Relay", cmd, "Accepted", "Federation hello accepted");
                await WriteLineAsync(stream, $"OK-HELLO {_config.RelayAddressToken} {_config.MaxSessions} {_sessions.ActiveCount}", ct);
                SafeClose(client);
                return true;
            }

            if (string.Equals(cmd, "RELAY-LOOKUP", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    await WriteLineAsync(stream, "ERR bad-relay-lookup", ct);
                    SafeClose(client);
                    return true;
                }
                var secret = parts.Length >= 2 ? parts[^1].Trim() : string.Empty;
                if (!IsFederationSecretOk(secret))
                {
                    RecordProbeAudit(client.Client.RemoteEndPoint as IPEndPoint, "Zer0Talk Relay", cmd, "Failure", "Invalid federation shared secret");
                    await WriteLineAsync(stream, "ERR unauthorized", ct);
                    SafeClose(client);
                    return true;
                }
                RecordProbeAudit(client.Client.RemoteEndPoint as IPEndPoint, "Zer0Talk Relay", cmd, "Accepted", "Federation lookup accepted");
                var uid = NormalizeUid(parts[1]);
                PruneDirectory();
                if (_directory.TryGetValue(uid, out var entry))
                {
                    var age = Math.Max(0, (int)(DateTime.UtcNow - entry.LastSeenUtc).TotalSeconds);
                    await WriteLineAsync(stream, $"PEER {uid} {entry.Host} {entry.Port} {age}", ct);
                }
                else
                {
                    await WriteLineAsync(stream, "MISS", ct);
                }
                SafeClose(client);
                return true;
            }

            if (string.Equals(cmd, "RELAY-HEALTH", StringComparison.OrdinalIgnoreCase))
            {
                var secret = parts.Length >= 2 ? parts[^1].Trim() : string.Empty;
                if (!IsFederationSecretOk(secret))
                {
                    RecordProbeAudit(client.Client.RemoteEndPoint as IPEndPoint, "Zer0Talk Relay", cmd, "Failure", "Invalid federation shared secret");
                    await WriteLineAsync(stream, "ERR unauthorized", ct);
                    SafeClose(client);
                    return true;
                }
                RecordProbeAudit(client.Client.RemoteEndPoint as IPEndPoint, "Zer0Talk Relay", cmd, "Accepted", "Federation health accepted");
                var active = _sessions.ActiveCount;
                var pending = _sessions.PendingCount;
                var max = Math.Max(1, _config.MaxSessions);
                var load = Math.Clamp((int)Math.Round((active * 100.0) / max), 0, 100);
                await WriteLineAsync(stream, $"HEALTH {load} {pending} {active} {max}", ct);
                SafeClose(client);
                return true;
            }

            if (string.Equals(cmd, "RELAY-DIR-DUMP", StringComparison.OrdinalIgnoreCase))
            {
                var secret = parts.Length >= 2 ? parts[^1].Trim() : string.Empty;
                if (!IsFederationSecretOk(secret))
                {
                    RecordProbeAudit(client.Client.RemoteEndPoint as IPEndPoint, "Zer0Talk Relay", cmd, "Failure", "Invalid federation shared secret");
                    await WriteLineAsync(stream, "ERR unauthorized", ct);
                    SafeClose(client);
                    return true;
                }

                RecordProbeAudit(client.Client.RemoteEndPoint as IPEndPoint, "Zer0Talk Relay", cmd, "Accepted", "Federation directory dump accepted");

                PruneDirectory();
                var entries = _directory
                    .Select(kv => $"{kv.Key},{kv.Value.Host},{kv.Value.Port}")
                    .ToList();
                await WriteLineAsync(stream, entries.Count == 0 ? "DIRDUMP" : $"DIRDUMP {string.Join(';', entries)}", ct);
                SafeClose(client);
                return true;
            }

            if (string.Equals(cmd, "RELAY-DISCONNECT", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    await WriteLineAsync(stream, "ERR bad-relay-disconnect", ct);
                    SafeClose(client);
                    return true;
                }

                var secret = parts.Length >= 2 ? parts[^1].Trim() : string.Empty;
                if (!IsFederationSecretOk(secret))
                {
                    RecordProbeAudit(client.Client.RemoteEndPoint as IPEndPoint, "Zer0Talk Relay", cmd, "Failure", "Invalid federation shared secret");
                    await WriteLineAsync(stream, "ERR unauthorized", ct);
                    SafeClose(client);
                    return true;
                }

                RecordProbeAudit(client.Client.RemoteEndPoint as IPEndPoint, "Zer0Talk Relay", cmd, "Accepted", "Federation disconnect accepted");

                var uid = NormalizeUid(parts[1]);
                var removed = _directory.TryRemove(uid, out _);
                try { _federation?.RemoveCachedUser(uid); } catch { }
                if (removed)
                {
                    Log?.Invoke($"RELAY-DISCONNECT: uid={uid} removed");
                    ReportClients();
                }

                await WriteLineAsync(stream, removed ? "OK" : "MISS", ct);
                SafeClose(client);
                return true;
            }

            await WriteLineAsync(stream, "ERR unsupported-federation-command", ct);
            RecordProbeAudit(client.Client.RemoteEndPoint as IPEndPoint, "Zer0Talk Relay", cmd, "Failure", "Unsupported federation command");
            SafeClose(client);
            return true;
        }
        catch
        {
            RecordProbeAudit(client.Client.RemoteEndPoint as IPEndPoint, "Zer0Talk Relay", "RELAY-*", "Failure", "Federation command exception");
            SafeClose(client);
            return true;
        }
    }

    private bool IsFederationClientAllowed(TcpClient client)
    {
        try
        {
            if (!_config.EnableFederation) return false;
            var remoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
            if (remoteIp is null) return false;
            if (string.Equals(_config.FederationTrustMode, "OpenNetwork", StringComparison.OrdinalIgnoreCase)) return true;

            foreach (var peer in _config.PeerRelays)
            {
                var host = ParsePeerHost(peer);
                if (string.IsNullOrWhiteSpace(host)) continue;
                try
                {
                    var ips = Dns.GetHostAddresses(host);
                    if (ips.Any(ip => ip.Equals(remoteIp))) return true;
                }
                catch
                {
                    if (string.Equals(host, remoteIp.ToString(), StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
        }
        catch { }
        return false;
    }

    private bool IsFederationSecretOk(string? provided)
    {
        var required = (_config.FederationSharedSecret ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(required)) return true;
        return string.Equals(required, (provided ?? string.Empty).Trim(), StringComparison.Ordinal);
    }

    private static string ParsePeerHost(string peerAddress)
    {
        if (string.IsNullOrWhiteSpace(peerAddress)) return string.Empty;
        var parts = peerAddress.Split(':', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2 ? string.Empty : parts[0].Trim();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Cleanup & discovery
    // ═══════════════════════════════════════════════════════════════

    private async Task CleanupLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _sessions.CleanupExpiredPending();
                PruneDirectory();
                PruneInvites();
                PruneBlockedEntries();
                PruneFirstArrivalStates();
                ReportStats();
            }
            catch { }
            try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch { }
        }
    }

    private void StartRelayDiscovery()
    {
        try
        {
            _discoverySocket = new UdpClient(AddressFamily.InterNetwork);
            _discoverySocket.EnableBroadcast = true;
            try { _discoverySocket.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1); } catch { }
            var token = _cts?.Token ?? CancellationToken.None;
            _discoveryTask = Task.Run(async () =>
            {
                var discoveryPort = _config.DiscoveryPort is >= 1 and <= 65535 ? _config.DiscoveryPort : DefaultDiscoveryPort;
                var payload = Encoding.UTF8.GetBytes($"RLY|{_config.RelayAddressToken}|{_config.Port}");
                while (!token.IsCancellationRequested && IsRunning)
                {
                    try
                    {
                        var socket = _discoverySocket;
                        if (socket == null) break;
                        try { await socket.SendAsync(payload, payload.Length, new IPEndPoint(DiscoveryMulticastGroup, discoveryPort)); } catch { }

                        var sent = false;
                        foreach (var bcast in GetBroadcastAddresses())
                        {
                            try { await socket.SendAsync(payload, payload.Length, new IPEndPoint(bcast, discoveryPort)); sent = true; } catch { }
                        }
                        if (!sent)
                        {
                            try { await socket.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Broadcast, discoveryPort)); } catch { }
                        }
                    }
                    catch { }
                    try { await Task.Delay(TimeSpan.FromSeconds(5), token); } catch { }
                }
            }, token);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Relay discovery start failed: {ex.Message}");
        }
    }

    private static IEnumerable<IPAddress> GetBroadcastAddresses()
    {
        var list = new List<IPAddress>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var mask = ua.IPv4Mask?.GetAddressBytes();
                    if (mask == null || mask.Length != 4) continue;
                    var ip = ua.Address.GetAddressBytes();
                    var bcast = new byte[4];
                    for (var i = 0; i < 4; i++) bcast[i] = (byte)(ip[i] | (mask[i] ^ 255));
                    list.Add(new IPAddress(bcast));
                }
            }
        }
        catch { }
        if (list.Count == 0) list.Add(IPAddress.Broadcast);
        return list;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Reporting
    // ═══════════════════════════════════════════════════════════════

    private void ReportStats()
    {
        StatsChanged?.Invoke(new RelayStats(
            _sessions.PendingCount,
            _sessions.ActiveCount,
            _totalConnections,
            Interlocked.Read(ref _offerCommands),
            Interlocked.Read(ref _pollCommands),
            Interlocked.Read(ref _waitPollCommands),
            Interlocked.Read(ref _ackCommands),
            Interlocked.Read(ref _ackMisses),
            _directory.Count));
    }

    private void ReportSessions() => SessionsChanged?.Invoke(_sessions.GetActiveSessions());

    private void ReportClients()
    {
        ClientsChanged?.Invoke(GetRegisteredClientsSnapshot());
    }

    private RelayClientInfo BuildClientInfo(string uid, DirectoryEntry entry)
    {
        var handle = BuildModerationHandle(uid);
        if (_config.ExposeSensitiveClientData)
        {
            var publicKey = string.IsNullOrWhiteSpace(entry.PublicKey) ? "(not provided)" : entry.PublicKey;
            return new RelayClientInfo(handle, uid, publicKey);
        }

        var maskedUid = $"uid-{BuildFingerprint(uid, 8)}";
        var maskedPublicKey = string.IsNullOrWhiteSpace(entry.PublicKey)
            ? "(not provided)"
            : $"pk-{BuildFingerprint(entry.PublicKey, 10)}";
        return new RelayClientInfo(handle, maskedUid, maskedPublicKey);
    }

    private string BuildModerationHandle(string uid)
    {
        var tokenSeed = string.IsNullOrWhiteSpace(_config.RelayAddressToken) ? "relay" : _config.RelayAddressToken;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(tokenSeed));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(uid ?? string.Empty));
        var hex = Convert.ToHexString(hash);
        var len = Math.Min(HandleFingerprintLength, hex.Length);
        return $"h-{hex[..len].ToLowerInvariant()}";
    }

    private static string BuildFingerprint(string value, int chars)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        var len = Math.Clamp(chars, 4, hex.Length);
        return hex[..len];
    }

    private bool IsUidBlocked(string uid)
    {
        PruneBlockedEntries();
        if (string.IsNullOrWhiteSpace(uid)) return false;
        return _blockedUids.TryGetValue(uid, out var untilUtc) && untilUtc > DateTime.UtcNow;
    }

    private bool IsIpBlocked(string ip)
    {
        PruneBlockedEntries();
        if (string.IsNullOrWhiteSpace(ip)) return false;
        return _blockedIps.TryGetValue(ip, out var untilUtc) && untilUtc > DateTime.UtcNow;
    }

    private void PruneBlockedEntries()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in _blockedUids)
        {
            if (entry.Value <= now)
            {
                _blockedUids.TryRemove(entry.Key, out _);
            }
        }

        foreach (var entry in _blockedIps)
        {
            if (entry.Value <= now)
            {
                _blockedIps.TryRemove(entry.Key, out _);
            }
        }
    }

    private QueueFirstArrivalState TrackFirstArrival(RelayRequest request)
    {
        var now = DateTime.UtcNow;
        var handle = string.IsNullOrWhiteSpace(request.Uid)
            ? "h-anon"
            : BuildModerationHandle(request.Uid);
        var role = string.IsNullOrWhiteSpace(request.Role) ? "?" : request.Role.Trim().ToUpperInvariant();
        var key = request.SessionKey;

        if (string.IsNullOrWhiteSpace(key))
        {
            return new QueueFirstArrivalState(handle, role, 1, TimeSpan.Zero, false);
        }

        var state = _firstArrivalBySession.AddOrUpdate(
            key,
            _ => new FirstArrivalState(handle, role, 1, now, now),
            (_, existing) =>
            {
                var sameSide = string.Equals(existing.Handle, handle, StringComparison.Ordinal) &&
                               string.Equals(existing.Role, role, StringComparison.Ordinal);
                var inWindow = (now - existing.LastSeenUtc) <= FirstArrivalStreakWindow;
                var streak = sameSide && inWindow ? existing.Streak + 1 : 1;
                var firstSeen = streak == 1 ? now : existing.FirstSeenUtc;
                return new FirstArrivalState(handle, role, streak, now, firstSeen);
            });

        var window = now - state.FirstSeenUtc;
        var shouldWarn = state.Streak >= 3 && state.Streak % 3 == 0;
        return new QueueFirstArrivalState(state.Handle, state.Role, state.Streak, window, shouldWarn);
    }

    private void PruneFirstArrivalStates()
    {
        var cutoff = DateTime.UtcNow - FirstArrivalStateTtl;
        foreach (var entry in _firstArrivalBySession)
        {
            if (entry.Value.LastSeenUtc < cutoff)
            {
                _firstArrivalBySession.TryRemove(entry.Key, out _);
            }
        }
    }

    private void PurgeInvitesForUid(string uid)
    {
        if (string.IsNullOrWhiteSpace(uid)) return;

        lock (_inviteGate)
        {
            _invitesByTarget.Remove(uid);

            var keys = _invitesByTarget.Keys.ToList();
            foreach (var key in keys)
            {
                var invites = _invitesByTarget[key];
                invites.RemoveAll(x => string.Equals(x.SourceUid, uid, StringComparison.OrdinalIgnoreCase));
                if (invites.Count == 0)
                {
                    _invitesByTarget.Remove(key);
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Utilities
    // ═══════════════════════════════════════════════════════════════

    private static void ConfigureSocket(TcpClient client)
    {
        client.NoDelay = true;
        try { client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true); } catch { }
    }

    private bool AllowClient(TcpClient client)
    {
        var endpoint = client.Client.RemoteEndPoint as IPEndPoint;
        if (endpoint?.Address is { } address && IPAddress.IsLoopback(address))
        {
            RecordProbeAudit(endpoint, "Unknown", "(pre-read)", "Attempt", "Inbound connection accepted (loopback)");
            return true;
        }

        var ip = endpoint?.Address.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ip)) return true;
        if (IsIpBlocked(ip))
        {
            RecordProbeAudit(endpoint, "Unknown", "(pre-read)", "Failure", "Operator block list denied connection");
            Log?.Invoke($"Relay operator-block denied {ip}");
            return false;
        }
        if (_rateLimiter.ShouldAllow(ip, out var retryAfter))
        {
            RecordProbeAudit(endpoint, "Unknown", "(pre-read)", "Attempt", "Inbound connection accepted");
            return true;
        }
        var text = retryAfter.HasValue ? $" retry={retryAfter.Value.TotalSeconds:F0}s" : string.Empty;
        RecordProbeAudit(endpoint, "Unknown", "(pre-read)", "Failure", $"Rate limit blocked{text}");
        Log?.Invoke($"Relay rate limit blocked {ip}{text}");
        return false;
    }

    private void RecordProbeAudit(IPEndPoint? endpoint, string sourceType, string command, string outcome, string reason)
    {
        var ip = endpoint?.Address.ToString() ?? "?";
        var port = endpoint?.Port ?? 0;
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] source={sourceType} remote={ip}:{port} cmd={command} outcome={outcome} reason={reason}";

        _probeAuditLogs.Enqueue(line);
        while (_probeAuditLogs.Count > ProbeAuditLimit && _probeAuditLogs.TryDequeue(out _)) { }

        try { ProbeAuditLogged?.Invoke(line); } catch { }
    }

    private static string GetCommandToken(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return "(none)";
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return "(none)";
        var idx = trimmed.IndexOf(' ');
        var token = idx > 0 ? trimmed[..idx] : trimmed;
        return token.Length <= 32 ? token.ToUpperInvariant() : token[..32].ToUpperInvariant();
    }

    private static string ClassifySourceType(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return "Unknown";
        if (command.StartsWith("RELAY-", StringComparison.OrdinalIgnoreCase)) return "Zer0Talk Relay";

        return command.ToUpperInvariant() switch
        {
            "RELAY" => "Zer0Talk Client",
            "REG" => "Zer0Talk Client",
            "LOOKUP" => "Zer0Talk Client",
            "OFFER" => "Zer0Talk Client",
            "POLL" => "Zer0Talk Client",
            "WAITPOLL" => "Zer0Talk Client",
            "ACK" => "Zer0Talk Client",
            "UNREG" => "Zer0Talk Client",
            _ => "Unknown"
        };
    }

    private static void SafeClose(TcpClient? client)
    {
        try { client?.Close(); } catch { }
    }

    /// <summary>
    /// Detect non-protocol traffic: TLS/HTTPS handshakes (binary), scanner probes, HTTP requests.
    /// Port 443 attracts a lot of automated scanning traffic that sends TLS ClientHello or HTTP requests.
    /// </summary>
    private static bool LooksLikeBinaryOrProbe(string line)
    {
        if (string.IsNullOrEmpty(line)) return false;

        // TLS ClientHello starts with byte 0x16 (22) — shows up as non-ASCII char
        if (line.Length > 0 && line[0] == '\x16') return true;

        // High ratio of non-printable/non-ASCII characters = binary data
        int nonAscii = 0;
        int len = Math.Min(line.Length, 60);
        for (int i = 0; i < len; i++)
        {
            char c = line[i];
            if (c < 0x20 || c > 0x7E) nonAscii++;
        }
        if (len > 0 && nonAscii * 100 / len > 30) return true; // >30% non-printable = binary

        // HTTP requests from browsers/bots hitting port 443
        if (line.StartsWith("GET ", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("POST ", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("HEAD ", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("OPTIONS ", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("CONNECT ", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static async Task WriteLineAsync(NetworkStream stream, string line, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), ct);
        await stream.FlushAsync(ct);
    }

    private static string NormalizeUid(string uid)
    {
        var s = (uid ?? string.Empty).Trim();
        return s.StartsWith("usr-", StringComparison.Ordinal) && s.Length > 4 ? s[4..] : s;
    }

    private static string NormalizePublicKey(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var trimmed = raw.Trim();
        return trimmed.Length > 2048 ? trimmed[..2048] : trimmed;
    }

    private static string TryBuildAuthRateKey(string cmd, string[] parts)
    {
        if (parts.Length < 2) return string.Empty;
        return cmd switch
        {
            "REG" => NormalizeUid(parts[1]),
            "LOOKUP" => NormalizeUid(parts[1]),
            "POLL" => NormalizeUid(parts[1]),
            "WAITPOLL" => NormalizeUid(parts[1]),
            "ACK" => NormalizeUid(parts[1]),
            "UNREG" => NormalizeUid(parts[1]),
            "OFFER" when parts.Length >= 3 => NormalizeUid(parts[2]),
            _ => string.Empty
        };
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
                if (builder.Length > 1024) return null; // Guard against oversized lines
            }
        }
        catch { return null; }
        return builder.ToString();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Inner types
    // ═══════════════════════════════════════════════════════════════

    private readonly struct QueueFirstArrivalState
    {
        public QueueFirstArrivalState(string handle, string role, int streak, TimeSpan window, bool shouldWarn)
        {
            Handle = handle;
            Role = role;
            Streak = streak;
            Window = window;
            ShouldWarn = shouldWarn;
        }

        public string Handle { get; }
        public string Role { get; }
        public int Streak { get; }
        public TimeSpan Window { get; }
        public bool ShouldWarn { get; }
    }

    private sealed class FirstArrivalState
    {
        public FirstArrivalState(string handle, string role, int streak, DateTime lastSeenUtc, DateTime firstSeenUtc)
        {
            Handle = handle;
            Role = role;
            Streak = streak;
            LastSeenUtc = lastSeenUtc;
            FirstSeenUtc = firstSeenUtc;
        }

        public string Handle { get; }
        public string Role { get; }
        public int Streak { get; }
        public DateTime LastSeenUtc { get; }
        public DateTime FirstSeenUtc { get; }
    }

    private readonly record struct DirectoryEntry(string Host, int Port, DateTime LastSeenUtc, string AuthToken, string PublicKey);

    private struct RendezvousInvite
    {
        public RendezvousInvite(string inviteId, string sourceUid, string sessionKey, DateTime createdUtc)
        {
            InviteId = inviteId;
            SourceUid = sourceUid;
            SessionKey = sessionKey;
            CreatedUtc = createdUtc;
            DeliveredUtc = null;
            Deliveries = 0;
            Acked = false;
        }

        public string InviteId { get; }
        public string SourceUid { get; }
        public string SessionKey { get; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? DeliveredUtc { get; set; }
        public int Deliveries { get; set; }
        public bool Acked { get; set; }
    }
}
