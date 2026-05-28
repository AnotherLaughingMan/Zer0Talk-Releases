using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zer0Talk.Models;
using Zer0Talk.Services;

namespace Zer0Talk.Tests;

public sealed class HybridShellIpcClientServiceTests
{
    [Fact]
    public void TryParseCapabilities_ValidPayload_ReturnsCapabilities()
    {
        using var doc = JsonDocument.Parse("""
        {
          "protocolVersion": 1,
          "commands": ["ipc.capabilities.get", "contacts.list.get"],
          "events": ["contacts.list.changed", "contacts.list.delta.changed", "unread.count.changed"],
          "schemas": { "contactsSnapshot": 1, "contactsDelta": 1 }
        }
        """);

        var ok = HybridShellIpcClientService.TryParseCapabilities(doc.RootElement, out var capabilities);

        Assert.True(ok);
        Assert.Equal(1, capabilities.ProtocolVersion);
        Assert.Contains("ipc.capabilities.get", capabilities.Commands);
        Assert.Contains("contacts.list.delta.changed", capabilities.Events);
        Assert.Equal(1, capabilities.Schemas["contactsDelta"]);
    }

    [Fact]
    public async Task ConnectAsync_NegotiatesCapabilities()
    {
        using var fixture = new HybridIpcFixture();
        using var client = new HybridShellIpcClientService(fixture.PipeName);

        var connected = await client.ConnectAsync();

        Assert.True(connected);
        Assert.True(client.IsConnected);
        Assert.Contains(HybridIpcHostService.CommandGetCapabilities, client.Capabilities.Commands);
        Assert.True(client.SupportsContactsDelta);
        Assert.True(client.SupportsUnreadCountDelta);
        Assert.True(client.SupportsMarkdownRender);
        Assert.True(client.SupportsMarkdownFormatApply);
        Assert.True(client.SupportsMarkdownUiConfig);
    }

    [Fact]
    public async Task ConnectedClient_ReceivesUnreadCountDeltaEvent()
    {
        using var fixture = new HybridIpcFixture();
        using var client = new HybridShellIpcClientService(fixture.PipeName);
        Assert.True(await client.ConnectAsync());

        var received = new TaskCompletionSource<(string uid, int count)>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.UnreadCountDeltaReceived += (uid, count) => received.TrySetResult((uid, count));

        fixture.Hub.RaiseUnreadPeerCountChanged("peer-a", 5);

        using var timeout = new CancellationTokenSource(3000);
        var delta = await received.Task.WaitAsync(timeout.Token);

        Assert.Equal("peer-a", delta.uid);
        Assert.Equal(5, delta.count);
    }

    [Fact]
    public async Task RequestContactsSnapshotAsync_ReturnsTypedSnapshot()
    {
        using var fixture = new HybridIpcFixture();

        using var client = new HybridShellIpcClientService(fixture.PipeName);
        Assert.True(await client.ConnectAsync());

        var snapshot = await client.RequestContactsSnapshotAsync();

        Assert.NotNull(snapshot);
        Assert.True(snapshot!.SchemaVersion >= 1);
        Assert.NotNull(snapshot.Contacts);
    }

    [Fact]
    public async Task RequestMarkdownRenderAsync_ReturnsHtml()
    {
        using var fixture = new HybridIpcFixture();

        using var client = new HybridShellIpcClientService(fixture.PipeName);
        Assert.True(await client.ConnectAsync());

        var rendered = await client.RequestMarkdownRenderAsync("# Hello");

        Assert.NotNull(rendered);
        Assert.Contains("<h1", rendered!.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyMarkdownFormatAsync_ReturnsFormattedMarkdown()
    {
        using var fixture = new HybridIpcFixture();

        using var client = new HybridShellIpcClientService(fixture.PipeName);
        Assert.True(await client.ConnectAsync());

        var formatted = await client.ApplyMarkdownFormatAsync("hello", 0, 5, "bold");

        Assert.NotNull(formatted);
        Assert.Equal("**hello**", formatted!.Markdown);
        Assert.Equal("bold", formatted.Kind);
    }

    [Fact]
    public async Task RequestMarkdownUiConfigAsync_ReturnsToolbarAndMiniEditorSettings()
    {
        using var fixture = new HybridIpcFixture();

        using var client = new HybridShellIpcClientService(fixture.PipeName);
        Assert.True(await client.ConnectAsync());

        var config = await client.RequestMarkdownUiConfigAsync();

        Assert.NotNull(config);
        Assert.True(config!.PreviewButton.Enabled);
        Assert.True(config.Toolbar.AutoHideOnSelectionClear);
        Assert.True(config.Toolbar.PinWhileApplyingActions);
        Assert.True(config.MiniEditor.Enabled);
        Assert.False(config.MiniEditor.UsesSplitPreviewPane);
    }

    [Fact]
    public async Task MarkdownStateCommands_RoundTripThroughClient()
    {
        using var fixture = new HybridIpcFixture();

        using var client = new HybridShellIpcClientService(fixture.PipeName);
        Assert.True(await client.ConnectAsync());

        Assert.True(client.SupportsMarkdownDraftState);
        Assert.True(client.SupportsMarkdownPreviewState);
        Assert.True(client.SupportsMarkdownToolbarState);
        Assert.True(client.SupportsMarkdownMiniEditorState);

        var draftSet = await client.SetMarkdownDraftAsync("tauri draft", 1, 5);
        Assert.NotNull(draftSet);
        Assert.Equal("tauri draft", draftSet!.Markdown);

        var draftGet = await client.RequestMarkdownDraftAsync();
        Assert.NotNull(draftGet);
        Assert.Equal(1, draftGet!.SelectionStart);
        Assert.Equal(5, draftGet.SelectionEnd);

        var preview = await client.SetMarkdownPreviewStateAsync(false);
        Assert.NotNull(preview);
        Assert.False(preview!.Visible);

        var toolbar = await client.SetMarkdownToolbarStateAsync(true, true);
        Assert.NotNull(toolbar);
        Assert.True(toolbar!.Visible);
        Assert.True(toolbar.Pinned);

        var mini = await client.SetMarkdownMiniEditorStateAsync(true, true, "note");
        Assert.NotNull(mini);
        Assert.True(mini!.Open);
        Assert.Equal("note", mini.Content);
    }

    private sealed class HybridIpcFixture : IDisposable
    {
        public string PipeName { get; } = $"zer0talk-shell-client-tests-{Guid.NewGuid():N}";
        public ContactManager Contacts { get; } = new();
        public NotificationService Notifications { get; } = new();
        public EventHub Hub { get; } = new();

        private readonly ContactsBridgeService _contactsBridge;
        private readonly ContactsIpcEndpointService _contactsEndpoint;
        private readonly UnreadStateService _unreadState;
        private readonly UnreadBridgeService _unreadBridge;
        private readonly UnreadIpcEndpointService _unreadEndpoint;
        private readonly MarkdownIpcEndpointService _markdownEndpoint;
        private readonly HybridIpcHostService _host;

        public HybridIpcFixture()
        {
            _contactsBridge = new ContactsBridgeService(Contacts, Notifications);
            _contactsEndpoint = new ContactsIpcEndpointService(_contactsBridge);
            _unreadState = new UnreadStateService(Notifications);
            _unreadBridge = new UnreadBridgeService(_unreadState, Hub);
            _unreadEndpoint = new UnreadIpcEndpointService(_unreadBridge, Hub);
            _markdownEndpoint = new MarkdownIpcEndpointService();
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
