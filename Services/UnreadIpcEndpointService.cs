using System;
using System.Text.Json;

namespace Zer0Talk.Services
{
    /// <summary>
    /// Minimal unread IPC endpoint contract.
    /// This class is transport-agnostic and can be hosted by named pipes, sockets, or shell bridges.
    /// </summary>
    public sealed class UnreadIpcEndpointService : IDisposable
    {
        public const int CountDeltaSchemaVersion = 1;
        public const string CommandGetSnapshot = "unread.snapshot.get";
        public const string EventSnapshotChanged = "unread.snapshot.changed";
        public const string EventCountChanged = "unread.count.changed";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private readonly UnreadBridgeService _bridge;
        private readonly EventHub _events;
        private readonly Action<UnreadSnapshotDto> _snapshotDtoHandler;
        private readonly Action<string, int> _unreadCountHandler;
        private bool _disposed;

        /// <summary>
        /// Transport can subscribe here to push unread updates as JSON.
        /// Arguments: event name, event payload JSON.
        /// </summary>
        public event Action<string, string>? NotificationJsonReady;

        public UnreadIpcEndpointService(UnreadBridgeService bridge, EventHub events)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _snapshotDtoHandler = dto => PublishSnapshotChanged(dto);
            _unreadCountHandler = (peerUid, unreadCount) => PublishUnreadCountChanged(peerUid, unreadCount);

            try
            {
                _events.UnreadSnapshotDtoChanged += _snapshotDtoHandler;
                _events.UnreadPeerCountChanged += _unreadCountHandler;
            }
            catch { }
        }

        public bool TryHandleRequest(string command, out string responseJson)
        {
            responseJson = string.Empty;
            if (string.IsNullOrWhiteSpace(command)) return false;

            if (!string.Equals(command.Trim(), CommandGetSnapshot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            responseJson = _bridge.GetSnapshotJson();
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _events.UnreadSnapshotDtoChanged -= _snapshotDtoHandler;
                _events.UnreadPeerCountChanged -= _unreadCountHandler;
            }
            catch { }
        }

        private void PublishSnapshotChanged(UnreadSnapshotDto snapshot)
        {
            if (snapshot == null) return;

            try
            {
                var payloadJson = JsonSerializer.Serialize(snapshot, JsonOptions);
                NotificationJsonReady?.Invoke(EventSnapshotChanged, payloadJson);
            }
            catch { }
        }

        private void PublishUnreadCountChanged(string peerUid, int unreadCount)
        {
            if (string.IsNullOrWhiteSpace(peerUid)) return;

            try
            {
                var dto = new UnreadCountChangedDto(CountDeltaSchemaVersion, peerUid, unreadCount, DateTime.UtcNow);
                var payloadJson = JsonSerializer.Serialize(dto, JsonOptions);
                NotificationJsonReady?.Invoke(EventCountChanged, payloadJson);
            }
            catch { }
        }

        private sealed record UnreadCountChangedDto(int SchemaVersion, string PeerUid, int UnreadCount, DateTime GeneratedUtc);
    }
}