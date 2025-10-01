using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace ZTalk.Utilities;

public sealed class FirstNonEmptyStringConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        foreach (var value in values)
        {
            if (value is string s && !string.IsNullOrWhiteSpace(s))
            {
                return s;
            }
        }

        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
