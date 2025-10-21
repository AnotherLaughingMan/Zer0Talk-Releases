using System;

namespace Zer0Talk.Models
{
    public class DisplayNameRecord
    {
        public string Name { get; set; } = string.Empty;
        public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
