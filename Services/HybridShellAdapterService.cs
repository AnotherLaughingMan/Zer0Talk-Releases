using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Zer0Talk.Services
{
    /// <summary>
    /// Stable adapter surface over hybrid shell consumer state.
    /// Exposes normalized contacts/unread snapshots and unified state-changed events.
    /// </summary>
    public sealed class HybridShellAdapterService : IDisposable
    {
        private readonly HybridShellConsumerService _consumer;
        private readonly object _stateGate = new();

        private bool _started;
        private bool _disposed;

        private IReadOnlyDictionary<string, ContactListItemDto> _contactsByUid =
            new Dictionary<string, ContactListItemDto>(StringComparer.OrdinalIgnoreCase);

        private IReadOnlyDictionary<string, int> _unreadByUid =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private HybridShellAdapterState _state = HybridShellAdapterState.Empty;

        public HybridShellAdapterService(HybridShellConsumerService consumer)
        {
            _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        }

        public bool IsStarted
        {
            get { lock (_stateGate) { return _started; } }
        }

        public HybridShellAdapterState CurrentState
        {
            get { lock (_stateGate) { return _state; } }
        }

        public event Action<HybridShellAdapterState>? StateChanged;

        public async Task<bool> StartAsync(bool contactsEnabled, bool unreadEnabled, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (!contactsEnabled && !unreadEnabled)
            {
                await StopAsync().ConfigureAwait(false);
                return false;
            }

            AttachHandlers();

            var started = await _consumer.StartAsync(contactsEnabled, unreadEnabled, cancellationToken).ConfigureAwait(false);
            if (!started)
            {
                lock (_stateGate)
                {
                    _started = false;
                }
                return false;
            }

            lock (_stateGate)
            {
                _started = true;
            }

            // Seed state from current consumer snapshots immediately.
            ApplyContactsSnapshot(_consumer.ContactsSnapshot);
            ApplyUnreadSnapshot(_consumer.UnreadSnapshot);
            RefreshState();

            return true;
        }

        public async Task StopAsync()
        {
            await _consumer.StopAsync().ConfigureAwait(false);
            DetachHandlers();

            HybridShellAdapterState next;
            lock (_stateGate)
            {
                _started = false;
                _contactsByUid = new Dictionary<string, ContactListItemDto>(StringComparer.OrdinalIgnoreCase);
                _unreadByUid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                _state = HybridShellAdapterState.Empty;
                next = _state;
            }

            try { StateChanged?.Invoke(next); } catch { }
        }

        public bool TryGetContact(string uid, out ContactListItemDto? contact)
        {
            contact = null;
            if (string.IsNullOrWhiteSpace(uid)) return false;

            lock (_stateGate)
            {
                return _contactsByUid.TryGetValue(uid, out contact);
            }
        }

        public int GetUnreadCount(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return 0;

            lock (_stateGate)
            {
                return _unreadByUid.TryGetValue(uid, out var count) ? count : 0;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { StopAsync().GetAwaiter().GetResult(); } catch { }
        }

        private void AttachHandlers()
        {
            _consumer.ContactsSnapshotChanged += OnContactsSnapshotChanged;
            _consumer.UnreadSnapshotChanged += OnUnreadSnapshotChanged;
        }

        private void DetachHandlers()
        {
            _consumer.ContactsSnapshotChanged -= OnContactsSnapshotChanged;
            _consumer.UnreadSnapshotChanged -= OnUnreadSnapshotChanged;
        }

        private void OnContactsSnapshotChanged(ContactsSnapshotDto snapshot)
        {
            ApplyContactsSnapshot(snapshot);
            RefreshState();
        }

        private void OnUnreadSnapshotChanged(UnreadSnapshotDto snapshot)
        {
            ApplyUnreadSnapshot(snapshot);
            RefreshState();
        }

        private void ApplyContactsSnapshot(ContactsSnapshotDto snapshot)
        {
            var map = new Dictionary<string, ContactListItemDto>(StringComparer.OrdinalIgnoreCase);
            if (snapshot.Contacts != null)
            {
                for (var i = 0; i < snapshot.Contacts.Count; i++)
                {
                    var item = snapshot.Contacts[i];
                    if (string.IsNullOrWhiteSpace(item.Uid)) continue;
                    map[item.Uid] = item;
                }
            }

            lock (_stateGate)
            {
                _contactsByUid = map;
            }
        }

        private void ApplyUnreadSnapshot(UnreadSnapshotDto snapshot)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (snapshot.Peers != null)
            {
                for (var i = 0; i < snapshot.Peers.Count; i++)
                {
                    var peer = snapshot.Peers[i];
                    if (string.IsNullOrWhiteSpace(peer.PeerUid)) continue;
                    if (peer.UnreadCount <= 0) continue;
                    map[peer.PeerUid] = peer.UnreadCount;
                }
            }

            lock (_stateGate)
            {
                _unreadByUid = map;
            }
        }

        private void RefreshState()
        {
            HybridShellAdapterState next;
            lock (_stateGate)
            {
                var totalUnread = _unreadByUid.Values.Sum();

                _state = new HybridShellAdapterState(
                    IsConnected: _consumer.IsRunning,
                    UsesContactsDelta: _consumer.UsingContactsDelta,
                    UsesUnreadDelta: _consumer.UsingUnreadDelta,
                    ContactsCount: _contactsByUid.Count,
                    TotalUnread: totalUnread,
                    LastUpdatedUtc: DateTime.UtcNow);

                next = _state;
            }

            try { StateChanged?.Invoke(next); } catch { }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HybridShellAdapterService));
            }
        }
    }

    public readonly record struct HybridShellAdapterState(
        bool IsConnected,
        bool UsesContactsDelta,
        bool UsesUnreadDelta,
        int ContactsCount,
        int TotalUnread,
        DateTime LastUpdatedUtc)
    {
        public static HybridShellAdapterState Empty { get; } = new(false, false, false, 0, 0, DateTime.MinValue);
    }
}
