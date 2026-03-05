using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Zer0Talk.Controls.Markdown;

/// <summary>
/// Custom Avalonia markdown renderer using Markdig for reliable parsing
/// Built specifically for Zer0Talk's P2P messaging needs with security-first design
/// </summary>
public sealed class Zer0TalkMarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline;

    public Zer0TalkMarkdownRenderer()
    {
        try
        {
            // Configure Markdig pipeline with P2P-safe features (minimal safe set)
            _pipeline = new MarkdownPipelineBuilder()
                .UseEmphasisExtras() // Strikethrough, superscript, etc.
                .UsePipeTables() // Table support
                .UseAutoLinks() // But we'll handle them safely
                .DisableHtml() // Security: No HTML for P2P safety
                .Build();
        }
        catch (Exception)
        {
            // Fallback to basic pipeline if advanced features fail
            try
            {
                _pipeline = new MarkdownPipelineBuilder()
                    .DisableHtml()
                    .Build();
            }
            catch
            {
                // Ultimate fallback - use default pipeline
                _pipeline = new MarkdownPipelineBuilder().Build();
            }
        }
    }

    /// <summary>
    /// Render markdown text to Avalonia UI controls
    /// </summary>
    public Control RenderMarkdown(string markdownText)
    {
        if (string.IsNullOrWhiteSpace(markdownText))
        {
            return new TextBlock { Text = string.Empty };
        }

        try
        {
            // Use a simplified but properly sizing approach
            // Parse markdown with Markdig 
            var document = Markdig.Markdown.Parse(markdownText, _pipeline);
            
            // If it's just a single paragraph, use a TextBlock for simplicity
            if (document.Count == 1 && document[0] is ParagraphBlock paragraph)
            {
                var textBlock = new TextBlock 
                { 
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                var inlines = new List<Avalonia.Controls.Documents.Inline>();
                if (paragraph.Inline != null)
                {
                    foreach (var inline in paragraph.Inline)
                    {
                        try
                        {
                            RenderInline(inline, inlines);
                        }
                        catch
                        {
                            // Add fallback text for problematic inlines
                            inlines.Add(new Run { Text = GetInlineText(inline as ContainerInline) });
                        }
                    }
                }

                foreach (var inline in inlines)
                {
                    try
                    {
                        textBlock.Inlines!.Add(inline);
                    }
                    catch
                    {
                        // Skip problematic inlines
                    }
                }

                // Fallback if no inlines added
                if (textBlock.Inlines!.Count == 0)
                {
                    textBlock.Text = markdownText;
                }

                return textBlock;
            }
            else
            {
                // For complex markdown, use a StackPanel
                var container = new StackPanel 
                { 
                    Spacing = 8,
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                
                foreach (var block in document)
                {
                    var control = RenderBlockSimple(block);
                    if (control != null)
                    {
                        container.Children.Add(control);
                    }
                }

                return container.Children.Count == 1 ? container.Children[0] : container;
            }
        }
        catch (Exception)
        {
            // Fallback to plain text on parsing errors
            return new TextBlock 
            { 
                Text = markdownText,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Orange // Indicate parsing issue
            };
        }
    }

    private Control? RenderBlock(Block block)
    {
        return block switch
        {
            ParagraphBlock paragraph => RenderParagraph(paragraph),
            HeadingBlock heading => RenderHeading(heading),
            QuoteBlock quote => RenderQuote(quote),
            CodeBlock code => RenderCodeBlock(code),
            ListBlock list => RenderList(list),
            ThematicBreakBlock => RenderHorizontalRule(),
            _ => new TextBlock { Text = $"[Unsupported block: {block.GetType().Name}]", FontStyle = FontStyle.Italic }
        };
    }

    private Control RenderParagraph(ParagraphBlock paragraph)
    {
        try
        {
            var textBlock = new TextBlock 
            { 
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 1.4
            };

            var inlines = new List<Avalonia.Controls.Documents.Inline>();
            foreach (var inline in paragraph.Inline!)
            {
                try
                {
                    RenderInline(inline, inlines);
                }
                catch
                {
                    // If inline rendering fails, add plain text
                    inlines.Add(new Run { Text = "[inline error]" });
                }
            }

            // Add all inlines to the TextBlock - ensure none are already parented
            foreach (var avaloniaInline in inlines)
            {
                try
                {
                    if (avaloniaInline.Parent == null)
                    {
                        textBlock.Inlines!.Add(avaloniaInline);
                    }
                    else
                    {
                        // Clone if already parented
                        if (avaloniaInline is Run run)
                        {
                            textBlock.Inlines!.Add(new Run 
                            { 
                                Text = run.Text,
                                FontWeight = run.FontWeight,
                                FontStyle = run.FontStyle,
                                TextDecorations = run.TextDecorations,
                                Foreground = run.Foreground,
                                Background = run.Background,
                                FontFamily = run.FontFamily
                            });
                        }
                    }
                }
                catch
                {
                    // If adding fails, skip this inline
                }
            }

            return textBlock;
        }
        catch
        {
            // Ultimate fallback
            return new TextBlock 
            { 
                Text = "[paragraph error]",
                TextWrapping = TextWrapping.Wrap 
            };
        }
    }

    private Control RenderHeading(HeadingBlock heading)
    {
        try
        {
            var textBlock = new TextBlock
            {
                FontWeight = FontWeight.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 16, 0, 8)
            };

            // Set font size based on heading level
            textBlock.FontSize = heading.Level switch
            {
                1 => 24,
                2 => 20,
                3 => 18,
                4 => 16,
                5 => 14,
                _ => 12
            };

            var inlines = new List<Avalonia.Controls.Documents.Inline>();
            foreach (var inline in heading.Inline!)
            {
                try
                {
                    RenderInline(inline, inlines);
                }
                catch
                {
                    inlines.Add(new Run { Text = "[inline error]" });
                }
            }

            // Add all inlines to the TextBlock - ensure none are already parented
            foreach (var avaloniaInline in inlines)
            {
                try
                {
                    if (avaloniaInline.Parent == null)
                    {
                        textBlock.Inlines!.Add(avaloniaInline);
                    }
                    else
                    {
                        // Clone if already parented
                        if (avaloniaInline is Run run)
                        {
                            textBlock.Inlines!.Add(new Run 
                            { 
                                Text = run.Text,
                                FontWeight = run.FontWeight,
                                FontStyle = run.FontStyle,
                                TextDecorations = run.TextDecorations,
                                Foreground = run.Foreground,
                                Background = run.Background,
                                FontFamily = run.FontFamily
                            });
                        }
                    }
                }
                catch
                {
                    // If adding fails, skip this inline
                }
            }

            return textBlock;
        }
        catch
        {
            // Ultimate fallback
            return new TextBlock 
            { 
                Text = "[heading error]",
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeight.Bold
            };
        }
    }

    private Control RenderQuote(QuoteBlock quote)
    {
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(100, 149, 237)), // Accent color
            BorderThickness = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(12, 8),
            Margin = new Thickness(0, 8),
            Background = new SolidColorBrush(Color.FromArgb(20, 100, 149, 237))
        };

        var container = new StackPanel { Spacing = 4 };
        
        foreach (var block in quote)
        {
            var control = RenderBlock(block);
            if (control != null)
            {
                container.Children.Add(control);
            }
        }

        border.Child = container;
        return border;
    }

    private Control RenderCodeBlock(CodeBlock codeBlock)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(40, 44, 52)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 8)
        };

        var textBlock = new TextBlock
        {
            Text = codeBlock.Lines.ToString(),
            FontFamily = new FontFamily("Consolas, Monaco, 'Courier New', monospace"),
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13
        };

        border.Child = textBlock;
        return border;
    }

    private Control RenderList(ListBlock list)
    {
        var container = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8) };

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is ListItemBlock item)
            {
                var itemContainer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                
                // Add bullet or number
                var bullet = new TextBlock
                {
                    Text = list.IsOrdered ? $"{i + 1}." : "•",
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 0, 0, 0),
                    MinWidth = 20
                };

                var content = new StackPanel { Spacing = 4 };
                foreach (var block in item)
                {
                    var control = RenderBlock(block);
                    if (control != null)
                    {
                        content.Children.Add(control);
                    }
                }

                itemContainer.Children.Add(bullet);
                itemContainer.Children.Add(content);
                container.Children.Add(itemContainer);
            }
        }

        return container;
    }

    private Control RenderHorizontalRule()
    {
        return new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            Margin = new Thickness(0, 16),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private void RenderInline(Markdig.Syntax.Inlines.Inline inline, List<Avalonia.Controls.Documents.Inline> inlines)
    {
        try
        {
            switch (inline)
            {
                case LiteralInline literal:
                    inlines.Add(new Run { Text = literal.Content.ToString() });
                    break;
                    
                case EmphasisInline emphasis:
                    var emphasisRun = CreateEmphasisRun(emphasis);
                    if (emphasisRun != null)
                        inlines.Add(emphasisRun);
                    break;
                    
                case CodeInline code:
                    var codeRun = new Run 
                    { 
                        Text = code.Content,
                        FontFamily = new FontFamily("Consolas, Monaco, 'Courier New', monospace"),
                        Background = new SolidColorBrush(Color.FromArgb(40, 68, 68, 68)),
                        Foreground = new SolidColorBrush(Color.FromRgb(240, 108, 108))
                    };
                    inlines.Add(codeRun);
                    break;
                    
                case LinkInline link:
                    if (link.IsImage)
                    {
                        // P2P Safety: Don't load external images, show placeholder
                        var imageRun = new Run
                        {
                            Text = $"🖼️ [{GetInlineText(link) ?? "Image"}]",
                            Foreground = new SolidColorBrush(Color.FromRgb(138, 43, 226)), // Purple for images
                            FontWeight = FontWeight.Medium,
                            Background = new SolidColorBrush(Color.FromArgb(20, 138, 43, 226))
                        };
                        inlines.Add(imageRun);
                    }
                    else
                    {
                        // P2P Safety: Don't render links as clickable, just show styled text
                        var linkRun = new Run
                        {
                            Text = $"[{GetInlineText(link)}]({link.Url})",
                            Foreground = new SolidColorBrush(Color.FromRgb(100, 149, 237)),
                            TextDecorations = TextDecorations.Underline
                        };
                        inlines.Add(linkRun);
                    }
                    break;

                case AutolinkInline autolink:
                    // P2P Safety: Show URL but don't make it clickable
                    var autolinkRun = new Run
                    {
                        Text = autolink.Url,
                        Foreground = new SolidColorBrush(Color.FromRgb(100, 149, 237)),
                        TextDecorations = TextDecorations.Underline
                    };
                    inlines.Add(autolinkRun);
                    break;

                case HtmlInline html:
                    // Check for spoiler syntax ||text||
                    var htmlText = html.Tag;
                    if (htmlText.StartsWith("||") && htmlText.EndsWith("||") && htmlText.Length > 4)
                    {
                        var spoilerText = htmlText.Substring(2, htmlText.Length - 4);
                        var spoilerRun = new Run
                        {
                            Text = spoilerText,
                            Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                            Foreground = new SolidColorBrush(Color.FromRgb(50, 50, 50)) // Hidden until selected
                        };
                        inlines.Add(spoilerRun);
                    }
                    else
                    {
                        // P2P Safety: Don't render HTML, show as text
                        var htmlRun = new Run
                        {
                            Text = htmlText,
                            Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0)), // Orange to indicate HTML
                            FontFamily = new FontFamily("Consolas, Monaco, 'Courier New', monospace")
                        };
                        inlines.Add(htmlRun);
                    }
                    break;

                case LineBreakInline:
                    inlines.Add(new LineBreak());
                    break;
                    
                default:
                    // Fallback: try to get text content
                    var fallbackText = GetInlineText(inline as ContainerInline);
                    if (!string.IsNullOrEmpty(fallbackText))
                    {
                        inlines.Add(new Run { Text = fallbackText });
                    }
                    break;
            }
        }
        catch
        {
            // If inline rendering fails, add plain text fallback
            try
            {
                var fallbackText = GetInlineText(inline as ContainerInline);
                if (!string.IsNullOrEmpty(fallbackText))
                {
                    inlines.Add(new Run { Text = fallbackText });
                }
            }
            catch
            {
                // Ultimate fallback
                inlines.Add(new Run { Text = "[rendering error]" });
            }
        }
    }

    private Avalonia.Controls.Documents.Inline? CreateEmphasisRun(EmphasisInline emphasis)
    {
        try
        {
            // For complex nested emphasis, collect all child inlines and apply formatting
            var childInlines = new List<Avalonia.Controls.Documents.Inline>();
            foreach (var child in emphasis)
            {
                RenderInline(child, childInlines);
            }

            // If we have complex nested content, create a span
            if (childInlines.Count > 1 || (childInlines.Count == 1 && !(childInlines[0] is Run)))
            {
                try
                {
                    var span = new Span();
                    foreach (var childInline in childInlines)
                    {
                        // Ensure each inline isn't already parented
                        if (childInline.Parent == null)
                        {
                            span.Inlines!.Add(childInline);
                        }
                        else
                        {
                            // Clone the inline if it's already parented
                            if (childInline is Run existingRun)
                            {
                                span.Inlines!.Add(new Run { Text = existingRun.Text });
                            }
                        }
                    }

                    // Apply emphasis formatting to the span
                    ApplyEmphasisFormatting(span, emphasis);
                    return span;
                }
                catch
                {
                    // Fallback to simple text run
                    var fallbackText = GetInlineText(emphasis);
                    if (!string.IsNullOrEmpty(fallbackText))
                    {
                        var fallbackRun = new Run { Text = fallbackText };
                        ApplyEmphasisFormatting(fallbackRun, emphasis);
                        return fallbackRun;
                    }
                    return null;
                }
            }

            // Simple case: single text run
            var text = GetInlineText(emphasis);
            if (string.IsNullOrEmpty(text))
                return null;

            var run = new Run { Text = text };
            ApplyEmphasisFormatting(run, emphasis);
            return run;
        }
        catch
        {
            // Ultimate fallback
            return new Run { Text = "[emphasis error]" };
        }
    }

    private void ApplyEmphasisFormatting(Avalonia.Controls.Documents.Inline inline, EmphasisInline emphasis)
    {
        // Apply formatting based on delimiter
        if (emphasis.DelimiterChar == '*')
        {
            if (emphasis.DelimiterCount == 2) // **bold**
            {
                inline.FontWeight = FontWeight.Bold;
            }
            else if (emphasis.DelimiterCount == 1) // *italic*
            {
                inline.FontStyle = FontStyle.Italic;
            }
        }
        else if (emphasis.DelimiterChar == '~' && emphasis.DelimiterCount == 2) // ~~strikethrough~~
        {
            inline.TextDecorations = TextDecorations.Strikethrough;
        }
        else if (emphasis.DelimiterChar == '_')
        {
            if (emphasis.DelimiterCount == 2) // __bold__
            {
                inline.FontWeight = FontWeight.Bold;
            }
            else if (emphasis.DelimiterCount == 1) // _italic_
            {
                inline.FontStyle = FontStyle.Italic;
            }
        }
        else if (emphasis.DelimiterChar == '+' && emphasis.DelimiterCount == 2) // ++underline++
        {
            inline.TextDecorations = TextDecorations.Underline;
        }
    }

    private string GetInlineText(ContainerInline? container)
    {
        if (container == null) return string.Empty;

        var textParts = new List<string>();
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    textParts.Add(literal.Content.ToString());
                    break;
                case ContainerInline nestedContainer:
                    textParts.Add(GetInlineText(nestedContainer));
                    break;
                case CodeInline code:
                    textParts.Add(code.Content);
                    break;
                case LineBreakInline:
                    textParts.Add("\n");
                    break;
                default:
                    // Handle other inline types
                    switch (inline)
                    {
                        case EmphasisInline emphasis:
                            textParts.Add(GetInlineText(emphasis));
                            break;
                        case LinkInline link:
                            var linkText = GetInlineText(link);
                            if (!string.IsNullOrEmpty(link.Url))
                            {
                                textParts.Add($"{linkText} ({link.Url})");
                            }
                            else
                            {
                                textParts.Add(linkText);
                            }
                            break;
                        case AutolinkInline autoLink:
                            textParts.Add(autoLink.Url ?? string.Empty);
                            break;
                        default:
                            // Try to get any text content from unknown inline types
                            if (inline is ContainerInline unknownContainer)
                            {
                                textParts.Add(GetInlineText(unknownContainer));
                            }
                            break;
                    }
                    break;
            }
        }
        return string.Join("", textParts);
    }

    /// <summary>
    /// Convert blocks to inlines for a single TextBlock approach (avoids visual parent conflicts)
    /// </summary>
    private void ConvertBlockToInlines(Block block, List<Avalonia.Controls.Documents.Inline> inlines)
    {
        try
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    if (paragraph.Inline != null)
                    {
                        foreach (var inline in paragraph.Inline)
                        {
                            RenderInline(inline, inlines);
                        }
                    }
                    break;
                    
                case HeadingBlock heading:
                    var headingRun = new Run 
                    { 
                        FontWeight = FontWeight.Bold,
                        FontSize = heading.Level switch
                        {
                            1 => 24,
                            2 => 20,
                            3 => 18,
                            4 => 16,
                            5 => 14,
                            _ => 12
                        }
                    };
                    
                    // Get the text content properly
                    if (heading.Inline != null)
                    {
                        var headingText = GetInlineText(heading.Inline);
                        headingRun.Text = headingText;
                    }
                    inlines.Add(headingRun);
                    break;
                    
                case CodeBlock code:
                    var codeText = string.Empty;
                    if (code.Lines.Lines != null)
                    {
                        codeText = string.Join("\n", code.Lines.Lines.Select(line => line.Slice.ToString()));
                    }
                    
                    var codeRun = new Run
                    {
                        Text = codeText,
                        FontFamily = new FontFamily("Consolas, Monaco, 'Courier New', monospace"),
                        Background = new SolidColorBrush(Color.FromArgb(60, 40, 44, 52)),
                        Foreground = new SolidColorBrush(Color.FromRgb(240, 240, 240))
                    };
                    inlines.Add(codeRun);
                    break;
                    
                case QuoteBlock quote:
                    inlines.Add(new Run { Text = "❝ ", Foreground = new SolidColorBrush(Color.FromRgb(100, 149, 237)) });
                    foreach (var childBlock in quote)
                    {
                        ConvertBlockToInlines(childBlock, inlines);
                    }
                    inlines.Add(new Run { Text = " ❞", Foreground = new SolidColorBrush(Color.FromRgb(100, 149, 237)) });
                    break;
                    
                case ListBlock list:
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i] is ListItemBlock item)
                        {
                            var bullet = list.IsOrdered ? $"{i + 1}. " : "• ";
                            inlines.Add(new Run { Text = bullet });
                            
                            foreach (var childBlock in item)
                            {
                                ConvertBlockToInlines(childBlock, inlines);
                            }
                            
                            if (i < list.Count - 1)
                            {
                                inlines.Add(new LineBreak());
                            }
                        }
                    }
                    break;
                    
                case ThematicBreakBlock:
                    inlines.Add(new Run { Text = "───────────", Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)) });
                    break;
                    
                default:
                    // Better fallback: try to extract any text content from the block
                    try
                    {
                        var blockText = block.ToString();
                        if (!string.IsNullOrEmpty(blockText) && blockText != block.GetType().Name)
                        {
                            inlines.Add(new Run { Text = blockText });
                        }
                        else
                        {
                            inlines.Add(new Run { Text = $"[Unsupported: {block.GetType().Name}]", FontStyle = FontStyle.Italic });
                        }
                    }
                    catch
                    {
                        inlines.Add(new Run { Text = "[Block Error]", FontStyle = FontStyle.Italic });
                    }
                    break;
            }
        }
        catch
        {
            // If block conversion fails entirely, add error indicator
            inlines.Add(new Run { Text = "[Render Error]", FontStyle = FontStyle.Italic, Foreground = Brushes.Red });
        }
    }

    /// <summary>
    /// Render blocks as simple, properly sizing controls
    /// </summary>
    private Control? RenderBlockSimple(Block block)
    {
        try
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    var textBlock = new TextBlock 
                    { 
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Top,
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };

                    if (paragraph.Inline != null)
                    {
                        var inlines = new List<Avalonia.Controls.Documents.Inline>();
                        foreach (var inline in paragraph.Inline)
                        {
                            RenderInline(inline, inlines);
                        }

                        foreach (var inline in inlines)
                        {
                            try
                            {
                                textBlock.Inlines!.Add(inline);
                            }
                            catch
                            {
                                // Skip problematic inlines
                            }
                        }

                        if (textBlock.Inlines!.Count == 0)
                        {
                            textBlock.Text = GetInlineText(paragraph.Inline);
                        }
                    }

                    return textBlock;

                case HeadingBlock heading:
                    var headingBlock = new TextBlock
                    {
                        FontWeight = FontWeight.Bold,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Top,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Margin = new Thickness(0, 16, 0, 8),
                        FontSize = heading.Level switch
                        {
                            1 => 24,
                            2 => 20,
                            3 => 18,
                            4 => 16,
                            5 => 14,
                            _ => 12
                        }
                    };

                    if (heading.Inline != null)
                    {
                        headingBlock.Text = GetInlineText(heading.Inline);
                    }

                    return headingBlock;

                case CodeBlock code:
                    var codeText = string.Empty;
                    if (code.Lines.Lines != null)
                    {
                        codeText = string.Join("\n", code.Lines.Lines.Select(line => line.Slice.ToString()));
                    }
                    
                    // Don't render empty code blocks
                    if (string.IsNullOrWhiteSpace(codeText))
                    {
                        return null;
                    }

                    var codeBlock = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(40, 44, 52)),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(12),
                        Margin = new Thickness(0, 8),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Top,
                        Child = new TextBlock
                        {
                            Text = codeText,
                            FontFamily = new FontFamily("Consolas, Monaco, 'Courier New', monospace"),
                            Foreground = Brushes.White,
                            TextWrapping = TextWrapping.NoWrap, // Code should not wrap
                            FontSize = 13,
                            VerticalAlignment = VerticalAlignment.Top,
                            HorizontalAlignment = HorizontalAlignment.Left
                        }
                    };

                    return codeBlock;

                default:
                    // Simple fallback for other block types
                    return new TextBlock 
                    { 
                        Text = $"[{block.GetType().Name}]",
                        FontStyle = FontStyle.Italic,
                        VerticalAlignment = VerticalAlignment.Top,
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
            }
        }
        catch
        {
            return new TextBlock 
            { 
                Text = "[Block Error]",
                FontStyle = FontStyle.Italic,
                Foreground = Brushes.Red,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
        }
    }
}