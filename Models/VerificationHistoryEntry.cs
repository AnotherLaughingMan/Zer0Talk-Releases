using System;
using System.Text.Json.Serialization;

namespace Zer0Talk.Models
{
    // Persisted trust-ceremony entry for a contact.
    public sealed class VerificationHistoryEntry
    {
        public DateTime VerifiedAtUtc { get; set; }

        // Fingerprint is a display string derived from the observed key at verification time.
        public string Fingerprint { get; set; } = string.Empty;

        // Method examples: "Mutual Intent", "Peer Completion".
        public string Method { get; set; } = string.Empty;

        [JsonIgnore]
        public string VerifiedAtLocalDisplay =>
            VerifiedAtUtc == default
                ? "Unknown"
                : VerifiedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }
}
