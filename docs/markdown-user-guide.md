# ZTalk Markdown Formatting Guide

## How to Use Markdown in ZTalk

### Option 1: Use the Formatting Buttons (Above Input Box)

Located just above your message input box, you'll see these buttons:

```
[B]  [I]  [U]  [S]  [>]  [</>]  [||]
```

- **B** = Bold (`**text**`)
- **I** = Italic (`*text*`)
- **U** = Underline (`__text__`)
- **S** = Strikethrough (`~~text~~`)
- **>** = Quote (`> text`)
- **</>** = Code (`` `code` `` or ` ```block``` `)
- **||** = Spoiler (`||text||`)

**How to use**:
1. Type or select text in the input box
2. Click a button to apply formatting
3. Send your message!

---

### Option 2: Use the Floating Toolbar (Discord-Style)

When you **select text** in the message input box, a floating toolbar appears above it:

```
┌────────────────────────────────────┐
│  [B] [I] [S]  │  [</>] [🔗]  │ [||] │
└────────────────────────────────────┘
```

**How to use**:
1. **Select any text** in your message
2. Toolbar appears automatically above selection
3. Click a button to format the selected text
4. Toolbar stays visible until you clear the selection

**Buttons**:
- **B** = Bold
- **I** = Italic  
- **S** = Strikethrough
- **</>** = Code
- **🔗** = Link (wraps in `[text](url)`)
- **||** = Spoiler

---

### Option 3: Type Markdown Syntax Directly

You can type markdown syntax directly and it will render automatically:

| Type This | You Get This |
|-----------|--------------|
| `**bold text**` | **bold text** |
| `*italic text*` | *italic text* |
| `~~strikethrough~~` | ~~strikethrough~~ |
| `` `inline code` `` | `inline code` |
| `# Heading 1` | Large heading |
| `## Heading 2` | Medium heading |
| `> quote` | Blockquote (indented with bar) |
| `- item` | Bullet list item |
| `1. item` | Numbered list item |
| `[text](url)` | Link (shows as underlined) |
| `\|\|spoiler\|\|` | Spoiler text |

---

### Code Blocks

For multi-line code, use triple backticks:

````
```
function hello() {
    return "world";
}
```
````

With language highlighting:

````
```javascript
console.log("Hello!");
```
````

---

### Tips & Tricks

**Combining Formats**:
- `**bold *and italic***` works!
- `` **bold with `code`** `` works too!

**Keyboard Selection**:
- Use `Shift+Arrow keys` to select text
- Floating toolbar appears automatically

**Button Behavior**:
- **No selection**: Inserts placeholder text (selected for you to type over)
- **With selection**: Wraps your selected text with formatting

---

### Security & Safety

✅ **Safe for P2P**: HTML is disabled, no external resources load automatically  
✅ **Links**: Shown as text with URL in parentheses (not clickable by default)  
✅ **Images**: Show placeholder emoji instead of loading external content

---

### Examples

**Simple message**:
```
Hey! Check out **this new feature** - it's *amazing*!
```

**With code**:
```
To install, run: `npm install ztalk`
```

**Quote with formatting**:
```
> **Important**: Don't forget to ~~backup~~ **backup** your data!
```

**Lists**:
```
Tasks:
- [x] Complete markdown system
- [x] Add floating toolbar
- [ ] Add more features
```

---

## Need Help?

If you're not seeing markdown formatting:
1. Make sure you're viewing received messages (not just the input)
2. Check that `UseMarkdig` is enabled (should be by default)
3. Try restarting the app

Enjoy enhanced messaging with markdown! 🎉
