using System.Collections.Generic;
using Zer0Talk.Models;
using Xunit;

namespace Zer0Talk.Tests;

public class ContactUnreadBadgeTests
{
    [Fact]
    public void UnreadCount_Change_RaisesUnreadAndHasUnread()
    {
        var contact = new Contact();
        var changed = new List<string>();

        contact.PropertyChanged += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.PropertyName))
            {
                changed.Add(args.PropertyName!);
            }
        };

        contact.UnreadCount = 1;

        Assert.Contains(nameof(Contact.UnreadCount), changed);
        Assert.Contains(nameof(Contact.HasUnread), changed);
        Assert.True(contact.HasUnread);
    }

    [Fact]
    public void UnreadCount_Zero_ClearsHasUnread()
    {
        var contact = new Contact { UnreadCount = 3 };

        contact.UnreadCount = 0;

        Assert.False(contact.HasUnread);
    }
}
