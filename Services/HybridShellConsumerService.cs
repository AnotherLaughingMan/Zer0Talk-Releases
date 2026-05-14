using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Zer0Talk.Services
{
    /// <summary>
    /// Coordinates shell-side consumption over the hybrid IPC client.
    /// Prefers delta events when supported, and falls back to periodic snapshots when not.
    /// </summary>
    public sealed class HybridShellConsumerService : IDisposable
    {
        private static readonly TimeSpan SnapshotPollInterval = TimeSpan.FromSeconds(5);

        private readonly HybridShellIpcClientService _client;
        private readonly object _stateGate = new();

        private CancellationTokenSource? _pollCts;
        private Task? _pollLoopTask;
        private bool _handlersAttached;
        private bool _disposed;

        private bool _contactsEnabled;
        private bool _unreadEnabled;
        private bool _useContactsDelta;
        private bool _useUnreadDelta;

        private ContactsSnapshotDto _contactsSnapshot = new(
            ContactsBridgeService.SnapshotSchemaVersion,
            DateTime.UtcNow,
            Array.Empty<ContactListItemDto>(),
            0,
            0);

        private UnreadSnapshotDto _unreadSnapshot = new(
            UnreadBridgeService.SnapshotSchemaVersion,
            DateTime.UtcNow,
            Array.Empty<UnreadPeerCountDto>(),
            0);

        public HybridShellConsumerService(HybridShellIpcClientService client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public bool IsRunning { get; private set; }

        public bool UsingContactsDelta
        {
            get { lock (_stateGate) { return _useContactsDelta; } }
        }

        public bool UsingUnreadDelta
        {
            get { lock (_stateGate) { return _useUnreadDelta; } }
        }

        public ContactsSnapshotDto ContactsSnapshot
        {
            get { lock (_stateGate) { return _contactsSnapshot; } }
        }

        public UnreadSnapshotDto UnreadSnapshot
        {
            get { lock (_stateGate) { return _unreadSnapshot; } }
        }

        public event Action<ContactsSnapshotDto>? ContactsSnapshotChanged;
        public event Action<UnreadSnapshotDto>? UnreadSnapshotChanged;

        public async Task<bool> StartAsync(bool contactsEnabled, bool unreadEnabled, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (!contactsEnabled && !unreadEnabled)
            {
                await StopAsync().ConfigureAwait(false);
                return false;
            }

            _contactsEnabled = contactsEnabled;
            _unreadEnabled = unreadEnabled;

            if (!await _client.ConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                IsRunning = false;
                return false;
            }

            AttachHandlers();

            lock (_stateGate)
            {
                _useContactsDelta = contactsEnabled && _client.SupportsContactsDelta;
                _useUnreadDelta = unreadEnabled && _client.SupportsUnreadCountDelta;
            }

            if (contactsEnabled)
            {
                var contacts = await _client.RequestContactsSnapshotAsync(cancellationToken).ConfigureAwait(false);
                if (contacts != null)
                {
                    UpdateContactsSnapshot(contacts);
                }
            }

            if (unreadEnabled)
            {
                var unread = await _client.RequestUnreadSnapshotAsync(cancellationToken).ConfigureAwait(false);
                if (unread != null)
                {
                    UpdateUnreadSnapshot(unread);
                }
            }

            StartSnapshotPollIfNeeded();
            IsRunning = true;
            return true;
        }

        public async Task StopAsync()
        {
            StopSnapshotPoll();
            DetachHandlers();
            IsRunning = false;
            await _client.DisconnectAsync().ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { StopAsync().GetAwaiter().GetResult(); } catch { }
        }

        private void AttachHandlers()
        {
            if (_handlersAttached) return;

            _client.ContactsSnapshotReceived += OnContactsSnapshotReceived;
            _client.ContactsDeltaReceived += OnContactsDeltaReceived;
            _client.UnreadSnapshotReceived += OnUnreadSnapshotReceived;
            _client.UnreadCountDeltaReceived += OnUnreadCountDeltaReceived;
            _handlersAttached = true;
        }

        private void DetachHandlers()
        {
            if (!_handlersAttached) return;

            _client.ContactsSnapshotReceived -= OnContactsSnapshotReceived;
            _client.ContactsDeltaReceived -= OnContactsDeltaReceived;
            _client.UnreadSnapshotReceived -= OnUnreadSnapshotReceived;
            _client.UnreadCountDeltaReceived -= OnUnreadCountDeltaReceived;
            _handlersAttached = false;
        }

        private void StartSnapshotPollIfNeeded()
        {
            StopSnapshotPoll();

            bool shouldPollContacts;
            bool shouldPollUnread;
            lock (_stateGate)
            {
                shouldPollContacts = _contactsEnabled && !_useContactsDelta;
                shouldPollUnread = _unreadEnabled && !_useUnreadDelta;
            }

            if (!shouldPollContacts && !shouldPollUnread)
            {
                return;
            }

            _pollCts = new CancellationTokenSource();
            _pollLoopTask = Task.Run(() => SnapshotPollLoopAsync(_pollCts.Token), _pollCts.Token);
        }

        private void StopSnapshotPoll()
        {
            var cts = _pollCts;
            _pollCts = null;

            try { cts?.Cancel(); } catch { }

            try
            {
                _pollLoopTask?.Wait(TimeSpan.FromSeconds(1));
            }
            catch { }

            _pollLoopTask = null;
            try { cts?.Dispose(); } catch { }
        }

        private async Task SnapshotPollLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(SnapshotPollInterval, cancellationToken).ConfigureAwait(false);

                    if (_contactsEnabled && !UsingContactsDelta)
                    {
                        var contacts = await _client.RequestContactsSnapshotAsync(cancellationToken).ConfigureAwait(false);
                        if (contacts != null)
                        {
                            UpdateContactsSnapshot(contacts);
                        }
                    }

                    if (_unreadEnabled && !UsingUnreadDelta)
                    {
                        var unread = await _client.RequestUnreadSnapshotAsync(cancellationToken).ConfigureAwait(false);
                        if (unread != null)
                        {
                            UpdateUnreadSnapshot(unread);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Keep poll loop resilient as a migration scaffold.
                }
            }
        }

        private void OnContactsSnapshotReceived(ContactsSnapshotDto snapshot)
        {
            if (!_contactsEnabled) return;
            UpdateContactsSnapshot(snapshot);
        }

        private void OnContactsDeltaReceived(ContactsIpcEndpointService.ContactsListDeltaDto delta)
        {
            if (!_contactsEnabled || !UsingContactsDelta) return;

            ContactsSnapshotDto next;
            lock (_stateGate)
            {
                var map = _contactsSnapshot.Contacts.ToDictionary(c => c.Uid, c => c, StringComparer.OrdinalIgnoreCase);

                for (var i = 0; i < delta.RemovedUids.Count; i++)
                {
                    var uid = delta.RemovedUids[i];
                    if (!string.IsNullOrWhiteSpace(uid)) map.Remove(uid);
                }

                for (var i = 0; i < delta.Added.Count; i++)
                {
                    var item = delta.Added[i];
                    if (!string.IsNullOrWhiteSpace(item.Uid)) map[item.Uid] = item;
                }

                for (var i = 0; i < delta.Updated.Count; i++)
                {
                    var item = delta.Updated[i];
                    if (!string.IsNullOrWhiteSpace(item.Uid)) map[item.Uid] = item;
                }

                var contacts = map.Values
                    .OrderByDescending(c => c.LastMessageUtc)
                    .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(c => c.Uid, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var totalUnread = 0;
                for (var i = 0; i < contacts.Count; i++)
                {
                    totalUnread += contacts[i].UnreadCount;
                }

                next = new ContactsSnapshotDto(
                    ContactsBridgeService.SnapshotSchemaVersion,
                    DateTime.UtcNow,
                    contacts,
                    contacts.Count,
                    totalUnread);

                _contactsSnapshot = next;
            }

            try { ContactsSnapshotChanged?.Invoke(next); } catch { }
        }

        private void OnUnreadSnapshotReceived(UnreadSnapshotDto snapshot)
        {
            if (!_unreadEnabled) return;
            UpdateUnreadSnapshot(snapshot);
        }

        private void OnUnreadCountDeltaReceived(string peerUid, int unreadCount)
        {
            if (!_unreadEnabled || !UsingUnreadDelta || string.IsNullOrWhiteSpace(peerUid)) return;

            UnreadSnapshotDto next;
            lock (_stateGate)
            {
                var map = _unreadSnapshot.Peers.ToDictionary(p => p.PeerUid, p => p.UnreadCount, StringComparer.OrdinalIgnoreCase);
                if (unreadCount <= 0)
                {
                    map.Remove(peerUid);
                }
                else
                {
                    map[peerUid] = unreadCount;
                }

                var peers = map
                    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => new UnreadPeerCountDto(kv.Key, kv.Value))
                    .ToList();

                var totalUnread = 0;
                for (var i = 0; i < peers.Count; i++)
                {
                    totalUnread += peers[i].UnreadCount;
                }

                next = new UnreadSnapshotDto(
                    UnreadBridgeService.SnapshotSchemaVersion,
                    DateTime.UtcNow,
                    peers,
                    totalUnread);

                _unreadSnapshot = next;
            }

            try { UnreadSnapshotChanged?.Invoke(next); } catch { }
        }

        private void UpdateContactsSnapshot(ContactsSnapshotDto snapshot)
        {
            lock (_stateGate)
            {
                _contactsSnapshot = snapshot;
            }
            try { ContactsSnapshotChanged?.Invoke(snapshot); } catch { }
        }

        private void UpdateUnreadSnapshot(UnreadSnapshotDto snapshot)
        {
            lock (_stateGate)
            {
                _unreadSnapshot = snapshot;
            }
            try { UnreadSnapshotChanged?.Invoke(snapshot); } catch { }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HybridShellConsumerService));
            }
        }
    }
}