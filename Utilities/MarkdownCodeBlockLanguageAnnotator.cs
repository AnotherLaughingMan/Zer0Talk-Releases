using System;
using System.Collections.Generic;
using System.Text;

namespace Zer0Talk.Utilities;

internal static class MarkdownCodeBlockLanguageAnnotator
{
    private static readonly HashSet<string> GenericLanguageHints = new(StringComparer.OrdinalIgnoreCase)
    {
        string.Empty,
        "text",
        "plain",
        "plaintext",
        "code",
        "language",
        "lang",
        "auto"
    };

    public static string Annotate(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return markdown;
        }

        if (markdown.IndexOf("```", StringComparison.Ordinal) == -1 && markdown.IndexOf("~~~", StringComparison.Ordinal) == -1)
        {
            return markdown;
        }

        var sb = new StringBuilder(markdown.Length + 64);
        var index = 0;
        while (index < markdown.Length)
        {
            var (line, newline, nextIndex) = ReadLine(markdown, index);
            index = nextIndex;

            if (TryParseFence(line, out var indent, out var fence, out var remainder))
            {
                var fenceChar = fence[0];
                var fenceLength = fence.Length;
                var langToken = remainder.Trim();

                var codeBuilder = new StringBuilder();
                var closingLine = string.Empty;
                var closingNewline = string.Empty;
                var closed = false;

                while (index < markdown.Length)
                {
                    var (innerLine, innerNewline, innerNext) = ReadLine(markdown, index);
                    index = innerNext;

                    if (IsClosingFence(innerLine, fenceChar, fenceLength))
                    {
                        closed = true;
                        closingLine = innerLine;
                        closingNewline = innerNewline;
                        break;
                    }

                    codeBuilder.Append(innerLine);
                    codeBuilder.Append(innerNewline);
                }

                var codeText = codeBuilder.ToString();
                var detected = string.Empty;

                if (NeedsDetection(langToken) && !string.IsNullOrWhiteSpace(codeText))
                {
                    detected = CodeLanguageDetector.Detect(codeText) ?? string.Empty;
                }

                var finalLang = NeedsDetection(langToken) ? detected : langToken;

                sb.Append(indent);
                sb.Append(fence);
                if (!string.IsNullOrEmpty(finalLang))
                {
                    sb.Append(finalLang);
                }
                sb.Append(newline);

                sb.Append(codeBuilder.ToString());

                if (closed)
                {
                    sb.Append(closingLine);
                    sb.Append(closingNewline);
                }

                continue;
            }

            sb.Append(line);
            sb.Append(newline);
        }

        return sb.ToString();
    }

    private static bool NeedsDetection(string langToken)
    {
        return string.IsNullOrWhiteSpace(langToken) || GenericLanguageHints.Contains(langToken.Trim());
    }

    private static (string line, string newline, int nextIndex) ReadLine(string text, int index)
    {
        if (index >= text.Length)
        {
            return (string.Empty, string.Empty, text.Length);
        }

        var lineEnd = index;
        while (lineEnd < text.Length && text[lineEnd] != '\n' && text[lineEnd] != '\r')
        {
            lineEnd++;
        }

        var line = text.Substring(index, lineEnd - index);
        var newline = string.Empty;

        if (lineEnd < text.Length)
        {
            if (text[lineEnd] == '\r' && lineEnd + 1 < text.Length && text[lineEnd + 1] == '\n')
            {
                newline = "\r\n";
                lineEnd += 2;
            }
            else
            {
                newline = text[lineEnd].ToString();
                lineEnd++;
            }
        }

        return (line, newline, lineEnd);
    }

    private static bool TryParseFence(string line, out string indent, out string fence, out string remainder)
    {
        indent = string.Empty;
        fence = string.Empty;
        remainder = string.Empty;

        var index = 0;
        while (index < line.Length && (line[index] == ' ' || line[index] == '\t'))
        {
            index++;
        }

        indent = line.Substring(0, index);
        if (index >= line.Length)
        {
            return false;
        }

        var fenceChar = line[index];
        if (fenceChar != '`' && fenceChar != '~')
        {
            return false;
        }

        var start = index;
        while (index < line.Length && line[index] == fenceChar)
        {
            index++;
        }

        var length = index - start;
        if (length < 3)
        {
            return false;
        }

        fence = line.Substring(start, length);
        remainder = line.Substring(index);
        return true;
    }

    private static bool IsClosingFence(string line, char fenceChar, int fenceLength)
    {
        if (!TryParseFence(line, out _, out var fence, out var remainder))
        {
            return false;
        }

        if (fence.Length < fenceLength)
        {
            return false;
        }

        if (fence[0] != fenceChar)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(remainder);
    }
}
