using System;
using Xunit;
using Zer0Talk.Services;

namespace Zer0Talk.Tests;

public class NotificationServiceUnreadSnapshotTests
{
    [Fact]
    public void Snapshot_AggregatesUnreadPerPeer_WithUidNormalization()
    {
        var service = new NotificationService();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        service.AddOrUpdateMessageNotice("Alice", "Hello", "usr-alice1234", first, incoming: false, isUnread: true);
        service.AddOrUpdateMessageNotice("Alice", "Again", "alice1234", second, incoming: false, isUnread: true);

        var snapshot = service.GetUnreadCountsSnapshot();

        Assert.True(snapshot.TryGetValue("alice1234", out var unread));
        Assert.Equal(2, unread);
    }

    [Fact]
    public void MarkMessageNoticeRead_ClearsMessageFromUnreadSnapshot()
    {
        var service = new NotificationService();
        var messageId = Guid.NewGuid();

        service.AddOrUpdateMessageNotice("Bob", "Ping", "usr-bob5678", messageId, incoming: false, isUnread: true);
        service.MarkMessageNoticeRead(messageId, scheduleRemoval: false);

        var snapshot = service.GetUnreadCountsSnapshot();

        Assert.False(snapshot.ContainsKey("bob5678"));
    }

    [Fact]
    public void MarkConversationMessageNoticesRead_ClearsOnlyTargetPeer()
    {
        var service = new NotificationService();

        service.AddOrUpdateMessageNotice("Carol", "One", "usr-carol9999", Guid.NewGuid(), incoming: false, isUnread: true);
        service.AddOrUpdateMessageNotice("Carol", "Two", "carol9999", Guid.NewGuid(), incoming: false, isUnread: true);
        service.AddOrUpdateMessageNotice("Dave", "Other", "usr-dave1111", Guid.NewGuid(), incoming: false, isUnread: true);

        service.MarkConversationMessageNoticesRead("usr-carol9999");

        var snapshot = service.GetUnreadCountsSnapshot();

        Assert.False(snapshot.ContainsKey("carol9999"));
        Assert.True(snapshot.TryGetValue("dave1111", out var daveUnread));
        Assert.Equal(1, daveUnread);
    }

    [Fact]
    public void MarkAllMessageNoticesRead_ClearsUnreadSnapshot()
    {
        var service = new NotificationService();

        service.AddOrUpdateMessageNotice("Eve", "Hi", "usr-eve2222", Guid.NewGuid(), incoming: false, isUnread: true);
        service.AddOrUpdateMessageNotice("Frank", "Hi", "frank3333", Guid.NewGuid(), incoming: false, isUnread: true);

        service.MarkAllMessageNoticesRead();

        var snapshot = service.GetUnreadCountsSnapshot();

        Assert.Empty(snapshot);
    }
}
