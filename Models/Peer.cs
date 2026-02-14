/*
    Peer node: network endpoint, capabilities, and status used by PeerManager.
*/
namespace Zer0Talk.Models
{
    public class Peer : System.ComponentModel.INotifyPropertyChanged
    {
        private string _uid = string.Empty;
        public required string UID
        {
            get => _uid;
            set
            {
                if (_uid != value)
                {
                    _uid = value;
                    OnPropertyChanged(nameof(UID));
                    OnPropertyChanged(nameof(DisplayUID));
                }
            }
        }

        private string _address = string.Empty;
        public required string Address
        {
            get => _address;
            set
            {
                if (_address != value)
                {
                    _address = value;
                    OnPropertyChanged(nameof(Address));
                }
            }
        }

        private int _port;
        public int Port
        {
            get => _port;
            set
            {
                if (_port != value)
                {
                    _port = value;
                    OnPropertyChanged(nameof(Port));
                }
            }
        }
        // Local-only trust flag (never transmitted). Persisted in peers.p2e.
        private bool _isTrusted;
        public bool IsTrusted
        {
            get => _isTrusted;
            set
            {
                if (_isTrusted != value)
                {
                    _isTrusted = value;
                    OnPropertyChanged(nameof(IsTrusted));
                }
            }
        }
        // Transient UI-only status (e.g., "Active"/"Discovered"). Not persisted.
        [System.Text.Json.Serialization.JsonIgnore]
        private string? _status;
        public string? Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                }
            }
        }
        // [MAJOR] Transient UI-only flag: indicates this peer is a configured/discovered Major Node.
        // Not persisted and never transmitted.
        [System.Text.Json.Serialization.JsonIgnore]
        private bool _isMajorNode;
        public bool IsMajorNode
        {
            get => _isMajorNode;
            set
            {
                if (_isMajorNode != value)
                {
                    _isMajorNode = value;
                    OnPropertyChanged(nameof(IsMajorNode));
                }
            }
        }
        // [PRIVACY/UI] Transient public key captured after a successful handshake; not persisted.
        // Used for UI-only display and identity verification without altering protocol or storage.
        [System.Text.Json.Serialization.JsonIgnore]
        private byte[]? _publicKey;
        public byte[]? PublicKey
        {
            get => _publicKey;
            set
            {
                if (!System.Linq.Enumerable.SequenceEqual(_publicKey ?? System.Array.Empty<byte>(), value ?? System.Array.Empty<byte>()))
                {
                    _publicKey = value;
                    OnPropertyChanged(nameof(PublicKey));
                    OnPropertyChanged(nameof(PublicKeyHex));
                    OnPropertyChanged(nameof(PublicKeyDisplay));
                }
            }
        }
        // [PRIVACY/UI] Derived hex for display; lower-case, no separators. Not persisted.
        [System.Text.Json.Serialization.JsonIgnore]
        public string? PublicKeyHex =>
            PublicKey != null && PublicKey.Length > 0
                ? System.Convert.ToHexStringLower(PublicKey)
                : (CachedPublicKey != null && CachedPublicKey.Length > 0
                    ? System.Convert.ToHexStringLower(CachedPublicKey)
                    : null);
        // [PRIVACY/UI] Compact display variant with ellipsis handled in XAML via TextTrimming.
        [System.Text.Json.Serialization.JsonIgnore]
        public string PublicKeyDisplay => string.IsNullOrWhiteSpace(PublicKeyHex) ? "Unknown until connected" : PublicKeyHex!;
        // [DISPLAY] Show UID with legacy prefix for UI while storing normalized core internally.
        [System.Text.Json.Serialization.JsonIgnore]
        public string DisplayUID => string.IsNullOrWhiteSpace(UID) ? string.Empty : (UID.StartsWith("usr-", System.StringComparison.Ordinal) ? UID : ("usr-" + UID));
        // [VERIFY] Result of UID<->PublicKey consistency check (UID recomputed from pub matches this UID). Transient.
        [System.Text.Json.Serialization.JsonIgnore]
        private bool _publicKeyVerified;
        public bool PublicKeyVerified
        {
            get => _publicKeyVerified;
            set
            {
                if (_publicKeyVerified != value)
                {
                    _publicKeyVerified = value;
                    OnPropertyChanged(nameof(PublicKeyVerified));
                }
            }
        }
        // [BLOCK] Transient UI-only flag indicating if this peer is blocked. Not persisted here (managed by PeerManager).
        [System.Text.Json.Serialization.JsonIgnore]
        private bool _isBlocked;
        public bool IsBlocked
        {
            get => _isBlocked;
            set
            {
                if (_isBlocked != value)
                {
                    _isBlocked = value;
                    OnPropertyChanged(nameof(IsBlocked));
                }
            }
        }
        // [GEO] Decorative country code hint for UI display (2-letter ISO code, e.g., "US", "GB"). Not accurate, just a general hint.
        [System.Text.Json.Serialization.JsonIgnore]
        private string? _countryCode;
        public string? CountryCode
        {
            get => _countryCode;
            set
            {
                if (_countryCode != value)
                {
                    _countryCode = value;
                    OnPropertyChanged(nameof(CountryCode));
                }
            }
        }
        // [GEO] Timestamp when country code was last determined (for cache expiry)
        [System.Text.Json.Serialization.JsonIgnore]
        private System.DateTime? _countryCodeCachedAt;
        public System.DateTime? CountryCodeCachedAt
        {
            get => _countryCodeCachedAt;
            set
            {
                if (_countryCodeCachedAt != value)
                {
                    _countryCodeCachedAt = value;
                    OnPropertyChanged(nameof(CountryCodeCachedAt));
                }
            }
        }
        // [CACHE] Timestamp when this peer was last seen online (for cache expiry logic)
        [System.Text.Json.Serialization.JsonIgnore]
        private System.DateTime? _lastSeenOnline;
        public System.DateTime? LastSeenOnline
        {
            get => _lastSeenOnline;
            set
            {
                if (_lastSeenOnline != value)
                {
                    _lastSeenOnline = value;
                    OnPropertyChanged(nameof(LastSeenOnline));
                }
            }
        }
        // [PUBKEY-CACHE] Cached public key for 30-day retention (security/performance)
        private byte[]? _cachedPublicKey;
        public byte[]? CachedPublicKey
        {
            get => _cachedPublicKey;
            set
            {
                if (!System.Linq.Enumerable.SequenceEqual(_cachedPublicKey ?? System.Array.Empty<byte>(), value ?? System.Array.Empty<byte>()))
                {
                    _cachedPublicKey = value;
                    OnPropertyChanged(nameof(CachedPublicKey));
                    OnPropertyChanged(nameof(PublicKeyHex));
                    OnPropertyChanged(nameof(PublicKeyDisplay));
                }
            }
        }
        // [PUBKEY-CACHE] Timestamp when public key was cached
        private System.DateTime? _publicKeyCachedAt;
        public System.DateTime? PublicKeyCachedAt
        {
            get => _publicKeyCachedAt;
            set
            {
                if (_publicKeyCachedAt != value)
                {
                    _publicKeyCachedAt = value;
                    OnPropertyChanged(nameof(PublicKeyCachedAt));
                }
            }
        }
        // [PUBKEY-CACHE] Flag indicating public key mismatch detected (potential imposter/corruption)
        [System.Text.Json.Serialization.JsonIgnore]
        private bool _publicKeyMismatch;
        public bool PublicKeyMismatch
        {
            get => _publicKeyMismatch;
            set
            {
                if (_publicKeyMismatch != value)
                {
                    _publicKeyMismatch = value;
                    OnPropertyChanged(nameof(PublicKeyMismatch));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        // Additional peer properties
    }
}
