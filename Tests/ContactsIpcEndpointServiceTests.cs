using System;
using System.Text.Json;
using Xunit;
using Zer0Talk.Models;
using Zer0Talk.Services;

namespace Zer0Talk.Tests;

public class ContactsIpcEndpointServiceTests
{
    [Fact]
    public void Constructor_NullBridge_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ContactsIpcEndpointService(null!));
    }

    [Fact]
    public void TryHandleRequest_GetContactsList_ReturnsSnapshotJson()
    {
        var contacts = new ContactManager();
        var notifications = new NotificationService();
        using var bridge = new ContactsBridgeService(contacts, notifications);
        using var endpoint = new ContactsIpcEndpointService(bridge);

        Assert.True(contacts.AddContact(new Contact { UID = "dave", DisplayName = "Dave" }, "test-pass"));

        var handled = endpoint.TryHandleRequest(ContactsIpcEndpointService.CommandGetContactsList, out var json);

        Assert.True(handled);
        Assert.False(string.IsNullOrWhiteSpace(json));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("totalContacts").GetInt32());
    }

    [Fact]
    public void TryHandleRequest_UnknownCommand_ReturnsFalse()
    {
        var contacts = new ContactManager();
        var notifications = new NotificationService();
        using var bridge = new ContactsBridgeService(contacts, notifications);
        using var endpoint = new ContactsIpcEndpointService(bridge);

        var handled = endpoint.TryHandleRequest("contacts.unknown", out var json);

        Assert.False(handled);
        Assert.Equal(string.Empty, json);
    }

    [Fact]
    public void ContactsChanged_RaisesNotificationJson()
    {
        var contacts = new ContactManager();
        var notifications = new NotificationService();
        using var bridge = new ContactsBridgeService(contacts, notifications);
        using var endpoint = new ContactsIpcEndpointService(bridge);

        var sawSnapshotEvent = false;
        string? receivedPayload = null;
        endpoint.NotificationJsonReady += (eventName, payloadJson) =>
        {
            if (eventName == ContactsIpcEndpointService.EventContactsListChanged)
            {
                sawSnapshotEvent = true;
                receivedPayload = payloadJson;
            }
        };

        Assert.True(contacts.AddContact(new Contact { UID = "erin", DisplayName = "Erin" }, "test-pass"));

        Assert.True(sawSnapshotEvent);
        Assert.False(string.IsNullOrWhiteSpace(receivedPayload));
    }

    [Fact]
    public void ContactsChanged_RaisesDeltaNotificationJson()
    {
        var contacts = new ContactManager();
        var notifications = new NotificationService();
        using var bridge = new ContactsBridgeService(contacts, notifications);
        using var endpoint = new ContactsIpcEndpointService(bridge);

        string? receivedDeltaPayload = null;
        endpoint.NotificationJsonReady += (eventName, payloadJson) =>
        {
            if (eventName == ContactsIpcEndpointService.EventContactsListDeltaChanged)
            {
                receivedDeltaPayload = payloadJson;
            }
        };

        Assert.True(contacts.AddContact(new Contact { UID = "usr-delta-a", DisplayName = "Delta A" }, "test-pass"));

        Assert.False(string.IsNullOrWhiteSpace(receivedDeltaPayload));
        using var doc = JsonDocument.Parse(receivedDeltaPayload!);
        var root = doc.RootElement;
        Assert.Equal(ContactsIpcEndpointService.DeltaSchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, root.GetProperty("addedCount").GetInt32());
        Assert.Equal(0, root.GetProperty("updatedCount").GetInt32());
        Assert.Equal(0, root.GetProperty("removedCount").GetInt32());
    }
}