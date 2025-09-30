using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ColorTextBlock.Avalonia;
using Markdown.Avalonia;
using Markdown.Avalonia.Parsers;
using Markdown.Avalonia.Plugins;

namespace P2PTalk.Controls.Markdown;

public sealed class ChatSpoilerPlugin : IMdAvPlugin
{
    public void Setup(SetupInfo info)
    {
        if (info is null) throw new ArgumentNullException(nameof(info));
        info.Register(new SpoilerInlineParser());
    }

    private sealed class SpoilerInlineParser : InlineParser
    {
        private static readonly Regex SpoilerPattern = new(@"\|\|(?<spoiler>.+?)\|\|", RegexOptions.Compiled | RegexOptions.Singleline);

        public SpoilerInlineParser()
            : base(SpoilerPattern, nameof(SpoilerInlineParser))
        {
        }

        public override IEnumerable<CInline> Convert(string text, Match firstMatch, IMarkdownEngine engine, out int parseTextBegin, out int parseTextEnd)
        {
            if (firstMatch is null) throw new ArgumentNullException(nameof(firstMatch));
            if (engine is null) throw new ArgumentNullException(nameof(engine));

            parseTextBegin = firstMatch.Index;
            parseTextEnd = firstMatch.Index + firstMatch.Length;

            var inner = firstMatch.Groups["spoiler"].Value;
            var control = new SpoilerInlineControl(inner, engine);

            return new CInline[] { new CInlineUIContainer(control) };
        }
    }
}

public sealed class SpoilerInlineControl : Border
{
    private readonly string _markdown;
    private readonly IMarkdownEngine _engine;
    private readonly CTextBlock _contentBlock;
    private readonly TextBlock _placeholderBlock;

    private bool _isRevealed;
    private bool _contentInitialized;

    public SpoilerInlineControl(string markdown, IMarkdownEngine engine)
    {
        _markdown = markdown ?? string.Empty;
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));

        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(6, 1);
        Margin = new Thickness(2, 0);
        Cursor = new Cursor(StandardCursorType.Hand);
        Background = Brushes.Transparent;
        Focusable = true;
        ClipToBounds = false;
        Classes.Add("chat-spoiler");

        _contentBlock = new CTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsHitTestVisible = false,
            IsVisible = false
        };

        _placeholderBlock = new TextBlock
        {
            Text = "Spoiler",
            FontStyle = FontStyle.Italic,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.75,
            IsHitTestVisible = false,
            IsVisible = true
        };

        var grid = new Grid { ClipToBounds = false };
        grid.Children.Add(_placeholderBlock);
        grid.Children.Add(_contentBlock);
        Child = grid;

        PointerPressed += OnPointerPressed;
        KeyDown += OnKeyDown;

        UpdateVisualState();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        ToggleSpoiler();
        Focus();
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (e.Key is Key.Enter or Key.Space)
        {
            ToggleSpoiler();
            e.Handled = true;
        }
    }

    private void InitializeContent()
    {
        try
        {
            var inlines = _engine.RunSpanGamut(_markdown) ?? Array.Empty<CInline>();
            _contentBlock.Content = new AvaloniaList<CInline>(inlines);
            _contentInitialized = true;
        }
        catch
        {
            // Fall back to plain text if markdown parsing fails
            _contentBlock.Content = new AvaloniaList<CInline>(new[] { new CRun { Text = _markdown } });
            _contentInitialized = true;
        }
    }

    private void UpdateVisualState()
    {
        _contentBlock.IsVisible = _isRevealed;
        _placeholderBlock.IsVisible = !_isRevealed;

        if (_placeholderBlock.IsVisible)
        {
            _placeholderBlock.Opacity = 0.75;
        }

        InvalidateMeasure();
        InvalidateArrange();

        if (Parent is Layoutable layoutable)
        {
            layoutable.InvalidateMeasure();
            layoutable.InvalidateArrange();
        }
    }

    private void ToggleSpoiler()
    {
        _isRevealed = !_isRevealed;
        if (_isRevealed && !_contentInitialized)
        {
            InitializeContent();
        }
        UpdateVisualState();
    }
}
