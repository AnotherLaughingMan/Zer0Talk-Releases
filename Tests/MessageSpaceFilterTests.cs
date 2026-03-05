using Zer0Talk.Models;
using Zer0Talk.Utilities;
using Xunit;

namespace Zer0Talk.Tests;

public class MessageSpaceFilterTests
{
    [Fact]
    public void Matches_ViewAll_AlwaysTrueForMessage()
    {
        var message = new Message { Content = "hello" };

        Assert.True(MessageSpaceFilter.Matches(message, 0, isUnread: false));
    }

    [Fact]
    public void Matches_ViewUnread_UsesUnreadFlag()
    {
        var message = new Message { Content = "hello" };

        Assert.True(MessageSpaceFilter.Matches(message, 1, isUnread: true));
        Assert.False(MessageSpaceFilter.Matches(message, 1, isUnread: false));
    }

    [Fact]
    public void Matches_ViewMentionsImportant_AcceptsPinnedStarredOrImportant()
    {
        var pinned = new Message { Content = "one", IsPinned = true };
        var starred = new Message { Content = "two", IsStarred = true };
        var important = new Message { Content = "three", IsImportant = true };
        var plain = new Message { Content = "four" };

        Assert.True(MessageSpaceFilter.Matches(pinned, 2, isUnread: false));
        Assert.True(MessageSpaceFilter.Matches(starred, 2, isUnread: false));
        Assert.True(MessageSpaceFilter.Matches(important, 2, isUnread: false));
        Assert.False(MessageSpaceFilter.Matches(plain, 2, isUnread: false));
    }

    [Fact]
    public void Matches_ViewAttachments_UsesAttachmentLikeSignal()
    {
        var withUrl = new Message { Content = "check https://example.com" };
        var plain = new Message { Content = "just text" };

        Assert.True(MessageSpaceFilter.Matches(withUrl, 3, isUnread: false));
        Assert.False(MessageSpaceFilter.Matches(plain, 3, isUnread: false));
    }
}