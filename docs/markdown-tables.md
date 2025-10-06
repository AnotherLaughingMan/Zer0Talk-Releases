# Custom Table Rendering

## Overview

Implemented custom table rendering with support for alignment, headers, and multiple rows. Tables integrate seamlessly with mixed markdown documents and avoid Avalonia parent tracking issues.

## Syntax

### Basic Table

```markdown
| Column 1 | Column 2 | Column 3 |
|----------|----------|----------|
| Row 1    | Data 1   | Info 1   |
| Row 2    | Data 2   | Info 2   |
```

### With Alignment

```markdown
| Left | Center | Right |
|:-----|:------:|------:|
| L1   |   C1   |    R1 |
| L2   |   C2   |    R2 |
```

**Alignment Syntax**:
- `:---` - Left aligned (default)
- `:---:` - Center aligned
- `---:` - Right aligned

## Visual Design

### Table Structure
- **Border**: 1px gray border around entire table
- **Corner Radius**: 4px for rounded corners
- **Margin**: 8px top and bottom
- **Background**: Semi-transparent gray for header row
- **Cell Borders**: 1px gray between columns
- **Cell Padding**: 8px horizontal, 6px vertical

### Header Row
- **Background**: Semi-transparent gray (rgba(100, 100, 100, 0.4))
- **Font Weight**: Bold
- **Bottom Border**: 1px separator below header

### Data Rows
- **Background**: Transparent
- **Font Weight**: Normal
- **Row Borders**: 1px gray between rows

## Implementation

### Detection

```csharp
private static bool IsTableMarkdown(string text)
{
    // Look for separator line: |---|---|
    var hasOnlyTableChars = trimmed.All(c => 
        c == '|' || c == '-' || c == ':' || char.IsWhiteSpace(c));
    
    return hasOnlyTableChars && trimmed.Count(c => c == '|') >= 2;
}
```

Tables are detected by finding the separator line with pipes and dashes.

### Parsing Logic

1. **Split lines** - Separate header, separator, and data rows
2. **Parse header** - Extract column names from first line
3. **Parse alignments** - Detect `:` in separator for alignment
4. **Parse data rows** - Extract cell content from remaining lines
5. **Create Grid** - Use Avalonia Grid with column definitions
6. **Apply styling** - Headers bold, cells aligned, borders added

### Cell Parsing

```csharp
private string[] ParseTableRow(string line)
{
    var trimmed = line.Trim();
    
    // Remove leading/trailing |
    if (trimmed.StartsWith("|")) trimmed = trimmed.Substring(1);
    if (trimmed.EndsWith("|")) trimmed = trimmed.Substring(0, trimmed.Length - 1);
    
    // Split by | and trim
    return trimmed.Split('|').Select(c => c.Trim()).ToArray();
}
```

### Alignment Detection

```csharp
private HorizontalAlignment[] ParseTableAlignments(string separator, int columnCount)
{
    foreach (cell in separator)
    {
        var startsWithColon = cell.StartsWith(":");
        var endsWithColon = cell.EndsWith(":");
        
        if (startsWithColon && endsWithColon)
            alignment = Center;
        else if (endsWithColon)
            alignment = Right;
        else
            alignment = Left;
    }
}
```

## Layout Structure

```
Border (table container, 1px border)
  └─ StackPanel (rows)
      ├─ Border (header row, gray background)
      │   └─ Grid
      │       ├─ Border (cell 1, left)
      │       ├─ Border (cell 2, center)
      │       └─ Border (cell 3, right)
      ├─ Border (1px separator)
      ├─ Border (data row 1)
      │   └─ Grid
      │       └─ Cells...
      └─ Border (data row 2)
          └─ Grid
              └─ Cells...
```

Each row is a Grid with equally-spaced columns, wrapped in a Border for styling.

## Features

### Column Alignment

All three alignments supported:

```markdown
| Left   | Center  | Right   |
|:-------|:-------:|--------:|
| Item 1 | Item 2  | Item 3  |
```

**Renders as**:
```
| Left   | Center | Right |  (visual example)
```

### Text Wrapping

Long cell content wraps automatically:

```markdown
| Column | Long Text |
|--------|-----------|
| A      | This is a very long piece of text that will wrap to multiple lines |
```

### Header Styling

Headers are visually distinct:
- **Bold text**
- **Gray background**
- **Separated by line**

### Flexible Columns

Tables adjust to content width:
- Columns share space equally (Star sizing)
- All columns stretch to fill available width

### Mixed with Other Markdown

Tables work seamlessly in mixed documents:

```markdown
# Data Table

Here's the results:

| Name  | Score | Grade |
|-------|------:|:-----:|
| Alice |    95 |   A   |
| Bob   |    87 |   B   |

More text below.
```

## Benefits

1. **No Parent Conflicts**: Fresh Grid + Borders for each table
2. **Proper Alignment**: Supports left, center, right alignment
3. **Visual Polish**: Borders, padding, header styling
4. **Text Wrapping**: Long content wraps naturally
5. **Mixed Markdown**: Works in documents with other elements

## Testing

### Test Cases

**1. Basic Table**:
```markdown
| A | B |
|---|---|
| 1 | 2 |
| 3 | 4 |
```
Expected: 2x2 table with headers

**2. With Alignment**:
```markdown
| Left | Center | Right |
|:-----|:------:|------:|
| L    | C      | R     |
```
Expected: Three columns with different alignments

**3. Long Content**:
```markdown
| Short | Long Content |
|-------|--------------|
| A     | This is a very long piece of text that should wrap |
```
Expected: Text wraps in second column

**4. In Mixed Document**:
```markdown
# Report

Some text

| Col1 | Col2 |
|------|------|
| A    | B    |

More text
```
Expected: Header, text, table, text in order

**5. Irregular Tables**:
```markdown
| A | B | C |
|---|---|---|
| 1 | 2 |
| 3 | 4 | 5 | 6 |
```
Expected: Handles missing/extra cells gracefully

### Debug Output

Look for:
```
[ZTalkMarkdownViewer] Using mixed markdown renderer
[CustomTable] Error: <if any>
[MixedMarkdown] Block render failed for table: <if any>
```

## Architecture

### Before (Not Implemented)

```
Table → MarkdigParser → No table support →
Falls back to plain text
```

### After (Working)

```
| Table | → Detect separator line → Parse header/data →
Create Grid with columns → Apply alignment/styling →
Fresh controls → SUCCESS
```

## Technical Details

### Grid Layout

```csharp
var row = new Grid();

// Equal-width columns
for (int i = 0; i < cells.Length; i++)
{
    row.ColumnDefinitions.Add(
        new ColumnDefinition { 
            Width = new GridLength(1, GridUnitType.Star) 
        }
    );
}

// Add cells
for (int i = 0; i < cells.Length; i++)
{
    var cell = CreateCell(cells[i]);
    Grid.SetColumn(cell, i);
    row.Children.Add(cell);
}
```

### Cell Styling

```csharp
var cellBorder = new Border
{
    BorderBrush = Gray,
    BorderThickness = new Thickness(left: i > 0 ? 1 : 0, 0, 0, 0),
    Padding = new Thickness(8, 6)
};

var cellText = new TextBlock
{
    Text = content,
    TextWrapping = Wrap,
    FontWeight = isHeader ? Bold : Normal,
    HorizontalAlignment = alignment
};
```

### Color Scheme

- **Borders**: rgb(80, 80, 80) - Medium gray
- **Header Background**: rgba(100, 100, 100, 0.4) - Semi-transparent
- **Data Background**: Transparent
- **Text**: Inherits from theme

## Use Cases

### Data Presentation

```markdown
| Product | Price  | Stock |
|---------|-------:|:-----:|
| Widget  | $19.99 |  Yes  |
| Gadget  | $29.99 |  No   |
```

### Comparison Tables

```markdown
| Feature  | Free | Pro  |
|----------|:----:|:----:|
| Storage  | 5GB  | 50GB |
| Support  | No   | Yes  |
```

### Schedule/Timeline

```markdown
| Time  | Event           |
|------:|-----------------|
| 09:00 | Meeting Start   |
| 10:30 | Break           |
| 11:00 | Presentation    |
```

### Test Results

```markdown
| Test Name    | Status | Time   |
|--------------|:------:|-------:|
| Unit Tests   | ✓ Pass | 2.3s   |
| Integration  | ✓ Pass | 15.7s  |
| E2E Tests    | ✗ Fail | 45.2s  |
```

## Future Enhancements

If needed:
- **Column Width Control**: Specify relative widths
- **Cell Styling**: Background colors per cell
- **Merged Cells**: Colspan/rowspan support
- **Sortable Tables**: Click header to sort
- **Inline Formatting**: **Bold**, *italic* in cells
- **Nested Tables**: Tables within cells

## Limitations

1. **No Inline Markdown**: Cell content is plain text only (could be enhanced)
2. **Equal Columns**: All columns same width (Star sizing)
3. **No Cell Merging**: Each cell is independent
4. **Simple Borders**: All borders same color/thickness
5. **No Sorting**: Static display only

These are acceptable for P2P messaging and documentation display.

## Related Files

- `Controls/Markdown/ZTalkMarkdownViewer.cs` - Table renderer + all custom renderers
- `docs/markdown-mixed-content.md` - Mixed markdown support
- `docs/CUSTOM-QUOTE-RENDERING.md` - Quote solution
- `docs/markdown-code-blocks.md` - Code block solution
- `docs/spoiler-implementation.md` - Spoiler solution
- `docs/markdown-headers.md` - Header solution
- `docs/markdown-lists.md` - List solution

## Status

✅ **IMPLEMENTED AND READY FOR TESTING**

Build: SUCCESS (4.8s)
Compile Errors: NONE
Supports: Headers, alignments, multiple rows
Layout: Grid-based with borders and padding
Mixed: Works in documents with other markdown
Alignment: Left (`:---`), Center (`:---:`), Right (`---:`)

**Test it**: Copy the table examples into a message and see them render with proper borders and alignment!
