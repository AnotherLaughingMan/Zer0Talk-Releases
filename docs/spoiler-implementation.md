# Custom Spoiler Rendering

## Overview

Implemented custom spoiler rendering with the classic "black bar" style that reveals on click. Spoilers use the `||text||` syntax and provide an interactive click-to-reveal experience.

## Syntax

```markdown
||This text is hidden||

You can mix normal text with ||hidden spoilers|| inline.

||Multiple|| ||spoilers|| in one message!
```

## Visual Design

### Unrevealed State (Default)

- **Background**: Solid black (#000000)
- **Text Color**: Black (invisible against black background)
- **Cursor**: Hand pointer (indicates clickable)
- **Tooltip**: "SPOILER! Click to Reveal!" (shows on hover after 200ms)
- **Padding**: 4px horizontal, 2px vertical
- **Corner Radius**: 3px for subtle rounding

### Revealed State (After Click)

- **Background**: Semi-transparent dark gray (rgba(68, 68, 68, 0.6))
- **Text Color**: Light gray (#DCDCDC) - readable
- **Cursor**: Hand pointer (indicates clickable to hide)
- **Tooltip**: "Click to Hide"
- **Padding/Radius**: Same as unrevealed
- **Toggleable**: Can be clicked again to re-hide

## Implementation

### Detection

```csharp
private static bool HasSpoilerMarkdown(string text)
{
    return text.Contains("||");
}
```

### Parsing Logic

The custom renderer:
1. **Scan for `||` markers** - Find start and end of spoiler regions
2. **Extract content** - Get text between `||` markers
3. **Handle normal text** - Render text before/after spoilers normally
4. **Mixed formatting** - Support **bold**, *italic*, etc. in non-spoiler parts
5. **Multiple spoilers** - Handle multiple `||text||` in same message

### Interactive Control

Each spoiler is wrapped in an `InlineUIContainer` containing:

```csharp
Border (black background, click handler)
  └─ TextBlock (spoiler text, initially black)
```

**Click Handler** (Toggle):
- **Reveal**: Changes text black → light gray, background solid black → semi-transparent gray, tooltip → "Click to Hide"
- **Hide**: Changes text light gray → black, background semi-transparent → solid black, tooltip → "SPOILER! Click to Reveal!"
- Maintains hand cursor in both states (always clickable)
- State persists for message lifetime
- Infinite toggle - can reveal/hide as many times as desired

### Tooltip Configuration

```csharp
ToolTip.SetTip(spoilerBorder, "SPOILER! Click to Reveal!");
ToolTip.SetShowDelay(spoilerBorder, 200); // 200ms delay
```

## Features

### Inline Spoilers

Works seamlessly with other inline elements:

```markdown
This is **bold** and this is ||spoiler|| and this is *italic*
```

Renders as:
- Normal text
- **Bold text** (styled)
- █████████ (black bar with tooltip)
- *Italic text* (styled)

### Multiple Spoilers

```markdown
||First spoiler|| and ||second spoiler||
```

Each spoiler is independently clickable - revealing one doesn't reveal others.

### Mixed Content

```markdown
Regular text here

||Hidden secret||

More regular text
```

### Edge Cases

**Unclosed Spoiler**:
```markdown
||This has no closing
```
Treated as literal text: `||This has no closing`

**Empty Spoiler**:
```markdown
||||
```
Ignored (no visual element created)

**Nested Formatting** (not currently supported):
```markdown
||**bold inside**||
```
Renders as plain text inside black bar. Could be enhanced in future.

## User Experience

### Discovery

1. User sees █████████ (black bar) in message
2. Hovers cursor → tooltip appears: "SPOILER! Click to Reveal!"
3. Cursor changes to hand pointer (clickable indicator)

### Reveal

1. User clicks black bar
2. Bar transitions to semi-transparent gray
3. Text becomes visible (light gray)
4. Tooltip changes to "Click to Hide"
5. Cursor remains as hand pointer (still interactive)

### Hide (Toggle Back)

1. User clicks revealed spoiler
2. Bar transitions back to solid black
3. Text becomes hidden (black on black)
4. Tooltip changes back to "SPOILER! Click to Reveal!"
5. Spoiler can be revealed again (infinite toggle)

### Persistence

- Revealed/hidden state persists for message lifetime
- Scrolling away and back maintains current state
- Each spoiler remembers its own state independently
- Toggleable infinite times

## Benefits

1. **No Parent Conflicts**: Uses `InlineUIContainer` with fresh controls
2. **Intuitive UX**: Classic black bar everyone recognizes
3. **Clear Feedback**: Tooltip + cursor make interaction obvious
4. **Performance**: Simple click handler, no complex state management
5. **Reliable**: Works for single or multiple spoilers per message

## Testing

### Test Cases

**1. Single Spoiler**:
```markdown
||secret text||
```
Expected: Black bar, tooltip on hover, reveals on click

**2. Multiple Spoilers**:
```markdown
||first|| and ||second|| and ||third||
```
Expected: Three independent black bars, each reveals separately

**3. Mixed Content**:
```markdown
Before ||spoiler|| after
```
Expected: Normal text, black bar, normal text

**4. With Formatting**:
```markdown
**Bold** and ||spoiler|| and *italic*
```
Expected: Styled text + black bar + styled text

**5. Interaction**:
- Hover → Tooltip "SPOILER! Click to Reveal!" appears
- Click → Text reveals, tooltip changes to "Click to Hide"
- Click again → Text hides, tooltip back to "SPOILER! Click to Reveal!"
- Repeat → Infinite toggle between hidden/revealed states

### Debug Output

Look for:
```
[ZTalkMarkdownViewer] Using custom spoiler renderer
[CustomSpoiler] Error: <if any>
```

## Architecture

### Before (Incomplete)

```
||text|| → MarkdigParser → TextInline (plain text) →
No spoiler functionality
```

### After (Working)

```
||text|| → HasSpoilerMarkdown? → YES →
Parse manually → CreateSpoilerInline →
Border + TextBlock + Click Handler → Interactive Spoiler
```

## Technical Details

### Control Hierarchy

```
TextBlock (message container)
  └─ InlineCollection
      ├─ Run (normal text)
      ├─ InlineUIContainer
      │   └─ Border (spoiler)
      │       └─ TextBlock (spoiler text)
      ├─ Run (more normal text)
      └─ InlineUIContainer (another spoiler)
          └─ ...
```

### Event Handling

```csharp
spoilerBorder.PointerPressed += (sender, e) => {
    if (!isRevealed) {
        // Reveal: black → visible
        isRevealed = true;
    } else {
        // Hide: visible → black
        isRevealed = false;
    }
};
```

Each spoiler has its own closure-captured `isRevealed` boolean that toggles between true/false.

## Future Enhancements

If needed:
- **Nested Formatting**: Support `||**bold**||` with styled spoiler text
- **Animations**: Fade transition when revealing
- **Theme Support**: Light/dark mode spoiler colors
- **Accessibility**: Keyboard reveal (Enter key)
- **Settings**: Option to reveal all spoilers at once

## Limitations

1. **No Nested Formatting**: Spoiler content is plain text only
2. **No Animations**: Instant toggle (could add CSS-like transitions)
3. **Per-Message State**: Revealed state not saved between sessions (resets on app restart)

These are acceptable for P2P messaging use case.

## Related Files

- `Controls/Markdown/ZTalkMarkdownViewer.cs` - Custom renderer
- `Controls/Markdown/MarkdownParser.cs` - Spoiler detection (bypassed)
- `docs/CUSTOM-QUOTE-RENDERING.md` - Quote solution
- `docs/markdown-code-blocks.md` - Code block solution

## Status

✅ **IMPLEMENTED AND READY FOR TESTING**

Build: SUCCESS (5.2s)
Compile Errors: NONE
Features: Click-to-toggle, dynamic tooltip, multiple spoilers, mixed content
UX: Classic black bar style with hand cursor, toggleable reveal/hide
Interaction: Infinite toggle - reveal/hide as many times as desired
