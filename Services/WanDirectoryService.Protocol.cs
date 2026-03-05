using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Zer0Talk.Utilities;

namespace Zer0Talk.Services;

public sealed partial class WanDirectoryService
{
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
            var authToken = GetAuthTokenFor(sourceUid, host, port);
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

    private async Task<IReadOnlyList<RelayInvite>> TryPollWithEndpointAsync(string host, int port, string uid, string display, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, ct).ConfigureAwait(false);
            using var stream = client.GetStream();
            var authToken = GetAuthTokenFor(uid, host, port);
            var line = string.IsNullOrWhiteSpace(authToken)
                ? $"POLL {uid}\n"
                : $"POLL {uid} {authToken}\n";
            var bytes = Encoding.UTF8.GetBytes(line);
            await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);

            var resp = await ReadLineAsync(stream, TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(resp) || string.Equals(resp, "NONE", StringComparison.OrdinalIgnoreCase))
            {
                return Array.Empty<RelayInvite>();
            }
            if (resp.StartsWith("ERR unauthorized", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Log($"POLL rejected by relay: unauthorized — forcing re-registration");
                InvalidateAuthToken(host, port);
                await TryRegisterSelfAsync(ct).ConfigureAwait(false);
                return Array.Empty<RelayInvite>();
            }

            return ParseInvites(resp, display);
        }
        catch { return Array.Empty<RelayInvite>(); }
    }

    private async Task<IReadOnlyList<RelayInvite>> TryWaitPollWithEndpointAsync(string host, int port, string normalizedUid, string display, int waitMs, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, ct).ConfigureAwait(false);
            using var stream = client.GetStream();
            var authToken = GetAuthTokenFor(normalizedUid, host, port);
            var line = string.IsNullOrWhiteSpace(authToken)
                ? $"WAITPOLL {normalizedUid} {waitMs}\n"
                : $"WAITPOLL {normalizedUid} {waitMs} {authToken}\n";
            var bytes = Encoding.UTF8.GetBytes(line);
            await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);

            var resp = await ReadLineAsync(stream, TimeSpan.FromMilliseconds(waitMs + 1500), ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(resp) || string.Equals(resp, "NONE", StringComparison.OrdinalIgnoreCase))
            {
                return Array.Empty<RelayInvite>();
            }
            if (resp.StartsWith("ERR unauthorized", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Log($"WAITPOLL rejected by relay: unauthorized — forcing re-registration + retry");
                InvalidateAuthToken(host, port);
                await TryRegisterSelfAsync(ct).ConfigureAwait(false);

                // Retry once with fresh token
                var retryToken = GetAuthTokenFor(normalizedUid, host, port);
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
                            return ParseInvites(retryResp, display);
                    }
                    catch { }
                }
                return Array.Empty<RelayInvite>();
            }

            return ParseInvites(resp, display);
        }
        catch { return Array.Empty<RelayInvite>(); }
    }

    private async Task<bool> TryAckWithEndpointAsync(string host, int port, string uid, string inviteId, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, ct).ConfigureAwait(false);
            using var stream = client.GetStream();
            var authToken = GetAuthTokenFor(uid, host, port);
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

    private static IReadOnlyList<RelayInvite> ParseInvites(string responseLine, string display)
    {
        if (string.IsNullOrWhiteSpace(responseLine)) return Array.Empty<RelayInvite>();
        var parts = responseLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return Array.Empty<RelayInvite>();

        if (string.Equals(parts[0], "INVITE", StringComparison.OrdinalIgnoreCase))
        {
            var single = ParseInvitePayload(parts, display);
            return single == null ? Array.Empty<RelayInvite>() : new[] { single };
        }

        if (string.Equals(parts[0], "INVITES", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length < 2) return Array.Empty<RelayInvite>();
            var list = new List<RelayInvite>();
            var payload = string.Join(' ', parts.Skip(1));
            var entries = payload.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var entry in entries)
            {
                // Session keys contain ':' (uidA:uidB), so only split the first two delimiters.
                var fields = entry.Split(':', 3, StringSplitOptions.TrimEntries);
                if (fields.Length != 3) continue;
                var inviteId = fields[0];
                var sourceUid = NormalizeUid(fields[1]);
                var sessionKey = fields[2];
                if (string.IsNullOrWhiteSpace(inviteId) || string.IsNullOrWhiteSpace(sourceUid) || string.IsNullOrWhiteSpace(sessionKey)) continue;
                list.Add(new RelayInvite(inviteId, sourceUid, sessionKey, display));
            }

            if (list.Count == 0) return Array.Empty<RelayInvite>();
            return list;
        }

        return Array.Empty<RelayInvite>();
    }

    private static RelayInvite? ParseInvitePayload(string[] parts, string display)
    {
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

        var seedEndpoints = (settings.WanSeedNodes ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim());

        if (!settings.ForceSeedBootstrap && explicitEndpoints.Count > 0)
        {
            // Keep explicit endpoints first; seeds are appended as fallback.
        }

        IEnumerable<string> configured = explicitEndpoints.Concat(seedEndpoints);

        foreach (var endpoint in configured)
        {
            if (string.IsNullOrWhiteSpace(endpoint)) continue;
            if (!TryParseEndpoint(endpoint.Trim(), out var host, out var port)) continue;
            var key = $"{host}:{port}";
            if (!seen.Add(key)) continue;
            yield return (host, port, endpoint.Trim());

            // If no explicit port was provided, also try 8443 as a fallback.
            if (!HasExplicitPort(endpoint) && port != 8443)
            {
                var altKey = $"{host}:8443";
                if (seen.Add(altKey))
                {
                    yield return (host, 8443, $"{endpoint.Trim()}:8443");
                }
            }
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

    private string? GetAuthTokenFor(string uid, string host, int port)
    {
        var normalized = NormalizeUid(uid);
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        if (!string.Equals(normalized, _registeredUid, StringComparison.OrdinalIgnoreCase)) return null;
        _authTokensByEndpoint.TryGetValue(BuildEndpointKey(host, port), out var token);
        return token;
    }

    private void InvalidateAuthToken(string host, int port)
    {
        _authTokensByEndpoint.TryRemove(BuildEndpointKey(host, port), out _);
        if (_authTokensByEndpoint.IsEmpty)
        {
            _registeredUid = string.Empty;
            _lastRegisterUtc = DateTime.MinValue;
        }
    }

    private static string BuildEndpointKey(string host, int port) => $"{host}:{port}";
}
