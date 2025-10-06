# Custom List Rendering

## Overview

Implemented custom list rendering for both ordered and unordered markdown lists to avoid Avalonia parent tracking issues and provide consistent formatting.

## Syntax

### Unordered Lists

```markdown
- Item 1
- Item 2
- Item 3
```

Or with `*` or `+`:

```markdown
* Item with asterisk
+ Item with plus
- Item with dash
```

### Ordered Lists

```markdown
1. First item
2. Second item
3. Third item
```

## Visual Design

### Unordered Lists
- **Bullet**: `•` (bullet point character)
- **Spacing**: 8px between bullet and text
- **Alignment**: Bullet top-aligned with text

### Ordered Lists
- **Numbers**: `1.` `2.` `3.` etc.
- **Spacing**: 8px between number and text
- **Alignment**: Number top-aligned with text
- **Width**: Minimum 20px for number column

**List Styling**:
- **Item Spacing**: 4px between items
- **Text Wrapping**: Enabled (long items wrap)
- **Margin**: 8px top and bottom (visual separation)
- **Inline Formatting**: Supports **bold**, *italic*, `code` in items

## Implementation

### Detection

```csharp
private static bool IsListMarkdown(string text)
{
    var firstLine = lines[0].TrimStart();
    
    // Unordered: -, *, +
    if (firstLine.StartsWith("- ") || 
        firstLine.StartsWith("* ") || 
        firstLine.StartsWith("+ "))
        return true;
    
    // Ordered: 1. 2. 3.
    if (char.IsDigit(firstLine[0]) && 
        firstLine.Contains("."))
        return true;
}
```

### Parsing Logic

The custom renderer:
1. **Detect list type** - Check first line for ordered vs unordered
2. **Parse line-by-line** - Extract items by marker (-, *, +, 1., 2., etc.)
3. **Extract item text** - Get content after marker
4. **Parse inline formatting** - Support **bold**, *italic*, `code` in items
5. **Create layout** - Horizontal StackPanel with bullet/number + content
6. **Handle mixed content** - Support non-list text before/after list

### List Item Structure

```csharp
StackPanel (Horizontal)
  ├─ TextBlock (bullet/number)
  │   ├─ Text: "•" or "1."
  │   ├─ MinWidth: 20px
  │   └─ VerticalAlignment: Top
  └─ TextBlock (content)
      ├─ TextWrapping: Wrap
      ├─ Inline formatting
      └─ HorizontalAlignment: Stretch
```

## Features

### Inline Formatting Support

List items can contain inline markdown:

```markdown
- This is **bold** text
- This has *italic* styling
- This includes `inline code`
```

### Mixed List Types

Can detect and render different marker types:

```markdown
- Dash item
* Asterisk item
+ Plus item
```

All render with bullet points (•).

### Long Items

Items wrap naturally:

```markdown
- This is a very long list item that will wrap to multiple lines when the window is narrow enough that it cannot fit on a single line
```

### Mixed Content

```markdown
Some intro text

- List item 1
- List item 2

More text after list
```

### Edge Cases

**Empty List Item**:
```markdown
- 
- Item with content
```
Renders empty bullet (valid)

**No Space After Marker**:
```markdown
-Item (invalid)
- Item (valid)
```
Only valid syntax with space is detected.

**Multi-digit Numbers**:
```markdown
10. Tenth item
99. Ninety-ninth item
```
Works correctly.

## Benefits

1. **No Parent Conflicts**: Fresh StackPanel + TextBlock for each item
2. **Consistent Styling**: Standard bullet/number formatting
3. **Inline Support**: Nested formatting works correctly
4. **Performance**: Simple layout, no complex nested controls
5. **Reliable**: Works for ordered and unordered lists

## Testing

### Test Cases

**1. Unordered List**:
```markdown
- Item 1
- Item 2
- Item 3
```
Expected: Three items with bullets

**2. Ordered List**:
```markdown
1. First
2. Second
3. Third
```
Expected: Three items with numbers

**3. With Formatting**:
```markdown
- **Bold** item
- *Italic* item
- `Code` item
```
Expected: Bullets with styled text

**4. Long Items**:
```markdown
- This is a very long item that should wrap
- Short
```
Expected: First item wraps, maintains bullet alignment

**5. Mixed Content**:
```markdown
Header text

- Item 1
- Item 2

Footer text
```
Expected: Text, list, text (in order)

**6. Message Deletion**:
- Send list message
- Delete it
- Expected: No blanking, chat remains functional

### Debug Output

Look for:
```
[ZTalkMarkdownViewer] Using custom list renderer
[CustomList] Error: <if any>
```

## Architecture

### Before (Potential Issues)

```
- Item → MarkdownParser → List → MarkdownRenderer →
StackPanel (potential reuse) → Possible parent issues
```

### After (Working)

```
- Item → IsListMarkdown? → YES →
Parse manually → Extract items → Create layout →
Fresh StackPanel + TextBlock → SUCCESS
```

## Technical Details

### List Container Structure

```
StackPanel (list container)
  ├─ Spacing: 4px
  ├─ Margin: 8px top/bottom
  └─ Children:
      ├─ StackPanel (item 1, horizontal)
      │   ├─ TextBlock (bullet/number)
      │   └─ TextBlock (content with inlines)
      ├─ StackPanel (item 2, horizontal)
      │   └─ ...
      └─ StackPanel (item N, horizontal)
          └─ ...
```

### Bullet/Number Rendering

**Unordered**:
```csharp
new TextBlock {
    Text = "•",
    MinWidth = 20,
    VerticalAlignment = Top
};
```

**Ordered**:
```csharp
new TextBlock {
    Text = $"{index}.",  // 1. 2. 3. etc.
    MinWidth = 20,
    VerticalAlignment = Top
};
```

### Inline Parsing

List items parse their content for inline elements:

```csharp
var doc = _parser.Parse(itemText);
if (doc.Blocks[0] is Paragraph para) {
    foreach (var inline in para.Inlines) {
        var run = CreateInlineRun(inline);
        content.Inlines.Add(run);
    }
}
```

Reuses existing `CreateInlineRun()` method.

## Use Cases

### Task Lists

```markdown
- Complete feature A
- Test feature B
- Deploy to production
```

### Shopping Lists

```markdown
- Milk
- Bread
- Eggs
```

### Ordered Instructions

```markdown
1. Open the app
2. Navigate to settings
3. Change the option
4. Save changes
```

### Feature Lists

```markdown
- **Performance**: 50% faster
- **Security**: End-to-end encryption
- **UI**: New dark theme
```

## Future Enhancements

If needed:
- **Nested Lists**: Support indented sub-lists
- **Task Lists**: Add checkbox for `- [ ]` and `- [x]` syntax
- **Custom Bullets**: Different bullet styles per level
- **List Styles**: Different number formats (a. b. c. or i. ii. iii.)
- **Smart Numbers**: Auto-numbering regardless of input numbers

## Limitations

1. **No Nesting**: Nested/indented lists not currently supported
2. **Single Level Only**: All items at same level
3. **No Task Checkboxes**: `- [ ]` syntax not implemented
4. **Simple Markers**: Only -, *, + and 1. 2. 3. formats

These are acceptable for P2P messaging use case. Nested lists are rare in chat.

## Related Files

- `Controls/Markdown/ZTalkMarkdownViewer.cs` - Custom renderer
- `Controls/Markdown/MarkdownRenderer.cs` - Original (may still use for fallback)
- `Controls/Markdown/RenderModel.cs` - List and ListItem models
- `docs/CUSTOM-QUOTE-RENDERING.md` - Quote solution
- `docs/markdown-code-blocks.md` - Code block solution
- `docs/spoiler-implementation.md` - Spoiler solution
- `docs/markdown-headers.md` - Header solution

## Status

✅ **IMPLEMENTED AND READY FOR TESTING**

Build: SUCCESS (4.9s)
Compile Errors: NONE
Supports: Ordered (1. 2. 3.) and unordered (- * +) lists
Inline: **bold**, *italic*, `code` in items
Layout: Bullet/number + wrapped text
Spacing: 4px between items, 8px margins
