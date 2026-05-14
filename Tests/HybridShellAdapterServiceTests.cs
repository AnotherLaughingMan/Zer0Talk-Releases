using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zer0Talk.Models;
using Zer0Talk.Services;

namespace Zer0Talk.Tests;

public sealed class HybridShellAdapterServiceTests
{
    [Fact]
    public async Task StartAsync_LoadsAdapterStateAndLookup()
    {
        using var fixture = new HybridIpcFixture();

        using var client = new HybridShellIpcClientService(fixture.PipeName);
        using var consumer = new HybridShellConsumerService(client);
        using var adapter = new HybridShellAdapterService(consumer);

        Assert.True(await adapter.StartAsync(contactsEnabled: true, unreadEnabled: true));

        var state = adapter.CurrentState;
        Assert.True(state.IsConnected);
        Assert.True(state.UsesContactsDelta);
        Assert.True(state.UsesUnreadDelta);
        Assert.False(adapter.TryGetContact("missing", out _));
        Assert.Equal(0, adapter.GetUnreadCount("missing"));
    }

    [Fact]
    public async Task UnreadSnapshot_UpdatesAdapterState()
    {
        using var fixture = new HybridIpcFixture();
        using var client = new HybridShellIpcClientService(fixture.PipeName);
        using var consumer = new HybridShellConsumerService(client);
        using var adapter = new HybridShellAdapterService(consumer);

        Assert.True(await adapter.StartAsync(contactsEnabled: false, unreadEnabled: true));

        fixture.Hub.RaiseUnreadSnapshotChanged(new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["peer-z"] = 3
        });

        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (adapter.CurrentState.TotalUnread == 3)
            {
                break;
            }
            await Task.Delay(100);
        }

        var state = adapter.CurrentState;

        Assert.Equal(3, state.TotalUnread);
        Assert.Equal(3, adapter.GetUnreadCount("peer-z"));
    }

    [Fact]
    public async Task StopAsync_ResetsState()
    {
        using var fixture = new HybridIpcFixture();
        using var client = new HybridShellIpcClientService(fixture.PipeName);
        using var consumer = new HybridShellConsumerService(client);
        using var adapter = new HybridShellAdapterService(consumer);

        Assert.True(await adapter.StartAsync(contactsEnabled: true, unreadEnabled: true));
        await adapter.StopAsync();

        var state = adapter.CurrentState;
        Assert.False(state.IsConnected);
        Assert.Equal(0, state.ContactsCount);
        Assert.Equal(0, state.TotalUnread);
    }

    private sealed class HybridIpcFixture : IDisposable
    {
        public string PipeName { get; } = $"zer0talk-shell-adapter-tests-{Guid.NewGuid():N}";
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
            _unreadBridge.SnapshotChanged += dto => Hub.RaiseUnreadSnapshotDtoChanged(dto);
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
