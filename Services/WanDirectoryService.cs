using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Zer0Talk.Utilities;

namespace Zer0Talk.Services;

public sealed class WanDirectoryService
{
    private readonly SettingsService _settings;
    private readonly IdentityService _identity;
    private readonly NetworkService _network;
    private readonly NatTraversalService _nat;
    private DateTime _lastRegisterUtc = DateTime.MinValue;
    private string _registeredUid = string.Empty;
    private string _authToken = string.Empty;
    private static readonly TimeSpan RegisterInterval = TimeSpan.FromSeconds(45);

    public WanDirectoryService(SettingsService settings, IdentityService identity, NetworkService network, NatTraversalService nat)
    {
        _settings = settings;
        _identity = identity;
        _network = network;
        _nat = nat;
    }

    public sealed record LookupResult(string Host, int Port, string Source);
    public sealed record RelayInvite(string InviteId, string SourceUid, string SessionKey, string Source);

    public async Task<bool> TryRegisterSelfAsync(CancellationToken ct)
    {
        try
        {
            var now = DateTime.UtcNow;
            if ((now - _lastRegisterUtc) < RegisterInterval && !string.IsNullOrWhiteSpace(_authToken)) return false;
            
            var uid = NormalizeUid(_identity.UID);
            if (string.IsNullOrWhiteSpace(uid)) return false;

            var advertisedPort = _nat.MappedTcpPort ?? _network.ListeningPort ?? _settings.Settings.Port;
            if (advertisedPort <= 0 || advertisedPort > 65535) advertisedPort = _settings.Settings.Port > 0 ? _settings.Settings.Port : 9999;

            var publicKey = GetPublicKeyHex();

            foreach (var endpoint in GetCandidateRelayEndpoints())
            {
                var token = await TryRegisterWithEndpointAsync(endpoint.Host, endpoint.Port, uid, advertisedPort, publicKey, ct).ConfigureAwait(false);
                if (token != null)
                {
                    _registeredUid = uid;
                    _authToken = token;
                    _lastRegisterUtc = now;
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    public async Task<LookupResult?> LookupPeerAsync(string uid, CancellationToken ct)
    {
        try
        {
            var normalizedUid = NormalizeUid(uid);
            if (string.IsNullOrWhiteSpace(normalizedUid)) return null;

            foreach (var endpoint in GetCandidateRelayEndpoints())
            {
                var result = await TryLookupWithEndpointAsync(endpoint.Host, endpoint.Port, normalizedUid, ct).ConfigureAwait(false);
                if (result != null)
                {
                    return result with { Source = endpoint.Display };
                }
            }
        }
        catch { }

        return null;
    }

    public async Task<bool> TryOfferRendezvousAsync(string targetUid, string sourceUid, string sessionKey, CancellationToken ct)
    {
        try
        {
            var normalizedTarget = NormalizeUid(targetUid);
            var normalizedSource = NormalizeUid(sourceUid);
            if (string.IsNullOrWhiteSpace(normalizedTarget) || string.IsNullOrWhiteSpace(normalizedSource) || string.IsNullOrWhiteSpace(sessionKey)) return false;

            const int maxAttempts = 3;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                foreach (var endpoint in GetCandidateRelayEndpoints())
                {
                    try
                    {
                        if (await TryOfferWithEndpointAsync(endpoint.Host, endpoint.Port, normalizedTarget, normalizedSource, sessionKey, ct).ConfigureAwait(false))
                        {
                            Logger.Log($"OFFER delivered to {endpoint.Display} on attempt {attempt}/{maxAttempts}");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"OFFER attempt {attempt}/{maxAttempts} to {endpoint.Display} failed: {ex.Message}");
                    }
                }

                // Delay before retry (except after last attempt)
                if (attempt < maxAttempts)
                {
                    try { await Task.Delay(2000, ct); } catch { }
                }
            }

            Logger.Log($"OFFER delivery failed after {maxAttempts} attempts");
        }
        catch { }

        return false;
    }

    public async Task<IReadOnlyList<RelayInvite>> PollRelayInvitesAsync(string uid, CancellationToken ct)
    {
        var invites = new List<RelayInvite>();
        try
        {
            var normalizedUid = NormalizeUid(uid);
            if (string.IsNullOrWhiteSpace(normalizedUid)) return invites;

            foreach (var endpoint in GetCandidateRelayEndpoints())
            {
                var invite = await TryPollWithEndpointAsync(endpoint.Host, endpoint.Port, normalizedUid, endpoint.Display, ct).ConfigureAwait(false);
                if (invite != null)
                {
                    invites.Add(invite);
                }
            }
        }
        catch { }

        return invites;
    }

    public async Task<RelayInvite?> WaitForRelayInviteAsync(string uid, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            var normalizedUid = NormalizeUid(uid);
            if (string.IsNullOrWhiteSpace(normalizedUid)) return null;

            var candidates = GetCandidateRelayEndpoints().ToList();
            if (candidates.Count == 0) return null;

            // Long-poll first candidate (primary relay), then quick poll fallback on others.
            var primary = candidates[0];
            var waitMs = (int)Math.Clamp(timeout.TotalMilliseconds, 500, 15000);
            var primaryInvite = await TryWaitPollWithEndpointAsync(primary.Host, primary.Port, normalizedUid, primary.Display, waitMs, ct).ConfigureAwait(false);
            if (primaryInvite != null) return primaryInvite;

            for (var i = 1; i < candidates.Count; i++)
            {
                var c = candidates[i];
                var invite = await TryPollWithEndpointAsync(c.Host, c.Port, normalizedUid, c.Display, ct).ConfigureAwait(false);
                if (invite != null) return invite;
            }
        }
        catch { }

        return null;
    }

    public async Task<bool> TryAckRelayInviteAsync(string uid, string inviteId, CancellationToken ct)
    {
        try
        {
            var normalizedUid = NormalizeUid(uid);
            if (string.IsNullOrWhiteSpace(normalizedUid) || string.IsNullOrWhiteSpace(inviteId)) return false;

            foreach (var endpoint in GetCandidateRelayEndpoints())
            {
                if (await TryAckWithEndpointAsync(endpoint.Host, endpoint.Port, normalizedUid, inviteId, ct).ConfigureAwait(false))
                {
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    public async Task<bool> TryUnregisterAsync(CancellationToken ct)
    {
        try
        {
            var uid = NormalizeUid(_registeredUid);
            if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(_authToken)) return false;

            foreach (var endpoint in GetCandidateRelayEndpoints())
            {
                try
                {
                    using var client = new TcpClient();
                    using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    connectCts.CancelAfter(TimeSpan.FromSeconds(3));
                    await client.ConnectAsync(endpoint.Host, endpoint.Port, connectCts.Token).ConfigureAwait(false);
                    using var stream = client.GetStream();
                    var line = $"UNREG {uid} {_authToken}\n";
                    var bytes = Encoding.UTF8.GetBytes(line);
                    await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), ct).ConfigureAwait(false);
                    await stream.FlushAsync(ct).ConfigureAwait(false);

                    var resp = await ReadLineAsync(stream, TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(resp) && resp.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Log($"[WanDirectory] UNREG success on {endpoint.Display} for uid={uid}");
                        _registeredUid = string.Empty;
                        _authToken = string.Empty;
                        _lastRegisterUtc = DateTime.MinValue;
                        return true;
                    }
                }
                catch { }
            }
        }
        catch { }
        return false;
    }

    private async Task<string?> TryRegisterWithEndpointAsync(string host, int port, string uid, int advertisedPort, string publicKeyHex, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, ct).ConfigureAwait(false);
            using var stream = client.GetStream();
            var line = string.IsNullOrWhiteSpace(publicKeyHex)
                ? $"REG {uid} {advertisedPort}\n"
                : $"REG {uid} {advertisedPort} {publicKeyHex}\n";
            var bytes = Encoding.UTF8.GetBytes(line);
            await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);

            var resp = await ReadLineAsync(stream, TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(resp)) return null;
            var parts = resp.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1 && string.Equals(parts[0], "OK", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1])) return parts[1].Trim();
                return string.Empty; // backward compatibility with relays that return plain OK
            }
            return null;
        }
        catch { return null; }
    }

    private string GetPublicKeyHex()
    {
        try
        {
            var key = _identity.PublicKey;
            if (key == null || key.Length == 0) return string.Empty;
            return Convert.ToHexString(key).ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<LookupResult?> TryLookupWithEndpointAsync(string host, int port, string uid, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, ct).ConfigureAwait(false);
            using var stream = client.GetStream();
            var line = $"LOOKUP {uid}\n";
            var bytes = Encoding.UTF8.GetBytes(line);
            await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);

            var resp = await ReadLineAsync(stream, TimeSpan.FromSeconds(4), ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(resp) || string.Equals(resp, "MISS", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var parts = resp.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) return null;
            if (!string.Equals(parts[0], "PEER", StringComparison.OrdinalIgnoreCase)) return null;
            if (!int.TryParse(parts[3], out var resolvedPort) || resolvedPort < 1 || resolvedPort > 65535) return null;

            var resolvedUid = NormalizeUid(parts[1]);
            if (!string.Equals(resolvedUid, uid, StringComparison.OrdinalIgnoreCase)) return null;
            return new LookupResult(parts[2], resolvedPort, $"{host}:{port}");
        }
        catch { return null; }
    }

    private async Task<bool> TryOfferWithEndpointAsync(string host, int port, string targetUid, string sourceUid, string sessionKey, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, ct).ConfigureAwait(false);
            using var stream = client.GetStream();
            var authToken = GetAuthTokenFor(sourceUid);
            var line = string.IsNullOrWhiteSpace(authToken)
                ? $"OFFER {targetUid} {sourceUid} {sessionKey}\n"
                : $"OFFER {targetUid} {sourceUid} {sessionKey} {authToken}\n";
            var bytes = Encoding.UTF8.GetBytes(line);
            await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);

            var resp = await ReadLineAsync(stream, TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
            return resp != null && resp.StartsWith("OK", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private async Task<RelayInvite?> TryPollWithEndpointAsync(string host, int port, string uid, string display, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, ct).ConfigureAwait(false);
            using var stream = client.GetStream();
            var authToken = GetAuthTokenFor(uid);
            var line = string.IsNullOrWhiteSpace(authToken)
                ? $"POLL {uid}\n"
                : $"POLL {uid} {authToken}\n";
            var bytes = Encoding.UTF8.GetBytes(line);
            await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);

            var resp = await ReadLineAsync(stream, TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(resp) || string.Equals(resp, "NONE", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            if (resp.StartsWith("ERR unauthorized", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Log($"POLL rejected by relay: unauthorized — forcing re-registration");
                InvalidateAuthToken();
                await TryRegisterSelfAsync(ct).ConfigureAwait(false);
                return null;
            }

            return ParseInvite(resp, display);
        }
        catch { return null; }
    }

    private async Task<RelayInvite?> TryWaitPollWithEndpointAsync(string host, int port, string normalizedUid, string display, int waitMs, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, ct).ConfigureAwait(false);
            using var stream = client.GetStream();
            var authToken = GetAuthTokenFor(normalizedUid);
            var line = string.IsNullOrWhiteSpace(authToken)
                ? $"WAITPOLL {normalizedUid} {waitMs}\n"
                : $"WAITPOLL {normalizedUid} {waitMs} {authToken}\n";
            var bytes = Encoding.UTF8.GetBytes(line);
            await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);

            var resp = await ReadLineAsync(stream, TimeSpan.FromMilliseconds(waitMs + 1500), ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(resp) || string.Equals(resp, "NONE", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            if (resp.StartsWith("ERR unauthorized", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Log($"WAITPOLL rejected by relay: unauthorized — forcing re-registration + retry");
                InvalidateAuthToken();
                await TryRegisterSelfAsync(ct).ConfigureAwait(false);

                // Retry once with fresh token
                var retryToken = GetAuthTokenFor(normalizedUid);
                if (!string.IsNullOrWhiteSpace(retryToken))
                {
                    try
                    {
                        using var retryClient = new TcpClient();
                        await retryClient.ConnectAsync(host, port, ct).ConfigureAwait(false);
                        using var retryStream = retryClient.GetStream();
                        var retryLine = $"WAITPOLL {normalizedUid} {waitMs} {retryToken}\n";
                        var retryBytes = Encoding.UTF8.GetBytes(retryLine);
                        await retryStream.WriteAsync(retryBytes.AsMemory(0, retryBytes.Length), ct).ConfigureAwait(false);
                        await retryStream.FlushAsync(ct).ConfigureAwait(false);
                        var retryResp = await ReadLineAsync(retryStream, TimeSpan.FromMilliseconds(waitMs + 1500), ct).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(retryResp) && !string.Equals(retryResp, "NONE", StringComparison.OrdinalIgnoreCase))
                            return ParseInvite(retryResp, display);
                    }
                    catch { }
                }
                return null;
            }

            return ParseInvite(resp, display);
        }
        catch { return null; }
    }

    private async Task<bool> TryAckWithEndpointAsync(string host, int port, string uid, string inviteId, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, ct).ConfigureAwait(false);
            using var stream = client.GetStream();
            var authToken = GetAuthTokenFor(uid);
            var line = string.IsNullOrWhiteSpace(authToken)
                ? $"ACK {uid} {inviteId}\n"
                : $"ACK {uid} {inviteId} {authToken}\n";
            var bytes = Encoding.UTF8.GetBytes(line);
            await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
            var resp = await ReadLineAsync(stream, TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
            return string.Equals(resp, "OK", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static RelayInvite? ParseInvite(string responseLine, string display)
    {
        var parts = responseLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !string.Equals(parts[0], "INVITE", StringComparison.OrdinalIgnoreCase)) return null;

        if (parts.Length >= 4)
        {
            var inviteId = parts[1];
            var sourceUid = NormalizeUid(parts[2]);
            var sessionKey = parts[3];
            if (string.IsNullOrWhiteSpace(inviteId) || string.IsNullOrWhiteSpace(sourceUid) || string.IsNullOrWhiteSpace(sessionKey)) return null;
            return new RelayInvite(inviteId, sourceUid, sessionKey, display);
        }

        // Backward compatibility with older relay payload: INVITE <sourceUid> <sessionKey>
        var fallbackSource = NormalizeUid(parts[1]);
        var fallbackSession = parts[2];
        if (string.IsNullOrWhiteSpace(fallbackSource) || string.IsNullOrWhiteSpace(fallbackSession)) return null;
        return new RelayInvite(Guid.NewGuid().ToString("N"), fallbackSource, fallbackSession, display);
    }

    private IEnumerable<(string Host, int Port, string Display)> GetCandidateRelayEndpoints()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var settings = _settings.Settings;

        var explicitEndpoints = new[] { settings.RelayServer ?? string.Empty }
            .Concat(settings.SavedRelayServers ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToList();

        var useSeeds = settings.ForceSeedBootstrap || explicitEndpoints.Count == 0;
        var seedEndpoints = useSeeds
            ? (settings.WanSeedNodes ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
            : Enumerable.Empty<string>();

        IEnumerable<string> configured = explicitEndpoints.Concat(seedEndpoints);

        foreach (var endpoint in configured)
        {
            if (string.IsNullOrWhiteSpace(endpoint)) continue;
            if (!TryParseEndpoint(endpoint.Trim(), out var host, out var port)) continue;
            var key = $"{host}:{port}";
            if (!seen.Add(key)) continue;
            yield return (host, port, endpoint.Trim());
        }
    }

    public IReadOnlyList<string> GetBootstrapEndpointDisplays()
    {
        try
        {
            return GetCandidateRelayEndpoints()
                .Select(e => e.Display)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static bool TryParseEndpoint(string input, out string host, out int port)
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

    private static async Task<string?> ReadLineAsync(NetworkStream stream, TimeSpan timeout, CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(timeout);
        var builder = new StringBuilder();
        var one = new byte[1];
        try
        {
            while (!linkedCts.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(one.AsMemory(0, 1), linkedCts.Token).ConfigureAwait(false);
                if (read <= 0) return null;
                var ch = (char)one[0];
                if (ch == '\n') break;
                if (ch != '\r') builder.Append(ch);
            }
        }
        catch
        {
            return null;
        }

        return builder.ToString();
    }

    private static string NormalizeUid(string uid)
    {
        var s = (uid ?? string.Empty).Trim();
        return s.StartsWith("usr-", StringComparison.Ordinal) && s.Length > 4 ? s.Substring(4) : s;
    }

    private string? GetAuthTokenFor(string uid)
    {
        var normalized = NormalizeUid(uid);
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        if (!string.Equals(normalized, _registeredUid, StringComparison.OrdinalIgnoreCase)) return null;
        return _authToken;
    }

    private void InvalidateAuthToken()
    {
        _authToken = string.Empty;
        _registeredUid = string.Empty;
        _lastRegisterUtc = DateTime.MinValue;
    }
}
