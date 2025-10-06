# Custom Header Rendering

## Overview

Implemented custom header rendering for markdown headers (`#`, `##`, `###`, etc.) to avoid Avalonia parent tracking issues and provide consistent formatting.

## Syntax

```markdown
# Heading 1 (largest)
## Heading 2
### Heading 3
#### Heading 4
##### Heading 5
###### Heading 6 (smallest)
```

## Visual Design

Headers use **bold font weight** with varying sizes based on level:

| Level | Syntax | Font Size | Example |
|-------|--------|-----------|---------|
| 1 | `#` | 24px | **Heading 1** |
| 2 | `##` | 20px | **Heading 2** |
| 3 | `###` | 18px | **Heading 3** |
| 4 | `####` | 16px | **Heading 4** |
| 5 | `#####` | 14px | **Heading 5** |
| 6 | `######` | 12px | **Heading 6** |

**Styling**:
- **Font Weight**: Bold (all levels)
- **Text Wrapping**: Enabled (wraps on long headers)
- **Margin**: 16px top, 8px bottom (visual separation)
- **Alignment**: Left-aligned, full width

## Implementation

### Detection

```csharp
private static bool IsHeaderMarkdown(string text)
{
    var trimmed = text.TrimStart();
    return trimmed.StartsWith("#");
}
```

### Parsing Logic

The custom renderer:
1. **Count `#` characters** - Determines header level (1-6)
2. **Extract text** - Gets content after `#` symbols and space
3. **Parse inline formatting** - Supports **bold**, *italic*, `code` in headers
4. **Apply styling** - Sets font size and weight based on level
5. **Create TextBlock** - Fresh control with no parent conflicts

### Header Level Calculation

```csharp
int level = 0;
while (level < trimmed.Length && trimmed[level] == '#') {
    level++;
}

// Maximum heading level is 6
if (level > 6) level = 6;
if (level == 0) level = 1;
```

### Font Size Mapping

```csharp
textBlock.FontSize = level switch
{
    1 => 24,  // H1
    2 => 20,  // H2
    3 => 18,  // H3
    4 => 16,  // H4
    5 => 14,  // H5
    6 => 12,  // H6
    _ => 14   // Fallback
};
```

## Features

### Inline Formatting Support

Headers can contain inline markdown:

```markdown
### This is **bold** and *italic* and `code`
```

Renders as large bold text with nested formatting preserved.

### All Header Levels

```markdown
# Level 1
## Level 2
### Level 3
#### Level 4
##### Level 5
###### Level 6
```

Each level has distinct size while maintaining bold weight.

### Text Wrapping

Long headers automatically wrap:

```markdown
### This is a very long header that will wrap to multiple lines when the window is narrow
```

### Edge Cases

**Too Many `#`**:
```markdown
####### Seven hashes
```
Treated as level 6 (maximum)

**No Space After `#`**:
```markdown
###NoSpace
```
Still renders correctly: "NoSpace" as H3

**Empty Header**:
```markdown
###
```
Renders empty TextBlock (no visible content)

## Benefits

1. **No Parent Conflicts**: Fresh TextBlock created every render
2. **Consistent Styling**: Standard markdown header sizes
3. **Inline Support**: Nested formatting works correctly
4. **Performance**: Simple TextBlock, no complex layout
5. **Reliable**: Works for all header levels 1-6

## Testing

### Test Cases

**1. Single Header**:
```markdown
### Test Header
```
Expected: Bold text at 18px

**2. With Formatting**:
```markdown
### Header with **bold** and *italic*
```
Expected: Large bold text with nested styles

**3. All Levels**:
```markdown
# H1
## H2
### H3
#### H4
##### H5
###### H6
```
Expected: Decreasing font sizes, all bold

**4. Long Header**:
```markdown
### This is a very long header that should wrap when the window is too narrow to display it all on one line
```
Expected: Text wraps, maintains styling

**5. Message Deletion**:
- Send header message
- Delete it
- Expected: No blanking, chat remains functional

### Debug Output

Look for:
```
[ZTalkMarkdownViewer] Using custom header renderer
[CustomHeader] Error: <if any>
```

## Architecture

### Before (Potential Issues)

```
# Header → MarkdownParser → Heading → MarkdownRenderer →
TextBlock (potential reuse) → Possible parent issues
```

### After (Working)

```
# Header → IsHeaderMarkdown? → YES →
Parse manually → Count # → Extract text →
Create fresh TextBlock → Apply size/weight → SUCCESS
```

## Technical Details

### Control Structure

```
TextBlock (header)
  ├─ FontSize: 24/20/18/16/14/12 (based on level)
  ├─ FontWeight: Bold
  ├─ Margin: 16px top, 8px bottom
  └─ Inlines:
      ├─ Run (normal text)
      ├─ Run (bold text, FontWeight.Bold)
      ├─ Run (italic text, FontStyle.Italic)
      └─ Run (code text, monospace + colors)
```

### Inline Parsing

Headers parse their content for inline elements:

```csharp
var doc = _parser.Parse(headerText);
if (doc.Blocks[0] is Paragraph para) {
    foreach (var inline in para.Inlines) {
        var run = CreateInlineRun(inline);
        textBlock.Inlines.Add(run);
    }
}
```

This reuses the existing `CreateInlineRun()` method used by spoilers and quotes.

## Use Cases

### Document Structure

```markdown
# Main Title

Some intro text

## Section 1

Content here

### Subsection 1.1

More content

## Section 2

Final content
```

### Message Headers

```markdown
### Daily Update

Here's what happened today...
```

### Emphasized Text

```markdown
### IMPORTANT NOTICE

Please read carefully
```

## Future Enhancements

If needed:
- **Underlines**: Add horizontal line under H1/H2
- **Colors**: Different colors per level
- **Icons**: Add icons before headers
- **Anchors**: Make headers linkable
- **Collapse**: Collapsible sections under headers

## Limitations

1. **Block Element Only**: Headers must be on their own line
2. **No Nesting**: Can't nest headers inside other elements
3. **Simple Styling**: Just size and bold, no complex theming

These are acceptable for P2P messaging use case.

## Related Files

- `Controls/Markdown/ZTalkMarkdownViewer.cs` - Custom renderer
- `Controls/Markdown/MarkdownRenderer.cs` - Original (may still use for fallback)
- `Controls/Markdown/RenderModel.cs` - Heading model
- `docs/CUSTOM-QUOTE-RENDERING.md` - Quote solution
- `docs/markdown-code-blocks.md` - Code block solution
- `docs/spoiler-implementation.md` - Spoiler solution

## Status

✅ **IMPLEMENTED AND READY FOR TESTING**

Build: SUCCESS (4.7s)
Compile Errors: NONE
Supports: Levels 1-6, inline formatting, text wrapping
Font Sizes: 24/20/18/16/14/12 (H1-H6)
Style: Bold weight, proper spacing
