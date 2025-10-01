/*
    Peer management: tracks connected peers, known nodes, and connection lifecycle.
    - Coordinates with NetworkService for send/receive and with ContactManager for promotions.
*/
using System;
using System.Collections.Generic;
using System.Linq;

using ZTalk.Models;
using Models = ZTalk.Models;
using ZTalk.Utilities;

namespace ZTalk.Services
{
    public class PeerManager
    {
        private readonly SettingsService _settings;
        public PeerManager(SettingsService settings)
        {
            _settings = settings;
        }

        public List<Peer> Peers { get; private set; } = new();
        public event Action? Changed;

        private static string TrimUidPrefix(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return string.Empty;
            return uid.StartsWith("usr-", StringComparison.Ordinal) && uid.Length > 4 ? uid.Substring(4) : uid;
        }

        private static string NormalizeUid(string? uid) => TrimUidPrefix((uid ?? string.Empty).Trim());

        public void SetDiscovered(IEnumerable<Peer> peers)
        {
            var blocked = new HashSet<string>(_settings.Settings.BlockList ?? new());
            // [DEDUP] Build a UID-keyed map to enforce uniqueness and merge metadata.
            // Using OrdinalIgnoreCase prevents duplicates differing only by UID casing.
            var map = new Dictionary<string, Peer>(StringComparer.OrdinalIgnoreCase);

            // [DEDUP] Seed with existing (filtered) to preserve any prior state like IsTrusted.
            foreach (var e in Peers)
            {
                var norm = NormalizeUid(e.UID);
                if (blocked.Contains(norm)) continue;
                e.UID = norm; // normalize in-place to prevent future divergence
                map[norm] = e;
            }

            // [DEDUP] Merge incoming peers (filtered). Latest discovery updates Address/Port/Status; IsTrusted is preserved.
            foreach (var p in peers)
            {
                var norm = NormalizeUid(p.UID);
                if (blocked.Contains(norm)) continue;
                p.UID = norm; // normalize incoming
                if (map.TryGetValue(norm, out var prev))
                {
                    // Preserve local-only trust; prefer most recent network info and non-null status.
                    p.IsTrusted = prev.IsTrusted || p.IsTrusted;
                    if (string.IsNullOrWhiteSpace(p.Address)) p.Address = prev.Address;
                    if (p.Port == 0) p.Port = prev.Port;
                    p.Status ??= prev.Status;
                    // [VERIFY] Preserve any known public key and verification result when merging (UI-only).
                    if (p.PublicKey == null && prev.PublicKey != null)
                    {
                        p.PublicKey = prev.PublicKey;
                        p.PublicKeyVerified = prev.PublicKeyVerified;
                    }
                    map[norm] = p;
                }
                else
                {
                    map[norm] = p;
                }
            }

            // [MAJOR] Identify configured Major Nodes by endpoint match (host:port string equality on Address/Port).
            // This is best-effort and local-only; no network calls or persistence.
            var known = _settings.Settings.KnownMajorNodes ?? new List<string>();
            foreach (var peer in map.Values)
            {
                peer.IsMajorNode = false;
                try
                {
                    foreach (var entry in known)
                    {
                        var parts = entry.Split(':');
                        if (parts.Length != 2) continue;
                        var host = parts[0].Trim();
                        if (!int.TryParse(parts[1], out var kport)) continue;
                        if (kport == peer.Port && string.Equals(peer.Address, host, StringComparison.OrdinalIgnoreCase))
                        {
                            peer.IsMajorNode = true; break;
                        }
                        // Best-effort IP match when Address is IP and KnownMajorNodes host resolves to same IP
                        if (kport == peer.Port && System.Net.IPAddress.TryParse(peer.Address, out var ip))
                        {
                            try
                            {
                                var addrs = System.Net.Dns.GetHostAddresses(host);
                                foreach (var a in addrs)
                                {
                                    if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && a.Equals(ip))
                                    { peer.IsMajorNode = true; break; }
                                }
                                if (peer.IsMajorNode) break;
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            Peers = map.Values.ToList();
            Changed?.Invoke();
            try { AppServices.PeersStore.Save(Peers, AppServices.Passphrase); } catch { }

            // Propagate any known presence from discovery into contacts so UI badges update
            try
            {
                foreach (var peer in Peers)
                {
                    var status = peer.Status;
                    if (string.IsNullOrWhiteSpace(status)) continue;
                    PresenceStatus s = status switch
                    {
                        "Online" => PresenceStatus.Online,
                        "Idle" => PresenceStatus.Idle,
                        "Do Not Disturb" => PresenceStatus.DoNotDisturb,
                        "Invisible" => PresenceStatus.Invisible,
                        "Offline" => PresenceStatus.Offline,
                        _ => PresenceStatus.Online
                    };
                    // Always update contact presence (ContactManager ignores no-ops); log only on change
                    PresenceStatus? prev = null;
                    try { prev = AppServices.Contacts.Contacts.FirstOrDefault(c => string.Equals(c.UID, peer.UID, StringComparison.OrdinalIgnoreCase))?.Presence; } catch { }
                    AppServices.Contacts.SetPresence(peer.UID, s, System.TimeSpan.FromSeconds(20), Models.PresenceSource.Discovery);
                    if (prev != s)
                    {
                        try
                        {
                            if (Utilities.LoggingPaths.Enabled)
                            {
                                var line = $"{DateTime.Now:O} [Presence] {peer.UID} -> {s} src=Discovery";
                                System.IO.File.AppendAllText(Utilities.LoggingPaths.UI, line + Environment.NewLine);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        // [VERIFY] Update a peer's observed public key and compute basic identity consistency (UID == UID(pub)).
        // Local-only, transient; does not change persistence or discovery protocol.
        public void SetObservedPublicKey(string uid, byte[] publicKey)
        {
            uid = NormalizeUid(uid);
            if (string.IsNullOrWhiteSpace(uid) || publicKey == null || publicKey.Length == 0) return;
            var p = Peers.FirstOrDefault(x => string.Equals(x.UID, uid, StringComparison.OrdinalIgnoreCase));
            if (p == null)
            {
                p = new Peer { UID = uid, Address = uid, Port = 0, IsTrusted = false };
                Peers.Add(p);
            }
            
            // [CACHE] Check cached public key for mismatch detection (30-day cache)
            p.PublicKeyMismatch = false; // Reset flag by default
            if (p.CachedPublicKey != null && p.CachedPublicKey.Length > 0 && p.PublicKeyCachedAt.HasValue)
            {
                var cacheAge = System.DateTime.UtcNow - p.PublicKeyCachedAt.Value;
                if (cacheAge.TotalDays < 30) // Cache valid for 30 days
                {
                    // Compare incoming key with cached key
                    if (!publicKey.SequenceEqual(p.CachedPublicKey))
                    {
                        p.PublicKeyMismatch = true;
                        SafeLogNetError($"[SECURITY] Public key mismatch detected for {uid}! Possible imposter or key rotation.");
                        SafeLogNetError($"  Cached key: {Convert.ToHexStringLower(p.CachedPublicKey)}");
                        SafeLogNetError($"  New key: {Convert.ToHexStringLower(publicKey)}");
                    }
                }
            }
            
            // Update current public key and refresh cache
            p.PublicKey = publicKey;
            p.CachedPublicKey = (byte[])publicKey.Clone();
            p.PublicKeyCachedAt = System.DateTime.UtcNow;
            
            try
            {
                var claimed = IdentityService.ComputeUidFromPublicKey(publicKey);
                p.PublicKeyVerified = string.Equals(NormalizeUid(claimed), uid, StringComparison.Ordinal);
            }
            catch { p.PublicKeyVerified = false; }

            // [VERIFY] If a contact exists, propagate verification to contact-level flags so UI stays consistent and persist last-known key.
            try
            {
                var contact = AppServices.Contacts.Contacts.FirstOrDefault(c => string.Equals(c.UID, uid, StringComparison.OrdinalIgnoreCase));
                if (contact != null)
                {
                    var observedHex = Convert.ToHexStringLower(publicKey);
                    var expected = NormalizeHex(contact.ExpectedPublicKeyHex);
                    bool match;
                    if (!string.IsNullOrWhiteSpace(expected))
                    {
                        match = string.Equals(observedHex, expected, StringComparison.Ordinal);
                        if (!match)
                        {
                            SafeLogNetError($"Public key mismatch for {uid}: expected {expected}, observed {observedHex}");
                        }
                    }
                    else
                    {
                        // Fall back to UID-derived verification when no expected key is provided.
                        match = p.PublicKeyVerified;
                    }

                    // Unify peer and contact state, and notify UI via ContactManager.
                    p.PublicKeyVerified = match;
                    contact.PublicKeyVerified = match; // transient UI flag
                    AppServices.Contacts.SetPublicKeyVerified(uid, match); // raise Changed for contacts
                    // Persist last-known observed key for offline display
                    try { AppServices.Contacts.SetLastKnownPublicKeyHex(uid, observedHex, AppServices.Passphrase); } catch { }
                    // Persist last-known encrypted flag as true on successful handshake; NetworkService will set false on close via future hook
                    try { if (match) AppServices.Contacts.SetLastKnownEncrypted(uid, true, AppServices.Passphrase); } catch { }
                }
            }
            catch { }
            
            // Persist peer data including cached public key
            try { AppServices.PeersStore.Save(Peers, AppServices.Passphrase); } catch { }
            Changed?.Invoke();
        }

        // [VERIFY] Public setter to unify verification updates and raise change notification.
        public void SetPeerVerification(string uid, bool verified)
        {
            uid = NormalizeUid(uid);
            var p = Peers.FirstOrDefault(x => string.Equals(x.UID, uid, StringComparison.OrdinalIgnoreCase));
            if (p == null) return;
            p.PublicKeyVerified = verified;
            Changed?.Invoke();
        }

        public void ClearTransientStatuses()
        {
            try
            {
                var mutated = false;
                foreach (var peer in Peers)
                {
                    if (peer == null) continue;
                    if (!string.IsNullOrWhiteSpace(peer.Status))
                    {
                        peer.Status = null;
                        mutated = true;
                    }
                }
                if (mutated)
                {
                    Changed?.Invoke();
                }
            }
            catch { }
        }

        // Update transient presence/status for a peer and notify listeners.
        public void SetPeerStatus(string uid, string status)
        {
            uid = NormalizeUid(uid);
            if (string.IsNullOrWhiteSpace(uid)) return;
            var p = Peers.FirstOrDefault(x => string.Equals(x.UID, uid, StringComparison.OrdinalIgnoreCase));
            if (p == null)
            {
                p = new Peer { UID = uid, Address = uid, Port = 0 };
                Peers.Add(p);
            }
            var prev = p.Status ?? string.Empty;
            p.Status = status;
            try
            {
                var s = status switch
                {
                    "Online" => PresenceStatus.Online,
                    "Idle" => PresenceStatus.Idle,
                    "Do Not Disturb" => PresenceStatus.DoNotDisturb,
                    "Invisible" => PresenceStatus.Invisible,
                    "Offline" => PresenceStatus.Offline,
                    _ => PresenceStatus.Online
                };
                AppServices.Contacts.SetPresence(uid, s, System.TimeSpan.FromSeconds(60), Models.PresenceSource.Verified);
                try
                {
                    if (Utilities.LoggingPaths.Enabled)
                    {
                        var line = $"{DateTime.Now:O} [Presence] {uid} {prev}->{status} src=PeerStatus";
                        System.IO.File.AppendAllText(Utilities.LoggingPaths.UI, line + Environment.NewLine);
                    }
                }
                catch { }
            }
            catch { }
            Changed?.Invoke();
        }

        private static string? NormalizeHex(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            var s = hex.Trim().ToLowerInvariant();
            return s.Replace("-", string.Empty).Replace(":", string.Empty).Replace(" ", string.Empty);
        }

        // [VERIFY] Scoped logging for key mismatches only (network log)
        private static void SafeLogNetError(string line)
        {
            try
            {
                if (!Utilities.LoggingPaths.Enabled) return;
                var path = Utilities.LoggingPaths.Network;
                System.IO.File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] NETERR {line}\n");
            }
            catch { }
        }

        // Ensure contacts appear as peers with UID as primary address
        public void IncludeContacts()
        {
            var blocked = new HashSet<string>(_settings.Settings.BlockList ?? new());
            var contacts = AppServices.Contacts.Contacts;
            var contactPeers = contacts
                .Where(c => !blocked.Contains(c.UID))
                .Select(c => new Peer { UID = NormalizeUid(c.UID), Address = NormalizeUid(c.UID), Port = 0, IsTrusted = c.IsTrusted })
                .ToList();
            // Merge without duplicates
            var map = new Dictionary<string, Peer>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Peers)
            {
                var norm = NormalizeUid(p.UID);
                p.UID = norm;
                map[norm] = p;
            }
            foreach (var cp in contactPeers)
            {
                var norm = NormalizeUid(cp.UID);
                cp.UID = norm;
                if (map.TryGetValue(norm, out var prev))
                {
                    // Preserve previously learned network + verification state; augment trust from contacts
                    prev.IsTrusted = prev.IsTrusted || cp.IsTrusted;
                    // Keep prev (with PublicKey, PublicKeyVerified, Status, Address/Port) instead of replacing with a bare cp
                    map[norm] = prev;
                }
                else
                {
                    map[norm] = cp;
                }
            }
            Peers = map.Values.ToList();
            Changed?.Invoke();
            try { AppServices.PeersStore.Save(Peers, AppServices.Passphrase); } catch { }
        }

        // Local-only trust control
        public void SetTrusted(string uid, bool trusted)
        {
            uid = NormalizeUid(uid);
            if (string.IsNullOrWhiteSpace(uid)) return;
            var p = Peers.FirstOrDefault(x => string.Equals(x.UID, uid, StringComparison.OrdinalIgnoreCase));
            if (p == null)
            {
                // Create a local-only entry to track trust; address defaults to UID
                p = new Peer { UID = uid, Address = uid, Port = 0, IsTrusted = false };
                Peers.Add(p);
            }
            if (p.IsTrusted == trusted) return;
            p.IsTrusted = trusted;
            Changed?.Invoke();
            try { AppServices.PeersStore.Save(Peers, AppServices.Passphrase); } catch { }
            try { AppServices.Contacts.SetTrusted(uid, trusted, AppServices.Passphrase); } catch { }
        }

        public void Block(string uid)
        {
            uid = NormalizeUid(uid);
            if (string.IsNullOrWhiteSpace(uid)) return;
            var list = _settings.Settings.BlockList ??= new();
            if (!list.Contains(uid))
            {
                list.Add(uid);
                
                // [TIER-1-BLOCKING] Also block public key fingerprint and optionally IP
                var peer = Peers.FirstOrDefault(p => string.Equals(NormalizeUid(p.UID), uid, StringComparison.OrdinalIgnoreCase));
                if (peer != null)
                {
                    // Block public key fingerprint if available
                    if (peer.PublicKey != null && peer.PublicKey.Length > 0)
                    {
                        try
                        {
                            using var sha = System.Security.Cryptography.SHA256.Create();
                            var hash = sha.ComputeHash(peer.PublicKey);
                            var fingerprint = Convert.ToBase64String(hash);
                            
                            _settings.Settings.BlockedPublicKeyFingerprints ??= new();
                            if (!_settings.Settings.BlockedPublicKeyFingerprints.Contains(fingerprint))
                            {
                                _settings.Settings.BlockedPublicKeyFingerprints.Add(fingerprint);
                                Utilities.Logger.Log($"[BLOCK-PUBKEY] Added public key fingerprint to blocklist for {uid}");
                            }
                        }
                        catch { }
                    }
                    
                    // Optionally block IP address if it's not a local network address
                    if (!string.IsNullOrWhiteSpace(peer.Address) && 
                        System.Net.IPAddress.TryParse(peer.Address, out var ip))
                    {
                        var bytes = ip.GetAddressBytes();
                        bool isLocal = bytes.Length == 4 && 
                                      (bytes[0] == 10 || 
                                       bytes[0] == 127 ||
                                       (bytes[0] == 192 && bytes[1] == 168) ||
                                       (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31));
                        
                        if (!isLocal)
                        {
                            _settings.Settings.BlockedIpAddresses ??= new();
                            if (!_settings.Settings.BlockedIpAddresses.Contains(peer.Address))
                            {
                                _settings.Settings.BlockedIpAddresses.Add(peer.Address);
                                Utilities.Logger.Log($"[BLOCK-IP] Added IP address to blocklist: {peer.Address}");
                            }
                        }
                    }
                }
                
                _settings.Save(AppServices.Passphrase);
                // Terminate any active session with this peer
                try { AppServices.Network.DisconnectPeer(uid); } catch { }
                // Don't remove peer from list - just mark as blocked via Changed event and RefreshLists
                Changed?.Invoke();
            }
        }

        public void Unblock(string uid)
        {
            uid = NormalizeUid(uid);
            var list = _settings.Settings.BlockList ??= new();
            if (list.Remove(uid))
            {
                // [TIER-1-BLOCKING] Also remove from enhanced blocklists
                var peer = Peers.FirstOrDefault(p => string.Equals(NormalizeUid(p.UID), uid, StringComparison.OrdinalIgnoreCase));
                if (peer != null)
                {
                    // Remove public key fingerprint
                    if (peer.PublicKey != null && peer.PublicKey.Length > 0)
                    {
                        try
                        {
                            using var sha = System.Security.Cryptography.SHA256.Create();
                            var hash = sha.ComputeHash(peer.PublicKey);
                            var fingerprint = Convert.ToBase64String(hash);
                            _settings.Settings.BlockedPublicKeyFingerprints?.Remove(fingerprint);
                            Utilities.Logger.Log($"[UNBLOCK-PUBKEY] Removed public key fingerprint from blocklist for {uid}");
                        }
                        catch { }
                    }
                    
                    // Remove IP address
                    if (!string.IsNullOrWhiteSpace(peer.Address))
                    {
                        if (_settings.Settings.BlockedIpAddresses?.Remove(peer.Address) == true)
                        {
                            Utilities.Logger.Log($"[UNBLOCK-IP] Removed IP address from blocklist: {peer.Address}");
                        }
                    }
                }
                
                _settings.Save(AppServices.Passphrase);
                Changed?.Invoke();
            }
        }

        public void ClearAllBlocks()
        {
            _settings.Settings.BlockList?.Clear();
            _settings.Save(AppServices.Passphrase);
            Changed?.Invoke();
        }
    }
}
