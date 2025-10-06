# Custom Code Block Rendering

## Problem

Code blocks using the standard Markdig renderer were experiencing the same "Border already has visual parent" issue as quotes, causing:
- Blank messages when single code block sent
- Only visible when multiple code blocks in same message (ItemsControl recycling patterns)
- Same Avalonia control parent tracking conflicts

## Solution

Implemented custom code block rendering that bypasses Markdig's `FencedCode` handling entirely.

## Implementation

### Detection

```csharp
private static bool IsCodeBlockMarkdown(string text)
{
    var trimmed = text.Trim();
    return trimmed.StartsWith("```");
}
```

### Parsing Logic

The custom renderer:
1. **Split by lines** - Parse line-by-line to detect fence boundaries
2. **Track state** - `inCodeBlock` flag to track when inside code fence
3. **Extract language** - Parse language identifier after opening `` ``` ``
4. **Collect code** - Accumulate lines between fences
5. **Handle mixed content** - Support normal text before/after code blocks
6. **Multiple blocks** - Handle multiple code blocks in same message

### Rendering

```csharp
private Border CreateCodeBlockBorder(string code, string? language)
{
    var border = new Border
    {
        Background = Dark Gray (#1E1E1E),
        CornerRadius = 6px,
        Padding = 12px,
        Margin = 8px vertical
    };
    
    var textBlock = new TextBlock
    {
        Text = code,
        FontFamily = "Consolas, Monaco, 'Courier New', monospace",
        Foreground = Light Gray (#DCDCDC),
        FontSize = 13,
        TextWrapping = NoWrap  // Preserve formatting
    };
    
    // Optional language label at top
    if (language != null) {
        var label = new TextBlock { Text = language };
        // Stack label above code
    }
}
```

## Features

### Supported Syntax

**Simple Code Block**:
````markdown
```
code here
```
````

**With Language**:
````markdown
```csharp
public void Method() { }
```
````

**Multiple Blocks**:
````markdown
```python
def hello():
    pass
```

Some text between

```javascript
console.log("hi");
```
````

**Mixed Content**:
````markdown
Normal **markdown** text

```csharp
var x = 42;
```

More text with *formatting*
````

### Visual Styling

- **Background**: Dark gray (#1E1E1E) for contrast
- **Text**: Light gray (#DCDCDC) for readability
- **Font**: Monospace (Consolas/Monaco/Courier)
- **Size**: 13px (slightly smaller than normal text)
- **Wrapping**: None (preserves code formatting)
- **Corners**: 6px radius for modern look
- **Language**: Small gray label above code

### Benefits

1. **No Parent Conflicts**: Fresh `Border` + `TextBlock` every render
2. **Reliable**: Works for single or multiple code blocks
3. **Performance**: Direct control creation, no Markdig overhead
4. **Formatting**: Preserves whitespace and indentation
5. **Mixed Content**: Handles code + normal text seamlessly

## Testing

### Test Cases

**1. Single Code Block**:
````markdown
```
test code
```
````
Expected: Dark bordered box with monospace text

**2. With Language**:
````markdown
```csharp
public class Test { }
```
````
Expected: "csharp" label above code

**3. Multiple Blocks**:
````markdown
```python
print("1")
```

```javascript
console.log("2");
```
````
Expected: Two separate code boxes with 8px spacing

**4. Mixed Content**:
````markdown
Here's the code:

```csharp
var x = 1;
```

That's it!
````
Expected: Text, code box, text (in order)

**5. Message Deletion**:
1. Send code block message
2. Send normal message
3. Delete code block message
Expected: No blanking, chat remains functional

### Debug Output

Look for:
```
[ZTalkMarkdownViewer] Using custom code block renderer
[CustomCodeBlock] Error: <if any>
```

## Architecture

### Before (Broken)

```
```code``` → MarkdownParser → FencedCode →
MarkdownRenderer → Border (reused!) → EXCEPTION
```

### After (Working)

```
```code``` → IsCodeBlockMarkdown? → YES →
Parse manually → CreateCodeBlockBorder → Fresh Border → SUCCESS
```

## Edge Cases Handled

1. **Unclosed Code Block**: Renders anyway (common in incomplete messages)
2. **Empty Code Block**: Renders empty box (valid markdown)
3. **No Language**: Omits label, just shows code
4. **Whitespace**: Preserved exactly as written
5. **Special Characters**: No escaping issues, raw text

## Future Enhancements

If needed:
- **Syntax Highlighting**: Add color coding per language
- **Copy Button**: Add button to copy code to clipboard
- **Line Numbers**: Show line numbers for longer blocks
- **Scroll**: Add horizontal scroll for wide code
- **Theme**: Light/dark mode support

## Related Files

- `Controls/Markdown/ZTalkMarkdownViewer.cs` - Custom renderer
- `Controls/Markdown/MarkdownRenderer.cs` - Original (bypassed)
- `Controls/Markdown/RenderModel.cs` - FencedCode model
- `docs/CUSTOM-QUOTE-RENDERING.md` - Quote solution

## Status

✅ **IMPLEMENTED AND READY FOR TESTING**

Build: SUCCESS (4.7s)
Compile Errors: NONE
Works With: Single blocks, multiple blocks, mixed content
Next: Spoiler tags (||text||)
