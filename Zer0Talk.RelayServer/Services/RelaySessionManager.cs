using System;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace Zer0Talk.RelayServer.Services;

public sealed class RelaySessionManager
{
    public enum PairOutcome
    {
        Paired,
        Queued,
        RejectedAlreadyQueued,
        RejectedCapacity,
        RejectedAlreadyActive,
        RejectedIncompatible,
        RejectedCooldown
    }

    private readonly ConcurrentDictionary<string, RelayPending> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RelaySession> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _recentFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly RelayConfig _config;
    private static readonly TimeSpan SessionCooldown = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan UnknownPendingTimeout = TimeSpan.FromSeconds(20);

    public RelaySessionManager(RelayConfig config)
    {
        _config = config;
    }

    public int PendingCount => _pending.Count;
    public int ActiveCount => _active.Count;

    public System.Collections.Generic.IReadOnlyList<RelaySessionInfo> GetActiveSessions()
    {
        var list = new System.Collections.Generic.List<RelaySessionInfo>();
        foreach (var entry in _active)
        {
            list.Add(new RelaySessionInfo(entry.Key, entry.Value.LeftUid, entry.Value.RightUid));
        }
        return list;
    }

    public PairOutcome TryPairOrQueue(RelayRequest request, TcpClient client, NetworkStream stream, out RelaySession? session)
    {
        session = null;
        if (_pending.Count >= _config.MaxPending || _active.Count >= _config.MaxSessions)
        {
            SafeClose(client);
            return PairOutcome.RejectedCapacity;
        }

        var key = request.SessionKey;
        
        // Check if actually active (not just lingering in dictionary during cleanup)
        if (_active.TryGetValue(key, out var existingSession))
        {
            // Only clean up if TCP state is definitively dead.
            var isDead = !existingSession.IsConnected;
            
            if (isDead)
            {
                // Session TCP is dead, allow replacement.
                _active.TryRemove(key, out _);
                existingSession.Close();
            }
            else
            {
                SafeClose(client);
                return PairOutcome.RejectedAlreadyActive;
            }
        }

        if (_recentFailures.TryGetValue(key, out var failTime) && (DateTime.UtcNow - failTime) < SessionCooldown)
        {
            SafeClose(client);
            return PairOutcome.RejectedCooldown;
        }

        if (_pending.TryRemove(key, out var existing))
        {
            var now = DateTime.UtcNow;

            if (IsPendingExpired(existing, now))
            {
                SafeClose(existing.Client);
                _pending[key] = new RelayPending(request, client, stream, now);
                return PairOutcome.Queued;
            }

            // Verify the pending client's TCP is still alive before attempting to pair.
            // If the pending client disconnected (timeout, crash, etc.), its TcpClient.Connected
            // goes false and attempting to pair would fail when writing PAIRED to the dead stream.
            var pendingAlive = RelaySession.IsClientConnected(existing.Client);

            if (!pendingAlive)
            {
                // Pending client is dead — discard it and queue the new one in its place.
                SafeClose(existing.Client);
                _pending[key] = new RelayPending(request, client, stream, now);
                return PairOutcome.Queued;
            }

            if (IsCompatible(existing.Request, request))
            {
                session = new RelaySession(existing.Request, existing.Client, existing.Stream, request, client, stream);
                _active[key] = session;
                return PairOutcome.Paired;
            }

            if (IsSameSide(existing.Request, request))
            {
                // Keep the original pending side to avoid queue churn when one peer
                // retries before the counterpart has a chance to arrive.
                _pending[key] = existing;
                SafeClose(client);
                return PairOutcome.RejectedAlreadyQueued;
            }

            SafeClose(existing.Client);
            SafeClose(client);
            return PairOutcome.RejectedIncompatible;
        }

        _pending[key] = new RelayPending(request, client, stream, DateTime.UtcNow);
        return PairOutcome.Queued;
    }

    public void RemoveSession(RelaySession session, out TimeSpan lifetime)
    {
        lifetime = DateTime.UtcNow - session.StartedUtc;
        if (!string.IsNullOrWhiteSpace(session.SessionKey))
        {
            _active.TryRemove(session.SessionKey, out _);
            if (lifetime < TimeSpan.FromSeconds(2))
            {
                _recentFailures[session.SessionKey] = DateTime.UtcNow;
            }
        }
    }

    public bool DisconnectSession(string sessionKey)
    {
        if (_active.TryRemove(sessionKey, out var session))
        {
            session.Close();
            return true;
        }
        return false;
    }

    public int DisconnectSessionsForUid(string uid)
    {
        if (string.IsNullOrWhiteSpace(uid)) return 0;
        var disconnected = 0;

        foreach (var entry in _active)
        {
            var session = entry.Value;
            if (!string.Equals(session.LeftUid, uid, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(session.RightUid, uid, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (_active.TryRemove(entry.Key, out var removed))
            {
                removed.Close();
                disconnected++;
            }
        }

        foreach (var entry in _pending)
        {
            var pendingUid = entry.Value.Request.Uid;
            if (!string.Equals(pendingUid, uid, StringComparison.OrdinalIgnoreCase)) continue;
            if (_pending.TryRemove(entry.Key, out var pending))
            {
                SafeClose(pending.Client);
            }
        }

        return disconnected;
    }

    public void CleanupExpiredPending()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in _pending)
        {
            if (!IsPendingExpired(entry.Value, now)) continue;
            if (_pending.TryRemove(entry.Key, out var pending))
            {
                SafeClose(pending.Client);
            }
        }

        foreach (var entry in _recentFailures)
        {
            if ((now - entry.Value) > SessionCooldown)
            {
                _recentFailures.TryRemove(entry.Key, out _);
            }
        }
    }

    private static bool IsCompatible(RelayRequest a, RelayRequest b)
    {
        if (!string.IsNullOrWhiteSpace(a.Role) && !string.IsNullOrWhiteSpace(b.Role) &&
            string.Equals(a.Role, b.Role, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(a.Uid) && !string.IsNullOrWhiteSpace(b.Uid) &&
            string.Equals(a.Uid, b.Uid, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool IsSameSide(RelayRequest a, RelayRequest b)
    {
        if (!string.IsNullOrWhiteSpace(a.Role) && !string.IsNullOrWhiteSpace(b.Role) &&
            string.Equals(a.Role, b.Role, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(a.Uid) && !string.IsNullOrWhiteSpace(b.Uid) &&
            string.Equals(a.Uid, b.Uid, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private bool IsPendingExpired(RelayPending pending, DateTime now)
    {
        var age = now - pending.CreatedUtc;
        var configuredTimeout = TimeSpan.FromSeconds(Math.Max(5, _config.PendingTimeoutSeconds));
        var isUnknownSide = string.IsNullOrWhiteSpace(pending.Request.Uid);
        var timeout = isUnknownSide && configuredTimeout > UnknownPendingTimeout
            ? UnknownPendingTimeout
            : configuredTimeout;
        return age >= timeout;
    }

    private static void SafeClose(TcpClient client)
    {
        try { client.Close(); } catch { }
    }

    private sealed class RelayPending
    {
        public RelayPending(RelayRequest request, TcpClient client, NetworkStream stream, DateTime createdUtc)
        {
            Request = request;
            Client = client;
            Stream = stream;
            CreatedUtc = createdUtc;
        }

        public RelayRequest Request { get; }
        public TcpClient Client { get; }
        public NetworkStream Stream { get; }
        public DateTime CreatedUtc { get; }
    }
}
