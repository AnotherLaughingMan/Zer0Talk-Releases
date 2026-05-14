using Zer0Talk.Services;
using Xunit;

namespace Zer0Talk.Tests;

public class EventHubUnreadSnapshotDtoTests
{
    [Fact]
    public void RaiseUnreadSnapshotDtoChanged_InvokesSubscribers()
    {
        var hub = new EventHub();
        UnreadSnapshotDto? received = null;
        hub.UnreadSnapshotDtoChanged += dto => received = dto;

        var payload = new UnreadSnapshotDto(
            UnreadBridgeService.SnapshotSchemaVersion,
            System.DateTime.UtcNow,
            new[]
            {
                new UnreadPeerCountDto("alice1234", 2),
                new UnreadPeerCountDto("bob5678", 1)
            },
            3);

        hub.RaiseUnreadSnapshotDtoChanged(payload);

        Assert.NotNull(received);
        Assert.Equal(3, received!.TotalUnread);
        Assert.Equal(UnreadBridgeService.SnapshotSchemaVersion, received.SchemaVersion);
    }
}
