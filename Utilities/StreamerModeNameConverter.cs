using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Zer0Talk.Utilities
{
    /// <summary>
    /// MultiValueConverter: values[0] = display name (string), values[1] = IsStreamerMode (bool).
    /// When streamer mode is active, scrambles the name with random chars seeded by the name's hash
    /// so the same name always produces the same scrambled output (stable across re-renders).
    /// </summary>
    public sealed class StreamerModeNameConverter : IMultiValueConverter
    {
        private static readonly char[] ScrambleChars =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%&*".ToCharArray();

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values is null || values.Count < 2)
                return values?[0]?.ToString() ?? string.Empty;

            var name = values[0]?.ToString() ?? string.Empty;

            if (values[1] is bool streamerMode && streamerMode && name.Length > 0)
                return Scramble(name);

            return name;
        }

        private static string Scramble(string input)
        {
            // Use a stable seed from the string hash so the same name always scrambles identically
            var seed = GetStableHash(input);
            var rng = new Random(seed);
            var chars = new char[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                if (char.IsWhiteSpace(input[i]))
                    chars[i] = ' '; // preserve word spacing
                else
                    chars[i] = ScrambleChars[rng.Next(ScrambleChars.Length)];
            }
            return new string(chars);
        }

        private static int GetStableHash(string s)
        {
            // FNV-1a 32-bit for deterministic cross-session stability
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in s)
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                return (int)hash;
            }
        }
    }
}
