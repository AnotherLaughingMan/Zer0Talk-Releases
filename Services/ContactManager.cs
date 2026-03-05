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

using Zer0Talk.Containers;
using System.Diagnostics;
using Zer0Talk.Models;
using Zer0Talk.Utilities;

namespace Zer0Talk.Services
{
    public partial class ContactManager
    {
        private const string FileName = "contacts.p2e";
        private readonly P2EContainer _container = new();
        private readonly List<Contact> _contacts = new();
        // Protect concurrent save/read of the contacts list when persisting asynchronously
        private readonly object _saveLock = new();
        public IReadOnlyList<Contact> Contacts => _contacts;
        public event Action? Changed;

        private string GetPath()
        {
            return Zer0Talk.Utilities.AppDataPaths.Combine(FileName);
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
                        Zer0Talk.Utilities.ErrorLogger.LogException(new InvalidOperationException(report.ToString()), source: "Contacts.Sanitize");
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
                lock (_saveLock)
                {
                    var json = JsonSerializer.Serialize(_contacts, SerializationDefaults.Indented);
                    var bytes = Encoding.UTF8.GetBytes(json);
                    var path = GetPath();
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        if (Utilities.LoggingPaths.Enabled)
                            Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Contacts] Save start size={bytes.Length}\n");
                    }
                    catch { }
                    _container.SaveFile(path, bytes, passphrase);
                    sw.Stop();
                    Logger.Log($"Contacts saved: {path} ({bytes.Length} bytes before encryption)");
                    try
                    {
                        if (Utilities.LoggingPaths.Enabled)
                            Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Contacts] Save complete size={bytes.Length} elapsedMs={sw.ElapsedMilliseconds}\n");
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Contacts save failed: {ex.Message}");
                try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Contacts] Save FAILED: {ex.Message}\n"); } catch { }
            }
        }

        public bool AddContact(Contact contact, string passphrase)
        {
            contact.UID = NormalizeUid(contact.UID);
            if (string.IsNullOrWhiteSpace(contact.UID)) return false;
            if (string.IsNullOrWhiteSpace(contact.DisplayName)) contact.DisplayName = contact.UID;
            lock (_saveLock)
            {
                if (_contacts.Any(c => string.Equals(c.UID, contact.UID, StringComparison.OrdinalIgnoreCase))) return false;
                _contacts.Add(contact);
            }
            // Notify UI immediately so the new contact appears without waiting for disk I/O.
            Changed?.Invoke();
            // Persist asynchronously to avoid blocking UI while encrypting/writing to disk.
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try { Save(passphrase); } catch { }
            });
            return true;
        }

        // Force trigger the Changed event (for external UI refresh when needed)
        public void NotifyChanged()
        {
            Changed?.Invoke();
        }

        public bool RemoveContact(string uid, string passphrase)
        {
            uid = NormalizeUid(uid);
            var removed = _contacts.RemoveAll(c => string.Equals(c.UID, uid, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
            {
                Save(passphrase);
                Changed?.Invoke();

                // Clean up peer entry and disconnect any active session
                try { AppServices.Peers.RemovePeer(uid); } catch { }
                try { AppServices.Network.DisconnectPeer(uid); } catch { }
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

        // Persisted trust-ceremony record for profile history and auditing.
        public bool RecordVerification(string uid, string passphrase, string fingerprint, string method)
        {
            try
            {
                uid = NormalizeUid(uid);
                if (string.IsNullOrWhiteSpace(uid)) return false;
                var c = _contacts.FirstOrDefault(x => string.Equals(x.UID, uid, StringComparison.OrdinalIgnoreCase));
                if (c == null) return false;

                var normalizedFingerprint = (fingerprint ?? string.Empty).Trim();
                var normalizedMethod = string.IsNullOrWhiteSpace(method) ? "Verified" : method.Trim();
                var now = DateTime.UtcNow;

                c.IsVerified = true;
                c.PublicKeyVerified = true;
                c.LastVerifiedUtc = now;

                c.VerificationHistory ??= new List<VerificationHistoryEntry>();
                var latest = c.VerificationHistory.Count > 0 ? c.VerificationHistory[0] : null;

                // Dedupe repeated notifications for the same ceremony in a short window.
                var isDuplicateRecent = latest != null
                    && string.Equals(latest.Fingerprint, normalizedFingerprint, StringComparison.Ordinal)
                    && string.Equals(latest.Method, normalizedMethod, StringComparison.Ordinal)
                    && (now - latest.VerifiedAtUtc) <= TimeSpan.FromMinutes(5);

                if (!isDuplicateRecent)
                {
                    c.VerificationHistory.Insert(0, new VerificationHistoryEntry
                    {
                        VerifiedAtUtc = now,
                        Fingerprint = normalizedFingerprint,
                        Method = normalizedMethod,
                    });

                    const int maxEntries = 20;
                    if (c.VerificationHistory.Count > maxEntries)
                    {
                        c.VerificationHistory.RemoveRange(maxEntries, c.VerificationHistory.Count - maxEntries);
                    }
                }

                Save(passphrase);
                Changed?.Invoke();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
