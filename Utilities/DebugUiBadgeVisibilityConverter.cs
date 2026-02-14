using System;
using System.Globalization;

using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Zer0Talk.Utilities
{
    public sealed class DebugUiBadgeVisibilityConverter : IMultiValueConverter
    {
        private const double DefaultMinWidth = 1360d;

        public object Convert(System.Collections.Generic.IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                if (values.Count < 2) return false;

                var isDebugUi = values[0] is bool b && b;
                if (!isDebugUi) return false;

                var minWidth = DefaultMinWidth;
                if (parameter != null && double.TryParse(parameter.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
                {
                    minWidth = parsed;
                }

                var width = values[1] switch
                {
                    double d => d,
                    float f => f,
                    decimal m => (double)m,
                    _ => 0d
                };

                return width >= minWidth;
            }
            catch
            {
                return false;
            }
        }
    }
}
