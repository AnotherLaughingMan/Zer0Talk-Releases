using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Zer0Talk.Controls;

/// <summary>
/// Floating markdown formatting toolbar that appears when text is selected.
/// Similar to Discord's text selection toolbar.
/// </summary>
public partial class MarkdownToolbar : UserControl
{
    private int? _selectionSnapshotStart;
    private int? _selectionSnapshotEnd;
    private DateTime _selectionSnapshotAt = DateTime.MinValue;
    private const int SelectionSnapshotTtlMs = 3000;

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
        AddHandler(InputElement.PointerPressedEvent, OnToolbarPointerPressed, RoutingStrategies.Tunnel);
    }

    private void OnToolbarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            CaptureSelectionSnapshot(TargetTextBox);
        }
        catch { }
    }

    private static (int Start, int End) GetClampedSelection(TextBox input, int textLength)
    {
        var rawStart = input.SelectionStart;
        var rawEnd = input.SelectionEnd;
        var start = Math.Min(rawStart, rawEnd);
        start = Math.Max(0, Math.Min(start, textLength));
        var end = Math.Max(rawStart, rawEnd);
        end = Math.Max(0, Math.Min(end, textLength));
        return (start, end);
    }

    private void CaptureSelectionSnapshot(TextBox? input)
    {
        if (input is null)
        {
            return;
        }

        var text = input.Text ?? string.Empty;
        var (start, end) = GetClampedSelection(input, text.Length);
        if (end <= start)
        {
            return;
        }

        _selectionSnapshotStart = start;
        _selectionSnapshotEnd = end;
        _selectionSnapshotAt = DateTime.UtcNow;
    }

    private bool TryResolveSelectionRange(TextBox input, out int start, out int end)
    {
        var text = input.Text ?? string.Empty;
        var length = text.Length;
        var current = GetClampedSelection(input, length);
        start = current.Start;
        end = current.End;

        if (end > start)
        {
            CaptureSelectionSnapshot(input);
            return true;
        }

        var snapshotIsFresh = (DateTime.UtcNow - _selectionSnapshotAt).TotalMilliseconds <= SelectionSnapshotTtlMs;
        if (!snapshotIsFresh || !_selectionSnapshotStart.HasValue || !_selectionSnapshotEnd.HasValue)
        {
            start = end = Math.Max(0, Math.Min(input.CaretIndex, length));
            return false;
        }

        start = Math.Max(0, Math.Min(_selectionSnapshotStart.Value, length));
        end = Math.Max(0, Math.Min(_selectionSnapshotEnd.Value, length));
        if (end <= start)
        {
            start = end = Math.Max(0, Math.Min(input.CaretIndex, length));
            return false;
        }

        return true;
    }

    private void BoldButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplyFormatting("**", "**", "bold text");
    }

    private void ItalicButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplyFormatting("*", "*", "italic text");
    }

    private void UnderlineButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplyFormatting("++", "++", "underline");
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
            var hasSelection = TryResolveSelectionRange(input, out var start, out var end);

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
                CaptureSelectionSnapshot(input);
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
                CaptureSelectionSnapshot(input);
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
            var hasSelection = TryResolveSelectionRange(input, out var start, out var end);

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
            CaptureSelectionSnapshot(input);
            input.Focus();
        }
        catch { }
    }
}
