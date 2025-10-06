# Markdown Error Log - Quick Reference

## Location
```
Relative to exe: logs/markdown-errors.log
Debug Build: bin/Debug/net9.0/logs/markdown-errors.log
Release Build: bin/Release/net9.0/logs/markdown-errors.log
Published App: [AppFolder]/logs/markdown-errors.log
```

## How to View

### PowerShell:
```powershell
# Navigate to exe folder first
cd bin/Debug/net9.0  # or wherever your exe is

# View last 20 errors
Get-Content "logs/markdown-errors.log" -Tail 20

# View all errors
Get-Content "logs/markdown-errors.log"

# Monitor in real-time
Get-Content "logs/markdown-errors.log" -Wait -Tail 10

# Clear the log
Remove-Item "logs/markdown-errors.log" -Force
```

### File Explorer:
1. Navigate to your ZTalk exe location (e.g., `bin/Debug/net9.0/`)
2. Open the `logs/` folder
3. Open `markdown-errors.log` with Notepad

**Tip**: Right-click on ZTalk.exe → "Open file location" to find it quickly

## Log Entry Format

```
[TIMESTAMP] STAGE Error: ERROR_MESSAGE
  Type: EXCEPTION_TYPE
  Markdown (truncated): MARKDOWN_CONTENT
  Stack: STACK_TRACE
```

### Example:
```
[2025-10-05 14:30:22] Parse Error: Index was outside the bounds of the array.
  Type: IndexOutOfRangeException
  Markdown (truncated): **Bold text with [link](https://very-long-url...
  Stack: at ZTalk.Controls.Markdown.MarkdownParser.ConvertInline(Inline inline)
         at ZTalk.Controls.Markdown.MarkdownParser.ConvertParagraph(Block block)
```

## Error Stages

| Stage | Meaning | What Happens |
|-------|---------|--------------|
| **Parse** | Markdig parsing failed | Falls back to plain text |
| **Render** | UI control creation failed | Shows "[Render Error]" |
| **TopLevel** | Unexpected error | Shows fallback TextBlock |

## Common Errors

### 1. IndexOutOfRangeException
**Cause**: Malformed markdown structure  
**Effect**: Block skipped, message continues rendering  
**User Impact**: Minimal - shows as plain text

### 2. NullReferenceException
**Cause**: Missing markdown element  
**Effect**: Block shows error indicator  
**User Impact**: See "[Block render error]" in message

### 3. ArgumentException
**Cause**: Invalid markdown syntax  
**Effect**: Parser returns null, falls back to plain text  
**User Impact**: Message shows unformatted

### 4. StackOverflowException
**Cause**: Deeply nested markdown (rare)  
**Effect**: Caught at top level, shows fallback  
**User Impact**: Message truncated or plain text

## What to Do When Errors Occur

### For Users:
1. **Don't Panic**: App won't crash, you won't lose data
2. **Check Message**: If it looks odd, try deleting and resending
3. **Report**: If persistent, share error log with support

### For Developers:
1. **Check Error Log**: See patterns in failures
2. **Identify Markdown**: Look at truncated content in log
3. **Reproduce**: Try to recreate the issue locally
4. **Fix Parser/Renderer**: Add handling for new case

### For Support:
1. **Request Log**: Ask user to share `markdown-errors.log`
2. **Check Timestamps**: When did errors start?
3. **Look for Patterns**: Same error type recurring?
4. **Suggest Fix**: Delete problematic message or disable markdown

## Error Statistics

### To Count Errors by Type:
```powershell
cd bin/Debug/net9.0  # Navigate to exe folder
$log = Get-Content "logs/markdown-errors.log"
$log | Select-String "Type: " | Group-Object | Sort-Object Count -Descending
```

### To Find Recent Errors:
```powershell
$log = Get-Content "logs/markdown-errors.log"
$log | Select-String "\[2025-10-05" | Select-Object -Last 10
```

### To Extract Problematic Markdown:
```powershell
$log = Get-Content "logs/markdown-errors.log"
$log | Select-String "Markdown \(truncated\):" | ForEach-Object { $_.Line }
```

## Troubleshooting

### Problem: Log file is huge
**Solution**: 
```powershell
cd bin/Debug/net9.0
# Archive old log
Move-Item "logs/markdown-errors.log" "logs/markdown-errors-old.log"
```

### Problem: Too many errors
**Solution**:
1. Disable markdown temporarily: Set `UseMarkdig = false`
2. Clear message archive: Check for corrupt messages
3. Update app: May have fixes for common errors

### Problem: Same error repeating
**Solution**:
1. Identify the message causing it (check truncated markdown)
2. Delete that specific message
3. Or disable markdown for that contact

## Log Rotation (Future Enhancement)

Currently logs append indefinitely. Consider implementing:
- Max file size (10MB)
- Keep last 7 days only
- Compress old logs
- Auto-cleanup on app start

## Privacy Note

⚠️ **Warning**: Error log contains message content (truncated to 200 chars)

Before sharing log with support:
- Review for sensitive content
- Redact personal information if needed
- Consider sharing only error types/counts

## Debug Mode

For developers, enable verbose logging:
```csharp
// In ZTalkMarkdownViewer.cs
System.Diagnostics.Debug.WriteLine($"[MarkdownViewer] Parsing: {text}");
```

Output appears in:
- Visual Studio: Debug Output window
- Rider: Run window
- Console apps: Standard output

## Health Check

### Good Log (Normal):
```
# Few entries, spread over time
# Mix of error types
# No repeated patterns
```

### Concerning Log:
```
# Hundreds of entries in minutes
# Same error type/message repeated
# Recent spike in errors
```

If you see concerning patterns, investigate immediately!

## Summary

✅ **Location**: `logs/markdown-errors.log` (in same folder as ZTalk.exe)  
✅ **Purpose**: Diagnose markdown issues without crashing app  
✅ **Contains**: Timestamp, error type, markdown snippet, stack trace  
✅ **Action**: Review periodically, share with support if issues persist  
✅ **Privacy**: Contains message content (truncated) - review before sharing

The error log is your friend - it helps diagnose issues while keeping the app stable!
