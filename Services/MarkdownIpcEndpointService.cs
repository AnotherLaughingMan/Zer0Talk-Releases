using System;
using System.Linq;
using System.Text.Json;
using Markdig;

namespace Zer0Talk.Services
{
    /// <summary>
    /// Markdown IPC endpoint for hybrid shell clients (for example, Tauri).
    /// Offers markdown-to-HTML rendering without Avalonia dependencies.
    /// </summary>
    public sealed class MarkdownIpcEndpointService
    {
        public const int RenderSchemaVersion = 1;
        public const int FormatSchemaVersion = 1;
        public const int UiConfigSchemaVersion = 1;
        public const int StateSchemaVersion = 1;
        public const string CommandRender = "markdown.render.get";
        public const string CommandFormatApply = "markdown.format.apply";
        public const string CommandUiConfigGet = "markdown.ui.config.get";
        public const string CommandDraftGet = "markdown.draft.get";
        public const string CommandDraftSet = "markdown.draft.set";
        public const string CommandPreviewStateGet = "markdown.preview.state.get";
        public const string CommandPreviewStateSet = "markdown.preview.state.set";
        public const string CommandToolbarStateGet = "markdown.toolbar.state.get";
        public const string CommandToolbarStateSet = "markdown.toolbar.state.set";
        public const string CommandMiniEditorStateGet = "markdown.mini-editor.state.get";
        public const string CommandMiniEditorStateSet = "markdown.mini-editor.state.set";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private readonly MarkdownPipeline _pipeline;
        private readonly MarkdownComposerStateService _state;

        public MarkdownIpcEndpointService(MarkdownComposerStateService? state = null)
        {
            _pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .Build();
            _state = state ?? new MarkdownComposerStateService();
        }

        public bool TryHandleRequest(string command, JsonElement requestRoot, out string responseJson)
        {
            responseJson = string.Empty;
            if (string.IsNullOrWhiteSpace(command)) return false;

            var normalized = command.Trim();

            if (string.Equals(normalized, CommandRender, StringComparison.OrdinalIgnoreCase))
            {
                var markdown = ExtractMarkdown(requestRoot);
                var html = string.Empty;
                try
                {
                    html = Markdown.ToHtml(markdown, _pipeline);
                }
                catch
                {
                    html = string.Empty;
                }

                var payload = new MarkdownRenderDto(RenderSchemaVersion, DateTime.UtcNow, markdown, html);
                responseJson = JsonSerializer.Serialize(payload, JsonOptions);
                return true;
            }

            if (string.Equals(normalized, CommandFormatApply, StringComparison.OrdinalIgnoreCase))
            {
                var request = ExtractFormatRequest(requestRoot);
                var result = ApplyFormat(request);
                var payload = new MarkdownFormatDto(
                    FormatSchemaVersion,
                    DateTime.UtcNow,
                    result.Markdown,
                    result.SelectionStart,
                    result.SelectionEnd,
                    request.Kind,
                    request.Level);
                responseJson = JsonSerializer.Serialize(payload, JsonOptions);
                return true;
            }

            if (string.Equals(normalized, CommandUiConfigGet, StringComparison.OrdinalIgnoreCase))
            {
                var payload = new MarkdownUiConfigDto(
                    UiConfigSchemaVersion,
                    DateTime.UtcNow,
                    new PreviewButtonConfigDto(
                        Id: "markdown-preview-toggle",
                        Icon: "eye",
                        Tooltip: "Toggle markdown preview",
                        Enabled: true),
                    new ToolbarConfigDto(
                        AutoHideOnSelectionClear: true,
                        PinWhileApplyingActions: true,
                        Actions: new[]
                        {
                            new ToolbarActionDto("header", "H", "Header Size", SupportsLevel: true, MaxLevel: 6),
                            new ToolbarActionDto("bold", "B", "Bold", false, 0),
                            new ToolbarActionDto("italic", "I", "Italic", false, 0),
                            new ToolbarActionDto("underline", "U", "Underline", false, 0),
                            new ToolbarActionDto("strikethrough", "S", "Strikethrough", false, 0),
                            new ToolbarActionDto("spoiler", "||", "Spoiler", false, 0),
                            new ToolbarActionDto("link", "link", "Link", false, 0),
                            new ToolbarActionDto("quote", ">", "Quote", false, 0),
                            new ToolbarActionDto("inlinecode", "</>", "Inline Code", false, 0),
                            new ToolbarActionDto("codeblock", "{}", "Code Block", false, 0)
                        }),
                    new MiniEditorConfigDto(
                        Enabled: true,
                        Mode: "wysiwyg-markdown",
                        AutoHideToolbarOnSelectionClear: true,
                        PinToolbarForSequentialActions: true,
                        SupportsNotepadSurface: true,
                        SupportsSendToCanvas: true,
                        UsesSplitPreviewPane: false));

                responseJson = JsonSerializer.Serialize(payload, JsonOptions);
                return true;
            }

            if (string.Equals(normalized, CommandDraftGet, StringComparison.OrdinalIgnoreCase))
            {
                var draft = _state.GetDraft();
                var payload = new MarkdownDraftStateDto(StateSchemaVersion, DateTime.UtcNow, draft.Markdown, draft.SelectionStart, draft.SelectionEnd);
                responseJson = JsonSerializer.Serialize(payload, JsonOptions);
                return true;
            }

            if (string.Equals(normalized, CommandDraftSet, StringComparison.OrdinalIgnoreCase))
            {
                var request = ExtractDraftSetRequest(requestRoot);
                var draft = _state.SetDraft(request.Markdown, request.SelectionStart, request.SelectionEnd);
                var payload = new MarkdownDraftStateDto(StateSchemaVersion, DateTime.UtcNow, draft.Markdown, draft.SelectionStart, draft.SelectionEnd);
                responseJson = JsonSerializer.Serialize(payload, JsonOptions);
                return true;
            }

            if (string.Equals(normalized, CommandPreviewStateGet, StringComparison.OrdinalIgnoreCase))
            {
                var visible = _state.GetPreviewVisible();
                var payload = new MarkdownPreviewStateDto(StateSchemaVersion, DateTime.UtcNow, visible);
                responseJson = JsonSerializer.Serialize(payload, JsonOptions);
                return true;
            }

            if (string.Equals(normalized, CommandPreviewStateSet, StringComparison.OrdinalIgnoreCase))
            {
                var visible = ExtractBoolPayload(requestRoot, "visible", defaultValue: true);
                var next = _state.SetPreviewVisible(visible);
                var payload = new MarkdownPreviewStateDto(StateSchemaVersion, DateTime.UtcNow, next);
                responseJson = JsonSerializer.Serialize(payload, JsonOptions);
                return true;
            }

            if (string.Equals(normalized, CommandToolbarStateGet, StringComparison.OrdinalIgnoreCase))
            {
                var toolbar = _state.GetToolbarState();
                var payload = new MarkdownToolbarStateDto(StateSchemaVersion, DateTime.UtcNow, toolbar.Visible, toolbar.Pinned);
                responseJson = JsonSerializer.Serialize(payload, JsonOptions);
                return true;
            }

            if (string.Equals(normalized, CommandToolbarStateSet, StringComparison.OrdinalIgnoreCase))
            {
                var visible = ExtractBoolPayload(requestRoot, "visible", defaultValue: false);
                var pinned = ExtractBoolPayload(requestRoot, "pinned", defaultValue: false);
                var toolbar = _state.SetToolbarState(visible, pinned);
                var payload = new MarkdownToolbarStateDto(StateSchemaVersion, DateTime.UtcNow, toolbar.Visible, toolbar.Pinned);
                responseJson = JsonSerializer.Serialize(payload, JsonOptions);
                return true;
            }

            if (string.Equals(normalized, CommandMiniEditorStateGet, StringComparison.OrdinalIgnoreCase))
            {
                var mini = _state.GetMiniEditorState();
                var payload = new MarkdownMiniEditorStateDto(StateSchemaVersion, DateTime.UtcNow, mini.Open, mini.Pinned, mini.Content);
                responseJson = JsonSerializer.Serialize(payload, JsonOptions);
                return true;
            }

            if (string.Equals(normalized, CommandMiniEditorStateSet, StringComparison.OrdinalIgnoreCase))
            {
                var open = ExtractBoolPayload(requestRoot, "open", defaultValue: false);
                var pinned = ExtractBoolPayload(requestRoot, "pinned", defaultValue: false);
                var content = ExtractStringPayload(requestRoot, "content");
                var mini = _state.SetMiniEditorState(open, pinned, content);
                var payload = new MarkdownMiniEditorStateDto(StateSchemaVersion, DateTime.UtcNow, mini.Open, mini.Pinned, mini.Content);
                responseJson = JsonSerializer.Serialize(payload, JsonOptions);
                return true;
            }

            return false;
        }

        private static string ExtractMarkdown(JsonElement root)
        {
            try
            {
                if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                {
                    return string.Empty;
                }

                if (!payload.TryGetProperty("markdown", out var markdownElement))
                {
                    return string.Empty;
                }

                return markdownElement.ValueKind == JsonValueKind.String
                    ? markdownElement.GetString() ?? string.Empty
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static MarkdownFormatRequest ExtractFormatRequest(JsonElement root)
        {
            try
            {
                if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                {
                    return MarkdownFormatRequest.Empty;
                }

                var markdown = payload.TryGetProperty("markdown", out var markdownElement) && markdownElement.ValueKind == JsonValueKind.String
                    ? markdownElement.GetString() ?? string.Empty
                    : string.Empty;

                var selectionStart = payload.TryGetProperty("selectionStart", out var startElement) && startElement.TryGetInt32(out var start)
                    ? start
                    : 0;
                var selectionEnd = payload.TryGetProperty("selectionEnd", out var endElement) && endElement.TryGetInt32(out var end)
                    ? end
                    : selectionStart;

                var kind = payload.TryGetProperty("kind", out var kindElement) && kindElement.ValueKind == JsonValueKind.String
                    ? kindElement.GetString() ?? string.Empty
                    : string.Empty;
                var level = payload.TryGetProperty("level", out var levelElement) && levelElement.TryGetInt32(out var parsedLevel)
                    ? parsedLevel
                    : 1;

                return new MarkdownFormatRequest(markdown, selectionStart, selectionEnd, kind.Trim(), level);
            }
            catch
            {
                return MarkdownFormatRequest.Empty;
            }
        }

        private static MarkdownDraftSetRequest ExtractDraftSetRequest(JsonElement root)
        {
            try
            {
                if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                {
                    return MarkdownDraftSetRequest.Empty;
                }

                var markdown = payload.TryGetProperty("markdown", out var markdownElement) && markdownElement.ValueKind == JsonValueKind.String
                    ? markdownElement.GetString() ?? string.Empty
                    : string.Empty;
                var selectionStart = payload.TryGetProperty("selectionStart", out var startElement) && startElement.TryGetInt32(out var start)
                    ? start
                    : 0;
                var selectionEnd = payload.TryGetProperty("selectionEnd", out var endElement) && endElement.TryGetInt32(out var end)
                    ? end
                    : selectionStart;
                return new MarkdownDraftSetRequest(markdown, selectionStart, selectionEnd);
            }
            catch
            {
                return MarkdownDraftSetRequest.Empty;
            }
        }

        private static bool ExtractBoolPayload(JsonElement root, string name, bool defaultValue)
        {
            try
            {
                if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                {
                    return defaultValue;
                }

                if (!payload.TryGetProperty(name, out var value))
                {
                    return defaultValue;
                }

                return value.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => defaultValue
                };
            }
            catch
            {
                return defaultValue;
            }
        }

        private static string ExtractStringPayload(JsonElement root, string name)
        {
            try
            {
                if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                {
                    return string.Empty;
                }

                return payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? string.Empty
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static MarkdownFormatResult ApplyFormat(MarkdownFormatRequest request)
        {
            var text = request.Markdown ?? string.Empty;
            var start = Math.Clamp(request.SelectionStart, 0, text.Length);
            var end = Math.Clamp(request.SelectionEnd, 0, text.Length);
            if (end < start)
            {
                (start, end) = (end, start);
            }

            var kind = (request.Kind ?? string.Empty).ToLowerInvariant();
            return kind switch
            {
                "bold" => ApplyInline(text, start, end, "**", "**", "bold text"),
                "italic" => ApplyInline(text, start, end, "*", "*", "italic text"),
                "underline" => ApplyInline(text, start, end, "++", "++", "underline"),
                "strikethrough" or "strike" => ApplyInline(text, start, end, "~~", "~~", "strike"),
                "spoiler" => ApplyInline(text, start, end, "||", "||", "spoiler"),
                "inlinecode" or "code" => ApplyInline(text, start, end, "`", "`", "code"),
                "codeblock" => ApplyCodeBlock(text, start, end),
                "quote" => ApplyQuote(text, start, end),
                "link" => ApplyLink(text, start, end),
                "header" => ApplyHeader(text, start, end, request.Level),
                _ => new MarkdownFormatResult(text, start, end)
            };
        }

        private static MarkdownFormatResult ApplyInline(string text, int start, int end, string prefix, string suffix, string placeholder)
        {
            if (start == end)
            {
                var insertion = string.Concat(prefix, placeholder, suffix);
                var markdown = text.Insert(start, insertion);
                var nextStart = start + prefix.Length;
                return new MarkdownFormatResult(markdown, nextStart, nextStart + placeholder.Length);
            }

            var selected = text.Substring(start, end - start);
            var replacement = string.Concat(prefix, selected, suffix);
            var markdownReplaced = text.Remove(start, end - start).Insert(start, replacement);
            var selectionStart = start + prefix.Length;
            return new MarkdownFormatResult(markdownReplaced, selectionStart, selectionStart + selected.Length);
        }

        private static MarkdownFormatResult ApplyCodeBlock(string text, int start, int end)
        {
            var selected = start == end ? "code" : text.Substring(start, end - start);
            var block = $"```{Environment.NewLine}{selected}{Environment.NewLine}```";
            var markdown = text.Remove(start, end - start).Insert(start, block);
            var selectionStart = start + 3 + Environment.NewLine.Length;
            return new MarkdownFormatResult(markdown, selectionStart, selectionStart + selected.Length);
        }

        private static MarkdownFormatResult ApplyQuote(string text, int start, int end)
        {
            if (start == end)
            {
                const string placeholder = "quote text";
                var insertion = $"> {placeholder}";
                var markdown = text.Insert(start, insertion);
                var selectionStart = start + 2;
                return new MarkdownFormatResult(markdown, selectionStart, selectionStart + placeholder.Length);
            }

            var selected = text.Substring(start, end - start).Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = selected.Split('\n').Select(line => $"> {line}");
            var replacement = string.Join(Environment.NewLine, lines);
            var markdownReplaced = text.Remove(start, end - start).Insert(start, replacement);
            return new MarkdownFormatResult(markdownReplaced, start, start + replacement.Length);
        }

        private static MarkdownFormatResult ApplyLink(string text, int start, int end)
        {
            const string url = "https://example.com";
            if (start == end)
            {
                const string label = "link text";
                var insertion = $"[{label}]({url})";
                var markdown = text.Insert(start, insertion);
                var selectionStart = start + label.Length + 3;
                return new MarkdownFormatResult(markdown, selectionStart, selectionStart + url.Length);
            }

            var selected = text.Substring(start, end - start);
            var replacement = $"[{selected}]({url})";
            var markdownReplaced = text.Remove(start, end - start).Insert(start, replacement);
            var linkStart = start + selected.Length + 3;
            return new MarkdownFormatResult(markdownReplaced, linkStart, linkStart + url.Length);
        }

        private static MarkdownFormatResult ApplyHeader(string text, int start, int end, int level)
        {
            level = Math.Clamp(level, 1, 6);
            var prefix = new string('#', level) + " ";

            if (start == end)
            {
                const string placeholder = "heading";
                var insertion = prefix + placeholder;
                var markdown = text.Insert(start, insertion);
                var selectionStart = start + prefix.Length;
                return new MarkdownFormatResult(markdown, selectionStart, selectionStart + placeholder.Length);
            }

            var selected = text.Substring(start, end - start).Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = selected.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                lines[i] = prefix + trimmed.TrimStart('#').TrimStart();
            }

            var replacement = string.Join(Environment.NewLine, lines);
            var markdownReplaced = text.Remove(start, end - start).Insert(start, replacement);
            return new MarkdownFormatResult(markdownReplaced, start, start + replacement.Length);
        }

        private sealed record MarkdownRenderDto(int SchemaVersion, DateTime GeneratedUtc, string Markdown, string Html);
        private sealed record MarkdownFormatDto(int SchemaVersion, DateTime GeneratedUtc, string Markdown, int SelectionStart, int SelectionEnd, string Kind, int Level);
        private sealed record MarkdownUiConfigDto(int SchemaVersion, DateTime GeneratedUtc, PreviewButtonConfigDto PreviewButton, ToolbarConfigDto Toolbar, MiniEditorConfigDto MiniEditor);
        private sealed record MarkdownDraftStateDto(int SchemaVersion, DateTime GeneratedUtc, string Markdown, int SelectionStart, int SelectionEnd);
        private sealed record MarkdownPreviewStateDto(int SchemaVersion, DateTime GeneratedUtc, bool Visible);
        private sealed record MarkdownToolbarStateDto(int SchemaVersion, DateTime GeneratedUtc, bool Visible, bool Pinned);
        private sealed record MarkdownMiniEditorStateDto(int SchemaVersion, DateTime GeneratedUtc, bool Open, bool Pinned, string Content);
        private sealed record PreviewButtonConfigDto(string Id, string Icon, string Tooltip, bool Enabled);
        private sealed record ToolbarConfigDto(bool AutoHideOnSelectionClear, bool PinWhileApplyingActions, ToolbarActionDto[] Actions);
        private sealed record ToolbarActionDto(string Kind, string Icon, string Tooltip, bool SupportsLevel, int MaxLevel);
        private sealed record MiniEditorConfigDto(bool Enabled, string Mode, bool AutoHideToolbarOnSelectionClear, bool PinToolbarForSequentialActions, bool SupportsNotepadSurface, bool SupportsSendToCanvas, bool UsesSplitPreviewPane);
        private readonly record struct MarkdownFormatRequest(string Markdown, int SelectionStart, int SelectionEnd, string Kind, int Level)
        {
            public static MarkdownFormatRequest Empty { get; } = new(string.Empty, 0, 0, string.Empty, 1);
        }
        private readonly record struct MarkdownDraftSetRequest(string Markdown, int SelectionStart, int SelectionEnd)
        {
            public static MarkdownDraftSetRequest Empty { get; } = new(string.Empty, 0, 0);
        }
        private readonly record struct MarkdownFormatResult(string Markdown, int SelectionStart, int SelectionEnd);
    }
}
