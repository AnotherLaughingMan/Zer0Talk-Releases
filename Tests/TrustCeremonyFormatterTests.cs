using Zer0Talk.Utilities;
using Xunit;

namespace Zer0Talk.Tests;

public class TrustCeremonyFormatterTests
{
    [Fact]
    public void FingerprintFromPublicKeyHex_EmptyInput_ReturnsUnavailable()
    {
        var result = TrustCeremonyFormatter.FingerprintFromPublicKeyHex(string.Empty);

        Assert.Equal("Unavailable", result);
    }

    [Fact]
    public void FingerprintFromPublicKeyHex_ValidHex_IsDeterministicAndGrouped()
    {
        var keyHex = "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF";

        var first = TrustCeremonyFormatter.FingerprintFromPublicKeyHex(keyHex);
        var second = TrustCeremonyFormatter.FingerprintFromPublicKeyHex(keyHex);

        Assert.Equal(first, second);
        Assert.Contains(" ", first);
        Assert.Equal(79, first.Length); // 64 hex chars grouped by 4 => 15 spaces.
    }
}
