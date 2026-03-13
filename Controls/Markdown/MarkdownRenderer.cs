using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace Zer0Talk.Controls.Markdown;

/// <summary>
/// Renders a RenderModel Document into Avalonia UI controls.
/// Creates fresh controls per render - never reuses visual elements.
/// </summary>
public sealed class MarkdownRenderer
{
    // Theme colors - can be made configurable later
    private static readonly IBrush CodeBackground = new SolidColorBrush(Color.FromRgb(40, 44, 52));
    private static readonly IBrush CodeForeground = Brushes.White;
    private static readonly IBrush InlineCodeBackground = new SolidColorBrush(Color.FromArgb(40, 68, 68, 68));
    private static readonly IBrush InlineCodeForeground = new SolidColorBrush(Color.FromRgb(240, 108, 108));
    private static readonly IBrush LinkForeground = new SolidColorBrush(Color.FromRgb(100, 149, 237));
    private static readonly IBrush QuoteBorder = new SolidColorBrush(Color.FromRgb(100, 149, 237));
    private static readonly IBrush QuoteBackground = new SolidColorBrush(Color.FromArgb(20, 100, 149, 237));
    private static readonly FontFamily MonospaceFont = new("Consolas, Monaco, 'Courier New', monospace");

    /// <summary>
    /// Render a Document into an Avalonia Control.
    /// Returns a TextBlock for simple single-paragraph content,
    /// or a StackPanel for complex multi-block content.
    /// CRITICAL: Never throws - always returns a valid Control for safe rendering.
    /// </summary>
    public Control Render(Document document)
    {
        if (document == null || document.Blocks == null || document.Blocks.Count == 0)
        {
            return new TextBlock { Text = string.Empty };
        }

        try
        {
            // Optimize for single paragraph - most common case in chat
            if (document.Blocks.Count == 1 && document.Blocks[0] is Paragraph paragraph)
            {
                try
                {
                    return RenderParagraphAsTextBlock(paragraph);
                }
                catch (Exception paraEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[MarkdownRenderer] Single paragraph render failed: {paraEx.Message}");
                    return CreateErrorTextBlock("[Paragraph render error]");
                }
            }

            // Multi-block content - use StackPanel
            var container = new StackPanel
            {
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            // Render each block defensively
            foreach (var block in document.Blocks)
            {
                try
                {
                    var control = RenderBlock(block);
                    if (control != null)
                    {
                        container.Children.Add(control);
                    }
                }
                catch (Exception blockEx)
                {
                    // Single block failed - add error indicator, continue with others
                    System.Diagnostics.Debug.WriteLine($"[MarkdownRenderer] Block render failed: {blockEx.Message}");
                    try
                    {
                        container.Children.Add(CreateErrorTextBlock("[Block render error]"));
                    }
                    catch { }
                }
            }

            // Return container with whatever we successfully rendered
            return container.Children.Count switch
            {
                0 => CreateErrorTextBlock("[Empty render]"),
                1 => container.Children[0],
                _ => container
            };
        }
        catch (Exception topEx)
        {
            // Top-level rendering failure - return error indicator
            System.Diagnostics.Debug.WriteLine($"[MarkdownRenderer] Top-level render failed: {topEx.Message}");
            return CreateErrorTextBlock("[Render Error]");
        }
    }

    private static TextBlock CreateErrorTextBlock(string message)
    {
        return new TextBlock
        {
            Text = message,
            Foreground = Brushes.Orange,
            FontStyle = FontStyle.Italic,
            TextWrapping = TextWrapping.Wrap
        };
    }

    /// <summary>
    /// Render a block-level element.
    /// </summary>
    private Control? RenderBlock(BlockNode block)
    {
        try
        {
            return block switch
            {
                Paragraph p => RenderParagraphAsTextBlock(p),
                Heading h => RenderHeading(h),
                BlockQuote q => RenderBlockQuote(q),
                FencedCode c => RenderFencedCode(c),
                List l => RenderList(l),
                _ => null // Unsupported block types
            };
        }
        catch
        {
            return null; // Skip blocks that fail to render
        }
    }

    private TextBlock RenderParagraphAsTextBlock(Paragraph paragraph)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        if (paragraph.Inlines != null)
        {
            foreach (var inline in paragraph.Inlines)
            {
                RenderInline(inline, textBlock.Inlines!);
            }
        }

        // Fallback if no inlines were added
        if (textBlock.Inlines!.Count == 0)
        {
            textBlock.Text = string.Empty;
        }

        // Wire spoiler toggle if the paragraph contains any spoiler runs.
        WireSpoilerToggle(textBlock, paragraph.Inlines);

        return textBlock;
    }

    /// <summary>
    /// If any SpoilerInline nodes are present, attach a PointerPressed handler on
    /// the TextBlock that toggles hidden/revealed state for every spoiler Run.
    /// </summary>
    private void WireSpoilerToggle(TextBlock textBlock, List<InlineNode>? inlines)
    {
        if (inlines == null || !ContainsSpoiler(inlines)) return;

        textBlock.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);

        var revealed = false;
        textBlock.PointerPressed += (_, e) =>
        {
            try
            {
                revealed = !revealed;
                textBlock.Inlines!.Clear();
                foreach (var inline in inlines)
                    RenderInlineWithReveal(inline, textBlock.Inlines!, revealed);
                e.Handled = true;
            }
            catch { }
        };
    }

    private void RenderInlineWithReveal(InlineNode inline, InlineCollection inlines, bool revealed)
    {
        if (inline is SpoilerInline spoiler && !string.IsNullOrEmpty(spoiler.Text))
        {
            var mask = new string('\u2588', spoiler.Text.Length);
            inlines.Add(new Run
            {
                Text = revealed ? spoiler.Text : mask,
                Foreground = revealed
                    ? new SolidColorBrush(Color.FromRgb(220, 220, 220))
                    : new SolidColorBrush(Colors.Black),
                Background = revealed
                    ? new SolidColorBrush(Color.FromArgb(60, 68, 68, 68))
                    : new SolidColorBrush(Colors.Black)
            });
            return;
        }
        RenderInline(inline, inlines);
    }

    private static bool ContainsSpoiler(List<InlineNode> inlines)
    {
        foreach (var n in inlines)
        {
            if (n is SpoilerInline) return true;
            if (n is Emphasis em && ContainsSpoiler(em.Inlines)) return true;
        }
        return false;
    }

    private TextBlock RenderHeading(Heading heading)
    {
        var textBlock = new TextBlock
        {
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch,
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
            6 => 12,
            _ => 14
        };

        if (heading.Inlines != null)
        {
            foreach (var inline in heading.Inlines)
            {
                RenderInline(inline, textBlock.Inlines!);
            }
        }

        return textBlock;
    }

    private Border RenderBlockQuote(BlockQuote quote)
    {
        try
        {
            var border = new Border
            {
                BorderBrush = QuoteBorder,
                BorderThickness = new Thickness(4, 0, 0, 0),
                Padding = new Thickness(12, 8),
                Margin = new Thickness(0, 8),
                Background = QuoteBackground,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                // CRITICAL: Force unique instance with name to prevent Avalonia caching
                Name = $"Quote_{Guid.NewGuid().ToString("N").Substring(0, 8)}"
            };

            var container = new StackPanel
            {
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                // CRITICAL: Force unique instance
                Name = $"QuoteContainer_{Guid.NewGuid().ToString("N").Substring(0, 8)}"
            };

            if (quote.Blocks != null)
            {
                foreach (var block in quote.Blocks)
                {
                    try
                    {
                        var control = RenderBlock(block);
                        if (control != null)
                        {
                            container.Children.Add(control);
                        }
                    }
                    catch (Exception blockEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MarkdownRenderer] Quote block render failed: {blockEx.Message}");
                        // Add error indicator but continue
                        try
                        {
                            container.Children.Add(CreateErrorTextBlock("[Quote block error]"));
                        }
                        catch { }
                    }
                }
            }

            border.Child = container;
            return border;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MarkdownRenderer] BlockQuote render failed: {ex.Message}");
            // Return a border with error text instead of bare TextBlock
            return new Border
            {
                Child = CreateErrorTextBlock("[Blockquote error]"),
                Padding = new Thickness(12, 8),
                Margin = new Thickness(0, 8)
            };
        }
    }

    private Border RenderFencedCode(FencedCode code)
    {
        var border = new Border
        {
            Background = CodeBackground,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var textBlock = new TextBlock
        {
            Text = code.Code ?? string.Empty,
            FontFamily = MonospaceFont,
            Foreground = CodeForeground,
            TextWrapping = TextWrapping.NoWrap,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Top
        };

        // Add language label if present
        if (!string.IsNullOrWhiteSpace(code.Language))
        {
            var languageLabel = new TextBlock
            {
                Text = code.Language,
                FontFamily = MonospaceFont,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            };

            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(languageLabel);
            stack.Children.Add(textBlock);
            border.Child = stack;
        }
        else
        {
            border.Child = textBlock;
        }

        return border;
    }

    private StackPanel RenderList(List list)
    {
        var container = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        if (list.Items != null)
        {
            for (int i = 0; i < list.Items.Count; i++)
            {
                var item = list.Items[i];
                var itemControl = RenderListItem(item, list.IsOrdered, i + 1);
                if (itemControl != null)
                {
                    container.Children.Add(itemControl);
                }
            }
        }

        return container;
    }

    private StackPanel? RenderListItem(ListItem item, bool isOrdered, int index)
    {
        var itemContainer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        // Add bullet or number
        var bullet = new TextBlock
        {
            Text = isOrdered ? $"{index}." : "•",
            VerticalAlignment = VerticalAlignment.Top,
            MinWidth = 20
        };

        var content = new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        if (item.Blocks != null)
        {
            foreach (var block in item.Blocks)
            {
                var control = RenderBlock(block);
                if (control != null)
                {
                    content.Children.Add(control);
                }
            }
        }

        itemContainer.Children.Add(bullet);
        itemContainer.Children.Add(content);

        return itemContainer;
    }

    /// <summary>
    /// Render an inline element into a collection of Avalonia Inlines.
    /// </summary>
    private void RenderInline(InlineNode inline, InlineCollection inlines)
    {
        try
        {
            switch (inline)
            {
                case TextInline text:
                    if (!string.IsNullOrEmpty(text.Text))
                    {
                        inlines.Add(new Run { Text = text.Text });
                    }
                    break;

                case Emphasis emphasis:
                    var emphasisInline = RenderEmphasis(emphasis);
                    if (emphasisInline != null)
                    {
                        inlines.Add(emphasisInline);
                    }
                    break;

                case InlineCode code:
                    if (!string.IsNullOrEmpty(code.Code))
                    {
                        inlines.Add(new Run
                        {
                            Text = code.Code,
                            FontFamily = MonospaceFont,
                            Background = InlineCodeBackground,
                            Foreground = InlineCodeForeground
                        });
                    }
                    break;

                case Link link:
                    var linkInline = RenderLink(link);
                    if (linkInline != null)
                    {
                        inlines.Add(linkInline);
                    }
                    break;

                case SpoilerInline spoiler:
                    if (!string.IsNullOrEmpty(spoiler.Text))
                    {
                        var hiddenMask = new string('\u2588', spoiler.Text.Length);
                        inlines.Add(new Run
                        {
                            Text = hiddenMask,
                            Foreground = new SolidColorBrush(Colors.Black),
                            Background = new SolidColorBrush(Colors.Black)
                        });
                    }
                    break;
            }
        }
        catch
        {
            // Skip inlines that fail to render
        }
    }

    private Avalonia.Controls.Documents.Inline? RenderEmphasis(Emphasis emphasis)
    {
        if (emphasis.Inlines == null || emphasis.Inlines.Count == 0)
        {
            return null;
        }

        try
        {
            // Create a Span to hold nested inlines with formatting
            var span = new Span();

            // Apply style based on emphasis type
            switch (emphasis.Style?.ToLowerInvariant())
            {
                case "bold":
                    span.FontWeight = FontWeight.Bold;
                    break;
                case "italic":
                    span.FontStyle = FontStyle.Italic;
                    break;
                case "strikethrough":
                    span.TextDecorations = TextDecorations.Strikethrough;
                    break;
                case "underline":
                    span.TextDecorations = TextDecorations.Underline;
                    break;
            }

            // Add child inlines
            foreach (var child in emphasis.Inlines)
            {
                RenderInline(child, span.Inlines!);
            }

            return span;
        }
        catch
        {
            return null;
        }
    }

    private Avalonia.Controls.Documents.Inline? RenderLink(Link link)
    {
        if (link.Inlines == null || link.Inlines.Count == 0)
        {
            return null;
        }

        try
        {
            // P2P Safety: Don't make links clickable, just style them
            var span = new Span
            {
                Foreground = LinkForeground,
                TextDecorations = TextDecorations.Underline
            };

            // Add link text
            foreach (var child in link.Inlines)
            {
                RenderInline(child, span.Inlines!);
            }

            // Append URL in parentheses if present
            if (!string.IsNullOrWhiteSpace(link.Url))
            {
                span.Inlines!.Add(new Run
                {
                    Text = $" ({link.Url})",
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                    FontSize = 12
                });
            }

            return span;
        }
        catch
        {
            return null;
        }
    }
}
