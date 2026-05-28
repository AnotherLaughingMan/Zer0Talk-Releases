using System;
using System.Threading;
using System.Threading.Tasks;

namespace Zer0Talk.Services
{
    /// <summary>
    /// Stable adapter surface for hybrid markdown shell integration.
    /// Wraps markdown IPC commands into a single stateful service for Tauri shell consumers.
    /// </summary>
    public sealed class HybridShellMarkdownAdapterService : IDisposable
    {
        private readonly HybridShellIpcClientService _client;
        private readonly object _stateGate = new();

        private bool _started;
        private bool _disposed;

        private HybridShellMarkdownAdapterState _state = HybridShellMarkdownAdapterState.Empty;
        private HybridShellIpcClientService.MarkdownUiConfigResponseDto? _uiConfig;
        private HybridShellIpcClientService.MarkdownDraftStateResponseDto? _draft;
        private HybridShellIpcClientService.MarkdownPreviewStateResponseDto? _preview;
        private HybridShellIpcClientService.MarkdownToolbarStateResponseDto? _toolbar;
        private HybridShellIpcClientService.MarkdownMiniEditorStateResponseDto? _miniEditor;

        public HybridShellMarkdownAdapterService(HybridShellIpcClientService client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public bool IsStarted
        {
            get { lock (_stateGate) { return _started; } }
        }

        public HybridShellMarkdownAdapterState CurrentState
        {
            get { lock (_stateGate) { return _state; } }
        }

        public HybridShellIpcClientService.MarkdownUiConfigResponseDto? UiConfig
        {
            get { lock (_stateGate) { return _uiConfig; } }
        }

        public HybridShellIpcClientService.MarkdownDraftStateResponseDto? Draft
        {
            get { lock (_stateGate) { return _draft; } }
        }

        public HybridShellIpcClientService.MarkdownPreviewStateResponseDto? Preview
        {
            get { lock (_stateGate) { return _preview; } }
        }

        public HybridShellIpcClientService.MarkdownToolbarStateResponseDto? Toolbar
        {
            get { lock (_stateGate) { return _toolbar; } }
        }

        public HybridShellIpcClientService.MarkdownMiniEditorStateResponseDto? MiniEditor
        {
            get { lock (_stateGate) { return _miniEditor; } }
        }

        public event Action<HybridShellMarkdownAdapterState>? StateChanged;

        public async Task<bool> StartAsync(bool markdownEnabled, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (!markdownEnabled)
            {
                await StopAsync().ConfigureAwait(false);
                return false;
            }

            if (!await _client.ConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                lock (_stateGate)
                {
                    _started = false;
                    _state = HybridShellMarkdownAdapterState.Empty;
                }
                return false;
            }

            var uiConfig = await _client.RequestMarkdownUiConfigAsync(cancellationToken).ConfigureAwait(false);
            var draft = await _client.RequestMarkdownDraftAsync(cancellationToken).ConfigureAwait(false);
            var preview = await _client.RequestMarkdownPreviewStateAsync(cancellationToken).ConfigureAwait(false);
            var toolbar = await _client.RequestMarkdownToolbarStateAsync(cancellationToken).ConfigureAwait(false);
            var miniEditor = await _client.RequestMarkdownMiniEditorStateAsync(cancellationToken).ConfigureAwait(false);

            lock (_stateGate)
            {
                _started = true;
                _uiConfig = uiConfig;
                _draft = draft;
                _preview = preview;
                _toolbar = toolbar;
                _miniEditor = miniEditor;
                _state = BuildState();
            }

            try { StateChanged?.Invoke(CurrentState); } catch { }
            return true;
        }

        public async Task StopAsync()
        {
            await _client.DisconnectAsync().ConfigureAwait(false);

            HybridShellMarkdownAdapterState state;
            lock (_stateGate)
            {
                _started = false;
                _uiConfig = null;
                _draft = null;
                _preview = null;
                _toolbar = null;
                _miniEditor = null;
                _state = HybridShellMarkdownAdapterState.Empty;
                state = _state;
            }

            try { StateChanged?.Invoke(state); } catch { }
        }

        public async Task<HybridShellIpcClientService.MarkdownRenderResponseDto?> RenderAsync(string markdown, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return await _client.RequestMarkdownRenderAsync(markdown, cancellationToken).ConfigureAwait(false);
        }

        public async Task<HybridShellIpcClientService.MarkdownFormatResponseDto?> ApplyFormatAsync(string markdown, int selectionStart, int selectionEnd, string kind, int level = 1, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var formatted = await _client.ApplyMarkdownFormatAsync(markdown, selectionStart, selectionEnd, kind, level, cancellationToken).ConfigureAwait(false);
            if (formatted == null) return null;

            lock (_stateGate)
            {
                _draft = new HybridShellIpcClientService.MarkdownDraftStateResponseDto(
                    SchemaVersion: formatted.SchemaVersion,
                    GeneratedUtc: formatted.GeneratedUtc,
                    Markdown: formatted.Markdown,
                    SelectionStart: formatted.SelectionStart,
                    SelectionEnd: formatted.SelectionEnd);
                _state = BuildState();
            }

            try { StateChanged?.Invoke(CurrentState); } catch { }
            return formatted;
        }

        public async Task<HybridShellIpcClientService.MarkdownDraftStateResponseDto?> SetDraftAsync(string markdown, int selectionStart, int selectionEnd, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var draft = await _client.SetMarkdownDraftAsync(markdown, selectionStart, selectionEnd, cancellationToken).ConfigureAwait(false);
            if (draft == null) return null;

            lock (_stateGate)
            {
                _draft = draft;
                _state = BuildState();
            }

            try { StateChanged?.Invoke(CurrentState); } catch { }
            return draft;
        }

        public async Task<HybridShellIpcClientService.MarkdownPreviewStateResponseDto?> SetPreviewStateAsync(bool visible, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var preview = await _client.SetMarkdownPreviewStateAsync(visible, cancellationToken).ConfigureAwait(false);
            if (preview == null) return null;

            lock (_stateGate)
            {
                _preview = preview;
                _state = BuildState();
            }

            try { StateChanged?.Invoke(CurrentState); } catch { }
            return preview;
        }

        public async Task<HybridShellIpcClientService.MarkdownToolbarStateResponseDto?> SetToolbarStateAsync(bool visible, bool pinned, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var toolbar = await _client.SetMarkdownToolbarStateAsync(visible, pinned, cancellationToken).ConfigureAwait(false);
            if (toolbar == null) return null;

            lock (_stateGate)
            {
                _toolbar = toolbar;
                _state = BuildState();
            }

            try { StateChanged?.Invoke(CurrentState); } catch { }
            return toolbar;
        }

        public async Task<HybridShellIpcClientService.MarkdownMiniEditorStateResponseDto?> SetMiniEditorStateAsync(bool open, bool pinned, string content, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var miniEditor = await _client.SetMarkdownMiniEditorStateAsync(open, pinned, content, cancellationToken).ConfigureAwait(false);
            if (miniEditor == null) return null;

            lock (_stateGate)
            {
                _miniEditor = miniEditor;
                _state = BuildState();
            }

            try { StateChanged?.Invoke(CurrentState); } catch { }
            return miniEditor;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { StopAsync().GetAwaiter().GetResult(); } catch { }
        }

        private HybridShellMarkdownAdapterState BuildState()
        {
            return new HybridShellMarkdownAdapterState(
                IsConnected: _client.IsConnected,
                SupportsRender: _client.SupportsMarkdownRender,
                SupportsFormatApply: _client.SupportsMarkdownFormatApply,
                SupportsUiConfig: _client.SupportsMarkdownUiConfig,
                SupportsDraftState: _client.SupportsMarkdownDraftState,
                SupportsPreviewState: _client.SupportsMarkdownPreviewState,
                SupportsToolbarState: _client.SupportsMarkdownToolbarState,
                SupportsMiniEditorState: _client.SupportsMarkdownMiniEditorState,
                HasUiConfig: _uiConfig != null,
                HasDraft: _draft != null,
                HasPreviewState: _preview != null,
                HasToolbarState: _toolbar != null,
                HasMiniEditorState: _miniEditor != null,
                LastUpdatedUtc: DateTime.UtcNow);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HybridShellMarkdownAdapterService));
            }
        }
    }

    public readonly record struct HybridShellMarkdownAdapterState(
        bool IsConnected,
        bool SupportsRender,
        bool SupportsFormatApply,
        bool SupportsUiConfig,
        bool SupportsDraftState,
        bool SupportsPreviewState,
        bool SupportsToolbarState,
        bool SupportsMiniEditorState,
        bool HasUiConfig,
        bool HasDraft,
        bool HasPreviewState,
        bool HasToolbarState,
        bool HasMiniEditorState,
        DateTime LastUpdatedUtc)
    {
        public static HybridShellMarkdownAdapterState Empty { get; } = new(
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            DateTime.MinValue);
    }
}
