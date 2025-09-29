using System;

namespace ZTalk.Models
{
    public class DisplayNameRecord
    {
        public string Name { get; set; } = string.Empty;
        public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
