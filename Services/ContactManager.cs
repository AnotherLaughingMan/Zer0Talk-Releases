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
using System.Threading.Tasks;

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

        private static string GetDefaultRoamingPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, Zer0Talk.Utilities.AppDataPaths.NewRootName, FileName);
        }

        private static string GetLegacyPath()
        {
            return Path.Combine(Zer0Talk.Utilities.AppDataPaths.OldRoot, FileName);
        }

        private static string GetBackupDirectory()
        {
            return Zer0Talk.Utilities.AppDataPaths.Combine("backups", "contacts");
        }

        private static string BuildBackupPath(DateTime utc)
        {
            return Path.Combine(GetBackupDirectory(), $"contacts-{utc:yyyyMMdd-HHmmss}.p2e");
        }

        private static void PruneBackups(int keep, int maxAgeDays)
        {
            try
            {
                var dir = GetBackupDirectory();
                if (!Directory.Exists(dir)) return;

                keep = Math.Max(1, keep);
                maxAgeDays = Math.Max(0, maxAgeDays);

                var files = Directory.GetFiles(dir, "contacts-*.p2e")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ToList();

                var nowUtc = DateTime.UtcNow;
                var cutoffUtc = maxAgeDays > 0 ? nowUtc.AddDays(-maxAgeDays) : DateTime.MinValue;

                for (var i = keep; i < files.Count; i++)
                {
                    try { Utilities.SecureFileWiper.SecureWipeFile(files[i]); } catch { }
                }

                if (maxAgeDays > 0)
                {
                    for (var i = 0; i < Math.Min(keep, files.Count); i++)
                    {
                        try
                        {
                            var path = files[i];
                            var lastWriteUtc = File.GetLastWriteTimeUtc(path);
                            if (lastWriteUtc <= cutoffUtc)
                            {
                                Utilities.SecureFileWiper.SecureWipeFile(path);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private static void TryBackupExistingContacts(string path)
        {
            try
            {
                try
                {
                    if (!AppServices.Settings.Settings.AutoContactBackupsEnabled) return;
                }
                catch { }

                if (!File.Exists(path)) return;
                var dir = GetBackupDirectory();
                Directory.CreateDirectory(dir);
                var backupPath = BuildBackupPath(DateTime.UtcNow);
                File.Copy(path, backupPath, overwrite: false);

                var keep = 5;
                var maxAgeDays = 30;
                try
                {
                    var settings = AppServices.Settings.Settings;
                    keep = Math.Max(1, settings.ContactBackupMaxFiles);
                    maxAgeDays = Math.Max(0, settings.ContactBackupMaxAgeDays);
                }
                catch { }

                PruneBackups(keep, maxAgeDays);
            }
            catch { }
        }

        private static bool TryRecoverMissingContacts(string targetPath)
        {
            try
            {
                var targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                var defaultRoamingPath = GetDefaultRoamingPath();
                if (File.Exists(defaultRoamingPath)
                    && !string.Equals(defaultRoamingPath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(defaultRoamingPath, targetPath, overwrite: false);
                    Logger.Log($"Contacts recovery: restored missing contacts from default roaming path {defaultRoamingPath}");
                    return true;
                }

                var legacyPath = GetLegacyPath();
                if (File.Exists(legacyPath))
                {
                    File.Copy(legacyPath, targetPath, overwrite: false);
                    Logger.Log($"Contacts recovery: restored missing contacts from legacy path {legacyPath}");
                    return true;
                }

                var backupDir = GetBackupDirectory();
                if (!Directory.Exists(backupDir)) return false;

                var latestBackup = Directory.GetFiles(backupDir, "contacts-*.p2e")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (string.IsNullOrWhiteSpace(latestBackup) || !File.Exists(latestBackup)) return false;

                File.Copy(latestBackup, targetPath, overwrite: false);
                Logger.Log($"Contacts recovery: restored missing contacts from backup {latestBackup}");
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Load(string passphrase)
        {
            var path = GetPath();
            try
            {
                if (!File.Exists(path))
                {
                    TryRecoverMissingContacts(path);
                }

                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    Save(passphrase); // create empty encrypted container only when no recovery source exists
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
                    TryBackupExistingContacts(path);
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

        public bool TryImportFromLocalSources(string passphrase, out int importedCount, out int readableSourceCount)
        {
            importedCount = 0;
            readableSourceCount = 0;

            try
            {
                var candidates = new List<string>();
                var defaultRoamingPath = GetDefaultRoamingPath();
                var primaryPath = GetPath();
                var legacyPath = GetLegacyPath();
                var backupDir = GetBackupDirectory();

                if (!string.IsNullOrWhiteSpace(defaultRoamingPath)) candidates.Add(defaultRoamingPath);
                if (!string.IsNullOrWhiteSpace(primaryPath)) candidates.Add(primaryPath);
                if (!string.IsNullOrWhiteSpace(legacyPath)) candidates.Add(legacyPath);
                if (Directory.Exists(backupDir))
                {
                    try
                    {
                        var latestBackup = Directory.GetFiles(backupDir, "contacts-*.p2e")
                            .OrderByDescending(File.GetLastWriteTimeUtc)
                            .FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(latestBackup))
                        {
                            candidates.Add(latestBackup);
                        }
                    }
                    catch { }
                }

                var dedupedCandidates = candidates
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var path in dedupedCandidates)
                {
                    if (!File.Exists(path)) continue;
                    if (!TryLoadContactsFromFile(path, passphrase, out var loadedContacts)) continue;

                    readableSourceCount++;

                    if (loadedContacts.Count == 0) continue;

                    lock (_saveLock)
                    {
                        var existing = new HashSet<string>(_contacts.Select(c => NormalizeUid(c.UID)), StringComparer.OrdinalIgnoreCase);
                        foreach (var contact in loadedContacts)
                        {
                            var uid = NormalizeUid(contact.UID);
                            if (string.IsNullOrWhiteSpace(uid)) continue;
                            if (!existing.Add(uid)) continue;

                            contact.UID = uid;
                            if (string.IsNullOrWhiteSpace(contact.DisplayName)) contact.DisplayName = uid;
                            contact.PublicKeyVerified = contact.IsVerified || contact.PublicKeyVerified;
                            _contacts.Add(contact);
                            importedCount++;
                        }
                    }
                }

                if (importedCount > 0)
                {
                    Save(passphrase);
                    Changed?.Invoke();
                }

                return readableSourceCount > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool TryLoadContactsFromFile(string path, string passphrase, out List<Contact> contacts)
        {
            contacts = new List<Contact>();
            try
            {
                var bytes = _container.LoadFile(path, passphrase);
                var json = Encoding.UTF8.GetString(bytes);
                var parsed = JsonSerializer.Deserialize<List<Contact>>(json) ?? new List<Contact>();
                contacts = SanitizeContacts(parsed, out _);
                return true;
            }
            catch
            {
                contacts = new List<Contact>();
                return false;
            }
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

        public void SetPreferredRelay(string uid, string? relayAddress, string passphrase)
        {
            try
            {
                uid = NormalizeUid(uid);
                var c = _contacts.FirstOrDefault(x => string.Equals(x.UID, uid, StringComparison.OrdinalIgnoreCase));
                if (c == null) return;
                var next = string.IsNullOrWhiteSpace(relayAddress) ? null : relayAddress.Trim();
                if (string.Equals(c.PreferredRelay, next, StringComparison.OrdinalIgnoreCase)) return;
                c.PreferredRelay = next;
                Task.Run(() => Save(passphrase));
            }
            catch { }
        }

        public string? GetPreferredRelay(string uid)
        {
            try
            {
                uid = NormalizeUid(uid);
                var c = _contacts.FirstOrDefault(x => string.Equals(x.UID, uid, StringComparison.OrdinalIgnoreCase));
                return c?.PreferredRelay;
            }
            catch { return null; }
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
