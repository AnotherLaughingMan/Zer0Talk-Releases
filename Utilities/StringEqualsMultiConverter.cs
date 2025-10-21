using System;
using System.Globalization;
using System.Collections.Generic;
using Avalonia.Data.Converters;

namespace Zer0Talk.Utilities
{
    public sealed class StringEqualsMultiConverter : IMultiValueConverter
    {
        public object? Convert(System.Collections.Generic.IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values is null || values.Count < 2) return false;
            var a = values[0]?.ToString() ?? string.Empty;
            var b = values[1]?.ToString() ?? string.Empty;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
