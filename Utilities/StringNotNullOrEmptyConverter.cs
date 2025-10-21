using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Zer0Talk.Utilities
{
    public sealed class StringNotNullOrEmptyConverter : IValueConverter
    {
        public static readonly StringNotNullOrEmptyConverter Instance = new();
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            try { return !string.IsNullOrWhiteSpace(value as string); } catch { return false; }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
