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
        private readonly HybridIpcHostService _host;

        public HybridIpcFixture()
        {
            _contactsBridge = new ContactsBridgeService(Contacts, Notifications);
            _contactsEndpoint = new ContactsIpcEndpointService(_contactsBridge);
            _unreadState = new UnreadStateService(Notifications);
            _unreadBridge = new UnreadBridgeService(_unreadState, Hub);
            _unreadEndpoint = new UnreadIpcEndpointService(_unreadBridge, Hub);
            _host = new HybridIpcHostService(_contactsEndpoint, _unreadEndpoint, PipeName);
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
