using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Zer0Talk.Models;
using Zer0Talk.Utilities;

namespace Zer0Talk.Services
{
    /// <summary>
    /// Transport-neutral contacts list adapter for future shell/IPC integrations.
    /// Produces a stable contact-list snapshot, including canonical unread counts.
    /// </summary>
    public sealed class ContactsBridgeService : IDisposable
    {
        public const int SnapshotSchemaVersion = 1;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private readonly ContactManager _contacts;
        private readonly NotificationService _notifications;
        private readonly Action _contactsChangedHandler;
        private readonly Action<IReadOnlyDictionary<string, int>> _unreadSnapshotChangedHandler;
        private bool _disposed;

        public event Action<ContactsSnapshotDto>? SnapshotChanged;
        public event Action<string>? SnapshotJsonChanged;

        public ContactsBridgeService(ContactManager contacts, NotificationService notifications)
        {
            _contacts = contacts ?? throw new ArgumentNullException(nameof(contacts));
            _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

            _contactsChangedHandler = PublishCurrentSnapshot;
            _unreadSnapshotChangedHandler = _ => PublishCurrentSnapshot();

            try { _contacts.Changed += _contactsChangedHandler; } catch { }
            try { _notifications.UnreadSnapshotChanged += _unreadSnapshotChangedHandler; } catch { }
        }

        public ContactsSnapshotDto GetSnapshot()
        {
            try
            {
                return BuildSnapshot();
            }
            catch
            {
                return new ContactsSnapshotDto(SnapshotSchemaVersion, DateTime.UtcNow, Array.Empty<ContactListItemDto>(), 0, 0);
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
                return "{\"schemaVersion\":1,\"generatedUtc\":null,\"contacts\":[],\"totalContacts\":0,\"totalUnread\":0}";
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _contacts.Changed -= _contactsChangedHandler; } catch { }
            try { _notifications.UnreadSnapshotChanged -= _unreadSnapshotChangedHandler; } catch { }
        }

        private void PublishCurrentSnapshot()
        {
            try
            {
                var snapshot = BuildSnapshot();
                SnapshotChanged?.Invoke(snapshot);
                SnapshotJsonChanged?.Invoke(JsonSerializer.Serialize(snapshot, JsonOptions));
            }
            catch { }
        }

        private ContactsSnapshotDto BuildSnapshot()
        {
            var contacts = _contacts.Contacts ?? Array.Empty<Contact>();
            if (contacts.Count == 0)
            {
                return new ContactsSnapshotDto(SnapshotSchemaVersion, DateTime.UtcNow, Array.Empty<ContactListItemDto>(), 0, 0);
            }

            var items = new List<ContactListItemDto>(contacts.Count);
            var totalUnread = 0;

            for (var i = 0; i < contacts.Count; i++)
            {
                var c = contacts[i];
                if (c == null) continue;

                var uid = UidNormalization.TrimPrefix(c.UID ?? string.Empty);
                if (string.IsNullOrWhiteSpace(uid)) continue;

                var unread = 0;
                try { unread = _notifications.GetUnreadCountForPeer(uid); } catch { }
                if (unread < 0) unread = 0;
                totalUnread += unread;

                items.Add(new ContactListItemDto(
                    uid,
                    string.IsNullOrWhiteSpace(c.DisplayName) ? uid : c.DisplayName,
                    c.Bio,
                    unread,
                    c.Presence.ToString(),
                    c.ConnectionMode.ToString(),
                    c.LastMessagePreview,
                    c.LastMessageUtc));
            }

            items.Sort((a, b) =>
            {
                var aOnlinePriority = IsOnlinePriority(a.Presence);
                var bOnlinePriority = IsOnlinePriority(b.Presence);
                var byPresence = bOnlinePriority.CompareTo(aOnlinePriority);
                if (byPresence != 0) return byPresence;

                var byLastMessageUtc = Nullable.Compare(b.LastMessageUtc, a.LastMessageUtc);
                if (byLastMessageUtc != 0) return byLastMessageUtc;

                var byName = string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
                if (byName != 0) return byName;

                return string.Compare(a.Uid, b.Uid, StringComparison.OrdinalIgnoreCase);
            });

            return new ContactsSnapshotDto(
                SnapshotSchemaVersion,
                DateTime.UtcNow,
                items,
                items.Count,
                totalUnread);
        }

        private static bool IsOnlinePriority(string presence)
        {
            if (string.IsNullOrWhiteSpace(presence)) return false;
            return string.Equals(presence, PresenceStatus.Online.ToString(), StringComparison.OrdinalIgnoreCase)
                   || string.Equals(presence, PresenceStatus.Idle.ToString(), StringComparison.OrdinalIgnoreCase)
                   || string.Equals(presence, PresenceStatus.DoNotDisturb.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed record ContactListItemDto(
        string Uid,
        string DisplayName,
        string? Bio,
        int UnreadCount,
        string Presence,
        string ConnectionMode,
        string? LastMessagePreview,
        DateTime? LastMessageUtc);

    public sealed record ContactsSnapshotDto(
        int SchemaVersion,
        DateTime GeneratedUtc,
        IReadOnlyList<ContactListItemDto> Contacts,
        int TotalContacts,
        int TotalUnread);
}