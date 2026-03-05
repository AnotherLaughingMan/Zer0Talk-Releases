using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Platform;

namespace Zer0Talk.Utilities
{
    public static class ReactionEmojiCatalogLoader
    {
        private static readonly Uri EmojiCatalogUri = new("avares://Zer0Talk/Resources/Data/emoji-test.txt");
        private static readonly Lazy<IReadOnlyList<ReactionEmojiCategory>> CachedCategories =
            new(LoadCategoriesInternal);
        private static readonly Lazy<IReadOnlyDictionary<string, string>> CachedEmojiNames =
            new(LoadEmojiNamesInternal);

        public static IReadOnlyList<ReactionEmojiCategory> LoadCategories()
            => CachedCategories.Value;

        public static string? GetEmojiDisplayName(string? emoji)
        {
            var key = (emoji ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key)) return null;

            return CachedEmojiNames.Value.TryGetValue(key, out var name)
                ? name
                : null;
        }

        private static IReadOnlyList<ReactionEmojiCategory> LoadCategoriesInternal()
        {
            try
            {
                using var stream = AssetLoader.Open(EmojiCatalogUri);
                using var reader = new StreamReader(stream);
                var text = reader.ReadToEnd();
                var parsed = ParseEmojiTestText(text);
                if (parsed.Count > 0)
                {
                    return parsed;
                }
            }
            catch { }

            // Fallback if asset load ever fails.
            return new List<ReactionEmojiCategory>
            {
                new()
                {
                    IconEmoji = "\ud83d\ude80",
                    DisplayName = "Reactions",
                    Emojis = new List<string> { "\ud83d\udc4d", "\u2764\ufe0f", "\ud83d\ude02", "\ud83d\ude2e", "\ud83d\ude22", "\ud83d\ude21", "\ud83d\udd25" }
                }
            };
        }

        private static IReadOnlyDictionary<string, string> LoadEmojiNamesInternal()
        {
            try
            {
                using var stream = AssetLoader.Open(EmojiCatalogUri);
                using var reader = new StreamReader(stream);
                var text = reader.ReadToEnd();
                return ParseEmojiNames(text);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        public static IReadOnlyList<ReactionEmojiCategory> ParseEmojiTestText(string text)
        {
            var categories = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var order = new List<string>();
            var currentGroup = "Reactions";

            foreach (var rawLine in (text ?? string.Empty).Split('\n'))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.StartsWith("# group:", StringComparison.Ordinal))
                {
                    currentGroup = line.Substring("# group:".Length).Trim();
                    if (!categories.ContainsKey(currentGroup))
                    {
                        categories[currentGroup] = new HashSet<string>(StringComparer.Ordinal);
                        order.Add(currentGroup);
                    }
                    continue;
                }

                if (line.StartsWith("#", StringComparison.Ordinal)) continue;

                // Keep only fully-qualified emojis to avoid duplicate emoji variants
                if (!line.Contains("; fully-qualified", StringComparison.Ordinal))
                {
                    continue;
                }

                var hashIndex = line.IndexOf('#');
                if (hashIndex < 0 || hashIndex + 1 >= line.Length) continue;

                var afterHash = line.Substring(hashIndex + 1).Trim();
                if (string.IsNullOrWhiteSpace(afterHash)) continue;

                var firstSpace = afterHash.IndexOf(' ');
                var emoji = firstSpace > 0 ? afterHash.Substring(0, firstSpace).Trim() : afterHash;
                if (string.IsNullOrWhiteSpace(emoji)) continue;

                if (!categories.TryGetValue(currentGroup, out var bucket))
                {
                    bucket = new HashSet<string>(StringComparer.Ordinal);
                    categories[currentGroup] = bucket;
                    order.Add(currentGroup);
                }

                bucket.Add(emoji);
            }

            var result = new List<ReactionEmojiCategory>();
            foreach (var key in order)
            {
                if (!categories.TryGetValue(key, out var emojis) || emojis.Count == 0) continue;
                result.Add(new ReactionEmojiCategory
                {
                    IconEmoji = GetCategoryIcon(key),
                    DisplayName = key,
                    Emojis = emojis.ToList()
                });
            }

            return result;
        }

        private static IReadOnlyDictionary<string, string> ParseEmojiNames(string text)
        {
            var names = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var rawLine in (text ?? string.Empty).Split('\n'))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal)) continue;

                if (!(line.Contains("; fully-qualified", StringComparison.Ordinal) || line.Contains("; minimally-qualified", StringComparison.Ordinal)))
                {
                    continue;
                }

                var hashIndex = line.IndexOf('#');
                if (hashIndex < 0 || hashIndex + 1 >= line.Length) continue;

                var afterHash = line.Substring(hashIndex + 1).Trim();
                if (string.IsNullOrWhiteSpace(afterHash)) continue;

                var firstSpace = afterHash.IndexOf(' ');
                if (firstSpace <= 0) continue;

                var emoji = afterHash.Substring(0, firstSpace).Trim();
                if (string.IsNullOrWhiteSpace(emoji)) continue;

                var remainder = afterHash.Substring(firstSpace + 1).Trim();
                if (string.IsNullOrWhiteSpace(remainder)) continue;

                // Strip version token (`E1.0`, `E14.0`, etc.) to get the human-readable CLDR name.
                var versionSplit = remainder.IndexOf(' ');
                if (versionSplit <= 0 || versionSplit + 1 >= remainder.Length) continue;

                var displayName = remainder.Substring(versionSplit + 1).Trim();
                if (string.IsNullOrWhiteSpace(displayName)) continue;

                names[emoji] = displayName;
            }

            return names;
        }

        private static string GetCategoryIcon(string categoryName)
        {
            var name = (categoryName ?? string.Empty).Trim();
            return name switch
            {
                "Smileys & Emotion" => "\ud83d\ude03",
                "People & Body" => "\ud83d\udc4b",
                "Component" => "\ud83e\uddf1",
                "Animals & Nature" => "\ud83d\udc3e",
                "Food & Drink" => "\ud83c\udf55",
                "Travel & Places" => "\ud83d\ude97",
                "Activities" => "\u26bd",
                "Objects" => "\ud83d\udca1",
                "Symbols" => "\u2728",
                "Flags" => "\ud83c\udff3\ufe0f",
                "Reactions" => "\ud83d\ude80",
                _ => "\ud83d\ude42",
            };
        }
    }
}
