using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Zer0Talk.Services
{
    /// <summary>
    /// Transport-neutral unread adapter for future IPC/shell integrations.
    /// Produces stable DTO snapshots from the canonical unread read-model.
    /// </summary>
    public sealed class UnreadBridgeService : IDisposable
    {
        public const int SnapshotSchemaVersion = 1;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private readonly UnreadStateService _unreadState;
        private readonly EventHub _events;
        private readonly Action<IReadOnlyDictionary<string, int>> _eventHandler;
        private bool _disposed;

        public event Action<UnreadSnapshotDto>? SnapshotChanged;
        public event Action<string>? SnapshotJsonChanged;

        public UnreadBridgeService(UnreadStateService unreadState, EventHub events)
        {
            _unreadState = unreadState ?? throw new ArgumentNullException(nameof(unreadState));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _eventHandler = snapshot => PublishSnapshot(snapshot);

            try
            {
                _events.UnreadSnapshotChanged += _eventHandler;
            }
            catch { }
        }

        public UnreadSnapshotDto GetSnapshot()
        {
            try
            {
                return MapSnapshot(_unreadState.GetSnapshot());
            }
            catch
            {
                return new UnreadSnapshotDto(SnapshotSchemaVersion, DateTime.UtcNow, Array.Empty<UnreadPeerCountDto>(), 0);
            }
        }

        public string GetSnapshotJson()
        {
            try
            {
                return JsonSerializer.Serialize(GetSnapshot(), JsonOptions);
            }
            catch
            {
                return "{\"schemaVersion\":1,\"generatedUtc\":null,\"peers\":[],\"totalUnread\":0}";
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _events.UnreadSnapshotChanged -= _eventHandler;
            }
            catch { }
        }

        private void PublishSnapshot(IReadOnlyDictionary<string, int> snapshot)
        {
            try
            {
                var dto = MapSnapshot(snapshot);
                SnapshotChanged?.Invoke(dto);
                SnapshotJsonChanged?.Invoke(JsonSerializer.Serialize(dto, JsonOptions));
            }
            catch { }
        }

        private static UnreadSnapshotDto MapSnapshot(IReadOnlyDictionary<string, int> snapshot)
        {
            if (snapshot == null || snapshot.Count == 0)
            {
                return new UnreadSnapshotDto(SnapshotSchemaVersion, DateTime.UtcNow, Array.Empty<UnreadPeerCountDto>(), 0);
            }

            var peers = snapshot
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && kv.Value > 0)
                .Select(kv => new UnreadPeerCountDto(kv.Key, kv.Value))
                .OrderBy(p => p.PeerUid, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var total = 0;
            for (int i = 0; i < peers.Count; i++)
            {
                total += peers[i].UnreadCount;
            }

            return new UnreadSnapshotDto(SnapshotSchemaVersion, DateTime.UtcNow, peers, total);
        }
    }

    public sealed record UnreadPeerCountDto(string PeerUid, int UnreadCount);

    public sealed record UnreadSnapshotDto(int SchemaVersion, DateTime GeneratedUtc, IReadOnlyList<UnreadPeerCountDto> Peers, int TotalUnread);
}
