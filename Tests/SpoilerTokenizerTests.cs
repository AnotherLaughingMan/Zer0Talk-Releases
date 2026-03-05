using System.Linq;
using Zer0Talk.Utilities;
using Xunit;

namespace Zer0Talk.Tests;

public class SpoilerTokenizerTests
{
    [Fact]
    public void ContainsValidSpoiler_ReturnsTrue_WhenPairExists()
    {
        Assert.True(SpoilerTokenizer.ContainsValidSpoiler("alpha ||beta|| gamma"));
    }

    [Fact]
    public void ContainsValidSpoiler_ReturnsFalse_WhenDelimiterUnmatched()
    {
        Assert.False(SpoilerTokenizer.ContainsValidSpoiler("alpha ||beta"));
    }

    [Fact]
    public void Tokenize_PreservesPlainSpoilerAndTrailingMarkdown()
    {
        var tokens = SpoilerTokenizer.Tokenize("alpha ||beta|| ++underline++").ToArray();

        Assert.Equal(3, tokens.Length);
        Assert.Equal(new SpoilerToken("alpha ", false), tokens[0]);
        Assert.Equal(new SpoilerToken("beta", true), tokens[1]);
        Assert.Equal(new SpoilerToken(" ++underline++", false), tokens[2]);
    }

    [Fact]
    public void Tokenize_TreatsUnmatchedOpeningAsPlainText()
    {
        var tokens = SpoilerTokenizer.Tokenize("before ||dangling").ToArray();

        Assert.Single(tokens);
        Assert.Equal(new SpoilerToken("before ||dangling", false), tokens[0]);
    }

    [Fact]
    public void Tokenize_HandlesMultipleSpoilersWithSpacing()
    {
        var tokens = SpoilerTokenizer.Tokenize("a ||b|| c ||d|| e").ToArray();

        Assert.Equal(5, tokens.Length);
        Assert.Equal(new SpoilerToken("a ", false), tokens[0]);
        Assert.Equal(new SpoilerToken("b", true), tokens[1]);
        Assert.Equal(new SpoilerToken(" c ", false), tokens[2]);
        Assert.Equal(new SpoilerToken("d", true), tokens[3]);
        Assert.Equal(new SpoilerToken(" e", false), tokens[4]);
    }
}
