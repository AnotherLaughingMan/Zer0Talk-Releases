using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Zer0Talk.Utilities
{
    public sealed class ReactionEmojiTooltipConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var emoji = (value?.ToString() ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(emoji)) return string.Empty;

            var code = FlagImageCatalog.TryGetCountryCodeFromFlagEmoji(emoji);
            if (!string.IsNullOrWhiteSpace(code))
            {
                var countryName = FlagImageCatalog.TryGetCountryDisplayName(code);
                var normalizedCode = code.ToUpperInvariant();
                return string.IsNullOrWhiteSpace(countryName)
                    ? $"Flag: {normalizedCode}"
                    : $"Flag: {countryName} ({normalizedCode})";
            }

            return ReactionEmojiCatalogLoader.GetEmojiDisplayName(emoji) ?? emoji;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
