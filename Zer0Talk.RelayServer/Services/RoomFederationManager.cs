using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Zer0Talk.RelayServer.Services;

/// <summary>
/// Manages outbound persistent S2S connections to peer hosted servers, and accepts
/// inbound S2S connections from peers, enabling cross-server room event delivery and
/// invite routing without requiring members to connect to a foreign server.
///
/// Protocol (TCP on HostingS2SPort):
///   Acceptor → Connector : S2S-CHALLENGE  &lt;hexNonce&gt;
///   Connector → Acceptor : S2S-HELLO      &lt;selfId&gt; &lt;HMAC-SHA256(secret,nonce)&gt;
///   Acceptor → Connector : S2S-OK | S2S-ERR &lt;reason&gt;
///
///   Either side at any time:
///     S2S-NOTIFY     &lt;targetUid&gt; &lt;message...&gt;     deliver any room event to a local user
///     S2S-ROOM-INVITE &lt;targetUid&gt; &lt;roomId&gt; &lt;inviterUid&gt;   queue an offline invite
///     S2S-PING / S2S-PONG                            keepalive
/// </summary>
public sealed class RoomFederationManager : IDisposable
{
    private readonly RelayConfig _config;
    private readonly string _selfId;

    // Outbound: one persistent connection per peer address ("host:port")
    private readonly ConcurrentDictionary<string, PersistentS2SConnection> _outbound =
        new(StringComparer.OrdinalIgnoreCase);

    private TcpListener? _s2sListener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    public event Action<string>? Log;

    /// <summary>
    /// Fired when a peer requests delivery of a room event to a local (this-server) user.
    /// Subscriber (HostedServerHost) writes the message to the user's active TCP session.
    /// </summary>
    public event Func<string /*targetUid*/, string /*message*/, CancellationToken, Task>? IncomingNotification;

    /// <summary>
    /// Fired when a peer routes a room invite to a local user.
    /// Subscriber queues it for offline delivery and optionally pushes to active session.
    /// </summary>
    public event Func<string /*targetUid*/, string /*roomId*/, string /*inviterUid*/, CancellationToken, Task>? IncomingInvite;

    public bool IsRunning { get; private set; }

    public RoomFederationManager(RelayConfig config)
    {
        _config = config;
        // Use HostingAddress if set; fall back to relay token for S2S identification
        _selfId = string.IsNullOrWhiteSpace(config.HostingAddress)
            ? config.RelayAddressToken
            : config.HostingAddress;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Lifecycle
    // ═══════════════════════════════════════════════════════════════

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        IsRunning = true;

        // Start S2S inbound listener
        _s2sListener = new TcpListener(IPAddress.Any, _config.HostingS2SPort);
        _s2sListener.Start();
        _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        Log?.Invoke($"S2S federation listening on port {_config.HostingS2SPort}");

        // Establish outbound connections to each configured peer
        foreach (var peer in _config.PeerHostedServers)
        {
            var conn = new PersistentS2SConnection(
                peer, _selfId, _config.FederationSharedSecret,
                onIncoming: (line) => _ = Task.Run(() => DispatchAsync(line, _cts.Token)),
                log: msg => Log?.Invoke(msg));
            _outbound[peer] = conn;
            _ = Task.Run(() => conn.StartAsync(_cts.Token));
        }

        Log?.Invoke($"S2S federation started | peers={_config.PeerHostedServers.Count}");
    }

    public void Stop()
    {
        IsRunning = false;
        try { _cts?.Cancel(); } catch { }
        try { _s2sListener?.Stop(); } catch { }
        foreach (var c in _outbound.Values) c.Dispose();
        _outbound.Clear();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Outbound routing helpers (called by HostedServerHost)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Push a room event notification to a member on a peer server.
    /// peerAddress is the member's HomeServer (from RoomMembers table).
    /// </summary>
    public async Task NotifyMemberAsync(string peerAddress, string targetUid, string message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(peerAddress) || string.IsNullOrWhiteSpace(targetUid)) return;
        if (!_outbound.TryGetValue(peerAddress, out var conn)) return;
        await conn.SendAsync($"S2S-NOTIFY {targetUid} {message}", ct);
    }

    /// <summary>
    /// Route a room invite to a user on a peer server.
    /// </summary>
    public async Task InviteMemberAsync(string peerAddress, string targetUid, string roomId, string inviterUid, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(peerAddress) || string.IsNullOrWhiteSpace(targetUid)) return;
        if (!_outbound.TryGetValue(peerAddress, out var conn)) return;
        await conn.SendAsync($"S2S-ROOM-INVITE {targetUid} {roomId} {inviterUid}", ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Inbound S2S acceptor
    // ═══════════════════════════════════════════════════════════════

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && IsRunning)
        {
            TcpClient? client = null;
            try
            {
                client = await _s2sListener!.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleIncomingS2SAsync(client, ct), ct);
            }
            catch
            {
                client?.Close();
                if (!IsRunning) break;
            }
        }
    }

    private async Task HandleIncomingS2SAsync(TcpClient client, CancellationToken ct)
    {
        var stream = client.GetStream();
        try
        {
            client.ReceiveTimeout = 60_000;
            client.SendTimeout = 10_000;

            // Auth: send nonce challenge
            var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            await WriteLineAsync(stream, $"S2S-CHALLENGE {nonce}", ct);

            var hello = await ReadLineAsync(stream, ct);
            if (hello == null) return;

            var helloParts = hello.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (helloParts.Length < 3 || helloParts[0] != "S2S-HELLO")
            {
                await WriteLineAsync(stream, "S2S-ERR bad-hello", ct);
                return;
            }

            var peerId   = helloParts[1];
            var hmacHex  = helloParts[2];

            if (!ValidateHmac(nonce, hmacHex))
            {
                await WriteLineAsync(stream, "S2S-ERR bad-auth", ct);
                Log?.Invoke($"S2S inbound rejected (bad auth) | peer={peerId}");
                return;
            }

            await WriteLineAsync(stream, "S2S-OK", ct);
            Log?.Invoke($"S2S peer authenticated | peer={peerId}");

            // Bidirectional command loop (we receive; peer also receives from our outbound connection)
            while (!ct.IsCancellationRequested)
            {
                var line = await ReadLineAsync(stream, ct);
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line == "S2S-PING") { await WriteLineAsync(stream, "S2S-PONG", ct); continue; }
                await DispatchAsync(line, ct);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Log?.Invoke($"S2S inbound error: {ex.Message}");
        }
        finally
        {
            try { client.Close(); } catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Dispatch incoming S2S commands
    // ═══════════════════════════════════════════════════════════════

    private async Task DispatchAsync(string line, CancellationToken ct)
    {
        // S2S-NOTIFY <targetUid> <message...>
        if (line.StartsWith("S2S-NOTIFY ", StringComparison.Ordinal))
        {
            var rest = line[11..]; // skip "S2S-NOTIFY "
            var sp   = rest.IndexOf(' ');
            if (sp < 1) return;
            var targetUid = rest[..sp];
            var message   = rest[(sp + 1)..];
            if (IncomingNotification != null)
                await IncomingNotification(targetUid, message, ct);
            return;
        }

        // S2S-ROOM-INVITE <targetUid> <roomId> <inviterUid>
        if (line.StartsWith("S2S-ROOM-INVITE ", StringComparison.Ordinal))
        {
            var parts = line.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) return;
            if (IncomingInvite != null)
                await IncomingInvite(parts[1], parts[2], parts[3], ct);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  HMAC authentication
    // ═══════════════════════════════════════════════════════════════

    private bool ValidateHmac(string nonce, string hmacHex)
    {
        // If no shared secret configured, allow any authenticated peer (open S2S)
        if (string.IsNullOrWhiteSpace(_config.FederationSharedSecret))
            return true;

        try
        {
            var key      = Encoding.UTF8.GetBytes(_config.FederationSharedSecret);
            var data     = Encoding.UTF8.GetBytes(nonce);
            var expected = HMACSHA256.HashData(key, data);
            var actual   = Convert.FromHexString(hmacHex);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch { return false; }
    }

    // ═══════════════════════════════════════════════════════════════
    //  I/O
    // ═══════════════════════════════════════════════════════════════

    private static async Task WriteLineAsync(NetworkStream stream, string line, CancellationToken ct)
    {
        var data = Encoding.UTF8.GetBytes(line + "\n");
        await stream.WriteAsync(data, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<string?> ReadLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var sb  = new StringBuilder();
        var buf = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(buf, ct);
            if (read == 0) return null;
            var c = (char)buf[0];
            if (c == '\n') break;
            if (c != '\r') sb.Append(c);
            if (sb.Length > 4096) return null;
        }
        return sb.ToString();
    }

    public void Dispose() => Stop();
}

// ═══════════════════════════════════════════════════════════════════
//  Outbound persistent S2S connection (one per peer server)
// ═══════════════════════════════════════════════════════════════════

internal sealed class PersistentS2SConnection : IDisposable
{
    private readonly string _peerAddress;   // "host:port"
    private readonly string _selfId;
    private readonly string _secret;
    private readonly Action<string> _onIncoming;
    private readonly Action<string>? _log;

    private TcpClient?     _tcp;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private volatile bool _authenticated;

    public PersistentS2SConnection(
        string peerAddress, string selfId, string secret,
        Action<string> onIncoming, Action<string>? log)
    {
        _peerAddress = peerAddress;
        _selfId      = selfId;
        _secret      = secret;
        _onIncoming  = onIncoming;
        _log         = log;
    }

    public async Task StartAsync(CancellationToken externalCt)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var ct    = _cts.Token;
        var delay = TimeSpan.FromSeconds(5);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndRunAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _log?.Invoke($"S2S → {_peerAddress} lost: {ex.Message}; retry in {delay.TotalSeconds}s");
            }

            _authenticated = false;
            try { await Task.Delay(delay, ct); } catch { break; }

            // Back-off: cap at 60 s
            if (delay < TimeSpan.FromSeconds(60))
                delay = delay + delay;
        }
    }

    private async Task ConnectAndRunAsync(CancellationToken ct)
    {
        var idx  = _peerAddress.LastIndexOf(':');
        if (idx < 0 || !int.TryParse(_peerAddress[(idx + 1)..], out var port))
            throw new InvalidOperationException($"Invalid S2S peer address: {_peerAddress}");
        var host = _peerAddress[..idx];

        _tcp = new TcpClient { ReceiveTimeout = 60_000, SendTimeout = 10_000 };
        await _tcp.ConnectAsync(host, port, ct);
        _stream = _tcp.GetStream();

        // Handshake: read challenge, send HELLO with HMAC
        var challengeLine = await ReadLineAsync(_stream, ct);
        if (challengeLine == null || !challengeLine.StartsWith("S2S-CHALLENGE ", StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected S2S-CHALLENGE, got: {challengeLine}");

        var nonce = challengeLine[14..].Trim();
        var hmac  = ComputeHmac(nonce);
        await WriteLineAsync(_stream, $"S2S-HELLO {_selfId} {hmac}", ct);

        var response = await ReadLineAsync(_stream, ct);
        if (response == null || !response.StartsWith("S2S-OK", StringComparison.Ordinal))
            throw new InvalidOperationException($"S2S auth failed: {response}");

        _authenticated = true;
        _log?.Invoke($"S2S → {_peerAddress} connected");

        // Receive loop — handle any push from the other side
        while (!ct.IsCancellationRequested)
        {
            var line = await ReadLineAsync(_stream, ct);
            if (line == null) break;
            if (line == "S2S-PONG") continue;
            if (!string.IsNullOrWhiteSpace(line)) _onIncoming(line);
        }
    }

    public async Task SendAsync(string line, CancellationToken ct)
    {
        if (!_authenticated || _stream == null) return;

        await _writeLock.WaitAsync(ct);
        try
        {
            await WriteLineAsync(_stream, line, ct);
        }
        catch
        {
            _authenticated = false;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private string ComputeHmac(string nonce)
    {
        if (string.IsNullOrWhiteSpace(_secret)) return "no-secret";
        var key  = Encoding.UTF8.GetBytes(_secret);
        var data = Encoding.UTF8.GetBytes(nonce);
        return Convert.ToHexString(HMACSHA256.HashData(key, data)).ToLowerInvariant();
    }

    private static async Task WriteLineAsync(NetworkStream stream, string line, CancellationToken ct)
    {
        var data = Encoding.UTF8.GetBytes(line + "\n");
        await stream.WriteAsync(data, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<string?> ReadLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var sb  = new StringBuilder();
        var buf = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(buf, ct);
            if (read == 0) return null;
            var c = (char)buf[0];
            if (c == '\n') break;
            if (c != '\r') sb.Append(c);
            if (sb.Length > 4096) return null;
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        _authenticated = false;
        try { _cts?.Cancel(); } catch { }
        try { _tcp?.Close(); } catch { }
    }
}
