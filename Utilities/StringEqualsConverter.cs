using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Zer0Talk.Utilities
{
    public sealed class StringEqualsConverter : IValueConverter
    {
        public bool IgnoreCase { get; set; } = true;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var expected = parameter?.ToString() ?? string.Empty;
            var actual = value?.ToString() ?? string.Empty;
            return IgnoreCase
                ? string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
                : string.Equals(actual, expected, StringComparison.Ordinal);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
