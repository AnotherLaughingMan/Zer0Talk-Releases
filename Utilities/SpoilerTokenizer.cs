using System;
using System.Collections.Generic;

namespace Zer0Talk.Utilities
{
    public readonly record struct SpoilerToken(string Text, bool IsSpoiler);

    public static class SpoilerTokenizer
    {
        private const string Delimiter = "||";

        public static bool ContainsValidSpoiler(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            var start = text.IndexOf(Delimiter, StringComparison.Ordinal);
            if (start < 0) return false;

            var end = text.IndexOf(Delimiter, start + Delimiter.Length, StringComparison.Ordinal);
            return end >= 0;
        }

        public static IReadOnlyList<SpoilerToken> Tokenize(string? text)
        {
            var tokens = new List<SpoilerToken>();
            if (string.IsNullOrEmpty(text))
            {
                return tokens;
            }

            var currentPos = 0;
            while (currentPos < text.Length)
            {
                var spoilerStart = text.IndexOf(Delimiter, currentPos, StringComparison.Ordinal);
                if (spoilerStart == -1)
                {
                    var trailing = text.Substring(currentPos);
                    if (!string.IsNullOrEmpty(trailing))
                    {
                        tokens.Add(new SpoilerToken(trailing, false));
                    }

                    break;
                }

                var spoilerEnd = text.IndexOf(Delimiter, spoilerStart + Delimiter.Length, StringComparison.Ordinal);
                if (spoilerEnd == -1)
                {
                    var unmatched = text.Substring(currentPos);
                    if (!string.IsNullOrEmpty(unmatched))
                    {
                        tokens.Add(new SpoilerToken(unmatched, false));
                    }

                    break;
                }

                if (spoilerStart > currentPos)
                {
                    tokens.Add(new SpoilerToken(text.Substring(currentPos, spoilerStart - currentPos), false));
                }

                var spoilerText = text.Substring(
                    spoilerStart + Delimiter.Length,
                    spoilerEnd - spoilerStart - Delimiter.Length);
                tokens.Add(new SpoilerToken(spoilerText, true));

                currentPos = spoilerEnd + Delimiter.Length;
            }

            return tokens;
        }
    }
}
