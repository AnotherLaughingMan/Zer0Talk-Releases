using System;

namespace ZTalk.Models
{
    public class QueuedMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public required Message Message { get; set; }
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public int AttemptCount { get; set; }
        public DateTime? LastAttemptUtc { get; set; }
        public string Operation { get; set; } = "Chat"; // Chat, Edit, Delete
    }
}
