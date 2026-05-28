using System;

namespace Zer0Talk.Services
{
    /// <summary>
    /// In-process markdown composer state for hybrid shell workflows.
    /// Keeps draft, preview toggle, toolbar state, and mini-editor state.
    /// </summary>
    public sealed class MarkdownComposerStateService
    {
        private readonly object _gate = new();

        private string _draft = string.Empty;
        private int _selectionStart;
        private int _selectionEnd;
        private bool _previewVisible = true;
        private bool _toolbarVisible;
        private bool _toolbarPinned;
        private bool _miniEditorOpen;
        private bool _miniEditorPinned;
        private string _miniEditorContent = string.Empty;

        public MarkdownDraftState GetDraft()
        {
            lock (_gate)
            {
                return new MarkdownDraftState(_draft, _selectionStart, _selectionEnd);
            }
        }

        public MarkdownDraftState SetDraft(string markdown, int selectionStart, int selectionEnd)
        {
            lock (_gate)
            {
                _draft = markdown ?? string.Empty;
                var clampedStart = Math.Clamp(selectionStart, 0, _draft.Length);
                var clampedEnd = Math.Clamp(selectionEnd, 0, _draft.Length);
                if (clampedEnd < clampedStart)
                {
                    (clampedStart, clampedEnd) = (clampedEnd, clampedStart);
                }

                _selectionStart = clampedStart;
                _selectionEnd = clampedEnd;
                return new MarkdownDraftState(_draft, _selectionStart, _selectionEnd);
            }
        }

        public bool GetPreviewVisible()
        {
            lock (_gate)
            {
                return _previewVisible;
            }
        }

        public bool SetPreviewVisible(bool visible)
        {
            lock (_gate)
            {
                _previewVisible = visible;
                return _previewVisible;
            }
        }

        public MarkdownToolbarState GetToolbarState()
        {
            lock (_gate)
            {
                return new MarkdownToolbarState(_toolbarVisible, _toolbarPinned);
            }
        }

        public MarkdownToolbarState SetToolbarState(bool visible, bool pinned)
        {
            lock (_gate)
            {
                _toolbarVisible = visible;
                _toolbarPinned = pinned;
                return new MarkdownToolbarState(_toolbarVisible, _toolbarPinned);
            }
        }

        public MarkdownMiniEditorState GetMiniEditorState()
        {
            lock (_gate)
            {
                return new MarkdownMiniEditorState(_miniEditorOpen, _miniEditorPinned, _miniEditorContent);
            }
        }

        public MarkdownMiniEditorState SetMiniEditorState(bool open, bool pinned, string content)
        {
            lock (_gate)
            {
                _miniEditorOpen = open;
                _miniEditorPinned = pinned;
                _miniEditorContent = content ?? string.Empty;
                return new MarkdownMiniEditorState(_miniEditorOpen, _miniEditorPinned, _miniEditorContent);
            }
        }
    }

    public readonly record struct MarkdownDraftState(string Markdown, int SelectionStart, int SelectionEnd);

    public readonly record struct MarkdownToolbarState(bool Visible, bool Pinned);

    public readonly record struct MarkdownMiniEditorState(bool Open, bool Pinned, string Content);
}
