using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ZTalk.Utilities;

internal static class CodeLanguageDetector
{
    private delegate int ScoreEvaluator(string code);

    private const int MaxSampleLength = 8000;
    private const int MaxJsonParseLength = 4000;

    private static readonly IReadOnlyList<(string Language, ScoreEvaluator Evaluator)> Heuristics = new List<(string, ScoreEvaluator)>
    {
        ("json", ScoreJson),
        ("powershell", ScorePowerShell),
        ("csharp", ScoreCSharp),
        ("java", ScoreJava),
        ("cpp", ScoreCpp),
        ("rust", ScoreRust),
        ("python", ScorePython)
    };

    private static readonly Regex PythonDefPattern = new(@"(?m)^\s*(def|class)\s+\w+", RegexOptions.Compiled);
    private static readonly Regex PythonImportPattern = new(@"(?m)^\s*(from\s+\w+\s+)?import\s+", RegexOptions.Compiled);
    private static readonly Regex PythonCommentPattern = new(@"(?m)^\s*#", RegexOptions.Compiled);
    private static readonly Regex PythonLineEndingColonPattern = new(@"(?m):\s*$", RegexOptions.Compiled);
    private static readonly Regex PowerShellFunctionPattern = new(@"(?m)^\s*function\s+\w+-\w+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PowerShellCmdletPattern = new(@"(?m)\b(Get|Set|New|Test|Start|Stop|Write|Invoke|Out|Select)-\w+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RustFnPattern = new(@"(?m)^\s*fn\s+\w+", RegexOptions.Compiled);
    private static readonly Regex RustMacroPattern = new(@"\w+!\s*\(", RegexOptions.Compiled);
    private static readonly Regex JavaSignaturePattern = new(@"(?m)^\s*public\s+(final\s+)?class\s+\w+", RegexOptions.Compiled);
    private static readonly Regex JavaMainPattern = new(@"public\s+static\s+void\s+main\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CppIncludePattern = new(@"(?m)^\s*#include\s+", RegexOptions.Compiled);
    private static readonly Regex CppMainPattern = new(@"(?m)^\s*(int|auto)\s+main\s*\(", RegexOptions.Compiled);

    public static string? Detect(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var trimmed = code.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        var sample = trimmed.Length > MaxSampleLength ? trimmed.Substring(0, MaxSampleLength) : trimmed;
        var jsonSample = trimmed.Length <= MaxJsonParseLength ? trimmed : sample;

        string? bestLanguage = null;
        var bestScore = 0;

        foreach (var (language, evaluator) in Heuristics)
        {
            var source = language == "json" ? jsonSample : sample;
            var score = SafeScore(evaluator, source);
            if (score > bestScore)
            {
                bestScore = score;
                bestLanguage = language;
            }
        }

        return bestScore >= 4 ? bestLanguage : null;
    }

    private static int SafeScore(ScoreEvaluator evaluator, string code)
    {
        try
        {
            return evaluator(code);
        }
        catch
        {
            return 0;
        }
    }

    private static int ScoreJson(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return 0;
        }

        if (code.Length > MaxJsonParseLength)
        {
            return 0;
        }

        var trimmed = code.Trim();
        if (trimmed.Length == 0)
        {
            return 0;
        }

        var first = trimmed[0];
        if (first != '{' && first != '[')
        {
            return 0;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            return 10;
        }
        catch
        {
            return 0;
        }
    }

    private static int ScorePowerShell(string code)
    {
        var score = 0;
        if (code.Contains("$PSVersionTable", StringComparison.OrdinalIgnoreCase)) score += 4;
        if (PowerShellFunctionPattern.IsMatch(code)) score += 4;
        if (PowerShellCmdletPattern.IsMatch(code)) score += 3;
        if (code.Contains("Write-Host", StringComparison.OrdinalIgnoreCase)) score += 2;
        if (code.Contains("Param(", StringComparison.OrdinalIgnoreCase)) score += 2;
        if (code.Contains("<#", StringComparison.Ordinal)) score += 1;
        var dollarCount = CountOccurrences(code, '$');
        if (dollarCount >= 3) score += 2;
        if (dollarCount >= 6) score += 1;
        return score;
    }

    private static int ScoreCSharp(string code)
    {
        var score = 0;
        if (code.Contains("namespace ", StringComparison.Ordinal)) score += 4;
        if (code.Contains("using System", StringComparison.Ordinal)) score += 4;
        if (code.Contains("public class", StringComparison.Ordinal)) score += 3;
        if (code.Contains("public record", StringComparison.Ordinal)) score += 3;
        if (code.Contains(" get;", StringComparison.Ordinal) || code.Contains(" set;", StringComparison.Ordinal)) score += 2;
        if (code.Contains("Console.WriteLine", StringComparison.Ordinal)) score += 2;
        if (code.Contains("async Task", StringComparison.Ordinal)) score += 2;
        if (code.Contains("=>", StringComparison.Ordinal)) score += 1;
        if (code.Contains(" var ", StringComparison.Ordinal)) score += 1;
        if (code.Contains("List<", StringComparison.Ordinal)) score += 1;
        return score;
    }

    private static int ScoreJava(string code)
    {
        var score = 0;
        if (code.Contains("package ", StringComparison.Ordinal)) score += 3;
        if (code.Contains("import java.", StringComparison.Ordinal)) score += 3;
        if (JavaSignaturePattern.IsMatch(code)) score += 4;
        if (JavaMainPattern.IsMatch(code)) score += 4;
        if (code.Contains("System.out.println", StringComparison.Ordinal)) score += 3;
        if (code.Contains(" new ArrayList", StringComparison.Ordinal) || code.Contains(" new HashMap", StringComparison.Ordinal)) score += 2;
        if (code.Contains("implements ", StringComparison.Ordinal) || code.Contains("extends ", StringComparison.Ordinal)) score += 1;
        return score;
    }

    private static int ScoreCpp(string code)
    {
        var score = 0;
        if (CppIncludePattern.IsMatch(code)) score += 4;
        if (CppMainPattern.IsMatch(code)) score += 3;
        if (code.Contains("std::", StringComparison.Ordinal)) score += 4;
        if (code.Contains("->", StringComparison.Ordinal)) score += 1;
        if (code.Contains("::", StringComparison.Ordinal)) score += 1;
        if (code.Contains("template<", StringComparison.Ordinal)) score += 2;
        if (code.Contains("cout <<", StringComparison.Ordinal)) score += 2;
        return score;
    }

    private static int ScoreRust(string code)
    {
        var score = 0;
        if (RustFnPattern.IsMatch(code)) score += 3;
        if (code.Contains("let mut", StringComparison.Ordinal)) score += 4;
        if (code.Contains("pub struct", StringComparison.Ordinal) || code.Contains("pub enum", StringComparison.Ordinal)) score += 3;
        if (code.Contains("impl ", StringComparison.Ordinal)) score += 2;
        if (code.Contains("match ", StringComparison.Ordinal)) score += 2;
        if (code.Contains("::", StringComparison.Ordinal)) score += 1;
        if (RustMacroPattern.IsMatch(code)) score += 2;
        if (code.Contains("println!", StringComparison.Ordinal)) score += 3;
        return score;
    }

    private static int ScorePython(string code)
    {
        var score = 0;
        if (PythonDefPattern.IsMatch(code)) score += 4;
        if (PythonImportPattern.IsMatch(code)) score += 3;
        if (code.Contains("self", StringComparison.Ordinal)) score += 1;
        if (code.Contains("print(", StringComparison.Ordinal)) score += 1;
        if (PythonCommentPattern.IsMatch(code)) score += 1;
        if (PythonLineEndingColonPattern.IsMatch(code)) score += 1;
        return score;
    }

    private static int CountOccurrences(string text, char target)
    {
        var count = 0;
        foreach (var c in text)
        {
            if (c == target)
            {
                count++;
            }
        }

        return count;
    }
}
