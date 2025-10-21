using System;
using Zer0Talk.Controls.Markdown;

namespace Zer0Talk.Tests;

/// <summary>
/// Simple console test for the markdown parser (RenderModel only).
/// Run with: dotnet run --project scripts/MarkdownTest/MarkdownTest.csproj
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        var parser = new MarkdownParser();

        Console.WriteLine("=== Markdown Parser Test (RenderModel) ===\n");

        // Test 1: Simple paragraph
        TestMarkdown(parser, "Hello **bold** and *italic* text!");

        // Test 2: Heading
        TestMarkdown(parser, "# Heading Level 1\n## Heading Level 2");

        // Test 3: Code
        TestMarkdown(parser, "Inline `code` and block:\n```csharp\nvar x = 42;\n```");

        // Test 4: List
        TestMarkdown(parser, "- Item 1\n- Item 2\n  - Nested");

        // Test 5: Blockquote
        TestMarkdown(parser, "> This is a quote\n> Multiple lines");

        // Test 6: Links
        TestMarkdown(parser, "[Link text](https://example.com)");

        // Test 7: Mixed content
        TestMarkdown(parser, "**Bold** text with `code` and a [link](https://test.com)");

        // Test 8: Strikethrough
        TestMarkdown(parser, "This is ~~strikethrough~~ text");

        // Test 9: Ordered list
        TestMarkdown(parser, "1. First\n2. Second\n3. Third");

        // Test 10: Extreme length (crash test)
        Console.WriteLine("\n=== STRESS TESTS (Crash Prevention) ===\n");
        TestMarkdown(parser, new string('*', 1000)); // 1000 asterisks
        
        // Test 11: Malformed markdown
        TestMarkdown(parser, "**Bold without closing\n`Code without closing\n[Link (broken");
        
        // Test 12: Deeply nested
        TestMarkdown(parser, "> Quote with **bold *and `code` mixed*");
        
        // Test 13: Empty/null handling
        TestMarkdown(parser, "");
        TestMarkdown(parser, "   ");
        
        // Test 14: Special characters
        TestMarkdown(parser, "Test with <html> and ||spoiler|| and [link](javascript:alert)");
        
        // Test 15: Quotes (CRITICAL - was breaking)
        Console.WriteLine("\n=== QUOTE TESTS (Critical Fix) ===\n");
        TestMarkdown(parser, "> Simple quote");
        TestMarkdown(parser, "> Quote line 1\n> Quote line 2");
        TestMarkdown(parser, "> Quote with **bold** text");
        TestMarkdown(parser, "> Quote\n> > Nested quote");

        Console.WriteLine("\n=== All tests completed (including stress tests) ===");
    }

    static void TestMarkdown(MarkdownParser parser, string markdown)
    {
        Console.WriteLine($"Input: {markdown.Replace("\n", "\\n")}");
        
        var document = parser.Parse(markdown);
        
        if (document == null)
        {
            Console.WriteLine("  Result: PARSE FAILED");
        }
        else
        {
            Console.WriteLine($"  Result: SUCCESS - {document.Blocks.Count} block(s)");
            foreach (var block in document.Blocks)
            {
                Console.WriteLine($"    - {block.ToString()}");
            }
        }
        
        Console.WriteLine();
    }
}
