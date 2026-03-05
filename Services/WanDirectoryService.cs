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
    private readonly SettingsService _settings;
    private readonly IdentityService _identity;
    private readonly NetworkService _network;
    private readonly NatTraversalService _nat;
    private DateTime _lastRegisterUtc = DateTime.MinValue;
    private string _registeredUid = string.Empty;
    private readonly ConcurrentDictionary<string, string> _authTokensByEndpoint = new(StringComparer.OrdinalIgnoreCase);
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
            var uid = NormalizeUid(_identity.UID);
            if (string.IsNullOrWhiteSpace(uid)) return false;

            if ((now - _lastRegisterUtc) < RegisterInterval &&
                string.Equals(uid, _registeredUid, StringComparison.OrdinalIgnoreCase) &&
                !_authTokensByEndpoint.IsEmpty)
            {
                return false;
            }

            var advertisedPort = _nat.MappedTcpPort ?? _network.ListeningPort ?? _settings.Settings.Port;
            if (advertisedPort <= 0 || advertisedPort > 65535) advertisedPort = _settings.Settings.Port > 0 ? _settings.Settings.Port : 9999;

            var publicKey = GetPublicKeyHex();
            var success = false;

            foreach (var endpoint in GetCandidateRelayEndpoints())
            {
                var token = await TryRegisterWithEndpointAsync(endpoint.Host, endpoint.Port, uid, advertisedPort, publicKey, ct).ConfigureAwait(false);
                if (token != null)
                {
                    _authTokensByEndpoint[BuildEndpointKey(endpoint.Host, endpoint.Port)] = token;
                    success = true;
                }
            }

            if (success)
            {
                _registeredUid = uid;
                _lastRegisterUtc = now;
                return true;
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
                var endpointInvites = await TryPollWithEndpointAsync(endpoint.Host, endpoint.Port, normalizedUid, endpoint.Display, ct).ConfigureAwait(false);
                if (endpointInvites.Count > 0)
                {
                    invites.AddRange(endpointInvites);
                }
            }
        }
        catch { }

        return invites;
    }

    public async Task<IReadOnlyList<RelayInvite>> WaitForRelayInvitesAsync(string uid, TimeSpan timeout, CancellationToken ct)
    {
        var invites = new List<RelayInvite>();
        try
        {
            var normalizedUid = NormalizeUid(uid);
            if (string.IsNullOrWhiteSpace(normalizedUid)) return invites;

            var candidates = GetCandidateRelayEndpoints().ToList();
            if (candidates.Count == 0) return invites;

            // Long-poll first candidate (primary relay), then quick poll fallback on others.
            var primary = candidates[0];
            var waitMs = (int)Math.Clamp(timeout.TotalMilliseconds, 500, 15000);
            var primaryInvites = await TryWaitPollWithEndpointAsync(primary.Host, primary.Port, normalizedUid, primary.Display, waitMs, ct).ConfigureAwait(false);
            if (primaryInvites.Count > 0)
            {
                invites.AddRange(primaryInvites);
            }

            for (var i = 1; i < candidates.Count; i++)
            {
                var c = candidates[i];
                var endpointInvites = await TryPollWithEndpointAsync(c.Host, c.Port, normalizedUid, c.Display, ct).ConfigureAwait(false);
                if (endpointInvites.Count > 0)
                {
                    invites.AddRange(endpointInvites);
                }
            }
        }
        catch { }

        return invites;
    }

    public async Task<RelayInvite?> WaitForRelayInviteAsync(string uid, TimeSpan timeout, CancellationToken ct)
    {
        var invites = await WaitForRelayInvitesAsync(uid, timeout, ct).ConfigureAwait(false);
        return invites.FirstOrDefault();
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
            if (string.IsNullOrWhiteSpace(uid) || _authTokensByEndpoint.IsEmpty) return false;

            var unregisteredAny = false;

            foreach (var endpoint in GetCandidateRelayEndpoints())
            {
                try
                {
                    var endpointToken = GetAuthTokenFor(uid, endpoint.Host, endpoint.Port);
                    if (string.IsNullOrWhiteSpace(endpointToken)) continue;

                    using var client = new TcpClient();
                    using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    connectCts.CancelAfter(TimeSpan.FromSeconds(3));
                    await client.ConnectAsync(endpoint.Host, endpoint.Port, connectCts.Token).ConfigureAwait(false);
                    using var stream = client.GetStream();
                    var line = $"UNREG {uid} {endpointToken}\n";
                    var bytes = Encoding.UTF8.GetBytes(line);
                    await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), ct).ConfigureAwait(false);
                    await stream.FlushAsync(ct).ConfigureAwait(false);

                    var resp = await ReadLineAsync(stream, TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(resp) && resp.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Log($"[WanDirectory] UNREG success on {endpoint.Display} for uid={uid}");
                        _authTokensByEndpoint.TryRemove(BuildEndpointKey(endpoint.Host, endpoint.Port), out _);
                        unregisteredAny = true;
                    }
                }
                catch { }
            }

            if (unregisteredAny)
            {
                if (_authTokensByEndpoint.IsEmpty)
                {
                    _registeredUid = string.Empty;
                    _lastRegisterUtc = DateTime.MinValue;
                }
                return true;
            }
        }
        catch { }
        return false;
    }

}
