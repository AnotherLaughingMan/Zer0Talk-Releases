using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Zer0Talk.Containers;
using System.Diagnostics;
using Zer0Talk.Models;
using Zer0Talk.Utilities;

namespace Zer0Talk.Services
{
    public partial class ContactManager
    {
    // Transient: set Presence without persisting. Raises Changed so UI can refresh bindings.
    public void SetPresence(string uid, PresenceStatus status)
        {
            try
            {
                uid = NormalizeUid(uid);
                var c = _contacts.FirstOrDefault(x => string.Equals(x.UID, uid, StringComparison.OrdinalIgnoreCase));
                if (c == null) return;
                var prev = c.Presence;
                if (prev == status) { return; }
                c.Presence = status;
        c.LastPresenceUtc = System.DateTime.UtcNow;
        c.PresenceExpiresUtc = null;
        c.PresenceSource = PresenceSource.Manual;
                Changed?.Invoke();
                try
                {
                    if (Utilities.LoggingPaths.Enabled)
                    {
                        var line = $"{DateTime.Now:O} [Presence] {uid} {prev}->{status} src=Contacts";
                        System.IO.File.AppendAllText(Utilities.LoggingPaths.UI, line + Environment.NewLine);
                    }
                }
                catch { }
            }
            catch { }
        }

        // Transient: set Presence with TTL and source. TTL of null uses defaults based on source.
        public void SetPresence(string uid, PresenceStatus status, System.TimeSpan? ttl, PresenceSource source)
        {
            try
            {
                uid = NormalizeUid(uid);
                var c = _contacts.FirstOrDefault(x => string.Equals(x.UID, uid, System.StringComparison.OrdinalIgnoreCase));
                if (c == null) return;
                var now = System.DateTime.UtcNow;
                var prev = c.Presence;
                c.Presence = status;
                c.PresenceSource = source;
                c.LastPresenceUtc = now;
                var effectiveTtl = ttl ?? (source switch
                {
                    PresenceSource.Session => System.TimeSpan.FromMinutes(5), // session presence sustained until close
                    PresenceSource.Verified => System.TimeSpan.FromSeconds(60),
                    PresenceSource.Discovery => System.TimeSpan.FromSeconds(20),
                    PresenceSource.Manual => System.TimeSpan.FromSeconds(60),
                    _ => System.TimeSpan.FromSeconds(30)
                });
                c.PresenceExpiresUtc = (status == PresenceStatus.Offline || status == PresenceStatus.Invisible)
                    ? now // immediate expiry path; sweep will render Offline
                    : now + effectiveTtl;
                if (prev != c.Presence)
                {
                    Changed?.Invoke();
                }
                try
                {
                    if (Utilities.LoggingPaths.Enabled)
                    {
                        var line = $"{System.DateTime.Now:O} [Presence] {uid} {prev}->{status} src={source} ttl={(int)effectiveTtl.TotalSeconds}s";
                        System.IO.File.AppendAllText(Utilities.LoggingPaths.UI, line + System.Environment.NewLine);
                    }
                }
                catch { }
            }
            catch { }
        }

        // Expire stale presences: if PresenceExpiresUtc < now and no session is active, demote to Offline with small hold-down.
        public void ExpireStalePresences()
        {
            try
            {
                var now = System.DateTime.UtcNow;
                foreach (var c in _contacts)
                {
                    try
                    {
                        if (c.Presence == PresenceStatus.Offline) continue;
                        // Keep Online when a session exists regardless of timestamps
                        if (AppServices.Network.HasEncryptedSession(c.UID)) continue;
                        var exp = c.PresenceExpiresUtc;
                        if (exp.HasValue && exp.Value <= now)
                        {
                            var prev = c.Presence;
                            c.Presence = PresenceStatus.Offline;
                            c.PresenceSource = PresenceSource.Unknown;
                            c.PresenceExpiresUtc = null;
                            Changed?.Invoke();
                            try
                            {
                                if (Utilities.LoggingPaths.Enabled)
                                {
                                    var line = $"{System.DateTime.Now:O} [Presence] {c.UID} {prev}->Offline src=Expiry";
                                    System.IO.File.AppendAllText(Utilities.LoggingPaths.UI, line + System.Environment.NewLine);
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        // When unlocking after being offline, clear stale presences older than the provided threshold.
        public void ResetPresenceForUnlock(System.TimeSpan staleAfter)
        {
            try
            {
                var now = System.DateTime.UtcNow;
                var changed = false;
                foreach (var c in _contacts)
                {
                    try
                    {
                        if (c == null) continue;
                        if (c.Presence == PresenceStatus.Offline && c.LastPresenceUtc == null) continue;
                        var last = c.LastPresenceUtc;
                        if (last.HasValue && (now - last.Value) < staleAfter)
                        {
                            continue;
                        }

                        if (c.Presence != PresenceStatus.Offline || c.PresenceSource != PresenceSource.Unknown)
                        {
                            c.Presence = PresenceStatus.Offline;
                            c.PresenceSource = PresenceSource.Unknown;
                            c.PresenceExpiresUtc = null;
                            c.LastPresenceUtc = null;
                            changed = true;
                        }
                    }
                    catch { }
                }

                if (changed)
                {
                    Changed?.Invoke();
                }
            }
            catch { }
        }

        public void SetAllOffline()
        {
            try
            {
                var changed = false;
                foreach (var c in _contacts)
                {
                    try
                    {
                        if (c == null) continue;
                        if (c.Presence == PresenceStatus.Offline && c.PresenceSource == PresenceSource.Unknown && c.LastPresenceUtc == null)
                        {
                            continue;
                        }

                        c.Presence = PresenceStatus.Offline;
                        c.PresenceSource = PresenceSource.Unknown;
                        c.PresenceExpiresUtc = null;
                        c.LastPresenceUtc = null;
                        changed = true;
                    }
                    catch { }
                }

                if (changed)
                {
                    Changed?.Invoke();
                }
            }
            catch { }
        }

        // Persist last-known public key hex for a contact. Accepts raw hex with or without separators; stored normalized.
        public bool SetLastKnownPublicKeyHex(string uid, string hex, string passphrase)
        {
            try
            {
                uid = NormalizeUid(uid);
                var c = _contacts.FirstOrDefault(x => string.Equals(x.UID, uid, StringComparison.OrdinalIgnoreCase));
                if (c == null) return false;
                var norm = NormalizeHex(hex);
                if (string.IsNullOrWhiteSpace(norm)) return false;
                if (string.Equals(c.LastKnownPublicKeyHex, norm, StringComparison.Ordinal)) return true;
                c.LastKnownPublicKeyHex = norm;
                Save(passphrase);
                Changed?.Invoke();
                return true;
            }
            catch { return false; }
        }

        // Persist last-known encrypted session flag for a contact (for offline UI hinting only).
        public bool SetLastKnownEncrypted(string uid, bool encrypted, string passphrase)
        {
            try
            {
                uid = NormalizeUid(uid);
                var c = _contacts.FirstOrDefault(x => string.Equals(x.UID, uid, StringComparison.OrdinalIgnoreCase));
                if (c == null) return false;
                if (c.LastKnownEncrypted == encrypted) return true;
                c.LastKnownEncrypted = encrypted;
                Save(passphrase);
                Changed?.Invoke();
                return true;
            }
            catch { return false; }
        }

        // Persist Bio for a contact. Clamps to 280 characters; null/whitespace becomes null.
        public bool SetBio(string uid, string? bio, string passphrase)
        {
            try
            {
                uid = NormalizeUid(uid);
                var c = _contacts.FirstOrDefault(x => string.Equals(x.UID, uid, StringComparison.OrdinalIgnoreCase));
                if (c == null) return false;
                string? normalized = string.IsNullOrWhiteSpace(bio) ? null : (bio!.Length > 280 ? bio.Substring(0, 280) : bio);
                if (string.Equals(c.Bio ?? string.Empty, normalized ?? string.Empty, StringComparison.Ordinal)) return true;
                c.Bio = normalized;
                Save(passphrase);
                Changed?.Invoke();
                return true;
            }
            catch { return false; }
        }

        public bool SetMuteNotifications(string uid, bool muted, string passphrase)
        {
            try
            {
                uid = NormalizeUid(uid);
                var c = _contacts.FirstOrDefault(x => string.Equals(x.UID, uid, StringComparison.OrdinalIgnoreCase));
                if (c == null) return false;
                if (c.MuteNotifications == muted) return true;
                c.MuteNotifications = muted;
                Save(passphrase);
                Changed?.Invoke();
                return true;
            }
            catch { return false; }
        }

        public bool SetPriorityNotifications(string uid, bool priority, string passphrase)
        {
            try
            {
                uid = NormalizeUid(uid);
                var c = _contacts.FirstOrDefault(x => string.Equals(x.UID, uid, StringComparison.OrdinalIgnoreCase));
                if (c == null) return false;
                if (c.PriorityNotifications == priority) return true;
                c.PriorityNotifications = priority;
                Save(passphrase);
                Changed?.Invoke();
                return true;
            }
            catch { return false; }
        }

        private static string? NormalizeHex(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            var s = hex.Trim().ToLowerInvariant();
            return s.Replace("-", string.Empty).Replace(":", string.Empty).Replace(" ", string.Empty);
        }
    }
}
