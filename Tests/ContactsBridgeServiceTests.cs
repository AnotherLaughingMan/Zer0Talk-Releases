using System;
using System.Text.Json;
using Xunit;
using Zer0Talk.Models;
using Zer0Talk.Services;

namespace Zer0Talk.Tests;

public class ContactsBridgeServiceTests
{
    [Fact]
    public void Constructor_NullDependencies_Throws()
    {
        var contacts = new ContactManager();
        var notifications = new NotificationService();

        Assert.Throws<ArgumentNullException>(() => new ContactsBridgeService(null!, notifications));
        Assert.Throws<ArgumentNullException>(() => new ContactsBridgeService(contacts, null!));
    }

    [Fact]
    public void GetSnapshot_MapsContactsAndUnreadCounts()
    {
        var contacts = new ContactManager();
        var notifications = new NotificationService();
        using var bridge = new ContactsBridgeService(contacts, notifications);

        var alice = new Contact { UID = "usr-alice", DisplayName = "Alice" };
        var bob = new Contact { UID = "bob", DisplayName = "Bob" };

        Assert.True(contacts.AddContact(alice, "test-pass"));
        Assert.True(contacts.AddContact(bob, "test-pass"));

        notifications.AddOrUpdateMessageNotice("msg", "1", "alice", Guid.NewGuid(), incoming: false, isUnread: true);
        notifications.AddOrUpdateMessageNotice("msg", "2", "bob", Guid.NewGuid(), incoming: false, isUnread: true);
        notifications.AddOrUpdateMessageNotice("msg", "3", "bob", Guid.NewGuid(), incoming: false, isUnread: true);

        var snapshot = bridge.GetSnapshot();

        Assert.Equal(ContactsBridgeService.SnapshotSchemaVersion, snapshot.SchemaVersion);
        Assert.Equal(2, snapshot.TotalContacts);
        Assert.Equal(3, snapshot.TotalUnread);
        Assert.Equal("alice", snapshot.Contacts[0].Uid);
        Assert.Equal(1, snapshot.Contacts[0].UnreadCount);
        Assert.Equal("bob", snapshot.Contacts[1].Uid);
        Assert.Equal(2, snapshot.Contacts[1].UnreadCount);
    }

    [Fact]
    public void GetSnapshotJson_ReturnsValidPayload()
    {
        var contacts = new ContactManager();
        var notifications = new NotificationService();
        using var bridge = new ContactsBridgeService(contacts, notifications);

        Assert.True(contacts.AddContact(new Contact { UID = "carol", DisplayName = "Carol" }, "test-pass"));

        var json = bridge.GetSnapshotJson();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(ContactsBridgeService.SnapshotSchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, root.GetProperty("totalContacts").GetInt32());
    }

    [Fact]
    public void GetSnapshot_NormalizesUppercaseUsrPrefix()
    {
        var contacts = new ContactManager();
        var notifications = new NotificationService();
        using var bridge = new ContactsBridgeService(contacts, notifications);

        Assert.True(contacts.AddContact(new Contact { UID = "USR-alice", DisplayName = "Alice" }, "test-pass"));
        notifications.AddOrUpdateMessageNotice("msg", "1", "alice", Guid.NewGuid(), incoming: false, isUnread: true);

        var snapshot = bridge.GetSnapshot();

        Assert.Single(snapshot.Contacts);
        Assert.Equal("alice", snapshot.Contacts[0].Uid);
        Assert.Equal(1, snapshot.Contacts[0].UnreadCount);
    }

    [Fact]
    public void GetSnapshot_SortsLikeMainContactListParity()
    {
        var contacts = new ContactManager();
        var notifications = new NotificationService();
        using var bridge = new ContactsBridgeService(contacts, notifications);

        var now = DateTime.UtcNow;
        var onlineOlder = new Contact { UID = "usr-online-older", DisplayName = "Online Older", Presence = PresenceStatus.Online, LastMessageUtc = now.AddMinutes(-15) };
        var idleNewer = new Contact { UID = "usr-idle-newer", DisplayName = "Idle Newer", Presence = PresenceStatus.Idle, LastMessageUtc = now.AddMinutes(-5) };
        var offlineNewest = new Contact { UID = "usr-offline-newest", DisplayName = "Offline Newest", Presence = PresenceStatus.Offline, LastMessageUtc = now.AddMinutes(-1) };

        Assert.True(contacts.AddContact(onlineOlder, "test-pass"));
        Assert.True(contacts.AddContact(idleNewer, "test-pass"));
        Assert.True(contacts.AddContact(offlineNewest, "test-pass"));

        var snapshot = bridge.GetSnapshot();

        Assert.Equal(3, snapshot.Contacts.Count);
        Assert.Equal("idle-newer", snapshot.Contacts[0].Uid);
        Assert.Equal("online-older", snapshot.Contacts[1].Uid);
        Assert.Equal("offline-newest", snapshot.Contacts[2].Uid);
    }
}