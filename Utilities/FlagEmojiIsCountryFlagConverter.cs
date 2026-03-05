using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Zer0Talk.Utilities
{
    public sealed class FlagEmojiIsCountryFlagConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => FlagImageCatalog.IsFlagEmoji(value?.ToString());

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
