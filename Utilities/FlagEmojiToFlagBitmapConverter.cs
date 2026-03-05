using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Zer0Talk.Utilities
{
    public sealed class FlagEmojiToFlagBitmapConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => FlagImageCatalog.TryGetFlagImageFromFlagEmoji(value?.ToString());

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
