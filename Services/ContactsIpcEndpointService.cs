using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Zer0Talk.Services
{
    /// <summary>
    /// Minimal contacts list IPC endpoint contract.
    /// Transport-agnostic command/notification surface for shell migration.
    /// </summary>
    public sealed class ContactsIpcEndpointService : IDisposable
    {
        public const int DeltaSchemaVersion = 1;
        public const string CommandGetContactsList = "contacts.list.get";
        public const string EventContactsListChanged = "contacts.list.changed";
        public const string EventContactsListDeltaChanged = "contacts.list.delta.changed";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private readonly ContactsBridgeService _bridge;
        private readonly Action<ContactsSnapshotDto> _snapshotHandler;
        private ContactsSnapshotDto _lastSnapshot;
        private bool _disposed;

        public event Action<string, string>? NotificationJsonReady;

        public ContactsIpcEndpointService(ContactsBridgeService bridge)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            _lastSnapshot = _bridge.GetSnapshot();
            _snapshotHandler = snapshot => PublishContactsChanged(snapshot);
            try { _bridge.SnapshotChanged += _snapshotHandler; } catch { }
        }

        public bool TryHandleRequest(string command, out string responseJson)
        {
            responseJson = string.Empty;
            if (string.IsNullOrWhiteSpace(command)) return false;

            if (!string.Equals(command.Trim(), CommandGetContactsList, StringComparison.OrdinalIgnoreCase))
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
            try { _bridge.SnapshotChanged -= _snapshotHandler; } catch { }
        }

        private void PublishContactsChanged(ContactsSnapshotDto? currentSnapshot = null)
        {
            try
            {
                var current = currentSnapshot ?? _bridge.GetSnapshot();

                NotificationJsonReady?.Invoke(EventContactsListChanged, JsonSerializer.Serialize(current, JsonOptions));

                var delta = BuildDelta(_lastSnapshot, current);
                if (delta.AddedCount > 0 || delta.UpdatedCount > 0 || delta.RemovedCount > 0)
                {
                    NotificationJsonReady?.Invoke(EventContactsListDeltaChanged, JsonSerializer.Serialize(delta, JsonOptions));
                }

                _lastSnapshot = current;
            }
            catch { }
        }

        private static ContactsListDeltaDto BuildDelta(ContactsSnapshotDto previous, ContactsSnapshotDto current)
        {
            var prev = (previous?.Contacts ?? Array.Empty<ContactListItemDto>())
                .Where(c => !string.IsNullOrWhiteSpace(c.Uid))
                .ToDictionary(c => c.Uid, c => c, StringComparer.OrdinalIgnoreCase);

            var cur = (current?.Contacts ?? Array.Empty<ContactListItemDto>())
                .Where(c => !string.IsNullOrWhiteSpace(c.Uid))
                .ToDictionary(c => c.Uid, c => c, StringComparer.OrdinalIgnoreCase);

            var added = new List<ContactListItemDto>();
            var updated = new List<ContactListItemDto>();
            var removed = new List<string>();

            foreach (var kv in cur)
            {
                if (!prev.TryGetValue(kv.Key, out var prevItem))
                {
                    added.Add(kv.Value);
                    continue;
                }

                if (!AreEqual(prevItem, kv.Value))
                {
                    updated.Add(kv.Value);
                }
            }

            foreach (var kv in prev)
            {
                if (!cur.ContainsKey(kv.Key))
                {
                    removed.Add(kv.Key);
                }
            }

            return new ContactsListDeltaDto(
                DeltaSchemaVersion,
                DateTime.UtcNow,
                added,
                updated,
                removed,
                added.Count,
                updated.Count,
                removed.Count);
        }

        private static bool AreEqual(ContactListItemDto left, ContactListItemDto right)
        {
            return string.Equals(left.Uid, right.Uid, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal)
                   && string.Equals(left.Bio, right.Bio, StringComparison.Ordinal)
                   && left.UnreadCount == right.UnreadCount
                   && string.Equals(left.Presence, right.Presence, StringComparison.Ordinal)
                   && string.Equals(left.ConnectionMode, right.ConnectionMode, StringComparison.Ordinal)
                   && string.Equals(left.LastMessagePreview, right.LastMessagePreview, StringComparison.Ordinal)
                   && Nullable.Equals(left.LastMessageUtc, right.LastMessageUtc);
        }

        public sealed record ContactsListDeltaDto(
            int SchemaVersion,
            DateTime GeneratedUtc,
            IReadOnlyList<ContactListItemDto> Added,
            IReadOnlyList<ContactListItemDto> Updated,
            IReadOnlyList<string> RemovedUids,
            int AddedCount,
            int UpdatedCount,
            int RemovedCount);
    }
}