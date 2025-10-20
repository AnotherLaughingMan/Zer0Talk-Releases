# ZTalk Theme Editor - User Guide

**Version**: 1.0.0  
**Last Updated**: October 2025

---

## Table of Contents

1. [Introduction](#introduction)
2. [Getting Started](#getting-started)
3. [Editing Colors](#editing-colors)
4. [Working with Gradients](#working-with-gradients)
5. [Creating Custom Themes](#creating-custom-themes)
6. [Importing & Exporting Themes](#importing--exporting-themes)
7. [Tips & Best Practices](#tips--best-practices)

---

## Introduction

The **ZTalk Theme Editor** lets you customize every color and gradient in ZTalk's interface. Create professional-looking themes in minutes with an intuitive interface, friendly names, and quick preset options.

### What You Can Do

✅ **Edit Colors** - Change any color with clear, friendly names (no cryptic codes)  
✅ **Customize Gradients** - Beautiful gradient editor with 6 ready-made presets  
✅ **Clear Gradients** - One-click removal of gradient effects  
✅ **Start from Scratch** - Blank template with neutral colors  
✅ **Save & Share** - Export themes as .zttheme files  
✅ **Load Themes** - Import themes from others or your own backups

---

## Getting Started

### Opening the Theme Editor

1. Open **ZTalk**
2. Click the **🎨 Theme Editor** icon in the left sidebar.

A new Theme Editor window opens with all your theme customization tools.

### Understanding the Interface

The Theme Editor has three main sections:

**Left Panel - Colors & Gradients**
- **📝 Theme Metadata** section for editing theme info
- **🎨 Color Overrides** section shows all customizable colors with friendly names
- **🌈 Gradients** section shows gradient options with visual previews
- Each color shows its friendly name (like "Main Background") with the technical name below

**Right Panel - Theme Info**
- Shows current theme information and metadata
- Displays edit status (undo/redo availability, current editing mode)
- Shows recent colors palette for quick reuse

**Top Toolbar**
- **New Blank Theme** (+ icon) - Start with the blank template
- **Import Theme** (folder icon) - Load a theme file
- **Search Drives** (magnifying glass icon) - Find .zttheme files on your computer
- **Save** (floppy disk icon) - Save current theme (if it has a file path)
- **Save As** (floppy + arrow icon) - Save theme as a new file
- **Export Modified** (export arrow icon) - Export your edited theme
- **Undo/Redo** (↶/↷ icons) - Undo or redo color changes

---

## Editing Colors

### How to Edit a Color

1. Find the color in the **🎨 Color Overrides** section
2. You'll see a friendly name like **"Main Background"** with the technical name below in small text
3. The hex value is displayed as read-only in the Color Overrides panel
4. Click the **color preview square** (40x40 colored box) to open the color picker
5. The visual color picker popup opens with multiple ways to edit:
   - **Hex field**: Type the new hex color code (e.g., `#2A2A2A`)
   - **Visual picker**: Drag on the large saturation/value square
   - **Sliders**: Adjust Hue, Saturation, Value, and Alpha sliders
   - **RGB inputs**: Type exact Red, Green, Blue values (0-255)
6. Click **Apply** to save your changes, or **Cancel** to discard

### Color Format Guide

Colors use hexadecimal (hex) format:

| Format | Example | Description |
|--------|---------|-------------|
| `#RRGGBB` | `#FF0000` | Standard format (Red, Green, Blue) |
| `#RGB` | `#F00` | Short format (automatically expands) |

**Examples:**
- `#1C1C1C` - Dark grey (good for backgrounds)
- `#FFFFFF` - White (for text)
- `#FF6B6B` - Warm red (for accents)
- `#4FACFE` - Bright blue

**Quick Tips:**
- Colors must start with `#`
- Use 3 or 6 characters after the `#`
- Valid characters: 0-9, A-F

### Understanding Color Names

Instead of cryptic codes like "App.Background", you'll see clear names:

- **Main Background** - The main window background
- **Accent Color (Primary Highlight)** - Buttons, links, highlights
- **Text on Background** - Main text color
- **Border/Separator Lines** - Dividers and borders
- **Button Background** - Default button color
- **Success/Confirmation Color** - Success messages
- And many more...

The technical name (like `App.Background`) appears below in small grey text for reference.

---

## Working with Gradients

### What Are Gradients?

Gradients are smooth color transitions that add depth and visual interest. For example, the title bar can fade from dark blue at the top to lighter blue at the bottom.

### Finding Gradients

Scroll down to the **🌈 Gradients** section. You'll see gradients with friendly names:

- **Title Bar Gradient** - The top bar of ZTalk windows

### Editing a Gradient

**Step 1: Click Edit**
1. Find the gradient you want to customize
2. Click the **✏️ Edit** button
3. The gradient editor expands

**Step 2: Choose Your Method**

**Option A: Use a Preset (Quick but Sometimes Quirky)**
1. Scroll down to **Apply Preset** ComboBox
2. Click the dropdown to see preset options with visual previews
3. Choose from 6 beautiful presets:
   - **Sunset** - Warm red to yellow (135° angle)
   - **Ocean** - Cool blue to cyan (180° angle)
   - **Forest** - Green to teal (90° angle)
   - **Purple Haze** - Purple to pink (45° angle)
   - **Fire** - Bold red to orange (0° angle)
   - **Ice** - Soft blue gradient (270° angle)
4. Each preset shows a preview bar with the gradient colors
5. Selecting a preset should automatically fill in the Start Color, End Color, and Angle fields

**Note**: The preset feature is currently semi-working with some quirks. If a preset doesn't apply correctly, try selecting it again or manually enter the colors using Option B below.

**Option B: Type Custom Colors**
1. **Start Color**: Type a hex color (e.g., `#FF6B6B`) or click the color square to use the color picker
2. **End Color**: Type a hex color (e.g., `#FFD93D`) or click the color square to use the color picker
3. **Angle**: Type 0-360
   - `0°` = Left to right (Start to End)
   - `90°` = Bottom to top
   - `180°` = Right to left
   - `270°` = Top to bottom

**Note**: Unlike Color Overrides, gradient hex values can still be edited directly, though using the color picker is recommended for better accuracy.

**Step 3: Save**
- Click **💾 Save** to apply the gradient
- Click **❌ Cancel** to discard changes

### Removing a Gradient

Want a solid color instead of a gradient? Use the **Clear Gradient** button:

1. Click **✏️ Edit** on the gradient
2. Click **🧹 Clear Gradient** button
3. Both Start and End colors are set to your theme's Main Background color
4. This creates a "flat" look (no gradient visible)
5. Click **💾 Save**

**Why Clear Instead of Delete?**
Setting both colors the same is simpler than complex on/off settings. You can always add the gradient back by changing the colors again.

### Gradient Direction Guide

The **Angle** controls which way the gradient flows:

```
    270° (Top → Bottom)
       ↓
       
180° ← ■ → 0° (Left ↔ Right)
       
       ↑
     90° (Bottom → Top)
```

**Common Angles:**
- **0°** - Dynamic left-to-right sweep (Start to End)
- **90°** - Bottom-to-top rise (upward energy)
- **270°** - Subtle top-to-bottom fade (most natural)
- **45°** or **135°** - Diagonal energy
- **180°** - Right-to-left (reversed feel)

**Important Note**: Gradient visibility depends heavily on your color choices. Acute and obtuse angles (like 30°, 45°, 135°, 150°) may be barely noticeable with similar colors but very dramatic with contrasting colors. High-contrast gradients make any angle direction obvious, while subtle color transitions work best with angles that match your UI flow (0° for horizontal elements, 270° for vertical elements).

---

## Creating Custom Themes

### Starting from the Blank Template

The easiest way to create a custom theme is to start with the Blank Template:

**What You Get:**
- 21 neutral grey colors ready to customize
- 1 title bar gradient (neutral grey)
- Professional starting point

**How to Use It:**

1. Click the **New Blank Theme** button (+ icon) in the toolbar
2. The template loads with all grey colors and shows "🔒 Built-in Theme (Read-Only)" badge
3. Edit the colors one by one:
   - Start with **Main Background** (sets the tone)
   - Then **Text on Background** (for readability)
   - Then **Accent Color** (for personality)
   - Then customize the rest as desired
4. Edit the **Title Bar Gradient**:
   - Use a preset for quick results
   - Or create custom colors
   - Or clear it for a solid title bar
5. Click **💾 Save As** when done
6. Name your theme (e.g., "My Dark Blue Theme")
7. Choose where to save the .zttheme file

**Your theme is created!** You can now import it anytime.

### Customizing an Existing Theme

You can also modify the blank template or imported themes:

1. The current theme loads in the Theme Editor
2. Make your color changes
3. Customize gradients if desired
4. Click **💾 Save As** to create a new theme file
5. Your changes are saved without affecting the original

---

## Importing & Exporting Themes

### Exporting (Saving) Your Theme

**Save As:**
1. After making edits, click **💾 Save As**
2. Choose a location on your computer
3. Name your file (e.g., `ocean-blue-theme.zttheme`)
4. Click **Save**

**What Gets Saved:**
- All color customizations
- All gradient customizations  
- Theme name and description
- Everything needed to recreate your theme

**File Format:**
- `.zttheme` extension
- JSON format (human-readable)
- Small file size (~10-30 KB)

### Importing (Loading) Themes

**Load a Theme:**
1. Click **📁 Import Theme** button
2. Browse to your .zttheme file
3. Select it and click **Open**
4. The theme loads into the editor
5. You'll see all the colors and gradients
6. Make changes if desired, or use as-is

**Where to Get Themes:**
- Create your own
- Download from friends
- Share via email, Discord, etc.
- Future: Theme marketplace (coming soon!)

### Sharing Your Themes

To share a theme with others:
1. Save your theme as a .zttheme file
2. Send the file to others via:
   - Email attachment
   - Discord/Slack
   - Cloud storage link (Dropbox, Google Drive)
   - USB drive
3. Recipients import it using **📁 Import Theme**

**That's it!** No complicated installations or setup.

---

## Tips & Best Practices

### Creating Great Color Schemes

**Start with a Base Color:**
1. Choose your Main Background color first
2. This sets the mood (dark, light, warm, cool)
3. Build other colors around it

**Ensure Readability:**
- Text should clearly contrast with backgrounds
- Test with different lighting conditions
- Dark text on light backgrounds OR light text on dark backgrounds
- Avoid low-contrast combinations (grey on grey)

**Use Accent Colors Sparingly:**
- Pick ONE main accent color
- Use it for buttons, links, highlights
- Too many bright colors = visual chaos

**Test Your Theme:**
- Use it for a few hours before finalizing
- Check it during daytime and nighttime
- Make sure chat messages are readable
- Ensure buttons are clearly visible

### Gradient Best Practices

**Subtle is Usually Better:**
- Use similar colors for a professional look
- Example: `#2A2A2A` → `#1C1C1C` (subtle dark grey)
- Avoid extreme contrasts unless intentional

**Match Your Theme:**
- If your theme is cool (blues), use cool gradients
- If your theme is warm (reds/oranges), use warm gradients
- Consistency = polish

**When to Use Gradients:**
- Title bars (adds depth without distraction)
- Large background areas
- Special UI elements you want to highlight

**When to Skip Gradients:**
- Minimalist themes (use Clear Gradient)
- High-contrast themes
- When you prefer flat design

### Individual Color Editing Workflow

**Quick Individual Color Editing:**
1. Click the color preview square to open the visual color picker
2. Adjust using the saturation/value picker or sliders
3. Type exact hex codes in the hex field within the color picker popup
4. Click **Apply** to save

**Copy/Paste Individual Colors:**
- Click the **copy icon** next to any color to copy it
- Click the **paste icon** next to another color to apply the copied color
- Great for creating consistent color schemes

### Version Your Themes

**Save Iterations:**
- `my-theme-v1.zttheme` - First version
- `my-theme-v2.zttheme` - After tweaks
- `my-theme-final.zttheme` - Polished version

**Why Version:**
- Go back to earlier versions if needed
- Try experimental changes safely
- Track your evolution as a designer

---

## Troubleshooting

### "Color format is invalid"

**Problem:** Entered a color but got an error

**Solution:**
- Make sure it starts with `#`
- Use only 3 or 6 characters after the `#`
- Valid characters: 0-9, A-F
- Examples: `#FFF` or `#FFFFFF` (both work)

### Gradient Preset ComboBox Behavior

**Problem:** After selecting a preset, the ComboBox selection clears

**This is Normal:** The ComboBox resets after applying a preset so you can select another one.

**What's Happening:**
- The preset colors ARE being applied to the Start Color, End Color, and Angle fields
- You can see the gradient preview update in the UI
- The ComboBox clears to allow selecting the same or different preset again
- This is standard behavior for action-based ComboBoxes

### Theme Doesn't Look Right After Import

**Check:**
1. Make sure you imported the correct file
2. Verify the file wasn't corrupted during transfer
3. Try opening the .zttheme file in a text editor to check it's valid JSON
4. Re-export from the source if needed

### Want to Reset to Default

**Two Options:**

**Option 1: Reload Built-in Theme**
1. Close Theme Editor
2. Click the **🎨 Theme Editor** icon in the left sidebar to reopen
3. Click **📁 Import Theme** and load a different theme

**Option 2: Start Fresh**
1. Click **📄 New from Blank**
2. Start over with neutral colors

---

## Quick Reference

### Common Hex Colors

| Color | Hex Code | Use For |
|-------|----------|---------|
| Black | `#000000` | Deep backgrounds |
| Dark Grey | `#1C1C1C` | Main backgrounds |
| Medium Grey | `#2A2A2A` | Cards, panels |
| Light Grey | `#E0E0E0` | Text on dark |
| White | `#FFFFFF` | Text on dark |
| Red | `#FF0000` | Errors, alerts |
| Green | `#00FF00` | Success |
| Blue | `#0000FF` | Links, accents |
| Warm Red | `#FF6B6B` | Friendly accent |
| Cool Blue | `#4FACFE` | Professional accent |

### Gradient Preset Quick Reference

| Preset | Start Color | End Color | Angle | Best For |
|--------|-------------|-----------|-------|----------|
| **Sunset** | #FF6B6B | #FFD93D | 135° | Warm, energetic themes |
| **Ocean** | #4FACFE | #00F2FE | 180° | Cool, professional themes |
| **Forest** | #38EF7D | #11998E | 90° | Natural, calming themes |
| **Purple Haze** | #A18CD1 | #FBC2EB | 45° | Creative, modern themes |
| **Fire** | #FF0844 | #FFBC0D | 0° | Bold, attention-grabbing |
| **Ice** | #E0EAFC | #CFDEF3 | 270° | Subtle, minimalist themes |

### Button Quick Reference

| Button | What It Does |
|--------|--------------|  
| **Color Preview Square** | Click to open visual color picker |
| **Apply** | Save color picker changes |
| **Cancel** | Discard color picker changes |
| **Copy Icon** | Copy a color for pasting elsewhere |
| **Paste Icon** | Paste previously copied color |
| **Edit Icon** (gradient) | Start editing a gradient |
| **Save Icon** (gradient) | Save gradient changes |
| **Cancel Icon** (gradient) | Cancel gradient editing |
| **Clear Gradient** | Set both colors the same (removes gradient effect) |
| **New Blank Theme** (+) | Load the blank template |
| **Save** (floppy disk) | Save current theme file |
| **Save As** (floppy + arrow) | Export theme as new file |
| **Import Theme** (folder) | Load a theme file |
| **Export Modified** (export arrow) | Export your edited theme |
| **Undo/Redo** (↶/↷) | Undo or redo color changes |---

## Getting Help

### Resources

- **This User Guide** - Comprehensive instructions
- **In-app Tooltips** - Hover over buttons for quick help
- **Community Discord** - Ask questions, share themes

### Reporting Issues

Found a bug? Please report:
1. What you were trying to do
2. What happened instead
3. Screenshots if possible
4. Your theme file if relevant

---

## What's Next?

### Coming Soon

🚀 **More Gradient Options**
- Add new gradients to your theme
- Multi-color gradients (3+ colors)
- Radial gradients

🚀 **Visual Color Picker**
- Click and drag to choose colors
- Color wheel interface
- Harmony suggestions

🚀 **Theme Marketplace**
- Browse community themes
- One-click installation
- Rate and review themes

---

**Happy Theming!** 🎨

Create beautiful themes and share them with the ZTalk community.

---

**End of User Guide**

---

## Understanding the Theme Editor

The **Theme Editor** displays and allows you to customize all aspects of your theme.

### Viewing Theme Information

**Theme Metadata Section**:
- **Theme ID**: Internal identifier (e.g., "my-custom-theme")
- **Display Name**: User-friendly name
- **Description**: Brief description of the theme
- **Version**: Theme version number (e.g., "1.0.0")
- **Author**: Theme creator

**Color Overrides Section**:
- Shows all customizable colors
- Format: Resource Key → Hex Color → Color Swatch
- Example: `App.Background → #1E1E1E → [■]`

**Gradients Section**:
- Shows all gradient definitions
- Format: Resource Key → Start Color → End Color (Angle)
- Example: `TitleBar.Gradient → #FF6B6B → #FFD93D (135°)`

### Understanding Resource Keys

Resource keys identify UI elements:
- `App.Background` - Main application background
- `App.Surface` - Surface elements (cards, panels)
- `App.Accent` - Accent color (highlights, focus)
- `App.Text` - Primary text color
- `TitleBar.Background` - Title bar color

---

## Editing Colors

### Single Color Edit

**Steps**:
1. Find the color you want to edit in the **Color Overrides** list
2. Click the **color preview square** (40x40 colored box) to open the color picker
3. The visual color picker popup opens where you can:
   - Edit the hex value in the **Hex field**
   - Use the visual picker by dragging on the saturation/value square
   - Adjust the HSV sliders
   - Enter RGB values directly
4. Enter a new hex color value in the color picker:
   - Short format: `#RGB` (e.g., `#F00` for red)
   - Standard format: `#RRGGBB` (e.g., `#FF0000`)
   - With alpha: `#AARRGGBB` (e.g., `#80FF0000` for semi-transparent red)
5. Click **Apply** to save or **Cancel** to discard

**Note**: The hex values shown in the Color Overrides panel are read-only. All editing is done through the color picker popup.

### Color Format Reference

| Format | Example | Description |
|--------|---------|-------------|
| `#RGB` | `#F00` | Short form (3 digits) |
| `#ARGB` | `#8F00` | Short with alpha |
| `#RRGGBB` | `#FF0000` | Standard (6 digits) |
| `#AARRGGBB` | `#80FF0000` | Standard with alpha |

### Validation

Colors are validated before saving:
- Must start with `#`
- Must be 3, 4, 7, or 9 characters long
- Must contain only valid hex digits (0-9, A-F)

**Invalid Examples**:
- ❌ `FF0000` (missing #)
- ❌ `#GGG` (invalid hex)
- ❌ `#12345` (wrong length)

### Undo/Redo

Every color edit is tracked in the undo stack:

**Undo**: Click **↺** or press `Ctrl+Z`
- Reverts the last color change
- Can undo up to 100 operations

**Redo**: Click **↻** or press `Ctrl+Y`
- Re-applies an undone change
- Redo stack clears when you make a new edit

**Revert All**: Click **↺ Revert All**
- Undoes ALL color edits at once
- Returns all colors to their original values

---



## Editing Gradients

Gradients add beautiful color transitions to UI elements like the title bar, backgrounds, and buttons. The Theme Editor makes it easy to customize these gradients with a visual editor and ready-made presets.

### What Are Gradients?

Gradients are smooth color transitions that can make your theme look more dynamic and professional. For example:
- **Title Bar**: A gradient from dark blue to light blue creates depth
- **Buttons**: A subtle gradient can make buttons feel more clickable
- **Backgrounds**: Gradients add visual interest without being distracting

### Where Gradients Appear in Your Theme

When you open the Theme Editor, scroll down to the **🌈 Gradients** section. You'll see a list of available gradients with user-friendly names:

- **Title Bar Gradient** - The top bar of ZTalk windows

Each gradient shows its friendly name (like "Title Bar Gradient") with the technical resource key below in small text (like "App.TitleBarBackground").

### Customizing the Title Bar Gradient (Quick Start)

Let's customize the title bar gradient as an example. The same steps work for any gradient.

**Step 1: Find the Gradient**
1. Open Theme Editor (click **🎨 Theme Editor** icon in left sidebar)
2. Scroll down to **🌈 Gradients** section
3. Look for **Title Bar Gradient**
   - Below it you'll see the resource key: `App.TitleBarBackground`

**Step 2: Start Editing**
1. Click the **✏️ Edit** button next to Title Bar Gradient
2. The gradient editor expands showing:
   - Start Color (where the gradient begins)
   - End Color (where it ends)
   - Angle field (direction of the gradient)
   - Quick preset options

**Step 3: Change the Colors**

You have two ways to customize:

**Option A: Type Color Codes**
- **Start Color**: Type a hex color like `#FF6B6B` (warm red)
- **End Color**: Type a hex color like `#FFD93D` (warm yellow)
- The gradient updates as you type

**Option B: Use Quick Presets**
1. Click the **Preset dropdown**
2. Choose from beautiful ready-made gradients:
   - **Sunset**: Warm red to yellow (perfect for title bars)
   - **Ocean**: Cool blue to cyan
   - **Forest**: Green to teal
   - **Purple Haze**: Purple to pink
   - **Fire**: Bold red to orange
   - **Ice**: Soft blue gradient
3. The dropdown shows a preview of each gradient
4. Colors automatically fill in when you select one

**Step 4: Adjust the Direction**
- Type in the **Angle field** to change gradient direction:
  - **0°** = Left to right (horizontal, Start to End)
  - **90°** = Bottom to top
  - **180°** = Right to left
  - **270°** = Top to bottom
- The gradient updates when you save

**Step 5: Save Your Changes**
- Click **💾 Save** to apply the gradient
- Click **❌ Cancel** to discard changes
- Your title bar immediately shows the new gradient!

**💡 Pro Tip**: If you're working on a custom theme that you have actively set on ZTalk, saving your theme will live update the theme and you will see the changes immediately throughout the application.

### Understanding the Gradient Editor

When editing a gradient, you'll see:

```
┌─────────────────────────────────────────────────────┐
│ Title Bar Gradient                     [✏️ Edit]    │
│ App.TitleBarBackground                              │
│                                                      │
│ Start Color:                                         │
│ [#FF6B6B________________]  ← Type hex color here    │
│                                                      │
│ End Color:                                           │
│ [#FFD93D________________]  ← Type hex color here    │
│                                                      │
│ Angle (0-360°):                                      │
│ [135°_______________]  ← Type angle here             │
│                                                      │
│ Apply Preset:                                        │
│ [Select preset ▼]  ← Choose ready-made gradient     │
│                                                      │
│ [💾 Save] [❌ Cancel]                                │
└─────────────────────────────────────────────────────┘
```

### Gradient Tips & Tricks

**Creating Subtle Gradients**:
- Use similar colors (e.g., `#2A2A2A` to `#1C1C1C`)
- Keep the angle at 90° or 180° for smooth transitions
- Perfect for professional-looking themes

**Creating Bold Gradients**:
- Use contrasting colors (e.g., `#FF0844` to `#FFBC0D`)
- Try diagonal angles like 45° or 135°
- Great for eye-catching themes

**Matching Your Brand**:
- Use your brand colors for Start/End
- Keep the angle consistent across gradients
- Creates a cohesive look

**Testing Gradients**:
- The gradient applies immediately when you save
- Look at the title bar to see how it looks
- You can always edit again if you want to adjust

### Quick Preset Reference

| Preset Name | Colors | Best For |
|------------|--------|----------|
| **Sunset** | Red → Yellow | Warm, energetic themes |
| **Ocean** | Blue → Cyan | Cool, professional themes |
| **Forest** | Green → Teal | Natural, calming themes |
| **Purple Haze** | Purple → Pink | Creative, modern themes |
| **Fire** | Red → Orange | Bold, attention-grabbing themes |
| **Ice** | Light Blue → Lighter Blue | Subtle, minimalist themes |

### What If I Don't See Any Gradients?

**Blank Template**: Now includes a default neutral grey gradient for the Title Bar that you can customize.

**Other Themes**: Some minimal themes might not have gradients defined. To add gradients:
1. Start with the Blank Template (has Title Bar gradient)
2. Or import a theme that has gradients
3. Customize the gradients to your liking
4. Export your modified theme

### Common Questions

**Q: Can I remove a gradient and use a solid color instead?**
A: Yes! Just set the Start Color and End Color to the same value. For example, both `#2A2A2A` creates a solid dark grey.

**Q: How do I make the gradient go from left to right?**
A: Set the Angle to **0°** (this is the default Start to End direction).

**Q: Can I add more gradients?**
A: Currently, you can edit the gradients that come with the theme. Support for adding new gradients will come in a future update.

**Q: What's the difference between 0° and 180°?**
A: They go in opposite directions:
- **0°**: Start color at left, End color at right
- **180°**: Start color at right, End color at left

---

## Managing Metadata

Theme metadata includes name, description, author, and version.

### Viewing Metadata

Metadata displays in the **Theme Metadata** section:
- Read-only by default
- Shows current theme information

### Editing Metadata

**Start Editing**:
1. Click **✏️ Edit Metadata** button
2. Metadata section switches to edit mode
3. Fields become editable TextBoxes

**Editable Fields**:
- **Display Name** (required): User-friendly theme name
- **Description**: Multi-line description of theme
- **Version**: Version number (e.g., "1.0.0", "2.1.3")
- **Author**: Your name or organization

**Validation**:
- Display Name cannot be empty
- Other fields are optional

**Saving Changes**:
1. Modify desired fields
2. Click **💾 Save**
3. Metadata updates immediately
4. Success toast appears

**Cancelling**:
- Click **❌ Cancel** to discard all changes

**Important**: Metadata edits only affect the display. To persist changes, you must export the theme.

---

## Creating Custom Themes from Blank Template

The **Blank Template** feature allows you to create themes from scratch with neutral colors.

### What is the Blank Template?

The blank template is a built-in theme with:
- **21 neutral grey colors** - A clean slate for customization
- **1 neutral gradient** (Title Bar) - Ready to customize with your colors
- **Read-only protection** - Must use "Save As" to create custom theme
- **Tags**: ["template", "built-in"]

This gives you a professional starting point where you can focus on adding your own colors and style.

### Creating a Custom Theme

**Step 1: Load Blank Template**
1. Open Theme Editor (click **🎨 Theme Editor** icon in left sidebar)
2. Click **📄 New from Blank** button
3. Template loads into editor with 21 grey colors
4. Orange badge appears: "🔒 Built-in Theme (Read-Only)"
5. Toast notification: "📄 Blank template loaded - Start customizing!"

**Step 2: Customize Colors**
1. Edit individual colors (see [Editing Colors](#editing-colors) section)
2. Use Recent Colors and Copy/Paste for consistency
3. All edits are tracked in undo/redo stack

**Step 3: Optionally Edit Metadata**
1. Click **✏️ Edit Metadata** button
2. Set Display Name (required): e.g., "My Dark Purple Theme"
3. Add Description: e.g., "Custom purple theme for evening coding"
4. Set Author: Your name
5. Set Version: e.g., "1.0.0"
6. Click **💾 Save**

**Step 4: Save Your Custom Theme**
1. Click **💾 Save As...** button
2. File save dialog appears
3. Enter filename: e.g., `my-purple-theme.zttheme`
4. Choose save location
5. Click **Save**
6. Success toast: "💾 Theme saved as: my-purple-theme.zttheme"

**What's Created**:
- New `.zttheme` file with your customizations
- Unique theme ID (e.g., `custom-20251018143022`)
- ThemeType: Custom
- IsReadOnly: false (editable when re-imported)
- All your color and metadata edits
- All your gradient customizations (including the Title Bar gradient)

### Understanding Read-Only Themes

**Built-in Blank Template**:
- Cannot be overwritten directly
- Orange badge: "🔒 Built-in Theme (Read-Only)"
- Must use **💾 Save As...** to create custom version
- Protects default template from accidental modification

**Custom Themes**:
- Created via "Save As" or "Export Modified"
- Can be edited and re-saved
- No read-only badge
- Full control over all aspects

### Save As vs Export Modified

| Feature | 💾 Save As... | 📤 Export Modified |
|---------|---------------|-------------------|
| **Available When** | Always (especially for built-in) | After making edits |
| **Purpose** | Create new custom theme | Save edited version |
| **Creates New ID** | Yes | Yes |
| **Resets ThemeType** | To "Custom" | To "Custom" |
| **Best For** | Starting from blank/built-in | Saving edits to any theme |

**When to Use Save As**:
- Creating theme from blank template
- Creating variant of an imported theme
- Starting new theme project

**When to Use Export Modified**:
- Saving edits to imported theme
- Backing up work-in-progress
- Creating theme snapshots

## Exporting Themes

Export your theme to share with others or back up your customizations.

### Export Current Theme

**Use Case**: Export the base theme without edits

**Steps**:
1. Click **Export Theme** button (top toolbar)
2. File save dialog appears
3. Choose location and filename
4. Default: `{ThemeName}.zttheme`
5. Click **Save**
6. Success toast: "✅ Theme exported to {filename}"

**What's Included**:
- Theme metadata (ID, name, description, etc.)
- All color overrides
- All gradient definitions
- Theme version and compatibility info

### Export Modified Theme

**Use Case**: Export theme WITH all your edits

**Steps**:
1. Make color/gradient/metadata edits
2. Scroll to **Theme Management** section
3. Click **� Export Modified** button
4. File save dialog appears
5. Default: `{ThemeName}.zttheme`
6. Click **Save**
7. Success toast appears

**What's Included**:
- All your color edits
- All your gradient edits
- Updated metadata
- Unique theme ID (e.g., `custom-20251018143022`)

**File Format**:
- Extension: `.zttheme`
- Format: JSON
- Size: ~15-50KB typical
- Encoding: UTF-8

### Sharing Themes

To share your theme with others:
1. Export your modified theme
2. Send the `.zttheme` file via email, Discord, etc.
3. Recipients can import it using the Import Theme feature

---

## Importing Themes

Import themes created by you or shared by others.

### Import Workflow

**Steps**:
1. Click **Import Theme** button (top toolbar)
2. File open dialog appears
3. Select a `.zttheme` file
4. Theme validation occurs:
   - JSON format check
   - Required fields check
   - Color format validation
   - Compatibility check
5. Theme preview loads in Theme Inspector
6. Success toast: "✅ Theme '{name}' imported and previewed"

### Validation & Safety

**Automatic Checks**:
- ✅ JSON syntax validation
- ✅ Required field presence (ID, DisplayName)
- ✅ Color format validation (#RRGGBB, etc.)
- ✅ Version compatibility check

**Warnings**:
- Invalid colors are flagged but import proceeds
- Warnings shown in toast notification

**Errors (Import Blocked)**:
- Invalid JSON syntax
- Missing required fields
- Corrupted file data

### After Import

Once imported, the theme loads into the Theme Editor:
- View all colors and gradients
- Make additional edits if desired
- Export modified version
- Switch to different theme anytime

**Note**: Imported themes are previewed, not permanently registered. To use permanently, export and re-import or save to theme library (future feature).

---

## Tips & Best Practices

### 🎨 Color Selection

**Use Color Theory**:
- Complementary colors for contrast (opposite on color wheel)
- Analogous colors for harmony (adjacent on color wheel)
- Monochromatic schemes for subtle themes

**Accessibility**:
- Maintain 4.5:1 contrast ratio for text (WCAG AA)
- Test with color blindness simulators
- Avoid red/green as sole differentiators

**Consistency**:
- Use similar saturation levels across colors
- Keep lightness consistent within sections
- Use accent color sparingly for emphasis

### 💾 Backup & Version Control

**Before Major Changes**:
1. Export current theme → `mytheme-backup.zttheme`
2. Make edits
3. Export modified → `mytheme-v2.zttheme`
4. Compare by importing each

**Version Naming**:
- Use semantic versioning: `1.0.0`, `1.1.0`, `2.0.0`
- Increment major version for breaking changes
- Increment minor version for new features
- Increment patch version for fixes

### 🚀 Workflow Optimization

**Use Undo/Redo For**:
- Experimenting with colors
- Comparing variations quickly
- Stepping through design iterations

**Use Recent Colors For**:
- Maintaining color consistency
- Quick access to palette
- Reducing typing errors

### 🎭 Creating Themes From Scratch

**Method 1: Using Blank Template** (Recommended)
1. **Load blank template**: Click **📄 New from Blank** button
2. **Define palette**: Choose 5-8 core colors for your theme
3. **Edit backgrounds**: Start with `App.Background`, `App.Surface`
4. **Edit text colors**: `App.Text`, `App.SecondaryText`
5. **Edit accent color**: `App.Accent`, `App.AccentHover`
6. **Edit borders**: `App.Border`, `App.Divider`
7. **Update metadata**: Name, description, author, version
8. **Save custom theme**: Click **💾 Save As...** to create .zttheme file
9. **Test**: Import and use for 24 hours, adjust as needed
10. **Iterate**: Make changes and export new versions

**Method 2: Modify Existing Theme**
1. **Start with base theme**: Import existing theme similar to goal
2. **Define palette**: Choose 5-8 core colors
3. **Edit backgrounds**: Start with `App.Background`, `App.Surface`
4. **Edit text colors**: `App.Text`, `App.SecondaryText`
5. **Edit accent color**: `App.Accent`, `App.AccentHover`
6. **Edit borders**: `App.Border`, `App.Divider`
7. **Edit gradients**: Title bars, surfaces (if theme has gradients)
8. **Update metadata**: Name, description, author
9. **Export modified**: Click **📤 Export Modified** to save changes
10. **Test**: Import and use for 24 hours, adjust as needed

---

## Troubleshooting

### Color Won't Save

**Problem**: Click Save but color doesn't update

**Solutions**:
- ✅ Check color format is valid (#RRGGBB)
- ✅ Check for typos in hex code
- ✅ Look for validation toast message
- ✅ Try cancelling and re-editing

### Undo Button Disabled

**Problem**: Can't click Undo button

**Causes**:
- No edits made yet (undo stack empty)
- All edits already undone

**Solution**: Make a new edit first

### Theme Import Failed

**Problem**: "❌ Import failed" toast appears

**Solutions**:
- ✅ Verify file is valid JSON (open in text editor)
- ✅ Check file has `.zttheme` extension
- ✅ Ensure file not corrupted (re-download)
- ✅ Check file size < 5MB
- ✅ Look for specific error in toast message

### Gradient Section Shows "No gradients defined"

**Problem**: Gradient section is empty with message "📋 No gradients defined in this theme"

**This is Normal For**:
- Blank template (has no gradients by design)
- Minimal themes
- Themes you created without gradients

**This is NOT an Error**: Gradients are optional decorative elements. A theme without gradients is perfectly valid.

**To Add Gradients** (Workaround):
1. Import a theme that has gradients (e.g., legacy-dark)
2. Edit the gradient colors to match your theme
3. Export the modified theme
4. You now have a theme with gradients

**Note**: Direct gradient creation will be added in Phase 4.

### Gradient Editor Not Opening

**Problem**: Click Edit but gradient editor doesn't expand

**Causes**:
- Gradient definition is null (no data)
- Another gradient already being edited

**Solutions**:
- ✅ Check "GradientPreview" shows data
- ✅ Cancel other gradient edit first
- ✅ Restart application if stuck

### Metadata Won't Save

**Problem**: "Theme name cannot be empty" error

**Solution**:
- Display Name is required field
- Enter at least 1 character
- Other fields (Description, Version, Author) are optional

### Can't Overwrite Built-in Theme

**Problem**: "Built-in Theme (Read-Only)" badge shown, Export Modified doesn't update theme

**This is Expected Behavior**: The built-in blank template is protected from modification.

**Solution**:
1. Click **💾 Save As...** button instead
2. Create new custom theme with your edits
3. Custom themes can be modified freely
4. Import your custom theme to use it

### Save/Cancel Buttons Greyed Out During Color Edit

**Problem**: Click to edit color but Save/Cancel buttons are disabled

**Root Cause**: This was a known issue that has been fixed. Buttons now properly enable when editing colors.

**If Still Experiencing**:
- ✅ Ensure you're running the latest version
- ✅ Click away from the color field, then back
- ✅ Cancel and start edit again
- ✅ Restart application if persists

---

## Keyboard Shortcuts

| Action | Shortcut | Notes |
|--------|----------|-------|
| **Undo** | `Ctrl+Z` | Undo last color edit |
| **Redo** | `Ctrl+Y` | Redo undone edit |

**Note**: Individual color edits don't have shortcuts. Use toolbar buttons.

---

## Advanced Topics

### Theme File Structure

A `.zttheme` file is JSON with this structure:

```json
{
  "Id": "custom-20251018143022",
  "DisplayName": "My Custom Theme",
  "Description": "A theme I created",
  "Author": "Your Name",
  "Version": "1.0.0",
  "BaseVariant": "Dark",
  "ColorOverrides": {
    "App.Background": "#1E1E1E",
    "App.Text": "#FFFFFF"
  },
  "Gradients": {
    "TitleBar.Gradient": {
      "StartColor": "#FF6B6B",
      "EndColor": "#FFD93D",
      "Angle": 135.0
    }
  },
  "AllowsCustomization": true,
  "CreatedAt": "2025-10-18T14:30:22Z",
  "ModifiedAt": "2025-10-18T14:30:22Z"
}
```

### Manual Editing

You CAN edit `.zttheme` files manually:
1. Open in text editor
2. Modify JSON carefully
3. Validate JSON syntax
4. Import modified file

**Caution**: Manual editing risks creating invalid themes. Use the Theme Editor when possible.

### Theme Registry

Themes are stored in memory during session:
- Blank template: Built into application
- Custom themes: Loaded from .zttheme files
- Imported themes: Loaded from .zttheme files

**Future Enhancement**: Persistent theme library (Phase 4)

---

## Getting Help

### Documentation

- **This User Guide**: Complete feature documentation
- **Step Documentation**: Technical docs for each step (docs/ folder)
- **Validation Report**: Testing and quality assurance (PHASE3-STEP8-VALIDATION.md)

### Support Channels

- **GitHub Issues**: Report bugs or request features
- **Discord**: Community support and theme sharing
- **Email**: Direct support for complex issues

### Reporting Bugs

When reporting issues, include:
1. ZTalk version
2. Steps to reproduce
3. Expected vs actual behavior
4. Screenshots (if UI related)
5. Log files (if crash)
6. .zttheme file (if import issue)

---

## Glossary

**Theme**: A collection of colors, gradients, and metadata defining the application's appearance

**Theme Editor**: UI window for viewing and customizing theme structure

**Color Override**: A custom color value replacing the default

**Gradient**: A color transition defined by start color, end color, and angle

**Resource Key**: Identifier for a UI element (e.g., "App.Background")

**Hex Color**: Color format using hexadecimal notation (#RRGGBB)

**Undo Stack**: History of edits enabling undo/redo

**Batch Mode**: Mode allowing selection and editing of multiple colors

**Recent Colors**: LRU list of last 10 colors used

**.zttheme File**: JSON file format for theme storage and sharing

**Metadata**: Descriptive information about a theme (name, author, version, etc.)

**Preset**: Pre-defined gradient configuration for quick application

**Live Preview**: Temporary theme application without permanent registration

**Round-Trip**: Process of exporting and re-importing a theme without data loss

---

## Version History

**1.0.0** (October 2025)
- Initial release of Theme Editor (Phase 3)
- Theme viewing and editing
- Theme export/import
- Color editing with undo/redo
- Batch color editing
- Gradient editing with presets
- Metadata editing
- Export modified themes

---

## What's Next?

### Coming in Phase 4

🚀 **Theme Management Operations**
- Rename themes with validation
- Duplicate themes (create copies)
- Delete custom themes with confirmation
- Theme library organization

🚀 **Enhanced Gradient Editor**
- Visual gradient preview with live rendering
- Multi-stop color gradients (3+ colors)
- Radial gradient support
- Gradient creation (not just editing)
- Undo/redo for gradient edits

🚀 **Custom Color Picker**
- Visual color picker widget (HSV/RGB)
- Color palette tools
- Eyedropper for sampling colors
- Color harmony suggestions

🚀 **Theme Marketplace**
- Share themes with community
- Browse and download themes
- Rating and reviews

🚀 **Advanced Features**
- Custom font selection
- Typography scaling
- Animation preferences
- Sound theme integration

---

**Thank you for using ZTalk Theme Editor!** 🎨

Create amazing themes and share them with the community. Happy theming!

---

**End of User Guide**
