using System.Linq;
using Zer0Talk.Models;
using Xunit;

namespace Zer0Talk.Tests;

public class MessageReactionTests
{
    [Fact]
    public void ApplyReaction_AddAndRemove_UpdatesAggregateCount()
    {
        var message = new Message();

        message.ApplyReaction("usr-alice", "👍", true);
        message.ApplyReaction("bob", "👍", true);

        var aggregate = message.ReactionAggregates.FirstOrDefault(x => x.Emoji == "👍");
        Assert.NotNull(aggregate);
        Assert.Equal(2, aggregate!.Count);

        message.ApplyReaction("alice", "👍", false);
        aggregate = message.ReactionAggregates.FirstOrDefault(x => x.Emoji == "👍");
        Assert.NotNull(aggregate);
        Assert.Equal(1, aggregate!.Count);
    }

    [Fact]
    public void ApplyReaction_DedupesSameActorAcrossUidPrefixForms()
    {
        var message = new Message();

        message.ApplyReaction("usr-alice", "❤️", true);
        message.ApplyReaction("alice", "❤️", true);

        var aggregate = message.ReactionAggregates.FirstOrDefault(x => x.Emoji == "❤️");
        Assert.NotNull(aggregate);
        Assert.Equal(1, aggregate!.Count);
    }

    [Fact]
    public void HasReaction_UsesNormalizedActorUid()
    {
        var message = new Message();
        message.ApplyReaction("usr-alice", "🔥", true);

        Assert.True(message.HasReaction("alice", "🔥"));
        Assert.True(message.HasReaction("usr-alice", "🔥"));
        Assert.False(message.HasReaction("bob", "🔥"));
    }
}
