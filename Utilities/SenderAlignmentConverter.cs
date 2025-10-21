using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Layout;

namespace Zer0Talk.Utilities
{
    public sealed class SenderAlignmentConverter : IMultiValueConverter
    {
        public object? Convert(System.Collections.Generic.IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                if (values == null || values.Count < 2) return HorizontalAlignment.Left;
                var senderUid = values[0]?.ToString() ?? string.Empty;
                var selfUid = values[1]?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(senderUid) || string.IsNullOrWhiteSpace(selfUid)) return HorizontalAlignment.Left;
                var same = string.Equals(Trim(senderUid), Trim(selfUid), StringComparison.OrdinalIgnoreCase);
                return same ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            }
            catch { return HorizontalAlignment.Left; }
        }

        public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static string Trim(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            return s.StartsWith("usr-", StringComparison.Ordinal) ? s.Substring(4) : s;
        }
    }
}
