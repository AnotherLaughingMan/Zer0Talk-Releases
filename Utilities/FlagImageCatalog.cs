using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Zer0Talk.Utilities
{
    // App-wide flag graphics catalog backed by embedded Assets/Flags PNG files.
    public static class FlagImageCatalog
    {
        private static readonly Uri FlagsIndexUri = new("avares://Zer0Talk/Resources/Data/flags-index.json");
        private static readonly HashSet<string> AvailableCodes = LoadAvailableCodes();
        private static readonly ConcurrentDictionary<string, IImage?> ImageCache = new(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyCollection<string> Codes => AvailableCodes;

        public static bool IsAvailable(string? countryCode)
            => AvailableCodes.Contains(NormalizeCountryCode(countryCode));

        public static Uri? TryGetFlagUri(string? countryCode)
        {
            var code = NormalizeCountryCode(countryCode);
            if (string.IsNullOrEmpty(code) || !AvailableCodes.Contains(code)) return null;
            return new Uri($"avares://Zer0Talk/Assets/Flags/{code}.png");
        }

        public static IImage? TryGetFlagImage(string? countryCode)
        {
            var code = NormalizeCountryCode(countryCode);
            if (string.IsNullOrEmpty(code) || !AvailableCodes.Contains(code)) return null;

            return ImageCache.GetOrAdd(code, static key =>
            {
                try
                {
                    using var stream = AssetLoader.Open(new Uri($"avares://Zer0Talk/Assets/Flags/{key}.png"));
                    return new Bitmap(stream);
                }
                catch
                {
                    return null;
                }
            });
        }

        public static bool IsFlagEmoji(string? emoji)
            => !string.IsNullOrWhiteSpace(TryGetCountryCodeFromFlagEmoji(emoji));

        public static string? TryGetCountryCodeFromFlagEmoji(string? emoji)
        {
            var value = (emoji ?? string.Empty).Trim();
            if (value.Length < 4) return null;

            var runes = value.EnumerateRunes().ToArray();
            if (runes.Length != 2) return null;

            var first = runes[0].Value;
            var second = runes[1].Value;
            if (first < 0x1F1E6 || first > 0x1F1FF) return null;
            if (second < 0x1F1E6 || second > 0x1F1FF) return null;

            var c1 = (char)('a' + (first - 0x1F1E6));
            var c2 = (char)('a' + (second - 0x1F1E6));
            var code = string.Concat(c1, c2);
            return AvailableCodes.Contains(code) ? code : null;
        }

        public static IImage? TryGetFlagImageFromFlagEmoji(string? emoji)
            => TryGetFlagImage(TryGetCountryCodeFromFlagEmoji(emoji));

        public static string? TryGetCountryDisplayName(string? countryCode)
        {
            var code = NormalizeCountryCode(countryCode);
            if (string.IsNullOrWhiteSpace(code)) return null;

            if (string.Equals(code, "eu", StringComparison.OrdinalIgnoreCase)) return "European Union";
            if (string.Equals(code, "un", StringComparison.OrdinalIgnoreCase)) return "United Nations";

            try
            {
                var region = new RegionInfo(code.ToUpperInvariant());
                return region.EnglishName;
            }
            catch
            {
                return null;
            }
        }

        private static HashSet<string> LoadAvailableCodes()
        {
            try
            {
                using var stream = AssetLoader.Open(FlagsIndexUri);
                using var doc = JsonDocument.Parse(stream);
                if (!doc.RootElement.TryGetProperty("codes", out var codes) || codes.ValueKind != JsonValueKind.Array)
                {
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                return codes.EnumerateArray()
                    .Select(e => NormalizeCountryCode(e.GetString()))
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static string NormalizeCountryCode(string? countryCode)
        {
            var value = (countryCode ?? string.Empty).Trim().ToLowerInvariant();
            if (value.Length == 2 || value == "eu" || value == "un") return value;
            return string.Empty;
        }
    }
}
