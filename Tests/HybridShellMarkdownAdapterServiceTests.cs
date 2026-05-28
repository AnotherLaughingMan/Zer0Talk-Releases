using System;
using System.Threading.Tasks;
using Xunit;
using Zer0Talk.Models;
using Zer0Talk.Services;

namespace Zer0Talk.Tests;

public sealed class HybridShellMarkdownAdapterServiceTests
{
    [Fact]
    public async Task StartAsync_LoadsMarkdownStateSurfaces()
    {
        using var fixture = new HybridIpcFixture();
        using var client = new HybridShellIpcClientService(fixture.PipeName);
        using var adapter = new HybridShellMarkdownAdapterService(client);

        Assert.True(await adapter.StartAsync(markdownEnabled: true));

        var state = adapter.CurrentState;
        Assert.True(state.IsConnected);
        Assert.True(state.SupportsRender);
        Assert.True(state.SupportsFormatApply);
        Assert.True(state.SupportsUiConfig);
        Assert.True(state.SupportsDraftState);
        Assert.True(state.SupportsPreviewState);
        Assert.True(state.SupportsToolbarState);
        Assert.True(state.SupportsMiniEditorState);
        Assert.True(state.HasUiConfig);
    }

    [Fact]
    public async Task Setters_UpdateStateAndSnapshots()
    {
        using var fixture = new HybridIpcFixture();
        using var client = new HybridShellIpcClientService(fixture.PipeName);
        using var adapter = new HybridShellMarkdownAdapterService(client);

        Assert.True(await adapter.StartAsync(markdownEnabled: true));

        var draft = await adapter.SetDraftAsync("adapter draft", 1, 6);
        Assert.NotNull(draft);
        Assert.Equal("adapter draft", draft!.Markdown);

        var preview = await adapter.SetPreviewStateAsync(false);
        Assert.NotNull(preview);
        Assert.False(preview!.Visible);

        var toolbar = await adapter.SetToolbarStateAsync(true, true);
        Assert.NotNull(toolbar);
        Assert.True(toolbar!.Visible);
        Assert.True(toolbar.Pinned);

        var mini = await adapter.SetMiniEditorStateAsync(true, true, "mini content");
        Assert.NotNull(mini);
        Assert.True(mini!.Open);
        Assert.Equal("mini content", mini.Content);
    }

    [Fact]
    public async Task ApplyFormatAsync_UpdatesDraftState()
    {
        using var fixture = new HybridIpcFixture();
        using var client = new HybridShellIpcClientService(fixture.PipeName);
        using var adapter = new HybridShellMarkdownAdapterService(client);

        Assert.True(await adapter.StartAsync(markdownEnabled: true));

        var formatted = await adapter.ApplyFormatAsync("hello", 0, 5, "bold");
        Assert.NotNull(formatted);
        Assert.Equal("**hello**", formatted!.Markdown);

        var snapshot = adapter.Draft;
        Assert.NotNull(snapshot);
        Assert.Equal("**hello**", snapshot!.Markdown);
    }

    private sealed class HybridIpcFixture : IDisposable
    {
        public string PipeName { get; } = $"zer0talk-shell-markdown-adapter-tests-{Guid.NewGuid():N}";
        public ContactManager Contacts { get; } = new();
        public NotificationService Notifications { get; } = new();
        public EventHub Hub { get; } = new();

        private readonly ContactsBridgeService _contactsBridge;
        private readonly ContactsIpcEndpointService _contactsEndpoint;
        private readonly UnreadStateService _unreadState;
        private readonly UnreadBridgeService _unreadBridge;
        private readonly UnreadIpcEndpointService _unreadEndpoint;
        private readonly MarkdownComposerStateService _markdownState;
        private readonly MarkdownIpcEndpointService _markdownEndpoint;
        private readonly HybridIpcHostService _host;

        public HybridIpcFixture()
        {
            _contactsBridge = new ContactsBridgeService(Contacts, Notifications);
            _contactsEndpoint = new ContactsIpcEndpointService(_contactsBridge);
            _unreadState = new UnreadStateService(Notifications);
            _unreadBridge = new UnreadBridgeService(_unreadState, Hub);
            _unreadBridge.SnapshotChanged += dto => Hub.RaiseUnreadSnapshotDtoChanged(dto);
            _unreadEndpoint = new UnreadIpcEndpointService(_unreadBridge, Hub);
            _markdownState = new MarkdownComposerStateService();
            _markdownEndpoint = new MarkdownIpcEndpointService(_markdownState);
            _host = new HybridIpcHostService(_contactsEndpoint, _unreadEndpoint, _markdownEndpoint, PipeName);
            _host.Start();
        }

        public void Dispose()
        {
            _host.Dispose();
            _unreadEndpoint.Dispose();
            _unreadBridge.Dispose();
            _unreadState.Dispose();
            _contactsEndpoint.Dispose();
            _contactsBridge.Dispose();
        }
    }
}
