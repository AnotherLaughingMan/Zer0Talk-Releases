using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Zer0Talk.Utilities
{
    // Returns true only if: values[0] is bool true AND values[1] is a non-empty string.
    public sealed class BoolAndStringNotEmptyConverter : IMultiValueConverter
    {
        public static readonly BoolAndStringNotEmptyConverter Instance = new();

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                if (values is null || values.Count < 2) return false;
                var flag = values[0] is bool b && b;
                var text = values[1]?.ToString() ?? string.Empty;
                return flag && !string.IsNullOrWhiteSpace(text);
            }
            catch { return false; }
        }
    }
}
