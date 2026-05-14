using System;
using System.Collections.Generic;
using System.Text.Json;
using Zer0Talk.Services;
using Xunit;

namespace Zer0Talk.Tests;

public class UnreadIpcEndpointServiceTests
{
    [Fact]
    public void Constructor_NullArguments_Throws()
    {
        var hub = new EventHub();
        var notificationService = new NotificationService();
        using var unreadState = new UnreadStateService(notificationService);
        using var bridge = new UnreadBridgeService(unreadState, hub);

        Assert.Throws<ArgumentNullException>(() => new UnreadIpcEndpointService(null!, hub));
        Assert.Throws<ArgumentNullException>(() => new UnreadIpcEndpointService(bridge, null!));
    }

    [Fact]
    public void TryHandleRequest_GetSnapshot_ReturnsBridgeJson()
    {
        var notificationService = new NotificationService();
        using var unreadState = new UnreadStateService(notificationService);
        var hub = new EventHub();
        using var bridge = new UnreadBridgeService(unreadState, hub);
        using var endpoint = new UnreadIpcEndpointService(bridge, hub);

        notificationService.AddOrUpdateMessageNotice("peer-b", "x", "body", Guid.NewGuid(), incoming: false, isUnread: true);
        notificationService.AddOrUpdateMessageNotice("peer-a", "x", "body", Guid.NewGuid(), incoming: false, isUnread: true);

        var handled = endpoint.TryHandleRequest(UnreadIpcEndpointService.CommandGetSnapshot, out var json);

        Assert.True(handled);
        Assert.False(string.IsNullOrWhiteSpace(json));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(UnreadBridgeService.SnapshotSchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(2, root.GetProperty("totalUnread").GetInt32());
    }

    [Fact]
    public void TryHandleRequest_UnknownCommand_ReturnsFalse()
    {
        var notificationService = new NotificationService();
        using var unreadState = new UnreadStateService(notificationService);
        var hub = new EventHub();
        using var bridge = new UnreadBridgeService(unreadState, hub);
        using var endpoint = new UnreadIpcEndpointService(bridge, hub);

        var handled = endpoint.TryHandleRequest("unread.unknown", out var json);

        Assert.False(handled);
        Assert.Equal(string.Empty, json);
    }

    [Fact]
    public void UnreadSnapshotDtoEvent_EmitsNotificationJson()
    {
        var notificationService = new NotificationService();
        using var unreadState = new UnreadStateService(notificationService);
        var hub = new EventHub();
        using var bridge = new UnreadBridgeService(unreadState, hub);
        using var endpoint = new UnreadIpcEndpointService(bridge, hub);

        string? receivedEventName = null;
        string? receivedPayload = null;
        endpoint.NotificationJsonReady += (eventName, payloadJson) =>
        {
            receivedEventName = eventName;
            receivedPayload = payloadJson;
        };

        var dto = new UnreadSnapshotDto(
            UnreadBridgeService.SnapshotSchemaVersion,
            DateTime.UtcNow,
            new List<UnreadPeerCountDto>
            {
                new("peer-a", 3)
            },
            3);

        hub.RaiseUnreadSnapshotDtoChanged(dto);

        Assert.Equal(UnreadIpcEndpointService.EventSnapshotChanged, receivedEventName);
        Assert.False(string.IsNullOrWhiteSpace(receivedPayload));

        using var doc = JsonDocument.Parse(receivedPayload!);
        var root = doc.RootElement;
        Assert.Equal(3, root.GetProperty("totalUnread").GetInt32());
        Assert.Equal(1, root.GetProperty("peers").GetArrayLength());
    }

    [Fact]
    public void UnreadCountDeltaEvent_EmitsNotificationJson()
    {
        var notificationService = new NotificationService();
        using var unreadState = new UnreadStateService(notificationService);
        var hub = new EventHub();
        using var bridge = new UnreadBridgeService(unreadState, hub);
        using var endpoint = new UnreadIpcEndpointService(bridge, hub);

        string? receivedEventName = null;
        string? receivedPayload = null;
        endpoint.NotificationJsonReady += (eventName, payloadJson) =>
        {
            receivedEventName = eventName;
            receivedPayload = payloadJson;
        };

        hub.RaiseUnreadPeerCountChanged("peer-z", 7);

        Assert.Equal(UnreadIpcEndpointService.EventCountChanged, receivedEventName);
        Assert.False(string.IsNullOrWhiteSpace(receivedPayload));

        using var doc = JsonDocument.Parse(receivedPayload!);
        var root = doc.RootElement;
        Assert.Equal(UnreadIpcEndpointService.CountDeltaSchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("peer-z", root.GetProperty("peerUid").GetString());
        Assert.Equal(7, root.GetProperty("unreadCount").GetInt32());
    }
}