using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZTalk.Services
{
    public class PresenceRefreshService
    {
        private readonly ConcurrentQueue<string> _queue = new();
        private readonly ConcurrentDictionary<string, byte> _queued = new(StringComparer.OrdinalIgnoreCase);
        private int _workerRunning;
        private static readonly TimeSpan DelayBetweenPeers = TimeSpan.FromMilliseconds(250);

        public void RequestUnlockSweep()
        {
            try
            {
                var contacts = AppServices.Contacts.Contacts.ToList();
                foreach (var contact in contacts)
                {
                    if (contact == null) continue;
                    Enqueue(contact.UID);
                }
            }
            catch { }

            StartWorkerIfNeeded();
        }

        private void Enqueue(string? uid)
        {
            var norm = Normalize(uid);
            if (string.IsNullOrEmpty(norm)) return;
            if (_queued.TryAdd(norm, 0))
            {
                _queue.Enqueue(norm);
            }
        }

        private void StartWorkerIfNeeded()
        {
            if (Interlocked.CompareExchange(ref _workerRunning, 1, 0) != 0) return;
            Task.Run(async () =>
            {
                try
                {
                    while (_queue.TryDequeue(out var uid))
                    {
                        _queued.TryRemove(uid, out _);
                        try { await RefreshPeerAsync(uid); } catch { }
                        try { await Task.Delay(DelayBetweenPeers); } catch { }
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _workerRunning, 0);
                    if (!_queue.IsEmpty)
                    {
                        StartWorkerIfNeeded();
                    }
                }
            });
        }

        private static string Normalize(string? uid)
            => string.IsNullOrWhiteSpace(uid)
                ? string.Empty
                : (uid.StartsWith("usr-", StringComparison.Ordinal) && uid.Length > 4 ? uid.Substring(4) : uid);

        private async Task RefreshPeerAsync(string uid)
        {
            try
            {
                var status = AppServices.Settings.Settings.Status;
                if (AppServices.Network.HasEncryptedSession(uid))
                {
                    await AppServices.Network.SendPresenceAsync(uid, status, CancellationToken.None);
                    return;
                }

                var peer = AppServices.Peers.Peers.FirstOrDefault(p => string.Equals(Normalize(p?.UID), uid, StringComparison.OrdinalIgnoreCase));
                if (peer == null || string.IsNullOrWhiteSpace(peer.Address) || peer.Port <= 0)
                {
                    return;
                }

                try { await AppServices.Network.ConnectWithRelayFallbackAsync(uid, peer.Address!, peer.Port, CancellationToken.None); } catch { }
                if (AppServices.Network.HasEncryptedSession(uid))
                {
                    await AppServices.Network.SendPresenceAsync(uid, status, CancellationToken.None);
                }
            }
            catch { }
        }
    }
}
