using System.Collections.Generic;
using Zer0Talk.Services;
using Xunit;

namespace Zer0Talk.Tests;

public class UnreadStateServiceTests
{
    [Fact]
    public void Constructor_NullNotificationService_Throws()
    {
        Assert.Throws<System.ArgumentNullException>(() => new UnreadStateService(null!));
    }

    [Fact]
    public void GetSnapshot_ReflectsNotificationUnreadState()
    {
        var notifications = new NotificationService();
        using var unreadState = new UnreadStateService(notifications);

        notifications.AddOrUpdateMessageNotice("Alice", "Hello", "usr-alice1234", System.Guid.NewGuid(), incoming: false, isUnread: true);
        notifications.AddOrUpdateMessageNotice("Alice", "Again", "alice1234", System.Guid.NewGuid(), incoming: false, isUnread: true);

        var snapshot = unreadState.GetSnapshot();

        Assert.True(snapshot.TryGetValue("alice1234", out var unread));
        Assert.Equal(2, unread);
    }

    [Fact]
    public void GetSnapshot_AfterRead_ClearState()
    {
        var notifications = new NotificationService();
        using var unreadState = new UnreadStateService(notifications);

        notifications.AddOrUpdateMessageNotice("Bob", "Hi", "usr-bob5678", System.Guid.NewGuid(), incoming: false, isUnread: true);
        notifications.MarkConversationMessageNoticesRead("bob5678");

        var snapshot = unreadState.GetSnapshot();

        Assert.DoesNotContain("bob5678", snapshot.Keys);
    }
}
