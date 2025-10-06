# CRITICAL DEBUG: ItemsControl Recycling Breaking Markdown

**Date**: October 5, 2025  
**Issue**: Avalonia ItemsControl is recycling ZTalkMarkdownViewer controls, causing "Border already has visual parent" errors

---

## The Real Problem

### What's Happening:
1. User sends message with `> quote`
2. ItemsControl creates ZTalkMarkdownViewer for that message
3. Renderer creates Border control, adds to visual tree
4. User sends another message
5. **ItemsControl RECYCLES the first ZTalkMarkdownViewer** for the new message
6. Renderer tries to create Border again
7. **BUT** the old Border is still attached to the old StackPanel parent
8. Avalonia throws: "Border already has visual parent"
9. Rendering fails → blank message

### Why Deleting Causes Blanking:
1. User deletes message
2. ItemsControl removes that message's viewer
3. **But viewer is recycled and reused for next message**
4. Old content still attached
5. Visual tree corrupted
6. **All messages disappear until new message forces refresh**

---

## Fixes Attempted

### Fix #1: Per-Instance Parser/Renderer ✅ (PARTIAL)
- Changed static to instance members
- **Result**: Helps but doesn't solve ItemsControl recycling

### Fix #2: Explicit Content Cleanup ✅ (PARTIAL)
- Clear Content = null before reassigning
- **Result**: Helps but Avalonia still tracks old parent

### Fix #3: DetachedFromVisualTree Handler ✅ (PARTIAL)
- Clean up when control detached
- **Result**: Too late - damage already done

### Fix #4: Wrapper StackPanel ❌ (DOESN'T HELP)
- Wrap content in container panel
- **Result**: Still same parent issue

### Fix #5: Unique Control Names ✅ (IN TESTING)
- Add GUID-based names to prevent caching
- **Result**: TESTING NOW

---

## Next Steps If Still Broken

### Option A: Disable ItemsControl Virtualization
```xaml
<ItemsControl VirtualizationMode="None">
```
**Pros**: Prevents recycling completely
**Cons**: Performance hit with many messages

### Option B: Force Panel Recreation
Override ZTalkMarkdownViewer to destroy and recreate internal panel on every render

### Option C: Use Different Control Type
Replace ContentControl with custom Panel that manages children differently

### Option D: Implement Custom Quote Rendering
Bypass Markdig's BlockQuote entirely, render quotes ourselves with simple Border + TextBlock

---

## Testing This Build

1. Run app
2. Send: `> quote test`
3. Check if quote shows with blue border
4. Send another message
5. Delete first message
6. **Expected**: Other messages stay visible

If STILL broken, we need Option A (disable virtualization) or Option D (custom quote rendering).
