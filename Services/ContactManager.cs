/*
    Contacts storage (contacts.p2e): load/save encrypted contact list after unlock.
    - Supports add/remove/update contact entries; used by MainWindow and Network peers.
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

using ZTalk.Containers;
using ZTalk.Models;
using ZTalk.Utilities;

namespace ZTalk.Services
{
    public class ContactManager
    {
        private const string FileName = "contacts.p2e";
        private readonly P2EContainer _container = new();
        private readonly List<Contact> _contacts = new();
        public IReadOnlyList<Contact> Contacts => _contacts;
        public event Action? Changed;

        private string GetPath()
        {
            return ZTalk.Utilities.AppDataPaths.Combine(FileName);
        }

        public void Load(string passphrase)
        {
            var path = GetPath();
            try
            {
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    Save(passphrase); // create empty encrypted container
                    return;
                }
                var bytes = _container.LoadFile(path, passphrase);
                var json = Encoding.UTF8.GetString(bytes);
                var list = JsonSerializer.Deserialize<List<Contact>>(json) ?? new List<Contact>();
                var sanitized = SanitizeContacts(list, out var report);
                _contacts.Clear();
                _contacts.AddRange(sanitized);
                // Initialize transient verification from persisted flag so UI shields render on startup
                foreach (var c in _contacts)
                {
                    try { c.PublicKeyVerified = c.IsVerified || c.PublicKeyVerified; } catch { }
                }
                // Presence is transient and should be driven by live network updates; don't override on load.
                if (report.Changed)
                {
                    try
                    {
                        Save(passphrase); // persist repairs
                        Logger.Log($"Contacts sanitized: {report}");
                        ZTalk.Utilities.ErrorLogger.LogException(new InvalidOperationException(report.ToString()), source: "Contacts.Sanitize");
                    }
                    catch { }
                }
                Changed?.Invoke();
            }
            catch (Exception ex)
            {
                Logger.Log($"Contacts load failed: {ex.Message}");
                // Keep in-memory list empty on failure
            }
        }

        private static string TrimUidPrefix(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return string.Empty;
            return uid.StartsWith("usr-", StringComparison.Ordinal) && uid.Length > 4 ? uid.Substring(4) : uid;
        }

        private static string NormalizeUid(string? uid)
        {
            return TrimUidPrefix((uid ?? string.Empty).Trim());
        }

        private sealed class SanitizeReport
        {
            public int DroppedNullOrEmpty { get; set; }
            public int DuplicatesRemoved { get; set; }
            public int DisplayNamesDefaulted { get; set; }
            public int UidsTrimmed { get; set; }
            public bool Changed => DroppedNullOrEmpty > 0 || DuplicatesRemoved > 0 || DisplayNamesDefaulted > 0 || UidsTrimmed > 0;
            public override string ToString() => $"Dropped={DroppedNullOrEmpty}, DuplicatesRemoved={DuplicatesRemoved}, DefaultedNames={DisplayNamesDefaulted}, UidsTrimmed={UidsTrimmed}";
        }

        private static List<Contact> SanitizeContacts(List<Contact> list, out SanitizeReport report)
        {
            report = new SanitizeReport();
            var result = new List<Contact>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in list)
            {
                if (c == null) { report.DroppedNullOrEmpty++; continue; }
                var uid = (c.UID ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(uid)) { report.DroppedNullOrEmpty++; continue; }
                var trimmed = TrimUidPrefix(uid);
                if (!string.Equals(uid, trimmed, StringComparison.Ordinal)) report.UidsTrimmed++;
                uid = trimmed;
                if (!seen.Add(uid)) { report.DuplicatesRemoved++; continue; }
                c.UID = uid;
                if (string.IsNullOrWhiteSpace(c.DisplayName)) { c.DisplayName = uid; report.DisplayNamesDefaulted++; }
                // Leave flags (IsSimulated/IsTrusted/IsVerified) as-is
                result.Add(c);
            }
            return result;
        }

        public void Save(string passphrase)
        {
            try
            {
                var json = JsonSerializer.Serialize(_contacts, SerializationDefaults.Indented);
                var bytes = Encoding.UTF8.GetBytes(json);
                var path = GetPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                _container.SaveFile(path, bytes, passphrase);
                Logger.Log($"Contacts saved: {path} ({bytes.Length} bytes before encryption)");
            }
            catch (Exception ex)
            {
                Logger.Log($"Contacts save failed: {ex.Message}");
            }
        }

        public bool AddContact(Contact contact, string passphrase)
        {
            contact.UID = NormalizeUid(contact.UID);
            if (string.IsNullOrWhiteSpace(contact.UID)) return false;
            if (string.IsNullOrWhiteSpace(contact.DisplayName)) contact.DisplayName = contact.UID;
            if (_contacts.Any(c => string.Equals(c.UID, contact.UID, StringComparison.OrdinalIgnoreCase))) return false;
            _contacts.Add(contact);
            Save(passphrase);
            Changed?.Invoke();
            return true;
        }

        public bool RemoveContact(string uid, string passphrase)
        {
            uid = NormalizeUid(uid);
            var removed = _contacts.RemoveAll(c => string.Equals(c.UID, uid, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
            {
                Save(passphrase);
                Changed?.Invoke();
            }
            return removed;
        }

        // Update a contact's display name and persist. Returns true if updated.
        public bool UpdateDisplayName(string uid, string newDisplayName, string passphrase)
        {
            try
            {
                uid = NormalizeUid(uid);
                if (string.IsNullOrWhiteSpace(uid)) return false;
                var c = _contacts.FirstOrDefault(x => string.Equals(x.UID, uid, StringComparison.OrdinalIgnoreCase));
                if (c == null) return false;
                newDisplayName = newDisplayName?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(newDisplayName) || string.Equals(c.DisplayName, newDisplayName, StringComparison.Ordinal))
                    return false;
                
                // Track display name history
                if (c.DisplayNameHistory == null)
                {
                    c.DisplayNameHistory = new System.Collections.Generic.List<DisplayNameRecord>();
                    // Add current name as first historical entry if not already tracked
                    if (!string.IsNullOrWhiteSpace(c.DisplayName))
                    {
                        c.DisplayNameHistory.Add(new DisplayNameRecord { Name = c.DisplayName, ChangedAtUtc = System.DateTime.UtcNow });
                    }
                }
                
                // Add new name to history
                c.DisplayNameHistory.Add(new DisplayNameRecord { Name = newDisplayName, ChangedAtUtc = System.DateTime.UtcNow });
                c.DisplayNameChangeCount++;
                c.DisplayName = newDisplayName;
                
                Save(passphrase);
                Changed?.Invoke();
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Local-only: set trusted flag for a contact and persist
        public bool SetTrusted(string uid, bool trusted, string passphrase)
        {
            try
            {
                uid = NormalizeUid(uid);
                var c = _contacts.FirstOrDefault(x => string.Equals(x.UID, uid, StringComparison.OrdinalIgnoreCase));
                if (c == null) return false;
                if (c.IsTrusted == trusted) return true;
                c.IsTrusted = trusted;
                Save(passphrase);
                Changed?.Invoke();
                return true;
            }
            catch
            {
                return false;
            }
        }

        // [VERIFY] Transient: set PublicKeyVerified on a contact (not persisted) and notify UI
        public void SetPublicKeyVerified(string uid, bool verified)
        {
            try
            {
                uid = NormalizeUid(uid);
                var c = _contacts.FirstOrDefault(x => string.Equals(x.UID, uid, StringComparison.OrdinalIgnoreCase));
                if (c == null) return;
                c.PublicKeyVerified = verified;
                Changed?.Invoke();
            }
            catch { }
        }

        // Persisted: set IsVerified on a contact (for manual/Debug toggles) and persist to contacts.p2e
        public bool SetIsVerified(string uid, bool isVerified, string passphrase)
        {
            try
            {
                uid = NormalizeUid(uid);
                var c = _contacts.FirstOrDefault(x => string.Equals(x.UID, uid, StringComparison.OrdinalIgnoreCase));
                if (c == null) return false;
                if (c.IsVerified == isVerified) return true;
                c.IsVerified = isVerified;
                Save(passphrase);
                Changed?.Invoke();
                return true;
            }
            catch
            {
                return false;
            }
        }

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

        private static string? NormalizeHex(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            var s = hex.Trim().ToLowerInvariant();
            return s.Replace("-", string.Empty).Replace(":", string.Empty).Replace(" ", string.Empty);
        }
    }
}
