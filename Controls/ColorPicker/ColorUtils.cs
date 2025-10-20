using System;
using Avalonia.Media;

namespace ZTalk.Controls.ColorPicker
{
    public static class ColorUtils
    {
        public static Color ColorFromHsv(double hue, double saturation, double value, byte alpha)
        {
            hue = NormalizeHue(hue);
            saturation = Math.Clamp(saturation, 0.0, 1.0);
            value = Math.Clamp(value, 0.0, 1.0);

            var c = value * saturation;
            var x = c * (1 - Math.Abs(((hue / 60.0) % 2) - 1));
            var m = value - c;

            double r1, g1, b1;

            if (hue < 60)
            {
                r1 = c; g1 = x; b1 = 0;
            }
            else if (hue < 120)
            {
                r1 = x; g1 = c; b1 = 0;
            }
            else if (hue < 180)
            {
                r1 = 0; g1 = c; b1 = x;
            }
            else if (hue < 240)
            {
                r1 = 0; g1 = x; b1 = c;
            }
            else if (hue < 300)
            {
                r1 = x; g1 = 0; b1 = c;
            }
            else
            {
                r1 = c; g1 = 0; b1 = x;
            }

            var r = (byte)Math.Clamp(Math.Round((r1 + m) * 255), 0, 255);
            var g = (byte)Math.Clamp(Math.Round((g1 + m) * 255), 0, 255);
            var b = (byte)Math.Clamp(Math.Round((b1 + m) * 255), 0, 255);

            return Color.FromArgb(alpha, r, g, b);
        }

        public static void ColorToHsv(Color color, out double h, out double s, out double v)
        {
            var r = color.R / 255.0;
            var g = color.G / 255.0;
            var b = color.B / 255.0;

            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var delta = max - min;

            if (delta < double.Epsilon)
            {
                h = 0;
            }
            else if (Math.Abs(max - r) < double.Epsilon)
            {
                h = 60 * (((g - b) / delta) % 6);
            }
            else if (Math.Abs(max - g) < double.Epsilon)
            {
                h = 60 * (((b - r) / delta) + 2);
            }
            else
            {
                h = 60 * (((r - g) / delta) + 4);
            }

            if (h < 0)
            {
                h += 360;
            }

            s = max <= 0 ? 0 : delta / max;
            v = max;
            h = NormalizeHue(h);
        }

        private static double NormalizeHue(double hue)
        {
            if (double.IsNaN(hue) || double.IsInfinity(hue))
                return 0;

            var normalized = hue % 360.0;
            if (normalized < 0)
            {
                normalized += 360.0;
            }

            return normalized;
        }
    }
}
