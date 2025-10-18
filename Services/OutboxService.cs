using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZTalk.Containers;
using ZTalk.Models;

namespace ZTalk.Services
{
    public class OutboxService
    {
        private readonly OutboxContainer _store = new();
        private readonly ConcurrentDictionary<string, bool> _draining = new(StringComparer.OrdinalIgnoreCase);

        public void Enqueue(string peerUid, Message msg, string passphrase)
        {
            try
            {
                if (msg.Id == Guid.Empty)
                {
                    msg.Id = Guid.NewGuid();
                }

                var qm = new QueuedMessage
                {
                    Id = msg.Id,
                    Message = msg,
                    Operation = "Chat"
                };

                _store.Enqueue(peerUid, qm, passphrase);
            }
            catch { }
        }

        public void EnqueueEdit(string peerUid, Guid messageId, string newContent, string passphrase)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(peerUid)) return;
                if (messageId == Guid.Empty) return;
                var list = _store.Load(peerUid, passphrase);
                var existing = list.FirstOrDefault(x => (x.Id == messageId || (x.Message?.Id == messageId))
                                                         && string.Equals(x.Operation, "Edit", StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    if (existing.Message == null)
                    {
                        existing.Message = new Message { Id = messageId };
                    }
                    existing.Id = messageId;
                    existing.Message.Id = messageId;
                    existing.Message.Content = newContent ?? string.Empty;
                    existing.Operation = "Edit";
                    if (existing.CreatedUtc == default)
                    {
                        existing.CreatedUtc = DateTime.UtcNow;
                    }
                }
                else
                {
                    var qm = new QueuedMessage
                    {
                        Id = messageId,
                        Message = new Message
                        {
                            Id = messageId,
                            Content = newContent ?? string.Empty
                        },
                        Operation = "Edit"
                    };
                    list.Add(qm);
                }
                _store.Save(peerUid, list.OrderBy(x => x.CreatedUtc).ToList(), passphrase);
            }
            catch { }
        }

        public async Task DrainAsync(string peerUid, string passphrase, CancellationToken ct)
        {
            peerUid = Trim(peerUid);
            if (string.IsNullOrWhiteSpace(peerUid)) return;
            if (!_draining.TryAdd(peerUid, true)) return; // already draining
            try
            {
                // Only attempt when a session exists
                if (!AppServices.Network.HasEncryptedSession(peerUid)) return;
                var list = _store.Load(peerUid, passphrase);
                if (list.Count == 0) return;
                // Sort oldest first
                list = list.OrderBy(x => x.CreatedUtc).ToList();
                foreach (var item in list)
                {
                    if (ct.IsCancellationRequested) break;
                    if (!AppServices.Network.HasEncryptedSession(peerUid)) break;
                    bool ok = false;
                    var op = item.Operation ?? "Chat";
                    switch (op)
                    {
                        case "Edit":
                            {
                                var mid = item.Message?.Id ?? item.Id;
                                if (mid == Guid.Empty)
                                {
                                    ok = true; // nothing to do, drop entry quietly
                                }
                                else
                                {
                                    var content = item.Message?.Content ?? string.Empty;
                                    ok = await AppServices.Network.SendEditMessageAsync(peerUid, mid, content, ct);
                                }
                                break;
                            }
                        case "Chat":
                        default:
                            {
                                var message = item.Message;
                                if (message == null)
                                {
                                    ok = true; // nothing to send
                                }
                                else
                                {
                                    if (message.Id == Guid.Empty && item.Id != Guid.Empty)
                                    {
                                        message.Id = item.Id;
                                    }
                                    if (message.Id == Guid.Empty)
                                    {
                                        ok = true; // invalid entry, discard
                                    }
                                    else
                                    {
                                        ok = await AppServices.Network.SendChatAsync(peerUid, message.Id, message.Content, ct);
                                    }
                                }
                                break;
                            }
                    }
                    item.AttemptCount++;
                    item.LastAttemptUtc = DateTime.UtcNow;
                    if (ok)
                    {
                        _store.Remove(peerUid, item.Id, passphrase);
                    }
                    else
                    {
                        // Leave in queue; stop early to avoid hammering
                        break;
                    }
                    // Short pacing to avoid burst
                    try { await Task.Delay(50, ct); } catch { }
                }
            }
            catch { }
            finally
            {
                _draining.TryRemove(peerUid, out _);
            }
        }

        public void DrainAllIfPossible(string passphrase)
        {
            try
            {
                var peers = AppServices.Peers.Peers.Select(p => p.UID).ToList();
                foreach (var uid in peers)
                {
                    if (AppServices.Network.HasEncryptedSession(uid))
                    {
                        _ = DrainAsync(uid, passphrase, CancellationToken.None);
                    }
                }
            }
            catch { }
        }

        private static string Trim(string uid)
            => uid.StartsWith("usr-", StringComparison.Ordinal) && uid.Length > 4 ? uid.Substring(4) : uid;

        public void CancelQueued(string peerUid, Guid messageId, string passphrase)
        {
            try { _store.Remove(peerUid, messageId, passphrase); } catch { }
        }

        public void UpdateQueued(string peerUid, Guid messageId, string newContent, string passphrase)
        {
            try { _store.Update(peerUid, messageId, newContent, passphrase); } catch { }
        }
    }
}
