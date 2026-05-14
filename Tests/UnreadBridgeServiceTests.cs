using System.Collections.Generic;
using Zer0Talk.Services;
using Xunit;

namespace Zer0Talk.Tests;

public class UnreadBridgeServiceTests
{
    [Fact]
    public void Constructor_NullDependencies_Throws()
    {
        var hub = new EventHub();
        var notifications = new NotificationService();
        using var unreadState = new UnreadStateService(notifications);

        Assert.Throws<System.ArgumentNullException>(() => new UnreadBridgeService(null!, hub));
        Assert.Throws<System.ArgumentNullException>(() => new UnreadBridgeService(unreadState, null!));
    }

    [Fact]
    public void GetSnapshot_MapsUnreadStateToDto()
    {
        var notifications = new NotificationService();
        using var unreadState = new UnreadStateService(notifications);
        var hub = new EventHub();
        using var bridge = new UnreadBridgeService(unreadState, hub);

        notifications.AddOrUpdateMessageNotice("A", "1", "usr-zeta", System.Guid.NewGuid(), incoming: false, isUnread: true);
        notifications.AddOrUpdateMessageNotice("A", "2", "alpha", System.Guid.NewGuid(), incoming: false, isUnread: true);

        var snapshot = bridge.GetSnapshot();

        Assert.Equal(UnreadBridgeService.SnapshotSchemaVersion, snapshot.SchemaVersion);
        Assert.Equal(2, snapshot.TotalUnread);
        Assert.Equal(2, snapshot.Peers.Count);
        Assert.Equal("alpha", snapshot.Peers[0].PeerUid);
        Assert.Equal("zeta", snapshot.Peers[1].PeerUid);
    }

    [Fact]
    public void EventHubUnreadSnapshot_RaisesBridgeEvents()
    {
        var notifications = new NotificationService();
        using var unreadState = new UnreadStateService(notifications);
        var hub = new EventHub();
        using var bridge = new UnreadBridgeService(unreadState, hub);

        UnreadSnapshotDto? receivedDto = null;
        string? receivedJson = null;
        bridge.SnapshotChanged += dto => receivedDto = dto;
        bridge.SnapshotJsonChanged += json => receivedJson = json;

        hub.RaiseUnreadSnapshotChanged(new Dictionary<string, int>
        {
            ["alice1234"] = 3,
            ["bob5678"] = 1
        });

        Assert.NotNull(receivedDto);
        Assert.Equal(UnreadBridgeService.SnapshotSchemaVersion, receivedDto!.SchemaVersion);
        Assert.Equal(4, receivedDto!.TotalUnread);
        Assert.NotNull(receivedJson);
        Assert.Contains("\"schemaVersion\":1", receivedJson);
        Assert.Contains("\"totalUnread\":4", receivedJson);
        Assert.Contains("\"peerUid\":\"alice1234\"", receivedJson);
    }
}
