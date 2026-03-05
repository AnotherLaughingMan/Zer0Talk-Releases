using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Zer0Talk.Utilities
{
    /// <summary>
    /// Converter that returns 72pt font size for emoji-only messages,
    /// 16pt for normal messages. Used in message display to show emoji at full size.
    /// </summary>
    public sealed class EmojiOnlyFontSizeConverter : IValueConverter
    {
        public static readonly EmojiOnlyFontSizeConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isEmojiOnly)
            {
                return isEmojiOnly ? 72d : 16d;
            }

            return 16d; // Default
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
