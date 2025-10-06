using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ZTalk.Controls;

/// <summary>
/// Floating markdown formatting toolbar that appears when text is selected.
/// Similar to Discord's text selection toolbar.
/// </summary>
public partial class MarkdownToolbar : UserControl
{
    public static readonly StyledProperty<TextBox?> TargetTextBoxProperty =
        AvaloniaProperty.Register<MarkdownToolbar, TextBox?>(nameof(TargetTextBox));

    public TextBox? TargetTextBox
    {
        get => GetValue(TargetTextBoxProperty);
        set => SetValue(TargetTextBoxProperty, value);
    }

    public MarkdownToolbar()
    {
        InitializeComponent();
    }

    private void BoldButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplyFormatting("**", "**", "bold text");
    }

    private void ItalicButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplyFormatting("*", "*", "italic text");
    }

    private void StrikeButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplyFormatting("~~", "~~", "strike");
    }

    private void CodeButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplyFormatting("`", "`", "code");
    }

    private void LinkButton_Click(object? sender, RoutedEventArgs e)
    {
        var input = TargetTextBox;
        if (input == null) return;

        try
        {
            var text = input.Text ?? string.Empty;
            var start = Math.Min(input.SelectionStart, input.SelectionEnd);
            var end = Math.Max(input.SelectionStart, input.SelectionEnd);
            var hasSelection = end > start;

            if (hasSelection)
            {
                var selectedText = text.Substring(start, end - start);
                var replacement = $"[{selectedText}](url)";
                var updatedText = text.Remove(start, end - start).Insert(start, replacement);
                input.Text = updatedText;
                
                // Select "url" part for easy replacement
                var urlStart = start + selectedText.Length + 3; // After "](
                input.SelectionStart = urlStart;
                input.SelectionEnd = urlStart + 3;
                input.CaretIndex = urlStart + 3;
            }
            else
            {
                var insertion = "[link text](url)";
                var updatedText = text.Insert(start, insertion);
                input.Text = updatedText;
                
                // Select "link text" for easy replacement
                input.SelectionStart = start + 1;
                input.SelectionEnd = start + 10;
                input.CaretIndex = start + 10;
            }

            input.Focus();
        }
        catch { }
    }

    private void SpoilerButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplyFormatting("||", "||", "spoiler");
    }

    private void ApplyFormatting(string prefix, string suffix, string placeholder)
    {
        var input = TargetTextBox;
        if (input == null) return;

        try
        {
            var text = input.Text ?? string.Empty;
            var start = Math.Min(input.SelectionStart, input.SelectionEnd);
            var end = Math.Max(input.SelectionStart, input.SelectionEnd);
            var hasSelection = end > start;

            string updatedText;
            int newSelectionStart;
            int newSelectionEnd;

            if (!hasSelection)
            {
                var insertion = string.Concat(prefix, placeholder, suffix);
                updatedText = text.Insert(start, insertion);
                newSelectionStart = start + prefix.Length;
                newSelectionEnd = newSelectionStart + placeholder.Length;
            }
            else
            {
                var selectedText = text.Substring(start, end - start);
                var replacement = string.Concat(prefix, selectedText, suffix);
                updatedText = text.Remove(start, end - start).Insert(start, replacement);
                newSelectionStart = start + prefix.Length;
                newSelectionEnd = newSelectionStart + selectedText.Length;
            }

            input.Text = updatedText;
            input.SelectionStart = newSelectionStart;
            input.SelectionEnd = newSelectionEnd;
            input.CaretIndex = newSelectionEnd;
            input.Focus();
        }
        catch { }
    }
}
