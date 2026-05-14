using System.Collections.Generic;
using Zer0Talk.Services;
using Xunit;

namespace Zer0Talk.Tests;

public class EventHubUnreadSnapshotTests
{
    [Fact]
    public void RaiseUnreadSnapshotChanged_InvokesSubscribers()
    {
        var hub = new EventHub();
        IReadOnlyDictionary<string, int>? received = null;
        hub.UnreadSnapshotChanged += snapshot => received = snapshot;

        var payload = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["alice1234"] = 3
        };

        hub.RaiseUnreadSnapshotChanged(payload);

        Assert.NotNull(received);
        Assert.Equal(3, received!["alice1234"]);
    }

    [Fact]
    public void RaiseUnreadSnapshotChanged_NullSnapshot_NoThrow()
    {
        var hub = new EventHub();
        hub.RaiseUnreadSnapshotChanged(null!);
    }
}
