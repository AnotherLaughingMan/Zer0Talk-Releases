using System;
using System.Globalization;
using Zer0Talk.Models;
using Zer0Talk.Utilities;
using Xunit;

namespace Zer0Talk.Tests;

public class DeliveryStatusConverterTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Fact]
    public void DeliveryStatusToGlyphConverter_MapsCanonicalGlyphs()
    {
        var converter = new DeliveryStatusToGlyphConverter();

        Assert.Equal("\uE823", converter.Convert(MessageDeliveryStatus.Pending, typeof(string), null, Culture));
        Assert.Equal("\uE73E", converter.Convert(MessageDeliveryStatus.Sent, typeof(string), null, Culture));
        Assert.Equal("\uE73E\uE73E", converter.Convert(MessageDeliveryStatus.Delivered, typeof(string), null, Culture));
        Assert.Equal("\uE73E", converter.Convert(MessageDeliveryStatus.Read, typeof(string), null, Culture));
        Assert.Equal(string.Empty, converter.Convert(MessageDeliveryStatus.None, typeof(string), null, Culture));
    }

    [Fact]
    public void DeliveryStatusReadVisibilityConverters_AreMutuallyExclusive()
    {
        var isRead = new DeliveryStatusIsReadConverter();
        var isNotRead = new DeliveryStatusIsNotReadConverter();

        Assert.True((bool)isRead.Convert(MessageDeliveryStatus.Read, typeof(bool), null, Culture)!);
        Assert.False((bool)isNotRead.Convert(MessageDeliveryStatus.Read, typeof(bool), null, Culture)!);

        Assert.False((bool)isRead.Convert(MessageDeliveryStatus.Delivered, typeof(bool), null, Culture)!);
        Assert.True((bool)isNotRead.Convert(MessageDeliveryStatus.Delivered, typeof(bool), null, Culture)!);
    }
}
