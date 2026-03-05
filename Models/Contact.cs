/*
    Contact entry: identity, display info, and trust flags.
*/
using System.Text.Json.Serialization;

namespace Zer0Talk.Models
{
    public class Contact : System.IEquatable<Contact>, System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        // Safe defaults to avoid nulls during (de)serialization or UI binding
        private string _uid = string.Empty;
        public string UID { get => _uid; set { if (_uid != value) { _uid = value; OnPropertyChanged(nameof(UID)); OnPropertyChanged(nameof(DisplayUID)); } } }
        private string _displayName = string.Empty;
        public string DisplayName { get => _displayName; set { if (_displayName != value) { _displayName = value; OnPropertyChanged(nameof(DisplayName)); } } }
        // Optional short biography text for this contact (<=280 chars)
        private string? _bio;
        public string? Bio { get => _bio; set { if (_bio != value) { _bio = value; OnPropertyChanged(nameof(Bio)); } } }
        // Count of how many times this contact changed their display name (if known)
        private int _displayNameChangeCount;
        public int DisplayNameChangeCount { get => _displayNameChangeCount; set { if (_displayNameChangeCount != value) { _displayNameChangeCount = value; OnPropertyChanged(nameof(DisplayNameChangeCount)); } } }
        // Full history of display name changes with timestamps
        private System.Collections.Generic.List<DisplayNameRecord>? _displayNameHistory;
        public System.Collections.Generic.List<DisplayNameRecord>? DisplayNameHistory { get => _displayNameHistory; set { if (_displayNameHistory != value) { _displayNameHistory = value; OnPropertyChanged(nameof(DisplayNameHistory)); } } }
        [JsonIgnore]
        public string DisplayUID => TrimPrefix(UID);
        // Local-only trust flag for UI hints; contacts store may ignore it, but we keep it for convenience.
        private bool _isTrusted;
        public bool IsTrusted { get => _isTrusted; set { if (_isTrusted != value) { _isTrusted = value; OnPropertyChanged(nameof(IsTrusted)); } } }
        // Persisted verification toggle for simulated/debug testing and manual assignment.
        // Real-time network verification remains in PublicKeyVerified (transient).
        private bool _isVerified;
        public bool IsVerified { get => _isVerified; set { if (_isVerified != value) { _isVerified = value; OnPropertyChanged(nameof(IsVerified)); } } }
        // [VERIFY] Optional expected public key (hex, lowercase no separators) captured during contact addition.
        // Persisted in contacts.p2e to allow matching when the peer connects.
        private string? _expectedPublicKeyHex;
        public string? ExpectedPublicKeyHex { get => _expectedPublicKeyHex; set { if (_expectedPublicKeyHex != value) { _expectedPublicKeyHex = value; OnPropertyChanged(nameof(ExpectedPublicKeyHex)); } } }
        // [TEST] Simulated contact (added without peer verification). Persisted for UI tagging and logic.
        private bool _isSimulated;
        public bool IsSimulated { get => _isSimulated; set { if (_isSimulated != value) { _isSimulated = value; OnPropertyChanged(nameof(IsSimulated)); } } }
        // [VERIFY/UI] Transient verification result from last observed connection (not persisted).
        [JsonIgnore]
        private bool _publicKeyVerified;
        public bool PublicKeyVerified { get => _publicKeyVerified; set { if (_publicKeyVerified != value) { _publicKeyVerified = value; OnPropertyChanged(nameof(PublicKeyVerified)); } } }
        // Persisted: last-known observed public key (hex, lowercase, no separators) for offline display
        private string? _lastKnownPublicKeyHex;
        public string? LastKnownPublicKeyHex { get => _lastKnownPublicKeyHex; set { if (_lastKnownPublicKeyHex != value) { _lastKnownPublicKeyHex = value; OnPropertyChanged(nameof(LastKnownPublicKeyHex)); } } }
        // Persisted: last-known encrypted session state (for offline/placeholder UI only; live status comes from NetworkService)
        private bool _lastKnownEncrypted;
        public bool LastKnownEncrypted { get => _lastKnownEncrypted; set { if (_lastKnownEncrypted != value) { _lastKnownEncrypted = value; OnPropertyChanged(nameof(LastKnownEncrypted)); } } }

        // Persisted: when this contact was last identity-verified by trust ceremony.
        private System.DateTime? _lastVerifiedUtc;
        public System.DateTime? LastVerifiedUtc { get => _lastVerifiedUtc; set { if (_lastVerifiedUtc != value) { _lastVerifiedUtc = value; OnPropertyChanged(nameof(LastVerifiedUtc)); } } }

        // Persisted: recent trust ceremony events for auditability in profile UI.
        private System.Collections.Generic.List<VerificationHistoryEntry>? _verificationHistory;
        public System.Collections.Generic.List<VerificationHistoryEntry>? VerificationHistory { get => _verificationHistory; set { if (_verificationHistory != value) { _verificationHistory = value; OnPropertyChanged(nameof(VerificationHistory)); } } }

        // Local notification policy flags.
        private bool _muteNotifications;
        public bool MuteNotifications { get => _muteNotifications; set { if (_muteNotifications != value) { _muteNotifications = value; OnPropertyChanged(nameof(MuteNotifications)); } } }

        private bool _priorityNotifications;
        public bool PriorityNotifications { get => _priorityNotifications; set { if (_priorityNotifications != value) { _priorityNotifications = value; OnPropertyChanged(nameof(PriorityNotifications)); } } }
    // Transient presence indicator used by UI badges
    // Default to Offline until a presence is observed or a direct session is active
    [JsonIgnore]
    private PresenceStatus _presence = PresenceStatus.Offline;
    public PresenceStatus Presence { get => _presence; set { if (_presence != value) { _presence = value; OnPropertyChanged(nameof(Presence)); } } }
    // Transient presence bookkeeping (not persisted)
    [JsonIgnore]
    private System.DateTime? _lastPresenceUtc;
    public System.DateTime? LastPresenceUtc { get => _lastPresenceUtc; set { if (_lastPresenceUtc != value) { _lastPresenceUtc = value; OnPropertyChanged(nameof(LastPresenceUtc)); } } }
    [JsonIgnore]
    private System.DateTime? _presenceExpiresUtc;
    public System.DateTime? PresenceExpiresUtc { get => _presenceExpiresUtc; set { if (_presenceExpiresUtc != value) { _presenceExpiresUtc = value; OnPropertyChanged(nameof(PresenceExpiresUtc)); } } }
    [JsonIgnore]
    private PresenceSource _presenceSource = PresenceSource.Unknown;
    public PresenceSource PresenceSource { get => _presenceSource; set { if (_presenceSource != value) { _presenceSource = value; OnPropertyChanged(nameof(PresenceSource)); } } }

        public Contact() { }

        public override string ToString() => string.IsNullOrWhiteSpace(DisplayName) ? DisplayUID : DisplayName;

        public bool Equals(Contact? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(TrimPrefix(UID), TrimPrefix(other.UID), System.StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object? obj) => obj is Contact c && Equals(c);
        public override int GetHashCode() => TrimPrefix(UID).ToLowerInvariant().GetHashCode();

        private static string TrimPrefix(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return string.Empty;
            return uid.StartsWith("usr-", System.StringComparison.Ordinal) && uid.Length > 4 ? uid.Substring(4) : uid;
        }
        // Additional contact properties
    }
}
