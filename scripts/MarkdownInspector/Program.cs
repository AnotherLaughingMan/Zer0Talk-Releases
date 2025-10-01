using System;
using Markdig;

var pipeline = new MarkdownPipelineBuilder()
	.UseAdvancedExtensions()
	.Build();

string[] samples =
[
	"||spoiler||",
	"""
```
line1
line2
```
""",
	"""
Before

```
code
```
""",
];

foreach (var sample in samples)
{
	Console.WriteLine("=== SAMPLE ===");
	Console.WriteLine(sample.Replace("\n", "\\n"));
	Console.WriteLine("--- HTML ---");
	Console.WriteLine(Markdown.ToHtml(sample, pipeline));
}
