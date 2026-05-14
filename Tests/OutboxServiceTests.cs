using System;
using System.IO;
using Zer0Talk.Containers;
using Zer0Talk.Services;
using Zer0Talk.Utilities;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Zer0Talk.Tests;

public class OutboxServiceTests
{
    [Fact]
    public void Enqueue_PersistsQueuedChatMessage_WithStableIdentifiers()
    {
        var suffix = "tests-" + Guid.NewGuid().ToString("N");
        var originalSuffix = AppDataPaths.Root;
        AppDataPaths.SetProfileSuffix(suffix);

        try
        {
            var passphrase = "unit-test-passphrase";
            var peerUid = "usr-peer-chat-test";
            var message = new Zer0Talk.Models.Message
            {
                Id = Guid.Empty,
                SenderUID = "usr-sender",
                RecipientUID = peerUid,
                Content = "queued offline message",
                Timestamp = DateTime.UtcNow
            };

            var outbox = new OutboxService();
            var store = new OutboxContainer();

            outbox.Enqueue(peerUid, message, passphrase);

            var loaded = store.Load(peerUid, passphrase);
            Assert.Single(loaded);
            Assert.Equal("Chat", loaded[0].Operation);
            Assert.NotEqual(Guid.Empty, loaded[0].Id);
            Assert.Equal(loaded[0].Id, loaded[0].Message?.Id);
            Assert.Equal("queued offline message", loaded[0].Message?.Content);
        }
        finally
        {
            try
            {
                var root = AppDataPaths.Root;
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
            catch { }

            AppDataPaths.SetProfileSuffix(null);
        }
    }

    [Fact]
    public void EnqueueEdit_UpdateAndCancel_PreservesSingleQueuedEditLifecycle()
    {
        var suffix = "tests-" + Guid.NewGuid().ToString("N");
        var originalSuffix = AppDataPaths.Root;
        AppDataPaths.SetProfileSuffix(suffix);

        try
        {
            var passphrase = "unit-test-passphrase";
            var peerUid = "usr-peer-test";
            var messageId = Guid.NewGuid();

            var outbox = new OutboxService();
            var store = new OutboxContainer();

            outbox.EnqueueEdit(peerUid, messageId, "first content", passphrase);
            outbox.EnqueueEdit(peerUid, messageId, "second content", passphrase);

            var afterEnqueue = store.Load(peerUid, passphrase);
            Assert.Single(afterEnqueue);
            Assert.Equal("Edit", afterEnqueue[0].Operation);
            Assert.Equal(messageId, afterEnqueue[0].Id);
            Assert.Equal("second content", afterEnqueue[0].Message?.Content);

            outbox.UpdateQueued(peerUid, messageId, "updated content", passphrase);
            var afterUpdate = store.Load(peerUid, passphrase);
            Assert.Single(afterUpdate);
            Assert.Equal("updated content", afterUpdate[0].Message?.Content);

            outbox.CancelQueued(peerUid, messageId, passphrase);
            var afterCancel = store.Load(peerUid, passphrase);
            Assert.Empty(afterCancel);
        }
        finally
        {
            try
            {
                var root = AppDataPaths.Root;
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
            catch { }

            AppDataPaths.SetProfileSuffix(null);
        }
    }
}
