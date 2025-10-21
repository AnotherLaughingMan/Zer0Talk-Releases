using System;
using Avalonia;
using Avalonia.Controls;
using Zer0Talk.Controls.Markdown;

namespace QuoteRenderTest;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp().Start(AppMain, args);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FATAL ERROR: {ex.Message}\n{ex.StackTrace}");
        }
    }

    static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    static void AppMain(Application app, string[] args)
    {
        // Test actual rendering
        var testCases = new[]
        {
            ("Simple paragraph", "Hello **world**"),
            ("Simple quote", "> This is a quote"),
            ("Multi-line quote", "> Line 1\n> Line 2"),
            ("Quote with bold", "> Quote with **bold** text"),
            ("Nested quote", "> Outer\n> > Nested"),
            ("Mixed content", "Normal text\n> Quote\nMore text"),
        };

        Console.WriteLine("=== AVALONIA CONTROL RENDERING TEST ===\n");

        var renderer = new MarkdownRenderer();
        var parser = new MarkdownParser();

        foreach (var (name, markdown) in testCases)
        {
            Console.WriteLine($"TEST: {name}");
            Console.WriteLine($"Input: {markdown.Replace("\n", "\\n")}");

            try
            {
                var doc = parser.Parse(markdown);
                if (doc == null)
                {
                    Console.WriteLine("  ERROR: Parser returned null");
                    continue;
                }

                Console.WriteLine($"  Parsed: {doc.Blocks.Count} blocks");
                
                var control = renderer.Render(doc);
                Console.WriteLine($"  Rendered: {control.GetType().Name}");
                
                if (control is Border border)
                {
                    Console.WriteLine($"    Border: BorderThickness={border.BorderThickness}");
                    if (border.Child is StackPanel stack)
                    {
                        Console.WriteLine($"    StackPanel: {stack.Children.Count} children");
                    }
                }
                else if (control is TextBlock tb)
                {
                    Console.WriteLine($"    TextBlock: InlineCount={tb.Inlines?.Count ?? 0}");
                }
                else if (control is StackPanel sp)
                {
                    Console.WriteLine($"    StackPanel: {sp.Children.Count} children");
                    foreach (var child in sp.Children)
                    {
                        Console.WriteLine($"      - {child.GetType().Name}");
                    }
                }

                Console.WriteLine("  ✓ SUCCESS");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ FAILED: {ex.Message}");
            }

            Console.WriteLine();
        }

        Console.WriteLine("Press Enter to exit...");
        Console.ReadLine();
    }
}

class App : Application
{
}
