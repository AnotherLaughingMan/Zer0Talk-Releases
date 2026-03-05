using System;
using System.Collections.Generic;
using Zer0Talk.Models;
using Xunit;

namespace Zer0Talk.Tests;

public class MessageModelTests
{
    [Fact]
    public void Content_Null_NormalizesToEmpty_AndRenderedContentMatches()
    {
        var message = new Message();

        message.Content = null!;

        Assert.Equal(string.Empty, message.Content);
        Assert.Equal(string.Empty, message.RenderedContent);
    }

    [Fact]
    public void Content_Update_RaisesContentAndRenderedContentNotifications()
    {
        var message = new Message();
        var changed = new List<string>();

        message.PropertyChanged += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.PropertyName))
            {
                changed.Add(args.PropertyName!);
            }
        };

        message.Content = "hello **world**";

        Assert.Contains(nameof(Message.Content), changed);
        Assert.Contains(nameof(Message.RenderedContent), changed);
        Assert.False(string.IsNullOrWhiteSpace(message.RenderedContent));
    }

    [Fact]
    public void Content_SetSameValue_DoesNotRaiseDuplicateNotifications()
    {
        var message = new Message { Content = "unchanged" };
        var changed = new List<string>();

        message.PropertyChanged += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.PropertyName))
            {
                changed.Add(args.PropertyName!);
            }
        };

        message.Content = "unchanged";

        Assert.DoesNotContain(nameof(Message.Content), changed);
        Assert.DoesNotContain(nameof(Message.RenderedContent), changed);
    }
}
