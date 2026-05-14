using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zer0Talk.Models;
using Zer0Talk.Services;

namespace Zer0Talk.Tests;

public sealed class HybridShellConsumerServiceTests
{
    [Fact]
    public async Task StartAsync_LoadsInitialSnapshots_AndEnablesDeltaModes()
    {
        using var fixture = new HybridIpcFixture();

        using var client = new HybridShellIpcClientService(fixture.PipeName);
        using var consumer = new HybridShellConsumerService(client);

        var started = await consumer.StartAsync(contactsEnabled: true, unreadEnabled: true);

        Assert.True(started);
        Assert.True(consumer.IsRunning);
        Assert.True(consumer.UsingContactsDelta);
        Assert.True(consumer.UsingUnreadDelta);
        Assert.True(consumer.ContactsSnapshot.SchemaVersion >= 1);
        Assert.True(consumer.UnreadSnapshot.SchemaVersion >= 1);
    }

    [Fact]
    public async Task UnreadSnapshotEvent_UpdatesUnreadSnapshot()
    {
        using var fixture = new HybridIpcFixture();
        using var client = new HybridShellIpcClientService(fixture.PipeName);
        using var consumer = new HybridShellConsumerService(client);

        Assert.True(await consumer.StartAsync(contactsEnabled: false, unreadEnabled: true));

        fixture.Hub.RaiseUnreadSnapshotChanged(new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["peer-x"] = 4
        });

        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (consumer.UnreadSnapshot.TotalUnread == 4)
            {
                break;
            }
            await Task.Delay(100);
        }

        var snapshot = consumer.UnreadSnapshot;

        Assert.Equal(4, snapshot.TotalUnread);
        Assert.Single(snapshot.Peers);
        Assert.Equal("peer-x", snapshot.Peers[0].PeerUid);
    }

    [Fact]
    public async Task StartAsync_WhenAllSurfacesDisabled_StopsConsumer()
    {
        using var fixture = new HybridIpcFixture();
        using var client = new HybridShellIpcClientService(fixture.PipeName);
        using var consumer = new HybridShellConsumerService(client);

        Assert.False(await consumer.StartAsync(contactsEnabled: false, unreadEnabled: false));
        Assert.False(consumer.IsRunning);
    }

    private sealed class HybridIpcFixture : IDisposable
    {
        public string PipeName { get; } = $"zer0talk-shell-consumer-tests-{Guid.NewGuid():N}";
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
