/*
    NetworkDiagnostics: lightweight, thread-safe counters and recent errors for networking.
    - Used by NetworkService to report discovery, handshake, and session stats.
*/
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Zer0Talk.Utilities
{
    public sealed class NetworkDiagnostics
    {
        private long _accepted;
        private long _connects;
        private long _handshakeOk;
        private long _handshakeFail;
        private long _udpBeaconsSent;
        private long _udpBeaconsRecv;
        private long _sessionsActive;
        private readonly ConcurrentQueue<string> _recentErrors = new();
        private const int MaxErrors = 32;

        public void IncAccepted() => Interlocked.Increment(ref _accepted);
        public void IncConnects() => Interlocked.Increment(ref _connects);
        public void IncHandshakeOk() => Interlocked.Increment(ref _handshakeOk);
        public void IncHandshakeFail(string reason)
        {
            Interlocked.Increment(ref _handshakeFail);
            if (!string.IsNullOrWhiteSpace(reason))
            {
                _recentErrors.Enqueue($"{DateTime.UtcNow:o} {reason}");
                while (_recentErrors.Count > MaxErrors && _recentErrors.TryDequeue(out _)) { }
            }
        }
        public void IncUdpBeaconSent() => Interlocked.Increment(ref _udpBeaconsSent);
        public void IncUdpBeaconRecv() => Interlocked.Increment(ref _udpBeaconsRecv);
        public void IncSessionsActive() => Interlocked.Increment(ref _sessionsActive);
        public void DecSessionsActive()
        {
            while (true)
            {
                var current = Interlocked.Read(ref _sessionsActive);
                if (current <= 0)
                {
                    if (current < 0)
                    {
                        Interlocked.Exchange(ref _sessionsActive, 0);
                    }
                    return;
                }
                if (Interlocked.CompareExchange(ref _sessionsActive, current - 1, current) == current)
                {
                    return;
                }
            }
        }

        public Snapshot GetSnapshot()
        {
            var errors = _recentErrors.ToArray();
            return new Snapshot
            {
                Accepted = Interlocked.Read(ref _accepted),
                Connects = Interlocked.Read(ref _connects),
                HandshakeOk = Interlocked.Read(ref _handshakeOk),
                HandshakeFail = Interlocked.Read(ref _handshakeFail),
                UdpBeaconsSent = Interlocked.Read(ref _udpBeaconsSent),
                UdpBeaconsRecv = Interlocked.Read(ref _udpBeaconsRecv),
                SessionsActive = Math.Max(0, Interlocked.Read(ref _sessionsActive)),
                RecentErrors = errors,
            };
        }

        public sealed class Snapshot
        {
            public long Accepted { get; init; }
            public long Connects { get; init; }
            public long HandshakeOk { get; init; }
            public long HandshakeFail { get; init; }
            public long UdpBeaconsSent { get; init; }
            public long UdpBeaconsRecv { get; init; }
            public long SessionsActive { get; init; }
            public string[] RecentErrors { get; init; } = Array.Empty<string>();
        }
    }
}
