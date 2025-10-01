using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ZTalk.Models;
using ZTalk.Utilities;

namespace ZTalk.Containers
{
    public class OutboxContainer
    {
        private readonly P2EContainer _p2e = new();

        private static string GetBaseDir()
        {
            var dir = ZTalk.Utilities.AppDataPaths.Combine("outbox");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string Sanitize(string uid) => string.IsNullOrWhiteSpace(uid) ? "unknown" : uid.Replace("/", "_").Replace("\\", "_");

        private static string FileForPeer(string peerUid)
        {
            var safe = Sanitize(peerUid);
            return Path.Combine(GetBaseDir(), safe + ".p2e");
        }

        public List<QueuedMessage> Load(string peerUid, string passphrase)
        {
            try
            {
                var path = FileForPeer(peerUid);
                if (!File.Exists(path)) return new List<QueuedMessage>();
                var raw = _p2e.LoadFile(path, passphrase);
                var json = System.Text.Encoding.UTF8.GetString(raw);
                var list = JsonSerializer.Deserialize<List<QueuedMessage>>(json) ?? new List<QueuedMessage>();
                NormalizeQueuedIdentifiers(peerUid, list, passphrase);
                return list;
            }
            catch { return new List<QueuedMessage>(); }
        }

        public void Save(string peerUid, List<QueuedMessage> items, string passphrase)
        {
            try
            {
                var path = FileForPeer(peerUid);
                var json = JsonSerializer.Serialize(items, SerializationDefaults.Compact);
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                _p2e.SaveFile(path, bytes, passphrase);
            }
            catch { }
        }

        public void Enqueue(string peerUid, QueuedMessage qm, string passphrase)
        {
            try
            {
                var list = Load(peerUid, passphrase);
                list.Add(qm);
                Save(peerUid, list, passphrase);
            }
            catch { }
        }

        public void Remove(string peerUid, Guid id, string passphrase)
        {
            try
            {
                var list = Load(peerUid, passphrase);
                var removed = list.RemoveAll(x => x.Id == id || (x.Message?.Id == id));
                if (removed > 0)
                {
                    Save(peerUid, list, passphrase);
                }
            }
            catch { }
        }

        public void Update(string peerUid, Guid id, string newContent, string passphrase)
        {
            try
            {
                var list = Load(peerUid, passphrase);
                var item = list.Find(x => x.Id == id || (x.Message?.Id == id));
                if (item?.Message == null)
                {
                    return;
                }

                if (item.Message.Id == Guid.Empty)
                {
                    var resolved = id != Guid.Empty ? id : item.Id;
                    if (resolved == Guid.Empty)
                    {
                        resolved = Guid.NewGuid();
                    }
                    item.Message.Id = resolved;
                }

                if (item.Id != item.Message.Id && item.Message.Id != Guid.Empty)
                {
                    item.Id = item.Message.Id;
                }

                item.Message.Content = newContent ?? string.Empty;
                Save(peerUid, list, passphrase);
            }
            catch { }
        }

        private void NormalizeQueuedIdentifiers(string peerUid, List<QueuedMessage> items, string passphrase)
        {
            var changed = false;
            foreach (var item in items)
            {
                if (item == null) continue;
                Guid messageId = item.Message?.Id ?? Guid.Empty;
                if (messageId == Guid.Empty && item.Id != Guid.Empty)
                {
                    messageId = item.Id;
                    if (item.Message != null)
                    {
                        item.Message.Id = messageId;
                    }
                    changed = true;
                }
                if (messageId != Guid.Empty && item.Id != messageId)
                {
                    item.Id = messageId;
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(item.Operation))
                {
                    item.Operation = "Chat";
                    changed = true;
                }
            }

            if (changed)
            {
                Save(peerUid, items, passphrase);
            }
        }
    }
}
