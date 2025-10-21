using System;

namespace Zer0Talk.Models
{
    public readonly struct MessagePurgeSummary
    {
        public MessagePurgeSummary(int messageFileCount, int outboxFileCount, int messagesDeleted, int queuedMessagesDeleted, long bytesWiped)
        {
            MessageFileCount = messageFileCount;
            OutboxFileCount = outboxFileCount;
            MessagesDeleted = messagesDeleted;
            QueuedMessagesDeleted = queuedMessagesDeleted;
            BytesWiped = bytesWiped;
        }

        public int MessageFileCount { get; }
        public int OutboxFileCount { get; }
        public int MessagesDeleted { get; }
        public int QueuedMessagesDeleted { get; }
        public long BytesWiped { get; }

        public MessagePurgeSummary WithAdded(MessagePurgeSummary other)
        {
            return new MessagePurgeSummary(
                MessageFileCount + other.MessageFileCount,
                OutboxFileCount + other.OutboxFileCount,
                MessagesDeleted + other.MessagesDeleted,
                QueuedMessagesDeleted + other.QueuedMessagesDeleted,
                BytesWiped + other.BytesWiped);
        }

        public override string ToString()
        {
            return $"MessageFiles={MessageFileCount}, OutboxFiles={OutboxFileCount}, MessagesDeleted={MessagesDeleted}, QueuedDeleted={QueuedMessagesDeleted}, BytesWiped={BytesWiped}";
        }
    }
}
