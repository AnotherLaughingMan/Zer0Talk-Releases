using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Zer0Talk.Models;
using Zer0Talk.Services;
using Xunit;

namespace Zer0Talk.Tests;

public sealed class HybridIpcHostServiceTests
{
    [Fact]
    public async Task Request_ContactsList_ReturnsResponsePayload()
    {
        var contacts = new ContactManager();
        var notifications = new NotificationService();
        using var contactsBridge = new ContactsBridgeService(contacts, notifications);
        using var contactsEndpoint = new ContactsIpcEndpointService(contactsBridge);

        using var unreadState = new UnreadStateService(notifications);
        var hub = new EventHub();
        using var unreadBridge = new UnreadBridgeService(unreadState, hub);
        using var unreadEndpoint = new UnreadIpcEndpointService(unreadBridge, hub);
        var markdownEndpoint = new MarkdownIpcEndpointService();

        var pipeName = $"zer0talk-hybrid-tests-{Guid.NewGuid():N}";
        using var host = new HybridIpcHostService(contactsEndpoint, unreadEndpoint, markdownEndpoint, pipeName);
        Assert.True(host.Start());

        Assert.True(contacts.AddContact(new Contact { UID = "usr-alice", DisplayName = "Alice" }, "test-pass"));

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(2000);

        using var reader = new StreamReader(client);
        using var writer = new StreamWriter(client) { AutoFlush = true };

        await writer.WriteLineAsync("{\"type\":\"request\",\"id\":\"r1\",\"command\":\"contacts.list.get\"}");

        var line = await ReadLineWithTimeoutAsync(reader, 3000);
        Assert.False(string.IsNullOrWhiteSpace(line));

        using var doc = JsonDocument.Parse(line!);
        var root = doc.RootElement;

        Assert.Equal("response", root.GetProperty("type").GetString());
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("r1", root.GetProperty("id").GetString());

        var payload = root.GetProperty("payload");
        Assert.Equal(1, payload.GetProperty("totalContacts").GetInt32());
    }

    [Fact]
    public async Task Request_UnknownCommand_ReturnsError()
    {
        var contacts = new ContactManager();
        var notifications = new NotificationService();
        using var contactsBridge = new ContactsBridgeService(contacts, notifications);
        using var contactsEndpoint = new ContactsIpcEndpointService(contactsBridge);

        using var unreadState = new UnreadStateService(notifications);
        var hub = new EventHub();
        using var unreadBridge = new UnreadBridgeService(unreadState, hub);
        using var unreadEndpoint = new UnreadIpcEndpointService(unreadBridge, hub);
        var markdownEndpoint = new MarkdownIpcEndpointService();

        var pipeName = $"zer0talk-hybrid-tests-{Guid.NewGuid():N}";
        using var host = new HybridIpcHostService(contactsEndpoint, unreadEndpoint, markdownEndpoint, pipeName);
        Assert.True(host.Start());

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(2000);

        using var reader = new StreamReader(client);
        using var writer = new StreamWriter(client) { AutoFlush = true };

        await writer.WriteLineAsync("{\"type\":\"request\",\"id\":\"r2\",\"command\":\"unknown.command\"}");

        var line = await ReadLineWithTimeoutAsync(reader, 3000);
        using var doc = JsonDocument.Parse(line!);
        var root = doc.RootElement;

        Assert.Equal("response", root.GetProperty("type").GetString());
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("unknown-command", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Request_GetCapabilities_ReturnsProtocolAndFeatureList()
    {
        var contacts = new ContactManager();
        var notifications = new NotificationService();
        using var contactsBridge = new ContactsBridgeService(contacts, notifications);
        using var contactsEndpoint = new ContactsIpcEndpointService(contactsBridge);

        using var unreadState = new UnreadStateService(notifications);
        var hub = new EventHub();
        using var unreadBridge = new UnreadBridgeService(unreadState, hub);
        using var unreadEndpoint = new UnreadIpcEndpointService(unreadBridge, hub);
        var markdownEndpoint = new MarkdownIpcEndpointService();

        var pipeName = $"zer0talk-hybrid-tests-{Guid.NewGuid():N}";
        using var host = new HybridIpcHostService(contactsEndpoint, unreadEndpoint, markdownEndpoint, pipeName);
        Assert.True(host.Start());

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(2000);

        using var reader = new StreamReader(client);
        using var writer = new StreamWriter(client) { AutoFlush = true };

        await writer.WriteLineAsync("{\"type\":\"request\",\"id\":\"caps-1\",\"command\":\"ipc.capabilities.get\"}");

        var line = await ReadLineWithTimeoutAsync(reader, 3000);
        Assert.False(string.IsNullOrWhiteSpace(line));

        using var doc = JsonDocument.Parse(line!);
        var root = doc.RootElement;

        Assert.Equal("response", root.GetProperty("type").GetString());
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(HybridIpcHostService.CommandGetCapabilities, root.GetProperty("command").GetString());

        var payload = root.GetProperty("payload");
        Assert.Equal(HybridIpcHostService.ProtocolVersion, payload.GetProperty("protocolVersion").GetInt32());
        Assert.True(payload.GetProperty("commands").GetArrayLength() >= 3);
        Assert.True(payload.GetProperty("events").GetArrayLength() >= 4);
        Assert.Contains(MarkdownIpcEndpointService.CommandRender, payload.GetProperty("commands").GetRawText());
        Assert.Contains(MarkdownIpcEndpointService.CommandFormatApply, payload.GetProperty("commands").GetRawText());
        Assert.Contains(MarkdownIpcEndpointService.CommandUiConfigGet, payload.GetProperty("commands").GetRawText());
    }

    [Fact]
    public async Task ContactsChanged_BroadcastsEventEnvelope()
    {
        var contacts = new ContactManager();
        var notifications = new NotificationService();
        using var contactsBridge = new ContactsBridgeService(contacts, notifications);
        using var contactsEndpoint = new ContactsIpcEndpointService(contactsBridge);

        using var unreadState = new UnreadStateService(notifications);
        var hub = new EventHub();
        using var unreadBridge = new UnreadBridgeService(unreadState, hub);
        using var unreadEndpoint = new UnreadIpcEndpointService(unreadBridge, hub);
        var markdownEndpoint = new MarkdownIpcEndpointService();

        var pipeName = $"zer0talk-hybrid-tests-{Guid.NewGuid():N}";
        using var host = new HybridIpcHostService(contactsEndpoint, unreadEndpoint, markdownEndpoint, pipeName);
        Assert.True(host.Start());

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(2000);

        using var reader = new StreamReader(client);

        Assert.True(contacts.AddContact(new Contact { UID = "usr-event-peer", DisplayName = "Event Peer" }, "test-pass"));

        var line = await ReadLineWithTimeoutAsync(reader, 3000);
        Assert.False(string.IsNullOrWhiteSpace(line));

        using var doc = JsonDocument.Parse(line!);
        var root = doc.RootElement;

        Assert.Equal("event", root.GetProperty("type").GetString());
        Assert.Equal(ContactsIpcEndpointService.EventContactsListChanged, root.GetProperty("event").GetString());
        Assert.True(root.TryGetProperty("payload", out var payload));
        Assert.Equal(1, payload.GetProperty("totalContacts").GetInt32());
    }

    [Fact]
    public async Task OversizedFrame_ReturnsFrameTooLargeError()
    {
        var contacts = new ContactManager();
        var notifications = new NotificationService();
        using var contactsBridge = new ContactsBridgeService(contacts, notifications);
        using var contactsEndpoint = new ContactsIpcEndpointService(contactsBridge);

        using var unreadState = new UnreadStateService(notifications);
        var hub = new EventHub();
        using var unreadBridge = new UnreadBridgeService(unreadState, hub);
        using var unreadEndpoint = new UnreadIpcEndpointService(unreadBridge, hub);
        var markdownEndpoint = new MarkdownIpcEndpointService();

        var pipeName = $"zer0talk-hybrid-tests-{Guid.NewGuid():N}";
        using var host = new HybridIpcHostService(contactsEndpoint, unreadEndpoint, markdownEndpoint, pipeName);
        Assert.True(host.Start());

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(2000);

        using var reader = new StreamReader(client);
        using var writer = new StreamWriter(client) { AutoFlush = true };

        var oversized = new string('a', 33000);
        await writer.WriteLineAsync(oversized);

        var line = await ReadLineWithTimeoutAsync(reader, 3000);
        Assert.False(string.IsNullOrWhiteSpace(line));

        using var doc = JsonDocument.Parse(line!);
        var root = doc.RootElement;

        Assert.Equal("response", root.GetProperty("type").GetString());
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("frame-too-large", root.GetProperty("error").GetString());
    }

    private static async Task<string?> ReadLineWithTimeoutAsync(StreamReader reader, int timeoutMs)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        return await reader.ReadLineAsync(cts.Token);
    }

    [Fact]
    public async Task Request_MarkdownRender_ReturnsHtmlPayload()
    {
        var contacts = new ContactManager();
        var notifications = new NotificationService();
        using var contactsBridge = new ContactsBridgeService(contacts, notifications);
        using var contactsEndpoint = new ContactsIpcEndpointService(contactsBridge);

        using var unreadState = new UnreadStateService(notifications);
        var hub = new EventHub();
        using var unreadBridge = new UnreadBridgeService(unreadState, hub);
        using var unreadEndpoint = new UnreadIpcEndpointService(unreadBridge, hub);
        var markdownEndpoint = new MarkdownIpcEndpointService();

        var pipeName = $"zer0talk-hybrid-tests-{Guid.NewGuid():N}";
        using var host = new HybridIpcHostService(contactsEndpoint, unreadEndpoint, markdownEndpoint, pipeName);
        Assert.True(host.Start());

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(2000);

        using var reader = new StreamReader(client);
        using var writer = new StreamWriter(client) { AutoFlush = true };

        await writer.WriteLineAsync("{\"type\":\"request\",\"id\":\"md-1\",\"command\":\"markdown.render.get\",\"payload\":{\"markdown\":\"# Hi\"}}");

        var line = await ReadLineWithTimeoutAsync(reader, 3000);
        Assert.False(string.IsNullOrWhiteSpace(line));

        using var doc = JsonDocument.Parse(line!);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());

        var payload = root.GetProperty("payload");
        Assert.Equal(MarkdownIpcEndpointService.RenderSchemaVersion, payload.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("# Hi", payload.GetProperty("markdown").GetString());
        Assert.Contains("<h1", payload.GetProperty("html").GetString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
