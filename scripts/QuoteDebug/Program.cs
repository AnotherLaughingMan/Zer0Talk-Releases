using System;
using ZTalk.Controls.Markdown;
using ZTalk.Utilities;

var testMarkdown = "> This is a quote\n> with multiple lines";

Console.WriteLine("=== QUOTE RENDERING DEBUG ===\n");
Console.WriteLine($"Input markdown:\n{testMarkdown}\n");

// Annotate (like Message.Content does)
var annotated = MarkdownCodeBlockLanguageAnnotator.Annotate(testMarkdown);
Console.WriteLine($"After annotation:\n{annotated}\n");

// Parse
var parser = new MarkdownParser();
var doc = parser.Parse(annotated);

if (doc == null)
{
    Console.WriteLine("ERROR: Parser returned null!");
    return 1;
}

Console.WriteLine($"Parsed document: {doc.Blocks.Count} blocks");
foreach (var block in doc.Blocks)
{
    Console.WriteLine($"  Block type: {block}");
}

// Check if UseMarkdig binding would work
Console.WriteLine("\n=== SIMULATION ===");
Console.WriteLine("If UseMarkdig = true:");
Console.WriteLine("  ZTalkMarkdownViewer.Markdown = RenderedContent");
Console.WriteLine("  -> Parser.Parse(text)");
Console.WriteLine("  -> Renderer.Render(document)");
Console.WriteLine("  -> Should show styled quote with border");

Console.WriteLine("\nIf UseMarkdig = false:");
Console.WriteLine("  TextBlock.Text = RenderedContent");
Console.WriteLine("  -> Shows raw markdown text");

// Let's check what the logs would show
Console.WriteLine("\n=== CHECK FOR LOGS ===");
var exeDir = AppContext.BaseDirectory;
var errorLogPath = System.IO.Path.Combine(exeDir, "logs", "markdown-errors.log");
var traceLogPath = System.IO.Path.Combine(exeDir, "logs", "markdown-trace.log");

Console.WriteLine($"Error log location: {errorLogPath}");
if (System.IO.File.Exists(errorLogPath))
{
    Console.WriteLine("ERROR LOG EXISTS! Contents:");
    Console.WriteLine(System.IO.File.ReadAllText(errorLogPath));
}
else
{
    Console.WriteLine("No error log found - rendering is working!");
}

Console.WriteLine($"\nTrace log location: {traceLogPath}");
if (System.IO.File.Exists(traceLogPath))
{
    Console.WriteLine("TRACE LOG EXISTS! Last 20 lines:");
    var lines = System.IO.File.ReadAllLines(traceLogPath);
    var lastLines = lines.Skip(Math.Max(0, lines.Length - 20)).ToArray();
    Console.WriteLine(string.Join("\n", lastLines));
}
else
{
    Console.WriteLine("No trace log found.");
}

return 0;
