using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ZTalk.Controls.Markdown
{
    /// <summary>
    /// Markdown viewer that uses the new parser → RenderModel → renderer pipeline.
    /// Creates fresh controls per render - never reuses visual elements.
    /// Safe for P2P messaging with HTML disabled and no external resource loading.
    /// </summary>
    public sealed class ZTalkMarkdownViewer : ContentControl
    {
        // Static constructor runs once per AppDomain - rotate logs on first load
        static ZTalkMarkdownViewer()
        {
            try
            {
                RotateLogs();
            }
            catch { }
        }
        
        // CRITICAL: Each viewer needs its own parser/renderer instances to avoid control reuse
        private readonly MarkdownParser _parser = new();
        private readonly MarkdownRenderer _renderer = new();

        // Simple bound Markdown text property (one-way)
        public static readonly StyledProperty<string> MarkdownProperty =
            AvaloniaProperty.Register<ZTalkMarkdownViewer, string>(nameof(Markdown), string.Empty, defaultBindingMode: BindingMode.OneWay);

        public string Markdown
        {
            get => GetValue(MarkdownProperty);
            set => SetValue(MarkdownProperty, value);
        }

        public ZTalkMarkdownViewer()
        {
            // Initialize to an empty plain TextBlock
            Content = CreateFallbackTextBlock(string.Empty);
            
            // CRITICAL: Clean up when detached from visual tree (ItemsControl recycling)
            this.DetachedFromVisualTree += OnDetachedFromVisualTree;
        }

        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            try
            {
                // Clear content to prevent reuse issues when ItemsControl recycles this control
                if (Content is Control control)
                {
                    control.IsVisible = false;
                }
                Content = null;
            }
            catch { }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            try
            {
                if (change.Property == MarkdownProperty)
                {
                    // CRITICAL: Clear old content first to prevent "already has parent" errors
                    try
                    {
                        if (Content is Control oldControl)
                        {
                            oldControl.IsVisible = false;
                            // Force remove from logical children
                            LogicalChildren.Remove(oldControl);
                        }
                        Content = null;
                    }
                    catch { }

                    var text = Markdown ?? string.Empty;
                    
                    // Debug logging to track rendering
                    System.Diagnostics.Debug.WriteLine($"[ZTalkMarkdownViewer] Rendering markdown: {text.Substring(0, Math.Min(50, text.Length))}...");

                    // Empty text - show empty content
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        Content = CreateFallbackTextBlock(string.Empty);
                        return;
                    }

                    // MIXED MARKDOWN RENDERING: Handle documents with multiple markdown types
                    if (HasMixedMarkdown(text))
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine("[ZTalkMarkdownViewer] Using mixed markdown renderer");
                            Content = RenderMixedMarkdown(text);
                            return;
                        }
                        catch (Exception mixedEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ZTalkMarkdownViewer] Mixed markdown render failed: {mixedEx.Message}");
                            // Fall through to single-type rendering
                        }
                    }

                    // CUSTOM QUOTE RENDERING: Bypass Markdig for quotes to avoid Avalonia parent issues
                    if (IsQuoteMarkdown(text))
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine("[ZTalkMarkdownViewer] Using custom quote renderer");
                            Content = RenderCustomQuote(text);
                            return;
                        }
                        catch (Exception quoteEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ZTalkMarkdownViewer] Custom quote render failed: {quoteEx.Message}");
                            // Fall through to normal rendering
                        }
                    }

                    // CUSTOM CODE BLOCK RENDERING: Bypass Markdig for code blocks to avoid Avalonia parent issues
                    if (IsCodeBlockMarkdown(text))
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine("[ZTalkMarkdownViewer] Using custom code block renderer");
                            Content = RenderCustomCodeBlock(text);
                            return;
                        }
                        catch (Exception codeEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ZTalkMarkdownViewer] Custom code block render failed: {codeEx.Message}");
                            // Fall through to normal rendering
                        }
                    }

                    // CUSTOM SPOILER RENDERING: Handle ||spoiler|| syntax with click-to-reveal
                    if (HasSpoilerMarkdown(text))
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine("[ZTalkMarkdownViewer] Using custom spoiler renderer");
                            Content = RenderCustomSpoiler(text);
                            return;
                        }
                        catch (Exception spoilerEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ZTalkMarkdownViewer] Custom spoiler render failed: {spoilerEx.Message}");
                            // Fall through to normal rendering
                        }
                    }

                    // CUSTOM HEADER RENDERING: Handle # ## ### etc. to avoid Avalonia parent issues
                    if (IsHeaderMarkdown(text))
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine("[ZTalkMarkdownViewer] Using custom header renderer");
                            Content = RenderCustomHeader(text);
                            return;
                        }
                        catch (Exception headerEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ZTalkMarkdownViewer] Custom header render failed: {headerEx.Message}");
                            // Fall through to normal rendering
                        }
                    }

                    // CUSTOM LIST RENDERING: Handle - * + (unordered) and 1. 2. (ordered) lists
                    if (IsListMarkdown(text))
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine("[ZTalkMarkdownViewer] Using custom list renderer");
                            Content = RenderCustomList(text);
                            return;
                        }
                        catch (Exception listEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ZTalkMarkdownViewer] Custom list render failed: {listEx.Message}");
                            // Fall through to normal rendering
                        }
                    }

                    // CUSTOM TABLE RENDERING: Handle | | tables with alignment support
                    if (IsTableMarkdown(text))
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine("[ZTalkMarkdownViewer] Using custom table renderer");
                            Content = RenderCustomTable(text);
                            return;
                        }
                        catch (Exception tableEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ZTalkMarkdownViewer] Custom table render failed: {tableEx.Message}");
                            // Fall through to normal rendering
                        }
                    }

                    // CUSTOM HORIZONTAL RULE RENDERING: Handle ---, ***, ___
                    if (IsHorizontalRuleMarkdown(text))
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine("[ZTalkMarkdownViewer] Using custom horizontal rule renderer");
                            Content = RenderHorizontalRule();
                            return;
                        }
                        catch (Exception hrEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ZTalkMarkdownViewer] Custom horizontal rule render failed: {hrEx.Message}");
                            // Fall through to normal rendering
                        }
                    }

                    // CRITICAL: Wrap parsing in try-catch to prevent crashes
                    Document? document = null;
                    try
                    {
                        document = _parser.Parse(text);
                    }
                    catch (Exception parseEx)
                    {
                        // Log parse failure and fallback
                        try
                        {
                            System.Diagnostics.Debug.WriteLine($"[MarkdownViewer] Parse failed: {parseEx.Message}");
                            LogError("Parse", parseEx, text);
                        }
                        catch { }
                        Content = CreateFallbackTextBlock(text);
                        return;
                    }

                    if (document != null)
                    {
                        // CRITICAL: Wrap rendering in try-catch to prevent crashes
                        try
                        {
                            System.Diagnostics.Debug.WriteLine($"[ZTalkMarkdownViewer] About to render, current Content type: {Content?.GetType().Name ?? "null"}");
                            var newContent = _renderer.Render(document);
                            System.Diagnostics.Debug.WriteLine($"[ZTalkMarkdownViewer] Rendered new content type: {newContent.GetType().Name}");
                            Content = newContent;
                            System.Diagnostics.Debug.WriteLine($"[ZTalkMarkdownViewer] Content assigned successfully");
                        }
                        catch (Exception renderEx)
                        {
                            // Log render failure and fallback
                            try
                            {
                                System.Diagnostics.Debug.WriteLine($"[MarkdownViewer] Render failed: {renderEx.Message}");
                                System.Diagnostics.Debug.WriteLine($"[MarkdownViewer] Stack: {renderEx.StackTrace}");
                                LogError("Render", renderEx, text);
                            }
                            catch { }
                            Content = CreateFallbackTextBlock(text);
                        }
                    }
                    else
                    {
                        // Parser returned null - fallback to plain text
                        Content = CreateFallbackTextBlock(text);
                    }
                }
            }
            catch (Exception topEx)
            {
                // Ultimate fallback - show plain text on any error
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[MarkdownViewer] Top-level error: {topEx.Message}");
                    LogError("TopLevel", topEx, Markdown ?? string.Empty);
                }
                catch { }
                
                try
                {
                    var text = Markdown ?? string.Empty;
                    Content = CreateFallbackTextBlock(text);
                }
                catch
                {
                    // Even fallback failed - use empty content
                    try
                    {
                        Content = CreateFallbackTextBlock("[Render Error]");
                    }
                    catch { }
                }
            }
        }

    private static void LogError(string stage, Exception ex, string markdown)
    {
        try
        {
            var truncated = markdown.Length > 200 ? markdown.Substring(0, 200) + "..." : markdown;
            
            // CRITICAL: Use local Logs/ folder, NOT AppData
            var exeDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
            var logPath = System.IO.Path.Combine(exeDir, "logs", "markdown-errors.log");
            
            var logDir = System.IO.Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(logDir) && !System.IO.Directory.Exists(logDir))
            {
                System.IO.Directory.CreateDirectory(logDir);
            }

            var logEntry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {stage} Error: {ex.Message}\n" +
                          $"  Type: {ex.GetType().Name}\n" +
                          $"  Markdown (truncated): {truncated}\n" +
                          $"  Stack: {ex.StackTrace}\n" +
                          $"  Inner: {ex.InnerException?.Message}\n\n";
            
            System.IO.File.AppendAllText(logPath, logEntry);
            
            // Also write to Debug output for immediate visibility
            System.Diagnostics.Debug.WriteLine($"[MARKDOWN ERROR] {stage}: {ex.Message}");
        }
        catch
        {
            // Don't let logging errors crash the app
        }
    }

    // Append structured debug messages to the same markdown error log so QA can
    // review a persistent record instead of relying solely on the Debug Console.
    private static void LogDebug(string tag, string message)
    {
        try
        {
            var exeDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
            var logPath = System.IO.Path.Combine(exeDir, "logs", "markdown-trace.log");
            var logDir = System.IO.Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(logDir) && !System.IO.Directory.Exists(logDir))
                System.IO.Directory.CreateDirectory(logDir);

            var entry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {tag}: {message}\n";
            System.IO.File.AppendAllText(logPath, entry);
            System.Diagnostics.Debug.WriteLine($"[MDLOG] {tag}: {message}");
        }
        catch
        {
            // Swallow logging errors to avoid crashes
        }
    }

    // Clear old log files on startup to prevent unbounded growth
    private static void RotateLogs()
    {
        try
        {
            var exeDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
            var logsDir = System.IO.Path.Combine(exeDir, "logs");
            if (!System.IO.Directory.Exists(logsDir))
                System.IO.Directory.CreateDirectory(logsDir);

            var errorPath = System.IO.Path.Combine(logsDir, "markdown-errors.log");
            var tracePath = System.IO.Path.Combine(logsDir, "markdown-trace.log");

            // Archive old error log if it exists and is over 1MB
            if (System.IO.File.Exists(errorPath))
            {
                try
                {
                    var fileInfo = new System.IO.FileInfo(errorPath);
                    if (fileInfo.Length > 1024 * 1024) // 1MB
                    {
                        var archivePath = System.IO.Path.Combine(logsDir, $"markdown-errors-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
                        System.IO.File.Move(errorPath, archivePath);
                    }
                }
                catch { }
            }

            // Archive old trace log if it exists and is over 1MB
            if (System.IO.File.Exists(tracePath))
            {
                try
                {
                    var fileInfo = new System.IO.FileInfo(tracePath);
                    if (fileInfo.Length > 1024 * 1024) // 1MB
                    {
                        var archivePath = System.IO.Path.Combine(logsDir, $"markdown-trace-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
                        System.IO.File.Move(tracePath, archivePath);
                    }
                }
                catch { }
            }
        }
        catch
        {
            // Don't propagate rotation failures
        }
    }
    
    // Check if text is quote markdown (starts with >)
    private static bool IsQuoteMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        
        // Check if any line starts with >
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        return lines.Length > 0 && lines[0].TrimStart().StartsWith(">");
    }

    // Check if text is code block markdown (starts with ``` or contains code fence)
    private static bool IsCodeBlockMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        
        var trimmed = text.Trim();
        // Check for fenced code block: ```language or just ```
        return trimmed.StartsWith("```");
    }

    // Check if text contains spoiler markdown ||text||
    private static bool HasSpoilerMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        
        // Check for ||spoiler|| syntax
        return text.Contains("||");
    }

    // Check if text is header markdown (starts with #)
    private static bool IsHeaderMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        
        var trimmed = text.TrimStart();
        // Check for header: # ## ### #### ##### ######
        return trimmed.StartsWith("#");
    }

    // Check if text is list markdown (starts with -, *, +, or digit.)
    private static bool IsListMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return false;
        
        var firstLine = lines[0].TrimStart();
        
        // Check for unordered list: -, *, +
        if (firstLine.StartsWith("- ") || firstLine.StartsWith("* ") || firstLine.StartsWith("+ "))
            return true;
        
        // Check for ordered list: 1. 2. 3. etc.
        if (firstLine.Length > 2 && char.IsDigit(firstLine[0]))
        {
            for (int i = 1; i < firstLine.Length; i++)
            {
                if (firstLine[i] == '.')
                    return true;
                if (!char.IsDigit(firstLine[i]))
                    break;
            }
        }
        
        return false;
    }

    // Check if text is horizontal rule (---, ***, ___)
    private static bool IsHorizontalRuleMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        
        var trimmed = text.Trim();
        
        // Must be at least 3 characters of the same type: --- or *** or ___
        if (trimmed.Length < 3) return false;
        
        // Check if it's all dashes, asterisks, or underscores (with optional spaces)
        var chars = trimmed.Where(c => !char.IsWhiteSpace(c)).ToArray();
        if (chars.Length < 3) return false;
        
        var firstChar = chars[0];
        if (firstChar != '-' && firstChar != '*' && firstChar != '_') return false;
        
        // All non-whitespace chars must be the same
        return chars.All(c => c == firstChar);
    }

    // Check if text is table markdown (contains | separators)
    private static bool IsTableMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        
        System.Diagnostics.Debug.WriteLine($"[IsTableMarkdown] Checking: {text.Substring(0, Math.Min(100, text.Length))}");
        
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        System.Diagnostics.Debug.WriteLine($"[IsTableMarkdown] Lines count: {lines.Length}");
        
        if (lines.Length < 2) return false; // Need at least header and separator
        
        // Look for table separator line (contains | and -)
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            System.Diagnostics.Debug.WriteLine($"[IsTableMarkdown] Checking line: '{trimmed}'");
            
            if (trimmed.Contains("|") && trimmed.Contains("-"))
            {
                // Check if it's a separator line: |---|---|
                var hasOnlyTableChars = trimmed.All(c => c == '|' || c == '-' || c == ':' || char.IsWhiteSpace(c));
                System.Diagnostics.Debug.WriteLine($"[IsTableMarkdown] Has table chars: {hasOnlyTableChars}, Pipe count: {trimmed.Count(c => c == '|')}");
                
                if (hasOnlyTableChars && trimmed.Count(c => c == '|') >= 2)
                {
                    System.Diagnostics.Debug.WriteLine("[IsTableMarkdown] DETECTED AS TABLE!");
                    return true;
                }
            }
        }
        
        System.Diagnostics.Debug.WriteLine("[IsTableMarkdown] NOT a table");
        return false;
    }

    // Check if text has multiple different markdown types (mixed document)
    private static bool HasMixedMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        
        System.Diagnostics.Debug.WriteLine($"[HasMixedMarkdown] Checking: {text.Substring(0, Math.Min(100, text.Length))}");
        
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
        bool hasQuote = false;
        bool hasCode = false;
        bool hasHeader = false;
        bool hasList = false;
        bool hasTable = false;
        bool hasHorizontalRule = false;
        bool hasNormal = false;
        
        bool inTable = false;
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (string.IsNullOrEmpty(trimmed)) continue;
            
            // Check for table separator line
            if (trimmed.Contains("|") && trimmed.Contains("-"))
            {
                var hasOnlyTableChars = trimmed.All(c => c == '|' || c == '-' || c == ':' || char.IsWhiteSpace(c));
                if (hasOnlyTableChars && trimmed.Count(c => c == '|') >= 2)
                {
                    hasTable = true;
                    inTable = true;
                    continue;
                }
            }
            
            // If in table, check if line still has |
            if (inTable)
            {
                if (!trimmed.Contains("|"))
                    inTable = false;
                else
                    continue;
            }
            
            // Check if this line could be a table header (before we've seen separator)
            // Don't count it as "normal" if it looks like a table line
            bool looksLikeTableLine = trimmed.Contains("|") && trimmed.Count(c => c == '|') >= 2;
            
            // Check for horizontal rule: ---, ***, ___
            if (trimmed.Length >= 3)
            {
                var chars = trimmed.Where(c => !char.IsWhiteSpace(c)).ToArray();
                if (chars.Length >= 3 && (chars[0] == '-' || chars[0] == '*' || chars[0] == '_') && chars.All(c => c == chars[0]))
                {
                    hasHorizontalRule = true;
                    continue;
                }
            }
            
            if (trimmed.StartsWith(">")) hasQuote = true;
            else if (trimmed.StartsWith("```")) hasCode = true;
            else if (trimmed.StartsWith("#")) hasHeader = true;
            else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("+ ") ||
                     (trimmed.Length > 2 && char.IsDigit(trimmed[0]) && trimmed.Contains("."))) hasList = true;
            else if (!looksLikeTableLine) hasNormal = true;  // Only count as normal if it doesn't look like a table line
        }
        
        // Count distinct types
        int distinctTypes = 0;
        if (hasQuote) distinctTypes++;
        if (hasCode) distinctTypes++;
        if (hasHeader) distinctTypes++;
        if (hasList) distinctTypes++;
        if (hasTable) distinctTypes++;
        if (hasHorizontalRule) distinctTypes++;
        if (hasNormal) distinctTypes++;
        
        System.Diagnostics.Debug.WriteLine($"[HasMixedMarkdown] Types found - Quote:{hasQuote} Code:{hasCode} Header:{hasHeader} List:{hasList} Table:{hasTable} HR:{hasHorizontalRule} Normal:{hasNormal} = {distinctTypes} types");
        
        // Mixed if we have 2+ types
        bool isMixed = distinctTypes >= 2;
        System.Diagnostics.Debug.WriteLine($"[HasMixedMarkdown] Result: {isMixed}");
        return isMixed;
    }

    // Render mixed markdown document with multiple element types
    private Control RenderMixedMarkdown(string text)
    {
        try
        {
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
            var container = new StackPanel
            {
                Spacing = 8,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };

            var currentBlock = new System.Collections.Generic.List<string>();
            string? currentBlockType = null; // "quote", "code", "header", "list", "normal"

            void FlushBlock()
            {
                if (currentBlock.Count == 0) return;
                
                var blockText = string.Join("\n", currentBlock);
                if (string.IsNullOrWhiteSpace(blockText) && currentBlockType != "code")
                {
                    currentBlock.Clear();
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[MixedMarkdown] Flushing {currentBlockType} block with {currentBlock.Count} lines");

                Control? blockControl = null;
                
                try
                {
                    switch (currentBlockType)
                    {
                        case "quote":
                            blockControl = RenderCustomQuote(blockText);
                            break;
                        case "code":
                            blockControl = RenderCustomCodeBlock(blockText);
                            break;
                        case "header":
                            blockControl = RenderCustomHeader(blockText);
                            break;
                        case "list":
                            blockControl = RenderCustomList(blockText);
                            break;
                        case "table":
                            System.Diagnostics.Debug.WriteLine($"[MixedMarkdown] Rendering table: {blockText.Substring(0, Math.Min(50, blockText.Length))}");
                            blockControl = RenderCustomTable(blockText);
                            break;
                        case "hr":
                            blockControl = RenderHorizontalRule();
                            break;
                        case "normal":
                        default:
                            if (!string.IsNullOrWhiteSpace(blockText))
                            {
                                blockControl = RenderNormalText(blockText);
                            }
                            break;
                    }
                    
                    if (blockControl != null)
                    {
                        container.Children.Add(blockControl);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MixedMarkdown] Block render failed for {currentBlockType}: {ex.Message}");
                    // Add as plain text fallback
                    if (!string.IsNullOrWhiteSpace(blockText))
                    {
                        container.Children.Add(CreateFallbackTextBlock(blockText));
                    }
                }
                
                currentBlock.Clear();
            }

            bool inCodeBlock = false;
            
            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();
                
                // Handle code blocks specially (they can contain anything)
                if (trimmed.StartsWith("```"))
                {
                    if (!inCodeBlock)
                    {
                        // Starting code block
                        FlushBlock();
                        currentBlockType = "code";
                        inCodeBlock = true;
                    }
                    else
                    {
                        // Ending code block
                        currentBlock.Add(line);
                        FlushBlock();
                        currentBlockType = null;
                        inCodeBlock = false;
                        continue;
                    }
                }
                
                if (inCodeBlock)
                {
                    currentBlock.Add(line);
                    continue;
                }
                
                // Detect block type
                string? lineType = null;
                
                System.Diagnostics.Debug.WriteLine($"[MixedMarkdown] Processing line: '{trimmed}', currentBlockType: {currentBlockType}");
                
                // Check for horizontal rule: ---, ***, ___
                if (trimmed.Length >= 3)
                {
                    var hrChars = trimmed.Where(c => !char.IsWhiteSpace(c)).ToArray();
                    if (hrChars.Length >= 3 && (hrChars[0] == '-' || hrChars[0] == '*' || hrChars[0] == '_') && hrChars.All(c => c == hrChars[0]))
                    {
                        lineType = "hr";
                        System.Diagnostics.Debug.WriteLine($"[MixedMarkdown] ✓ Detected horizontal rule");
                    }
                }
                
                // Check for table (has | and either separator or consistent | pattern)
                if (trimmed.Contains("|"))
                {
                    System.Diagnostics.Debug.WriteLine($"[MixedMarkdown] Line contains |, checking table...");
                    
                    // Check if it's table separator line
                    if (trimmed.Contains("-"))
                    {
                        var hasOnlyTableChars = trimmed.All(c => c == '|' || c == '-' || c == ':' || char.IsWhiteSpace(c));
                        System.Diagnostics.Debug.WriteLine($"[MixedMarkdown] Contains -, hasOnlyTableChars: {hasOnlyTableChars}, pipes: {trimmed.Count(c => c == '|')}");
                        
                        if (hasOnlyTableChars && trimmed.Count(c => c == '|') >= 2)
                        {
                            lineType = "table";
                            System.Diagnostics.Debug.WriteLine($"[MixedMarkdown] ✓ Detected table separator");
                        }
                    }
                    // If previous line was table and this has |, continue table
                    else if (currentBlockType == "table")
                    {
                        lineType = "table";
                        System.Diagnostics.Debug.WriteLine($"[MixedMarkdown] ✓ Continue table");
                    }
                    // First line of table (header)
                    else if (trimmed.Count(c => c == '|') >= 2)
                    {
                        lineType = "table";
                        System.Diagnostics.Debug.WriteLine($"[MixedMarkdown] ✓ Table header, pipes: {trimmed.Count(c => c == '|')}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[MixedMarkdown] Has | but not table (pipes: {trimmed.Count(c => c == '|')})");
                    }
                }
                
                if (lineType == null && trimmed.StartsWith(">"))
                    lineType = "quote";
                else if (lineType == null && trimmed.StartsWith("#"))
                    lineType = "header";
                else if (lineType == null && (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("+ ")))
                    lineType = "list";
                else if (lineType == null && trimmed.Length > 2 && char.IsDigit(trimmed[0]))
                {
                    for (int i = 1; i < trimmed.Length; i++)
                    {
                        if (trimmed[i] == '.')
                        {
                            lineType = "list";
                            break;
                        }
                        if (!char.IsDigit(trimmed[i]))
                            break;
                    }
                }
                
                if (lineType == null)
                    lineType = string.IsNullOrWhiteSpace(trimmed) ? null : "normal";
                
                // If block type changes, flush previous block (but blank lines don't count as a type change)
                if (lineType != null && lineType != currentBlockType && currentBlock.Count > 0)
                {
                    FlushBlock();
                    currentBlockType = lineType;
                }
                else if (currentBlockType == null && lineType != null)
                {
                    currentBlockType = lineType;
                }
                
                // Add line to current block (skip blank lines unless in code block)
                if (lineType != null)
                {
                    currentBlock.Add(line);
                }
                else if (currentBlockType == "code")
                {
                    // Preserve blank lines in code blocks
                    currentBlock.Add(line);
                }
            }
            
            // Flush any remaining block
            FlushBlock();

            // If only one child, return it directly
            if (container.Children.Count == 1)
            {
                var child = container.Children[0];
                container.Children.Clear();
                return child;
            }

            // If no children, return fallback
            if (container.Children.Count == 0)
            {
                return CreateFallbackTextBlock(text);
            }

            return container;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MixedMarkdown] Error: {ex.Message}");
            return CreateFallbackTextBlock(text);
        }
    }

    // Custom quote renderer - bypasses Markdig to avoid Avalonia parent issues
    private Control RenderCustomQuote(string text)
    {
        try
        {
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var container = new StackPanel
            {
                Spacing = 4,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };

            var currentQuoteLines = new System.Collections.Generic.List<string>();
            var normalLines = new System.Collections.Generic.List<string>();

            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith(">"))
                {
                    // Quote line - add any pending normal text first
                    if (normalLines.Count > 0)
                    {
                        container.Children.Add(RenderNormalText(string.Join("\n", normalLines)));
                        normalLines.Clear();
                    }
                    
                    // Extract quote content (remove > and optional space)
                    var quoteText = trimmed.Substring(1).TrimStart();
                    currentQuoteLines.Add(quoteText);
                }
                else
                {
                    // Normal line - add any pending quote first
                    if (currentQuoteLines.Count > 0)
                    {
                        container.Children.Add(CreateQuoteBorder(string.Join("\n", currentQuoteLines)));
                        currentQuoteLines.Clear();
                    }
                    
                    normalLines.Add(line);
                }
            }

            // Add any remaining content
            if (currentQuoteLines.Count > 0)
            {
                container.Children.Add(CreateQuoteBorder(string.Join("\n", currentQuoteLines)));
            }
            if (normalLines.Count > 0)
            {
                container.Children.Add(RenderNormalText(string.Join("\n", normalLines)));
            }

            // If only one child, return it directly
            if (container.Children.Count == 1)
            {
                var child = container.Children[0];
                container.Children.Clear();
                return child;
            }

            return container;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomQuote] Error: {ex.Message}");
            return CreateFallbackTextBlock(text);
        }
    }

    // Create quote border with fresh controls
    private Border CreateQuoteBorder(string quoteText)
    {
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(100, 149, 237)),
            BorderThickness = new Thickness(4, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(20, 100, 149, 237)),
            Padding = new Thickness(12, 8),
            Margin = new Thickness(0, 8),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };

        // Parse any inline markdown in the quote text
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };

        // Try to parse inline formatting (bold, italic, etc.)
        try
        {
            var doc = _parser.Parse(quoteText);
            if (doc != null && doc.Blocks.Count > 0 && doc.Blocks[0] is Paragraph para)
            {
                // Render the paragraph's inlines into the textblock
                foreach (var inline in para.Inlines)
                {
                    var run = CreateInlineRun(inline);
                    if (run != null && textBlock.Inlines != null)
                    {
                        textBlock.Inlines.Add(run);
                    }
                }
            }
            else
            {
                textBlock.Text = quoteText;
            }
        }
        catch
        {
            // Fallback to plain text
            textBlock.Text = quoteText;
        }

        border.Child = textBlock;
        return border;
    }

    // Create inline run from RenderModel inline
    private Avalonia.Controls.Documents.Run? CreateInlineRun(InlineNode inline)
    {
        try
        {
            switch (inline)
            {
                case TextInline text:
                    return new Avalonia.Controls.Documents.Run { Text = text.Text };
                
                case Emphasis emph:
                    var run = new Avalonia.Controls.Documents.Run();
                    var content = string.Join("", emph.Inlines.OfType<TextInline>().Select(t => t.Text));
                    run.Text = content;
                    
                    if (emph.Style == "italic")
                        run.FontStyle = FontStyle.Italic;
                    else if (emph.Style == "bold")
                        run.FontWeight = FontWeight.Bold;
                    
                    return run;
                
                case InlineCode code:
                    var codeRun = new Avalonia.Controls.Documents.Run
                    {
                        Text = code.Code,
                        FontFamily = new FontFamily("Consolas, Monaco, 'Courier New', monospace"),
                        Foreground = new SolidColorBrush(Color.FromRgb(240, 108, 108)),
                        Background = new SolidColorBrush(Color.FromArgb(40, 68, 68, 68))
                    };
                    return codeRun;
                
                default:
                    return new Avalonia.Controls.Documents.Run { Text = inline.ToString() ?? "" };
            }
        }
        catch
        {
            return null;
        }
    }

    // Render normal text with inline formatting
    private Control RenderNormalText(string text)
    {
        try
        {
            var doc = _parser.Parse(text);
            if (doc != null)
            {
                return _renderer.Render(doc);
            }
        }
        catch { }
        
        return CreateFallbackTextBlock(text);
    }

    // Custom code block renderer - bypasses Markdig to avoid Avalonia parent issues
    private Control RenderCustomCodeBlock(string text)
    {
        try
        {
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
            var container = new StackPanel
            {
                Spacing = 8,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };

            var inCodeBlock = false;
            var codeLines = new System.Collections.Generic.List<string>();
            var language = string.Empty;
            var normalLines = new System.Collections.Generic.List<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                
                if (line.TrimStart().StartsWith("```"))
                {
                    if (!inCodeBlock)
                    {
                        // Start of code block - add any pending normal text first
                        if (normalLines.Count > 0)
                        {
                            var normalText = string.Join("\n", normalLines);
                            if (!string.IsNullOrWhiteSpace(normalText))
                            {
                                container.Children.Add(RenderNormalText(normalText));
                            }
                            normalLines.Clear();
                        }
                        
                        // Extract language (after ```)
                        language = line.TrimStart().Substring(3).Trim();
                        inCodeBlock = true;
                        codeLines.Clear();
                    }
                    else
                    {
                        // End of code block - render it
                        var codeText = string.Join("\n", codeLines);
                        container.Children.Add(CreateCodeBlockBorder(codeText, language));
                        
                        codeLines.Clear();
                        language = string.Empty;
                        inCodeBlock = false;
                    }
                }
                else
                {
                    if (inCodeBlock)
                    {
                        codeLines.Add(line);
                    }
                    else
                    {
                        normalLines.Add(line);
                    }
                }
            }

            // Add any remaining content
            if (inCodeBlock && codeLines.Count > 0)
            {
                // Unclosed code block - render it anyway
                var codeText = string.Join("\n", codeLines);
                container.Children.Add(CreateCodeBlockBorder(codeText, language));
            }
            if (normalLines.Count > 0)
            {
                var normalText = string.Join("\n", normalLines);
                if (!string.IsNullOrWhiteSpace(normalText))
                {
                    container.Children.Add(RenderNormalText(normalText));
                }
            }

            // If only one child, return it directly
            if (container.Children.Count == 1)
            {
                var child = container.Children[0];
                container.Children.Clear();
                return child;
            }

            // If no children, return fallback
            if (container.Children.Count == 0)
            {
                return CreateFallbackTextBlock(text);
            }

            return container;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomCodeBlock] Error: {ex.Message}");
            return CreateFallbackTextBlock(text);
        }
    }

    // Create code block border with fresh controls
    private Border CreateCodeBlockBorder(string code, string? language)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)), // Dark background
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 8),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };

        var textBlock = new TextBlock
        {
            Text = code,
            FontFamily = new FontFamily("Consolas, Monaco, 'Courier New', monospace"),
            Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
            TextWrapping = TextWrapping.NoWrap,
            FontSize = 13,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };

        // Add language label if present
        if (!string.IsNullOrWhiteSpace(language))
        {
            var languageLabel = new TextBlock
            {
                Text = language,
                FontFamily = new FontFamily("Consolas, Monaco, 'Courier New', monospace"),
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
            };

            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(languageLabel);
            stack.Children.Add(textBlock);
            border.Child = stack;
        }
        else
        {
            border.Child = textBlock;
        }

        return border;
    }

    // Custom spoiler renderer - handles ||text|| with click-to-reveal
    private Control RenderCustomSpoiler(string text)
    {
        try
        {
            var textBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };

            var inlines = textBlock.Inlines;
            if (inlines == null) return CreateFallbackTextBlock(text);

            var currentPos = 0;
            var spoilerPattern = "||";

            while (currentPos < text.Length)
            {
                var spoilerStart = text.IndexOf(spoilerPattern, currentPos);
                
                if (spoilerStart == -1)
                {
                    // No more spoilers - add remaining text
                    var remaining = text.Substring(currentPos);
                    if (!string.IsNullOrEmpty(remaining))
                    {
                        AddFormattedText(inlines, remaining);
                    }
                    break;
                }

                // Add text before spoiler
                if (spoilerStart > currentPos)
                {
                    var beforeText = text.Substring(currentPos, spoilerStart - currentPos);
                    AddFormattedText(inlines, beforeText);
                }

                // Find end of spoiler
                var spoilerEnd = text.IndexOf(spoilerPattern, spoilerStart + 2);
                if (spoilerEnd == -1)
                {
                    // Unclosed spoiler - treat as normal text
                    var remaining = text.Substring(spoilerStart);
                    AddFormattedText(inlines, remaining);
                    break;
                }

                // Extract spoiler content
                var spoilerText = text.Substring(spoilerStart + 2, spoilerEnd - spoilerStart - 2);
                if (!string.IsNullOrEmpty(spoilerText))
                {
                    // Create spoiler inline element
                    var spoilerInline = CreateSpoilerInline(spoilerText);
                    inlines.Add(spoilerInline);
                }

                currentPos = spoilerEnd + 2;
            }

            return textBlock;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomSpoiler] Error: {ex.Message}");
            return CreateFallbackTextBlock(text);
        }
    }

    // Add formatted text (with bold, italic, etc.) to inline collection
    private void AddFormattedText(Avalonia.Controls.Documents.InlineCollection inlines, string text)
    {
        try
        {
            // Parse the text for inline formatting
            var doc = _parser.Parse(text);
            if (doc != null && doc.Blocks.Count > 0 && doc.Blocks[0] is Paragraph para)
            {
                foreach (var inline in para.Inlines)
                {
                    var run = CreateInlineRun(inline);
                    if (run != null)
                    {
                        inlines.Add(run);
                    }
                }
            }
            else
            {
                // Fallback to plain text
                inlines.Add(new Avalonia.Controls.Documents.Run { Text = text });
            }
        }
        catch
        {
            // Fallback to plain text
            inlines.Add(new Avalonia.Controls.Documents.Run { Text = text });
        }
    }

    // Create a clickable spoiler inline with black bar cover (toggleable)
    private Avalonia.Controls.Documents.InlineUIContainer CreateSpoilerInline(string spoilerText)
    {
        var spoilerBorder = new Border
        {
            Background = new SolidColorBrush(Colors.Black),
            Padding = new Thickness(4, 2),
            CornerRadius = new CornerRadius(3),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = new TextBlock
            {
                Text = spoilerText,
                Foreground = new SolidColorBrush(Colors.Black), // Hidden text
                FontWeight = FontWeight.Normal
            }
        };

        // Add tooltip
        ToolTip.SetTip(spoilerBorder, "SPOILER! Click to Reveal!");
        ToolTip.SetShowDelay(spoilerBorder, 200);

        // Track revealed state
        bool isRevealed = false;

        // Click handler to toggle reveal/hide
        spoilerBorder.PointerPressed += (sender, e) =>
        {
            if (sender is Border border && border.Child is TextBlock tb)
            {
                if (!isRevealed)
                {
                    // Reveal the spoiler
                    tb.Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220));
                    border.Background = new SolidColorBrush(Color.FromArgb(60, 68, 68, 68));
                    ToolTip.SetTip(border, "Click to Hide");
                    isRevealed = true;
                }
                else
                {
                    // Hide the spoiler again
                    tb.Foreground = new SolidColorBrush(Colors.Black);
                    border.Background = new SolidColorBrush(Colors.Black);
                    ToolTip.SetTip(border, "SPOILER! Click to Reveal!");
                    isRevealed = false;
                }
            }
        };

        // Wrap in InlineUIContainer
        return new Avalonia.Controls.Documents.InlineUIContainer
        {
            Child = spoilerBorder
        };
    }

    // Custom header renderer - bypasses Markdig to avoid Avalonia parent issues
    private Control RenderCustomHeader(string text)
    {
        try
        {
            var trimmed = text.TrimStart();
            
            // Count leading # characters
            int level = 0;
            while (level < trimmed.Length && trimmed[level] == '#')
            {
                level++;
            }

            // Maximum heading level is 6
            if (level > 6) level = 6;
            if (level == 0) level = 1;

            // Extract header text (after # and space)
            var headerText = trimmed.Substring(level).TrimStart();

            // Create header text block
            var textBlock = new TextBlock
            {
                FontWeight = FontWeight.Bold,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 16, 0, 8)
            };

            // Set font size based on heading level
            textBlock.FontSize = level switch
            {
                1 => 24,
                2 => 20,
                3 => 18,
                4 => 16,
                5 => 14,
                6 => 12,
                _ => 14
            };

            // Parse inline formatting in header text
            if (textBlock.Inlines != null)
            {
                try
                {
                    var doc = _parser.Parse(headerText);
                    if (doc != null && doc.Blocks.Count > 0 && doc.Blocks[0] is Paragraph para)
                    {
                        foreach (var inline in para.Inlines)
                        {
                            var run = CreateInlineRun(inline);
                            if (run != null)
                            {
                                textBlock.Inlines.Add(run);
                            }
                        }
                    }
                    else
                    {
                        textBlock.Text = headerText;
                    }
                }
                catch
                {
                    textBlock.Text = headerText;
                }
            }
            else
            {
                textBlock.Text = headerText;
            }

            return textBlock;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomHeader] Error: {ex.Message}");
            return CreateFallbackTextBlock(text);
        }
    }

    // Custom list renderer - bypasses Markdig to avoid Avalonia parent issues
    private Control RenderCustomList(string text)
    {
        try
        {
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
            var container = new StackPanel
            {
                Spacing = 4,
                Margin = new Thickness(0, 8),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };

            // Dictionary to track counters per (indent, container) pair
            var counters = new System.Collections.Generic.Dictionary<(int indent, StackPanel container), int>();
            var listTypes = new System.Collections.Generic.Dictionary<(int indent, StackPanel container), bool>(); // true = ordered

            // Stack to track current container hierarchy
            var containerStack = new System.Collections.Generic.Stack<(StackPanel container, int indent)>();
            containerStack.Push((container, -1));

            LogDebug("List", $"RenderCustomList start: {lines.Length} lines");
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Calculate indentation
                int indent = 0;
                foreach (var c in line)
                {
                    if (c == ' ') indent++;
                    else if (c == '\t') indent += 4;
                    else break;
                }

                var trimmed = line.TrimStart();
                bool isListItem = false;
                bool isOrdered = false;
                string itemText = string.Empty;

                // Check for unordered list item
                if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("+ "))
                {
                    isListItem = true;
                    isOrdered = false;
                    itemText = trimmed.Substring(2).TrimStart();
                }
                // Check for ordered list item
                else if (trimmed.Length > 2 && char.IsDigit(trimmed[0]))
                {
                    for (int i = 1; i < trimmed.Length; i++)
                    {
                        if (trimmed[i] == '.' && i + 1 < trimmed.Length)
                        {
                            isListItem = true;
                            isOrdered = true;
                            itemText = trimmed.Substring(i + 1).TrimStart();
                            break;
                        }
                        if (!char.IsDigit(trimmed[i]))
                            break;
                    }
                }

                if (!isListItem) continue;

                // Persistent log for each detected list item
                LogDebug("List", $"Detected item: '{itemText}' indent:{indent} isOrdered:{isOrdered}");

                // Pop stack until we're at the right level or shallower
                // Use strict greater-than so items at the SAME indent reuse the same level
                while (containerStack.Count > 1 && containerStack.Peek().indent > indent)
                {
                    containerStack.Pop();
                }

                var currentContainer = containerStack.Peek().container;
                var currentIndent = containerStack.Peek().indent;

                // If deeper than current, create nested container
                if (indent > currentIndent)
                {
                    var nestedContainer = new StackPanel
                    {
                        Spacing = 4,
                        Margin = new Thickness(20, 0, 0, 0),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
                    };
                    
                    currentContainer.Children.Add(nestedContainer);
                    LogDebug("List", $"Created nested container at indent {indent}");
                    containerStack.Push((nestedContainer, indent));
                    currentContainer = nestedContainer;
                    currentIndent = indent;
                }

                // Get or initialize the list type for this level
                var key = (currentIndent, currentContainer);
                if (!listTypes.ContainsKey(key))
                {
                    listTypes[key] = isOrdered;
                    counters[key] = 1;
                    LogDebug("List", $"Initialized level indent={currentIndent} isOrdered={isOrdered} counter=1");
                }

                // Get counter and create item
                int itemIndex = counters[key];
                var listItem = CreateListItem(itemText, listTypes[key], itemIndex);
                currentContainer.Children.Add(listItem);

                // Increment counter and persist log
                counters[key] = itemIndex + 1;
                LogDebug("List", $"Added item '{itemText}' index={itemIndex} indent={currentIndent}; counter now={counters[key]}");
            }

            if (container.Children.Count == 0)
            {
                return CreateFallbackTextBlock(text);
            }

            return container;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomList] Error: {ex.Message}");
            return CreateFallbackTextBlock(text);
        }
    }

    // Create a single list item with bullet/number and content
    private StackPanel CreateListItem(string itemText, bool isOrdered, int index)
    {
        var itemContainer = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };

        // Add bullet or number
        var bullet = new TextBlock
        {
            Text = isOrdered ? $"{index}." : "•",
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            MinWidth = 20,
            TextWrapping = TextWrapping.NoWrap
        };

        // Parse item text for inline formatting
        var content = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };

        try
        {
            var doc = _parser.Parse(itemText);
            if (doc != null && doc.Blocks.Count > 0 && doc.Blocks[0] is Paragraph para && content.Inlines != null)
            {
                foreach (var inline in para.Inlines)
                {
                    var run = CreateInlineRun(inline);
                    if (run != null)
                    {
                        content.Inlines.Add(run);
                    }
                }
            }
            else
            {
                content.Text = itemText;
            }
        }
        catch
        {
            content.Text = itemText;
        }

        itemContainer.Children.Add(bullet);
        itemContainer.Children.Add(content);

        return itemContainer;
    }

    // Custom horizontal rule renderer - simple divider line
    private Control RenderHorizontalRule()
    {
        return new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            Margin = new Thickness(0, 16),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            Opacity = 0.5
        };
    }

    // Custom table renderer - bypasses Markdig to avoid Avalonia parent issues
    private Control RenderCustomTable(string text)
    {
        try
        {
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2) return CreateFallbackTextBlock(text);

            var headerLine = lines[0];
            var separatorLine = lines.Length > 1 ? lines[1] : string.Empty;
            var dataLines = lines.Skip(2).ToArray();

            // Parse header
            var headers = ParseTableRow(headerLine);
            if (headers.Length == 0) return CreateFallbackTextBlock(text);

            // Parse alignments from separator
            var alignments = ParseTableAlignments(separatorLine, headers.Length);

            // Create table container
            var tableContainer = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 8),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
            };

            var table = new StackPanel
            {
                Spacing = 0,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };

            // Add header row
            var headerRow = CreateTableRow(headers, alignments, isHeader: true);
            table.Children.Add(headerRow);

            // Add separator
            var separator = new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };
            table.Children.Add(separator);

            // Add data rows
            foreach (var dataLine in dataLines)
            {
                var cells = ParseTableRow(dataLine);
                if (cells.Length > 0)
                {
                    var dataRow = CreateTableRow(cells, alignments, isHeader: false);
                    table.Children.Add(dataRow);
                }
            }

            tableContainer.Child = table;
            return tableContainer;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomTable] Error: {ex.Message}");
            return CreateFallbackTextBlock(text);
        }
    }

    // Parse table row into cells
    private string[] ParseTableRow(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return Array.Empty<string>();

        var trimmed = line.Trim();
        // Remove leading and trailing |
        if (trimmed.StartsWith("|")) trimmed = trimmed.Substring(1);
        if (trimmed.EndsWith("|")) trimmed = trimmed.Substring(0, trimmed.Length - 1);

        var cells = trimmed.Split('|');
        return cells.Select(c => c.Trim()).ToArray();
    }

    // Parse table alignments from separator line
    private Avalonia.Layout.HorizontalAlignment[] ParseTableAlignments(string separatorLine, int columnCount)
    {
        var cells = ParseTableRow(separatorLine);
        var alignments = new Avalonia.Layout.HorizontalAlignment[columnCount];

        for (int i = 0; i < columnCount; i++)
        {
            if (i < cells.Length)
            {
                var cell = cells[i];
                var startsWithColon = cell.StartsWith(":");
                var endsWithColon = cell.EndsWith(":");

                if (startsWithColon && endsWithColon)
                    alignments[i] = Avalonia.Layout.HorizontalAlignment.Center;
                else if (endsWithColon)
                    alignments[i] = Avalonia.Layout.HorizontalAlignment.Right;
                else
                    alignments[i] = Avalonia.Layout.HorizontalAlignment.Left;
            }
            else
            {
                alignments[i] = Avalonia.Layout.HorizontalAlignment.Left;
            }
        }

        return alignments;
    }

    // Create table row with cells
    private Border CreateTableRow(string[] cells, Avalonia.Layout.HorizontalAlignment[] alignments, bool isHeader)
    {
        var rowBorder = new Border
        {
            Background = isHeader 
                ? new SolidColorBrush(Color.FromArgb(40, 100, 100, 100))
                : new SolidColorBrush(Colors.Transparent),
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            BorderThickness = new Thickness(0, 0, 0, isHeader ? 0 : 1),
            Padding = new Thickness(0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };

        var row = new Grid
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };

        // Define columns (Auto-sized based on content)
        for (int i = 0; i < cells.Length; i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        // Add cells
        for (int i = 0; i < cells.Length; i++)
        {
            var cellBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                BorderThickness = new Thickness(i > 0 ? 1 : 0, 0, 0, 0),
                Padding = new Thickness(8, 6),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                MinWidth = 60  // Minimum width to make alignment visible
            };

            var cellText = new TextBlock
            {
                Text = cells[i],
                TextWrapping = TextWrapping.NoWrap,
                FontWeight = isHeader ? FontWeight.Bold : FontWeight.Normal,
                HorizontalAlignment = i < alignments.Length ? alignments[i] : Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            cellBorder.Child = cellText;
            Grid.SetColumn(cellBorder, i);
            row.Children.Add(cellBorder);
        }

        rowBorder.Child = row;
        return rowBorder;
    }
    
    // Helper: create a TextBlock for fallback rendering
    private static TextBlock CreateFallbackTextBlock(string text)
            => new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };
    }
}