using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Zer0Talk.Utilities
{
    // Converts a country/region code (e.g., "US", "de", "jp") into a reusable flag bitmap image.
    public sealed class CountryCodeToFlagBitmapConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var code = value?.ToString() ?? string.Empty;
            return FlagImageCatalog.TryGetFlagImage(code);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
