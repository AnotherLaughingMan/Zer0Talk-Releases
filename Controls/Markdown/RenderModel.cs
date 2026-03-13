using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Zer0Talk.Controls.Markdown;

/// <summary>
/// JSON-serializable render model for Markdown documents.
/// Represents the parsed structure of a Markdown document for rendering in Avalonia UI.
/// </summary>
public abstract class RenderNode : IEquatable<RenderNode>
{
    /// <summary>Gets the type discriminator for JSON polymorphic serialization.</summary>
    [JsonPropertyName("type")]
    public abstract string Type { get; }

    public abstract bool Equals(RenderNode? other);
    public override bool Equals(object? obj) => obj is RenderNode other && Equals(other);
    public override int GetHashCode() => Type.GetHashCode();
}

/// <summary>Root document node containing all top-level block elements.</summary>
public sealed class Document : RenderNode
{
    public override string Type => "document";

    [JsonPropertyName("blocks")]
    public List<BlockNode> Blocks { get; set; } = new();

    public override bool Equals(RenderNode? other)
    {
        if (other is not Document doc) return false;
        return Blocks.SequenceEqual(doc.Blocks);
    }

    public override int GetHashCode() => HashCode.Combine(Type, Blocks.Count);

    public override string ToString() => $"Document(Blocks={Blocks.Count})";
}

/// <summary>Base class for block-level elements (paragraphs, headings, lists, etc.).</summary>
public abstract class BlockNode : RenderNode
{
}

/// <summary>Paragraph block containing inline content.</summary>
public sealed class Paragraph : BlockNode
{
    public override string Type => "paragraph";

    [JsonPropertyName("inlines")]
    public List<InlineNode> Inlines { get; set; } = new();

    public override bool Equals(RenderNode? other)
    {
        if (other is not Paragraph para) return false;
        return Inlines.SequenceEqual(para.Inlines);
    }

    public override int GetHashCode() => HashCode.Combine(Type, Inlines.Count);

    public override string ToString() => $"Paragraph(Inlines={Inlines.Count})";
}

/// <summary>Heading block with level (1-6) and inline content.</summary>
public sealed class Heading : BlockNode
{
    public override string Type => "heading";

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("inlines")]
    public List<InlineNode> Inlines { get; set; } = new();

    public override bool Equals(RenderNode? other)
    {
        if (other is not Heading heading) return false;
        return Level == heading.Level && Inlines.SequenceEqual(heading.Inlines);
    }

    public override int GetHashCode() => HashCode.Combine(Type, Level, Inlines.Count);

    public override string ToString() => $"Heading(Level={Level}, Inlines={Inlines.Count})";
}

/// <summary>Block quote containing nested block elements.</summary>
public sealed class BlockQuote : BlockNode
{
    public override string Type => "blockquote";

    [JsonPropertyName("blocks")]
    public List<BlockNode> Blocks { get; set; } = new();

    public override bool Equals(RenderNode? other)
    {
        if (other is not BlockQuote quote) return false;
        return Blocks.SequenceEqual(quote.Blocks);
    }

    public override int GetHashCode() => HashCode.Combine(Type, Blocks.Count);

    public override string ToString() => $"BlockQuote(Blocks={Blocks.Count})";
}

/// <summary>Fenced code block with optional language identifier.</summary>
public sealed class FencedCode : BlockNode
{
    public override string Type => "fencedcode";

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    public override bool Equals(RenderNode? other)
    {
        if (other is not FencedCode code) return false;
        return Language == code.Language && Code == code.Code;
    }

    public override int GetHashCode() => HashCode.Combine(Type, Language, Code);

    public override string ToString() => $"FencedCode(Language={Language ?? "none"}, Length={Code.Length})";
}

/// <summary>Ordered or unordered list.</summary>
public sealed class List : BlockNode
{
    public override string Type => "list";

    [JsonPropertyName("ordered")]
    public bool IsOrdered { get; set; }

    [JsonPropertyName("items")]
    public List<ListItem> Items { get; set; } = new();

    public override bool Equals(RenderNode? other)
    {
        if (other is not List list) return false;
        return IsOrdered == list.IsOrdered && Items.SequenceEqual(list.Items);
    }

    public override int GetHashCode() => HashCode.Combine(Type, IsOrdered, Items.Count);

    public override string ToString() => $"List(Ordered={IsOrdered}, Items={Items.Count})";
}

/// <summary>List item containing nested block elements.</summary>
public sealed class ListItem : RenderNode
{
    public override string Type => "listitem";

    [JsonPropertyName("blocks")]
    public List<BlockNode> Blocks { get; set; } = new();

    public override bool Equals(RenderNode? other)
    {
        if (other is not ListItem item) return false;
        return Blocks.SequenceEqual(item.Blocks);
    }

    public override int GetHashCode() => HashCode.Combine(Type, Blocks.Count);

    public override string ToString() => $"ListItem(Blocks={Blocks.Count})";
}

/// <summary>Base class for inline-level elements (text, emphasis, links, etc.).</summary>
public abstract class InlineNode : RenderNode
{
}

/// <summary>Plain text inline node.</summary>
public sealed class TextInline : InlineNode
{
    public override string Type => "text";

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    public override bool Equals(RenderNode? other)
    {
        if (other is not TextInline text) return false;
        return Text == text.Text;
    }

    public override int GetHashCode() => HashCode.Combine(Type, Text);

    public override string ToString() => $"Text(\"{Text}\")";
}

/// <summary>Emphasis (italic/bold/strikethrough) inline node.</summary>
public sealed class Emphasis : InlineNode
{
    public override string Type => "emphasis";

    /// <summary>Emphasis style: "italic", "bold", "strikethrough".</summary>
    [JsonPropertyName("style")]
    public string Style { get; set; } = "italic";

    [JsonPropertyName("inlines")]
    public List<InlineNode> Inlines { get; set; } = new();

    public override bool Equals(RenderNode? other)
    {
        if (other is not Emphasis emph) return false;
        return Style == emph.Style && Inlines.SequenceEqual(emph.Inlines);
    }

    public override int GetHashCode() => HashCode.Combine(Type, Style, Inlines.Count);

    public override string ToString() => $"Emphasis(Style={Style}, Inlines={Inlines.Count})";
}

/// <summary>Inline code span.</summary>
public sealed class InlineCode : InlineNode
{
    public override string Type => "inlinecode";

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    public override bool Equals(RenderNode? other)
    {
        if (other is not InlineCode code) return false;
        return Code == code.Code;
    }

    public override int GetHashCode() => HashCode.Combine(Type, Code);

    public override string ToString() => $"InlineCode(\"{Code}\")";
}

/// <summary>Hyperlink inline node.</summary>
public sealed class Link : InlineNode
{
    public override string Type => "link";

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("inlines")]
    public List<InlineNode> Inlines { get; set; } = new();

    public override bool Equals(RenderNode? other)
    {
        if (other is not Link link) return false;
        return Url == link.Url && Title == link.Title && Inlines.SequenceEqual(link.Inlines);
    }

    public override int GetHashCode() => HashCode.Combine(Type, Url, Title, Inlines.Count);

    public override string ToString() => $"Link(Url=\"{Url}\", Inlines={Inlines.Count})";
}

/// <summary>Spoiler inline node — text hidden until revealed by the user.</summary>
public sealed class SpoilerInline : InlineNode
{
    public override string Type => "spoiler";

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    public override bool Equals(RenderNode? other)
    {
        if (other is not SpoilerInline spoiler) return false;
        return Text == spoiler.Text;
    }

    public override int GetHashCode() => HashCode.Combine(Type, Text);

    public override string ToString() => $"Spoiler(\"{Text}\")";
}
