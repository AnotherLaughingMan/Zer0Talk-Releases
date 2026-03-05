using Zer0Talk.Utilities;
using Xunit;

namespace Zer0Talk.Tests;

public class UidNormalizationTests
{
    [Theory]
    [InlineData("usr-abc123", "abc123")]
    [InlineData("USR-abc123", "abc123")]
    [InlineData("abc123", "abc123")]
    [InlineData("", "")]
    public void TrimPrefix_NormalizesExpectedValues(string input, string expected)
    {
        var actual = UidNormalization.TrimPrefix(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("usr-abc123", "abc123", true)]
    [InlineData("USR-ABC123", "usr-abc123", true)]
    [InlineData("abc123", "def456", false)]
    [InlineData("", "usr-abc123", false)]
    public void EqualsNormalized_HandlesPrefixAndCase(string left, string right, bool expected)
    {
        var actual = UidNormalization.EqualsNormalized(left, right);
        Assert.Equal(expected, actual);
    }
}
