using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ZTalk.Utilities
{
    public sealed class GreaterThanZeroConverter : IValueConverter
    {
        public static readonly GreaterThanZeroConverter Instance = new();
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                if (value is int i) return i > 0;
                if (value is long l) return l > 0;
                if (value is double d) return d > 0;
                if (value is float f) return f > 0;
                return false;
            }
            catch { return false; }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
