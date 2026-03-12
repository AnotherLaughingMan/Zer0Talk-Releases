using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Zer0Talk.Models;
using Zer0Talk.Utilities;

namespace Zer0Talk.Services;

/// <summary>
/// Manages the persistent TCP connection to the user's home hosted server.
/// Handles ACCOUNT-REG / ACCOUNT-AUTH challenge-response, DPAPI token storage,
/// and bi-directional command/push channel used by RoomService.
///
/// Protocol:
///   Server → Client : CHALLENGE &lt;hexNonce&gt;
///   Client → Server : ACCOUNT-REG &lt;uid&gt; &lt;pubkeyHex&gt; &lt;sig-of-nonce-hex&gt;
///                  OR ACCOUNT-AUTH &lt;uid&gt; &lt;token&gt;
///   Server → Client : OK [&lt;token&gt;] | ERR &lt;reason&gt;
///   Thereafter: any ROOM-* push lines arrive on the same connection.
/// </summary>
public sealed class ServerAccountService : IDisposable
{
    private readonly SettingsService _settings;
    private readonly IdentityService _identity;

    private TcpClient?       _tcp;
    private NetworkStream?   _stream;
    private CancellationTokenSource? _cts;
    private Task?            _receiveTask;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    // Serializes commands so only one is in-flight at a time; the receive loop
    // routes its response line back to the caller via _pendingCmdTcs.
    private readonly SemaphoreSlim _cmdLock = new(1, 1);
    private volatile TaskCompletionSource<string?>? _pendingCmdTcs;

    private volatile bool _authenticated;
    private string?      _cachedToken;

    /// <summary>
    /// Fired for every server-push line received after authentication
    /// (e.g. ROOM-INVITED, ROOM-ADMIN-ONLINE, ROOM-DELIVER, …).
    /// </summary>
    public event Action<string>? PushReceived;

    /// <summary>Fired when the connection is lost so RoomService can update state.</summary>
    public event Action? ConnectionLost;

    public bool IsAuthenticated => _authenticated;
    public bool IsConnected     => _tcp?.Connected == true && _stream != null;

    public ServerAccountService(SettingsService settings, IdentityService identity)
    {
        _settings = settings;
        _identity = identity;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Connect & authenticate
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Connect to the home server, perform ACCOUNT-REG or ACCOUNT-AUTH,
    /// then start the background receive loop.
    /// Returns true on successful authentication.
    /// </summary>
    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        var serverAddress = _settings.Settings.HomeServer;
        if (string.IsNullOrWhiteSpace(serverAddress)) return false;

        Disconnect();

        try
        {
            var (host, port) = ParseAddress(serverAddress);
            _tcp = new TcpClient { ReceiveTimeout = 60_000, SendTimeout = 10_000 };
            await _tcp.ConnectAsync(host, port, ct);
            _stream = _tcp.GetStream();

            // Read challenge
            var challengeLine = await ReadLineAsync(ct);
            if (challengeLine == null || !challengeLine.StartsWith("CHALLENGE ", StringComparison.Ordinal))
            {
                Logger.Log($"ServerAccountService: expected CHALLENGE, got: {challengeLine}");
                Disconnect();
                return false;
            }

            var nonce = challengeLine[10..].Trim();

            // Try token auth first; fall back to signature-based reg
            var existingToken = LoadToken();
            string? response = null;

            if (!string.IsNullOrWhiteSpace(existingToken))
            {
                await WriteLineAsync($"ACCOUNT-AUTH {_identity.UID} {existingToken}", ct);
                response = await ReadLineAsync(ct);
                if (response?.StartsWith("ERR", StringComparison.Ordinal) == true)
                {
                    Logger.Log($"ServerAccountService: token auth failed ({response}), re-registering");
                    ClearToken();
                    existingToken = null;
                }
            }

            if (string.IsNullOrWhiteSpace(existingToken))
            {
                var pubHex = Convert.ToHexString(_identity.PublicKey).ToLowerInvariant();
                var sig    = _identity.Sign(Encoding.UTF8.GetBytes(nonce));
                var sigHex = Convert.ToHexString(sig).ToLowerInvariant();
                await WriteLineAsync($"ACCOUNT-REG {_identity.UID} {pubHex} {sigHex}", ct);
                response = await ReadLineAsync(ct);
            }

            if (response == null || !response.StartsWith("OK", StringComparison.Ordinal))
            {
                Logger.Log($"ServerAccountService: auth rejected: {response}");
                Disconnect();
                return false;
            }

            // OK [<token>]
            var okParts = response.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (okParts.Length >= 2)
                SaveToken(okParts[1].Trim());

            _authenticated = true;
            Logger.Log($"ServerAccountService: authenticated to {serverAddress}");

            // Start background receive loop
            _cts = new CancellationTokenSource();
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"ServerAccountService: connect failed: {ex.Message}");
            Disconnect();
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Command send helpers (used by RoomService)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Send a command line and read the single-line response.</summary>
    /// <remarks>
    /// Routing: the background ReceiveLoopAsync reads all bytes from the stream.
    /// Command responses are demuxed back to the caller via _pendingCmdTcs so
    /// that SendCommandAsync never reads from _stream directly (which would race
    /// with the receive loop).
    /// </remarks>
    public async Task<string?> SendCommandAsync(string line, CancellationToken ct = default)
    {
        if (!_authenticated || _stream == null) return null;
        await _cmdLock.WaitAsync(ct);
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCmdTcs = tcs;
        try
        {
            await _writeLock.WaitAsync(ct);
            try { await WriteLineAsync(line, ct); }
            catch (Exception ex)
            {
                Logger.Log($"ServerAccountService: send failed: {ex.Message}");
                HandleDisconnect();
                return null;
            }
            finally { _writeLock.Release(); }

            // ReceiveLoopAsync will call tcs.TrySetResult() when the response arrives.
            return await tcs.Task.WaitAsync(ct);
        }
        finally
        {
            _pendingCmdTcs = null;
            _cmdLock.Release();
        }
    }

    /// <summary>Send without waiting for a response (fire-and-forget commands, e.g. PING).</summary>
    public async Task SendFireAndForgetAsync(string line, CancellationToken ct = default)
    {
        if (!_authenticated || _stream == null) return;
        await _writeLock.WaitAsync(ct);
        try { await WriteLineAsync(line, ct); }
        catch { HandleDisconnect(); }
        finally { _writeLock.Release(); }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Background receive loop
    // ═══════════════════════════════════════════════════════════════

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _stream != null)
            {
                var line = await ReadLineAsync(ct);
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line) || line == "PONG") continue;

                // If a command is awaiting a response, route this line to it.
                var pendingTcs = _pendingCmdTcs;
                if (pendingTcs != null && pendingTcs.TrySetResult(line))
                    continue;

                try { PushReceived?.Invoke(line); }
                catch (Exception ex) { Logger.Log($"ServerAccountService: push handler error: {ex.Message}"); }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Log($"ServerAccountService: receive loop error: {ex.Message}");
        }
        finally
        {
            HandleDisconnect();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Token storage (DPAPI on Windows, omit on other platforms)
    // ═══════════════════════════════════════════════════════════════

    private string? LoadToken()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return null;
            var blob = _settings.Settings.HomeServerTokenProtected;
            if (string.IsNullOrWhiteSpace(blob)) return null;
            var encrypted = Convert.FromBase64String(blob);
            [System.Runtime.Versioning.SupportedOSPlatform("windows")]
            static byte[] Unprotect(byte[] d) =>
                ProtectedData.Unprotect(d, null, DataProtectionScope.CurrentUser);
            var plain = Unprotect(encrypted);
            return Encoding.UTF8.GetString(plain);
        }
        catch { return null; }
    }

    private void SaveToken(string token)
    {
        _cachedToken = token;
        try
        {
            if (!OperatingSystem.IsWindows()) return;
            [System.Runtime.Versioning.SupportedOSPlatform("windows")]
            static byte[] Protect(byte[] d) =>
                ProtectedData.Protect(d, null, DataProtectionScope.CurrentUser);
            var plain  = Encoding.UTF8.GetBytes(token);
            var enc    = Protect(plain);
            _settings.Settings.HomeServerTokenProtected = Convert.ToBase64String(enc);
            _settings.Save(AppServices.Passphrase);
        }
        catch (Exception ex) { Logger.Log($"ServerAccountService: token save failed: {ex.Message}"); }
    }

    private void ClearToken()
    {
        _cachedToken = null;
        _settings.Settings.HomeServerTokenProtected = null;
        try { _settings.Save(AppServices.Passphrase); } catch { }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Cleanup
    // ═══════════════════════════════════════════════════════════════

    private void HandleDisconnect()
    {
        _authenticated = false;
        try { ConnectionLost?.Invoke(); } catch { }
        Disconnect();
    }

    public void Disconnect()
    {
        _authenticated = false;
        // Unblock any command waiting for a response so it doesn't hang.
        _pendingCmdTcs?.TrySetResult(null);
        _pendingCmdTcs = null;
        try { _cts?.Cancel(); } catch { }
        _cts = null;
        try { _tcp?.Close(); } catch { }
        _tcp     = null;
        _stream  = null;
    }

    public void Dispose() => Disconnect();

    // ═══════════════════════════════════════════════════════════════
    //  I/O
    // ═══════════════════════════════════════════════════════════════

    private async Task WriteLineAsync(string line, CancellationToken ct)
    {
        var data = Encoding.UTF8.GetBytes(line + "\n");
        await _stream!.WriteAsync(data, ct);
        await _stream!.FlushAsync(ct);
    }

    private async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        var sb  = new StringBuilder();
        var buf = new byte[1];
        while (true)
        {
            if (_stream == null) return null;
            var read = await _stream.ReadAsync(buf, ct);
            if (read == 0) return null;
            var c = (char)buf[0];
            if (c == '\n') break;
            if (c != '\r') sb.Append(c);
            if (sb.Length > 8192) return null;
        }
        return sb.ToString();
    }

    private static (string host, int port) ParseAddress(string addr)
    {
        var idx = addr.LastIndexOf(':');
        if (idx < 0 || !int.TryParse(addr[(idx + 1)..], out var p))
            throw new ArgumentException($"Invalid server address: {addr}");
        return (addr[..idx], p);
    }
}
