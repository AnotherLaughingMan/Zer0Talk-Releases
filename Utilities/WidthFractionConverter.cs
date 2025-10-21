using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Zer0Talk.Utilities
{
    public sealed class WidthFractionConverter : IValueConverter
    {
        public double Fraction { get; set; } = 0.72; // default to ~72% of area

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                double width = value switch
                {
                    double d => d,
                    float f => f,
                    int i => i,
                    long l => l,
                    _ => 0d
                };
                if (width <= 0) return 560d; // safe fallback
                double frac = Fraction;
                if (parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) frac = p;
                var result = Math.Max(240d, width * frac);
                return result;
            }
            catch { return 560d; }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
