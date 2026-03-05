/*
    Message container: encrypted on-disk storage of chat payloads (if used for persistence/caching).
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using Zer0Talk.Models;
using Zer0Talk.Utilities;

namespace Zer0Talk.Containers
{
    public class MessageContainer
    {
        private readonly P2EContainer _p2e = new();
        private static string GetBaseDir()
        {
            var dir = Zer0Talk.Utilities.AppDataPaths.Combine("messages");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string Sanitize(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return "unknown";
            var value = uid.Trim();
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(c, '_');
            }
            return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
        }

        private static string FileForPeer(string peerUid)
        {
            var safe = Sanitize(peerUid);
            return Path.Combine(GetBaseDir(), safe + ".p2e");
        }

        // Encrypts and stores a message into the peer's conversation file
        public void StoreMessage(string peerUid, Message message, string passphrase)
        {
            try
            {
                var path = FileForPeer(peerUid);
                var list = LoadMessages(peerUid, passphrase);
                list.Add(message);
                var json = JsonSerializer.Serialize(list, SerializationDefaults.Compact);
                _p2e.SaveFile(path, System.Text.Encoding.UTF8.GetBytes(json), passphrase);
                Logger.Log($"MessageContainer: stored message for peer={TrimUidPrefix(peerUid)} count={list.Count}");
            }
            catch (Exception ex)
            {
                Logger.Log($"MessageContainer: store failed for peer={TrimUidPrefix(peerUid)}: {ex.Message}");
            }
        }

        // Loads and decrypts all messages for a peer (returns empty list if none)
        public List<Message> LoadMessages(string peerUid, string passphrase)
        {
            try
            {
                var path = FileForPeer(peerUid);
                if (!File.Exists(path))
                {
                    return new List<Message>();
                }
                var raw = _p2e.LoadFile(path, passphrase);
                var json = System.Text.Encoding.UTF8.GetString(raw);
                var list = JsonSerializer.Deserialize<List<Message>>(json) ?? new List<Message>();
                // Normalize UIDs and repair missing IDs to keep attribution stable across versions
                var changed = false;
                foreach (var m in list)
                {
                    try
                    {
                        var originalSender = m.SenderUID ?? string.Empty;
                        var originalRecipient = m.RecipientUID ?? string.Empty;
                        var trimmedSender = TrimUidPrefix(originalSender);
                        var trimmedRecipient = TrimUidPrefix(originalRecipient);
                        if (!string.Equals(originalSender, trimmedSender, StringComparison.Ordinal))
                        {
                            m.SenderUID = trimmedSender;
                            changed = true;
                        }
                        if (!string.Equals(originalRecipient, trimmedRecipient, StringComparison.Ordinal))
                        {
                            m.RecipientUID = trimmedRecipient;
                            changed = true;
                        }
                        if (m.Id == Guid.Empty)
                        {
                            m.Id = Guid.NewGuid();
                            changed = true;
                        }
                    }
                    catch { }
                }
                // If any normalization occurred, persist the migrated conversation
                if (changed)
                {
                    try
                    {
                        var migrated = JsonSerializer.Serialize(list, SerializationDefaults.Compact);
                        _p2e.SaveFile(path, System.Text.Encoding.UTF8.GetBytes(migrated), passphrase);
                    }
                    catch { }
                }
                return list;
            }
            catch (Exception ex)
            {
                try
                {
                    var path = FileForPeer(peerUid);
                    Logger.Log($"MessageContainer: failed to load conversation for peer={TrimUidPrefix(peerUid)}: {ex.Message}");
                    TryQuarantineCorruptConversation(path);
                }
                catch { }
                return new List<Message>();
            }
        }

        private static void TryQuarantineCorruptConversation(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                var dir = Path.GetDirectoryName(path) ?? GetBaseDir();
                var file = Path.GetFileNameWithoutExtension(path);
                var quarantine = Path.Combine(dir, $"{file}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}.p2e");
                File.Move(path, quarantine, overwrite: false);
                Logger.Log($"MessageContainer: quarantined unreadable conversation file to {Path.GetFileName(quarantine)}");
            }
            catch { }
        }

        // Update an existing message content by Id (edit in place)
        public bool UpdateMessage(string peerUid, Guid messageId, string newContent, string passphrase)
        {
            try
            {
                var list = LoadMessages(peerUid, passphrase);
                var idx = list.FindIndex(m => m.Id == messageId);
                if (idx < 0) return false;
                list[idx].Content = newContent;
                list[idx].IsEdited = true;
                list[idx].EditedUtc = DateTime.UtcNow;
                var json = JsonSerializer.Serialize(list, SerializationDefaults.Compact);
                _p2e.SaveFile(FileForPeer(peerUid), System.Text.Encoding.UTF8.GetBytes(json), passphrase);
                return true;
            }
            catch { return false; }
        }

        public bool UpdateLinkPreview(string peerUid, Guid messageId, LinkPreview? preview, string passphrase)
        {
            try
            {
                var list = LoadMessages(peerUid, passphrase);
                var idx = list.FindIndex(m => m.Id == messageId);
                if (idx < 0) return false;
                list[idx].LinkPreview = preview;
                var json = JsonSerializer.Serialize(list, SerializationDefaults.Compact);
                _p2e.SaveFile(FileForPeer(peerUid), System.Text.Encoding.UTF8.GetBytes(json), passphrase);
                return true;
            }
            catch { return false; }
        }

        public bool UpdateFlags(string peerUid, Guid messageId, bool isPinned, bool isStarred, bool isImportant, string passphrase)
        {
            try
            {
                var list = LoadMessages(peerUid, passphrase);
                var idx = list.FindIndex(m => m.Id == messageId);
                if (idx < 0) return false;
                list[idx].IsPinned = isPinned;
                list[idx].IsStarred = isStarred;
                list[idx].IsImportant = isImportant;
                var json = JsonSerializer.Serialize(list, SerializationDefaults.Compact);
                _p2e.SaveFile(FileForPeer(peerUid), System.Text.Encoding.UTF8.GetBytes(json), passphrase);
                return true;
            }
            catch { return false; }
        }

        public bool ApplyReaction(string peerUid, Guid messageId, string actorUid, string emoji, bool isAdd, string passphrase)
        {
            try
            {
                var list = LoadMessages(peerUid, passphrase);
                var idx = list.FindIndex(m => m.Id == messageId);
                if (idx < 0) return false;
                list[idx].ApplyReaction(actorUid, emoji, isAdd);
                var json = JsonSerializer.Serialize(list, SerializationDefaults.Compact);
                _p2e.SaveFile(FileForPeer(peerUid), System.Text.Encoding.UTF8.GetBytes(json), passphrase);
                return true;
            }
            catch { return false; }
        }

        // Link a message to its simulated echo (pairing for retention co-delete)
        public bool UpdateRelated(string peerUid, Guid messageId, Guid relatedId, string passphrase)
        {
            try
            {
                var list = LoadMessages(peerUid, passphrase);
                var idx = list.FindIndex(m => m.Id == messageId);
                if (idx < 0) return false;
                list[idx].RelatedMessageId = relatedId;
                var json = JsonSerializer.Serialize(list, SerializationDefaults.Compact);
                _p2e.SaveFile(FileForPeer(peerUid), System.Text.Encoding.UTF8.GetBytes(json), passphrase);
                return true;
            }
            catch { return false; }
        }

        // Delete a message by Id and persist changes
        public bool DeleteMessage(string peerUid, Guid messageId, string passphrase)
        {
            try
            {
                var list = LoadMessages(peerUid, passphrase);
                var removed = list.RemoveAll(m => m.Id == messageId) > 0;
                if (!removed) return false;
                var json = JsonSerializer.Serialize(list, SerializationDefaults.Compact);
                _p2e.SaveFile(FileForPeer(peerUid), System.Text.Encoding.UTF8.GetBytes(json), passphrase);
                return true;
            }
            catch { return false; }
        }

        // Replace entire conversation (used when treating user-deleted as permanently removed)
        public bool ReplaceConversation(string peerUid, List<Message> messages, string passphrase)
        {
            try
            {
                var json = JsonSerializer.Serialize(messages ?? new List<Message>(), SerializationDefaults.Compact);
                _p2e.SaveFile(FileForPeer(peerUid), System.Text.Encoding.UTF8.GetBytes(json), passphrase);
                return true;
            }
            catch { return false; }
        }

        private static string TrimUidPrefix(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return string.Empty;
            return uid.StartsWith("usr-", StringComparison.Ordinal) && uid.Length > 4 ? uid.Substring(4) : uid;
        }
    }
}
