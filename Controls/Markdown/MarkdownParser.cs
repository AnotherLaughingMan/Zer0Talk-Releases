using System;
using System.Collections.Generic;
using System.Linq;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Zer0Talk.Controls.Markdown;

/// <summary>
/// Parser that converts Markdown text into a safe, serializable RenderModel.
/// Uses Markdig for robust parsing, then maps to our custom render model types.
/// </summary>
public sealed class MarkdownParser
{
    private readonly MarkdownPipeline _pipeline;

    public MarkdownParser()
    {
        // Configure Markdig pipeline with safe defaults
        // Security: HTML is disabled for P2P safety
        _pipeline = new MarkdownPipelineBuilder()
            .UseEmphasisExtras()      // Bold, italic, strikethrough
            .UsePipeTables()           // Table support
            .UseAutoLinks()            // Auto-detect URLs
            .DisableHtml()             // Security: no HTML injection
            .Build();
    }

    /// <summary>
    /// Parse markdown text into a RenderModel Document.
    /// Returns null if parsing fails.
    /// CRITICAL: Never throws - always returns Document or null for safe fallback.
    /// </summary>
    public Document? Parse(string markdownText)
    {
        if (string.IsNullOrWhiteSpace(markdownText))
        {
            return new Document { Blocks = new List<BlockNode>() };
        }

        try
        {
            // Parse with Markdig - wrapped in defensive try-catch
            MarkdownDocument? markdigDoc = null;
            try
            {
                markdigDoc = Markdig.Markdown.Parse(markdownText, _pipeline);
            }
            catch (Exception markdigEx)
            {
                // Markdig itself failed - log and return null
                System.Diagnostics.Debug.WriteLine($"[MarkdownParser] Markdig.Parse failed: {markdigEx.Message}");
                return null;
            }

            if (markdigDoc == null)
            {
                return null;
            }

            // Convert to RenderModel - each block wrapped in try-catch
            var blocks = new List<BlockNode>();
            try
            {
                foreach (var block in markdigDoc)
                {
                    try
                    {
                        var renderBlock = ConvertBlock(block);
                        if (renderBlock != null)
                        {
                            blocks.Add(renderBlock);
                        }
                    }
                    catch (Exception blockEx)
                    {
                        // Single block failed - skip it, continue with others
                        System.Diagnostics.Debug.WriteLine($"[MarkdownParser] Block conversion failed: {blockEx.Message}");
                        // Add a fallback block to indicate error
                        try
                        {
                            blocks.Add(new Paragraph 
                            { 
                                Inlines = new List<InlineNode> 
                                { 
                                    new TextInline { Text = "[Block render error]" } 
                                } 
                            });
                        }
                        catch { }
                    }
                }
            }
            catch (Exception iterEx)
            {
                // Iteration itself failed - return what we have so far
                System.Diagnostics.Debug.WriteLine($"[MarkdownParser] Block iteration failed: {iterEx.Message}");
            }

            // Always return a Document, even if empty
            return new Document { Blocks = blocks };
        }
        catch (Exception topEx)
        {
            // Top-level parser failure - log and return null
            System.Diagnostics.Debug.WriteLine($"[MarkdownParser] Top-level parse failed: {topEx.Message}");
            return null;
        }
    }

    /// <summary>
    /// Convert a Markdig Block to a RenderModel BlockNode.
    /// </summary>
    private BlockNode? ConvertBlock(Block block)
    {
        try
        {
            return block switch
            {
                ParagraphBlock paragraph => ConvertParagraph(paragraph),
                HeadingBlock heading => ConvertHeading(heading),
                QuoteBlock quote => ConvertBlockQuote(quote),
                CodeBlock code => ConvertCodeBlock(code),
                ListBlock list => ConvertList(list),
                // ThematicBreakBlock handled separately if needed
                _ => null // Unsupported block types are skipped
            };
        }
        catch
        {
            // Skip blocks that fail to convert
            return null;
        }
    }

    private Paragraph ConvertParagraph(ParagraphBlock paragraph)
    {
        var inlines = new List<InlineNode>();
        
        if (paragraph.Inline != null)
        {
            foreach (var inline in paragraph.Inline)
            {
                ConvertInline(inline, inlines);
            }
        }

        return new Paragraph { Inlines = inlines };
    }

    private Heading ConvertHeading(HeadingBlock heading)
    {
        var inlines = new List<InlineNode>();
        
        if (heading.Inline != null)
        {
            foreach (var inline in heading.Inline)
            {
                ConvertInline(inline, inlines);
            }
        }

        // Clamp level to valid range (1-6)
        var level = Math.Clamp(heading.Level, 1, 6);

        return new Heading 
        { 
            Level = level,
            Inlines = inlines 
        };
    }

    private BlockQuote ConvertBlockQuote(QuoteBlock quote)
    {
        var blocks = new List<BlockNode>();
        
        try
        {
            foreach (var block in quote)
            {
                try
                {
                    var renderBlock = ConvertBlock(block);
                    if (renderBlock != null)
                    {
                        blocks.Add(renderBlock);
                    }
                }
                catch (Exception blockEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[MarkdownParser] Quote block conversion failed: {blockEx.Message}");
                    // Skip problematic block in quote
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MarkdownParser] BlockQuote conversion failed: {ex.Message}");
        }

        return new BlockQuote { Blocks = blocks };
    }

    private FencedCode? ConvertCodeBlock(CodeBlock codeBlock)
    {
        // Extract code text from lines
        var codeText = string.Empty;
        if (codeBlock.Lines.Lines != null)
        {
            codeText = string.Join("\n", codeBlock.Lines.Lines.Select(line => line.Slice.ToString()));
        }

        // Don't create empty code blocks
        if (string.IsNullOrWhiteSpace(codeText))
        {
            return null;
        }

        // Get language identifier if it's a fenced code block
        string? language = null;
        if (codeBlock is FencedCodeBlock fenced && !string.IsNullOrWhiteSpace(fenced.Info))
        {
            language = fenced.Info.Trim();
        }

        return new FencedCode 
        { 
            Language = language,
            Code = codeText 
        };
    }

    private List? ConvertList(ListBlock list)
    {
        var items = new List<ListItem>();

        foreach (var item in list)
        {
            if (item is ListItemBlock listItem)
            {
                var blocks = new List<BlockNode>();
                
                foreach (var block in listItem)
                {
                    var renderBlock = ConvertBlock(block);
                    if (renderBlock != null)
                    {
                        blocks.Add(renderBlock);
                    }
                }

                items.Add(new ListItem { Blocks = blocks });
            }
        }

        return new List 
        { 
            IsOrdered = list.IsOrdered,
            Items = items 
        };
    }

    /// <summary>
    /// Convert a Markdig Inline to RenderModel InlineNodes.
    /// May add zero, one, or multiple inlines to the list.
    /// </summary>
    private void ConvertInline(Markdig.Syntax.Inlines.Inline inline, List<InlineNode> inlines)
    {
        try
        {
            switch (inline)
            {
                case LiteralInline literal:
                    var text = literal.Content.ToString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        inlines.Add(new TextInline { Text = text });
                    }
                    break;

                case LineBreakInline:
                    // Represent line breaks as newline text
                    inlines.Add(new TextInline { Text = "\n" });
                    break;

                case EmphasisInline emphasis:
                    var emphasisNode = ConvertEmphasis(emphasis);
                    if (emphasisNode != null)
                    {
                        inlines.Add(emphasisNode);
                    }
                    break;

                case CodeInline code:
                    if (!string.IsNullOrEmpty(code.Content))
                    {
                        inlines.Add(new InlineCode { Code = code.Content });
                    }
                    break;

                case LinkInline link:
                    var linkNode = ConvertLink(link);
                    if (linkNode != null)
                    {
                        inlines.Add(linkNode);
                    }
                    break;

                case AutolinkInline autolink:
                    // Convert autolinks to plain links
                    if (!string.IsNullOrEmpty(autolink.Url))
                    {
                        inlines.Add(new Link 
                        { 
                            Url = autolink.Url,
                            Title = null,
                            Inlines = new List<InlineNode> 
                            { 
                                new TextInline { Text = autolink.Url } 
                            }
                        });
                    }
                    break;

                case HtmlInline html:
                    // Security: HTML disabled, but handle spoiler syntax ||text||
                    var htmlText = html.Tag;
                    if (htmlText.StartsWith("||") && htmlText.EndsWith("||") && htmlText.Length > 4)
                    {
                        var spoilerText = htmlText.Substring(2, htmlText.Length - 4);
                        // Represent spoilers as regular text for now
                        // TODO: Add proper spoiler support to RenderModel
                        inlines.Add(new TextInline { Text = spoilerText });
                    }
                    // Otherwise skip HTML content
                    break;

                case ContainerInline container:
                    // Process nested inlines recursively
                    foreach (var child in container)
                    {
                        ConvertInline(child, inlines);
                    }
                    break;

                // Skip other inline types (they'll be handled by fallback or ignored)
            }
        }
        catch
        {
            // Skip inlines that fail to convert
        }
    }

    private Emphasis? ConvertEmphasis(EmphasisInline emphasis)
    {
        // Collect child inlines
        var childInlines = new List<InlineNode>();
        foreach (var child in emphasis)
        {
            ConvertInline(child, childInlines);
        }

        // Don't create empty emphasis nodes
        if (childInlines.Count == 0)
        {
            return null;
        }

        // Determine emphasis style from delimiter
        string style = "italic"; // default
        
        if (emphasis.DelimiterChar == '*' || emphasis.DelimiterChar == '_')
        {
            if (emphasis.DelimiterCount == 2)
            {
                style = "bold";
            }
            else if (emphasis.DelimiterCount == 1)
            {
                style = "italic";
            }
        }
        else if (emphasis.DelimiterChar == '~' && emphasis.DelimiterCount == 2)
        {
            style = "strikethrough";
        }

        return new Emphasis 
        { 
            Style = style,
            Inlines = childInlines 
        };
    }

    private Link? ConvertLink(LinkInline link)
    {
        // Collect link text from child inlines
        var childInlines = new List<InlineNode>();
        foreach (var child in link)
        {
            ConvertInline(child, childInlines);
        }

        // If no link text, use URL as text
        if (childInlines.Count == 0 && !string.IsNullOrEmpty(link.Url))
        {
            childInlines.Add(new TextInline { Text = link.Url });
        }

        // Skip empty links
        if (string.IsNullOrEmpty(link.Url) && childInlines.Count == 0)
        {
            return null;
        }

        // For images, we'll represent them as links with a special prefix
        // TODO: Add proper Image support to RenderModel if needed
        if (link.IsImage)
        {
            // Prepend image indicator
            childInlines.Insert(0, new TextInline { Text = "🖼️ " });
        }

        return new Link 
        { 
            Url = link.Url ?? string.Empty,
            Title = link.Title,
            Inlines = childInlines 
        };
    }

    /// <summary>
    /// Helper to extract plain text from Markdig inlines (for debugging/fallback).
    /// </summary>
    private static string ExtractText(ContainerInline container)
    {
        var parts = new List<string>();
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    parts.Add(literal.Content.ToString());
                    break;
                case CodeInline code:
                    parts.Add(code.Content);
                    break;
                case LineBreakInline:
                    parts.Add("\n");
                    break;
                case ContainerInline nested:
                    parts.Add(ExtractText(nested));
                    break;
            }
        }
        return string.Join("", parts);
    }
}
