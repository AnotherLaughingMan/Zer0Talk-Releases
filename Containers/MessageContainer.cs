/*
    Message container: encrypted on-disk storage of chat payloads (if used for persistence/caching).
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using ZTalk.Models;
using ZTalk.Utilities;

namespace ZTalk.Containers
{
    public class MessageContainer
    {
        private readonly P2EContainer _p2e = new();
        private static string GetBaseDir()
        {
            var dir = ZTalk.Utilities.AppDataPaths.Combine("messages");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string Sanitize(string uid) => string.IsNullOrWhiteSpace(uid) ? "unknown" : uid.Replace("/", "_").Replace("\\", "_");

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
            }
            catch { }
        }

        // Loads and decrypts all messages for a peer (returns empty list if none)
        public List<Message> LoadMessages(string peerUid, string passphrase)
        {
            try
            {
                var path = FileForPeer(peerUid);
                if (!File.Exists(path)) return new List<Message>();
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
            catch { return new List<Message>(); }
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

        // Update delivery metadata (status and delivered time) for an existing message
        public bool UpdateDelivery(string peerUid, Guid messageId, string? status, DateTime? deliveredUtc, string passphrase, DateTime? readUtc = null)
        {
            try
            {
                var list = LoadMessages(peerUid, passphrase);
                var idx = list.FindIndex(m => m.Id == messageId);
                if (idx < 0) return false;
                if (status != null) list[idx].DeliveryStatus = status;
                if (deliveredUtc.HasValue) list[idx].DeliveredUtc = deliveredUtc;
                if (readUtc.HasValue) list[idx].ReadUtc = readUtc;
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
