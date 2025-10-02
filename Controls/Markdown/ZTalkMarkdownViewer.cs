// Replace entire corrupted file with a minimal, safe stub for now.
// Purpose: provide a stable placeholder so the app can run while we fully redesign
// markdown rendering later. TODO: Rebuild markdown renderer using a safe, incremental
// approach (e.g., Markdig -> renderer that creates fresh controls per message and
// never reuses visual elements). For now this control renders plain text only.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;

namespace ZTalk.Controls.Markdown
{
    /// <summary>
    /// Minimal, safe stub of the markdown viewer used by XAML.
    /// Renders plain text only and intentionally avoids any complex rendering logic
    /// to prevent visual-parent and performance issues during startup or typing.
    /// TODO: Replace with a proper Markdig-based renderer implemented safely.
    /// </summary>
    public sealed class ZTalkMarkdownViewer : ContentControl
    {
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
            // Initialize to an empty plain TextBlock to avoid any expensive work at construction
            Content = CreatePlainTextBlock(string.Empty);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            try
            {
                if (change.Property == MarkdownProperty)
                {
                    // Lightweight update: render plain text only. No parsing, no heavy work.
                    var text = Markdown ?? string.Empty;
                    Content = CreatePlainTextBlock(text);
                }
            }
            catch
            {
                // Silently degrade to empty content if anything goes wrong
                Content = CreatePlainTextBlock(string.Empty);
            }
        }

        // Helper: create a TextBlock configured for chat message layout
        private static TextBlock CreatePlainTextBlock(string text)
            => new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Foreground = Brushes.Transparent == null ? Brushes.Black : Brushes.Black // defensive: ensure a brush exists
            };
    }
}