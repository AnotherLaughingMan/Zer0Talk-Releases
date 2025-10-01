using System;
using System.Globalization;

using Avalonia.Data.Converters;

namespace ZTalk.Utilities
{
    public class InverseBoolConverter : IValueConverter
    {
        public static readonly InverseBoolConverter Instance = new();
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool b ? !b : (object?)true;
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool b ? !b : (object?)true;
    }
}
