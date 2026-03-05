using System;
using System.Collections.Generic;
using System.Linq;
using Zer0Talk.Models;
using Models = Zer0Talk.Models;
using Zer0Talk.Utilities;

namespace Zer0Talk.Services
{
    public partial class PeerManager
    {
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
                            var hash = System.Security.Cryptography.SHA256.HashData(peer.PublicKey);
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
            var removedAny = false;

            // Remove all UID variants (legacy prefixed, mixed case, etc.).
            for (var i = list.Count - 1; i >= 0; i--)
            {
                var existing = list[i];
                if (string.Equals(NormalizeUid(existing), uid, StringComparison.OrdinalIgnoreCase))
                {
                    list.RemoveAt(i);
                    removedAny = true;
                }
            }

            if (removedAny)
            {
                // [TIER-1-BLOCKING] Also remove from enhanced blocklists
                var relatedPeers = Peers
                    .Where(p => string.Equals(NormalizeUid(p.UID), uid, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var peer in relatedPeers)
                {
                    // Remove public key fingerprints from any known keys.
                    if (peer.PublicKey != null && peer.PublicKey.Length > 0)
                    {
                        try
                        {
                            var hash = System.Security.Cryptography.SHA256.HashData(peer.PublicKey);
                            var fingerprint = Convert.ToBase64String(hash);
                            _settings.Settings.BlockedPublicKeyFingerprints?.Remove(fingerprint);
                            Utilities.Logger.Log($"[UNBLOCK-PUBKEY] Removed public key fingerprint from blocklist for {uid}");
                        }
                        catch { }
                    }

                    if (peer.CachedPublicKey != null && peer.CachedPublicKey.Length > 0)
                    {
                        try
                        {
                            var hash = System.Security.Cryptography.SHA256.HashData(peer.CachedPublicKey);
                            var fingerprint = Convert.ToBase64String(hash);
                            _settings.Settings.BlockedPublicKeyFingerprints?.Remove(fingerprint);
                            Utilities.Logger.Log($"[UNBLOCK-PUBKEY] Removed cached public key fingerprint from blocklist for {uid}");
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

                // Also remove fingerprint derived from contact last-known key, if present.
                try
                {
                    var contact = AppServices.Contacts.Contacts.FirstOrDefault(c => string.Equals(NormalizeUid(c.UID), uid, StringComparison.OrdinalIgnoreCase));
                    if (contact != null)
                    {
                        RemoveFingerprintForHex(contact.LastKnownPublicKeyHex, uid);
                        RemoveFingerprintForHex(contact.ExpectedPublicKeyHex, uid);
                    }
                }
                catch { }
                
                _settings.Save(AppServices.Passphrase);
                try { AppServices.Discovery.Restart(); } catch { }
                try { AppServices.Network.RequestAutoConnectSweep(); } catch { }
                Changed?.Invoke();
            }
        }

        private void RemoveFingerprintForHex(string? keyHex, string uid)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(keyHex)) return;
                var norm = keyHex.Trim().Replace("-", string.Empty).Replace(":", string.Empty).Replace(" ", string.Empty);
                var key = Convert.FromHexString(norm);
                if (key.Length == 0) return;

                var hash = System.Security.Cryptography.SHA256.HashData(key);
                var fingerprint = Convert.ToBase64String(hash);
                if (_settings.Settings.BlockedPublicKeyFingerprints?.Remove(fingerprint) == true)
                {
                    Utilities.Logger.Log($"[UNBLOCK-PUBKEY] Removed contact-derived public key fingerprint from blocklist for {uid}");
                }
            }
            catch { }
        }

        public void ClearAllBlocks()
        {
            _settings.Settings.BlockList?.Clear();
            _settings.Save(AppServices.Passphrase);
            Changed?.Invoke();
        }
    }
}
