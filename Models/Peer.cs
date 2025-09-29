/*
    Peer node: network endpoint, capabilities, and status used by PeerManager.
*/
namespace ZTalk.Models
{
    public class Peer
    {
        public required string UID { get; set; }
        public required string Address { get; set; }
        public int Port { get; set; }
        // Local-only trust flag (never transmitted). Persisted in peers.p2e.
        public bool IsTrusted { get; set; }
        // Transient UI-only status (e.g., "Active"/"Discovered"). Not persisted.
        [System.Text.Json.Serialization.JsonIgnore]
        public string? Status { get; set; }
        // [MAJOR] Transient UI-only flag: indicates this peer is a configured/discovered Major Node.
        // Not persisted and never transmitted.
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsMajorNode { get; set; }
        // [PRIVACY/UI] Transient public key captured after a successful handshake; not persisted.
        // Used for UI-only display and identity verification without altering protocol or storage.
        [System.Text.Json.Serialization.JsonIgnore]
        public byte[]? PublicKey { get; set; }
        // [PRIVACY/UI] Derived hex for display; lower-case, no separators. Not persisted.
        [System.Text.Json.Serialization.JsonIgnore]
        public string? PublicKeyHex => PublicKey == null ? null : System.Convert.ToHexStringLower(PublicKey);
        // [PRIVACY/UI] Compact display variant with ellipsis handled in XAML via TextTrimming.
        [System.Text.Json.Serialization.JsonIgnore]
        public string PublicKeyDisplay => string.IsNullOrWhiteSpace(PublicKeyHex) ? "Unknown until connected" : PublicKeyHex!;
        // [DISPLAY] Show UID with legacy prefix for UI while storing normalized core internally.
        [System.Text.Json.Serialization.JsonIgnore]
        public string DisplayUID => string.IsNullOrWhiteSpace(UID) ? string.Empty : (UID.StartsWith("usr-", System.StringComparison.Ordinal) ? UID : ("usr-" + UID));
        // [VERIFY] Result of UID<->PublicKey consistency check (UID recomputed from pub matches this UID). Transient.
        [System.Text.Json.Serialization.JsonIgnore]
        public bool PublicKeyVerified { get; set; }
        // Additional peer properties
    }
}
