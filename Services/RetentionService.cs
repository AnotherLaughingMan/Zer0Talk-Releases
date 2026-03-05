/*
    Data retention policies: auto-clean old logs, message caches, and temp files.
    - Runs opportunistically after unlock and on app idle.
*/
using System;

namespace Zer0Talk.Services
{
    using System.Buffers;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;

    using Zer0Talk.Containers;
    using Zer0Talk.Models;
    using Zer0Talk.Utilities;

    public partial class RetentionService
    {
        // Remove message files for contacts that no longer exist
        public void CleanupOrphanMessageFiles()
        {
            try
            {
                var dir = Zer0Talk.Utilities.AppDataPaths.Combine("messages");
                if (!System.IO.Directory.Exists(dir)) return;

                // Don't run cleanup if contacts list is empty - this likely means
                // contacts are being loaded/reloaded and we'd incorrectly delete all messages
                var contactsList = Zer0Talk.Services.AppServices.Contacts?.Contacts;
                if (contactsList == null || contactsList.Count == 0) return;

                var known = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var c in contactsList)
                    {
                        if (c?.UID is string uid && !string.IsNullOrWhiteSpace(uid))
                        {
                            known.Add(Trim(uid));
                        }
                    }
                }
                catch { }

                var files = System.IO.Directory.GetFiles(dir, "*.p2e");
                if (files.Length == 0) return;
                int deleted = 0;
                foreach (var path in files)
                {
                    try
                    {
                        var baseName = System.IO.Path.GetFileNameWithoutExtension(path) ?? string.Empty;
                        var uid = Trim(baseName);
                        if (string.IsNullOrWhiteSpace(uid)) continue;
                        if (!known.Contains(uid))
                        {
                            System.IO.File.Delete(path);
                            deleted++;
                            SafeRetentionLog($"Orphan delete: {System.IO.Path.GetFileName(path)} (uid={uid})");
                        }
                    }
                    catch { SafeRetentionLog($"Orphan delete failed on {System.IO.Path.GetFileName(path)}"); }
                }
                if (deleted > 0) SafeRetentionLog($"Orphan cleanup complete: filesDeleted={deleted}");
            }
            catch { }
        }

        // Remove outbox files for contacts that no longer exist
    public void CleanupOrphanOutboxFiles()
        {
            try
            {
                var dir = Zer0Talk.Utilities.AppDataPaths.Combine("outbox");
                if (!System.IO.Directory.Exists(dir)) return;

                // Don't run cleanup if contacts list is empty - this likely means
                // contacts are being loaded/reloaded and we'd incorrectly delete all outbox files
                var contactsList = Zer0Talk.Services.AppServices.Contacts?.Contacts;
                if (contactsList == null || contactsList.Count == 0) return;

                var known = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var c in contactsList)
                    {
                        if (c?.UID is string uid && !string.IsNullOrWhiteSpace(uid))
                        {
                            known.Add(Trim(uid));
                        }
                    }
                }
                catch { }

                var files = System.IO.Directory.GetFiles(dir, "*.p2e");
                if (files.Length == 0) return;
                int deleted = 0;
                foreach (var path in files)
                {
                    try
                    {
                        var baseName = System.IO.Path.GetFileNameWithoutExtension(path) ?? string.Empty;
                        var uid = Trim(baseName);
                        if (string.IsNullOrWhiteSpace(uid)) continue;
                        if (!known.Contains(uid))
                        {
                            System.IO.File.Delete(path);
                            deleted++;
                            SafeRetentionLog($"Orphan outbox delete: {System.IO.Path.GetFileName(path)} (uid={uid})");
                        }
                    }
                    catch { SafeRetentionLog($"Orphan outbox delete failed on {System.IO.Path.GetFileName(path)}"); }
                }
                if (deleted > 0) SafeRetentionLog($"Orphan outbox cleanup complete: filesDeleted={deleted}");
            }
            catch { }
        }

        public MessagePurgeSummary BurnConversationSecurely(string peerUid, string passphrase, bool useEnhancedBurn = false)
        {
            if (string.IsNullOrWhiteSpace(peerUid))
            {
                throw new ArgumentException("Peer UID is required for conversation burn.", nameof(peerUid));
            }

            if (string.IsNullOrWhiteSpace(passphrase))
            {
                throw new ArgumentException("Passphrase is required for secure burn.", nameof(passphrase));
            }

            var trimmed = Trim(peerUid);
            var sanitized = Sanitize(trimmed);

            int messageFiles = 0;
            int outboxFiles = 0;
            int messagesDeleted = 0;
            int queuedDeleted = 0;
            long bytesWiped = 0;

            try
            {
                var messagesPath = Path.Combine(AppDataPaths.Combine("messages"), sanitized + ".p2e");
                if (File.Exists(messagesPath))
                {
                    try
                    {
                        var existing = LoadMessagesFromFile(messagesPath, passphrase);
                        messagesDeleted += existing.Count;
                    }
                    catch { }

                    bytesWiped += useEnhancedBurn ? SecureBurnFileEnhanced(messagesPath) : SecureBurnFile(messagesPath);
                    messageFiles++;
                }

                var outboxPath = Path.Combine(AppDataPaths.Combine("outbox"), sanitized + ".p2e");
                if (File.Exists(outboxPath))
                {
                    try
                    {
                        var queued = LoadQueuedMessagesFromFile(outboxPath, passphrase);
                        queuedDeleted += queued.Count;
                    }
                    catch { }

                    bytesWiped += useEnhancedBurn ? SecureBurnFileEnhanced(outboxPath) : SecureBurnFile(outboxPath);
                    outboxFiles++;
                }

                if (bytesWiped > 0)
                {
                    var burnType = useEnhancedBurn ? "enhanced" : "standard";
                    SafeRetentionLog($"Conversation burn complete ({burnType}): peer={sanitized} messageFiles={messageFiles} outboxFiles={outboxFiles} bytes={bytesWiped}");
                }

                return new MessagePurgeSummary(messageFiles, outboxFiles, messagesDeleted, queuedDeleted, bytesWiped);
            }
            catch
            {
                SafeRetentionLog($"Conversation burn failed: peer={sanitized}");
                throw;
            }
        }

        public MessagePurgeSummary PurgeAllMessagesSecurely(string passphrase)
        {
            if (string.IsNullOrWhiteSpace(passphrase))
            {
                throw new ArgumentException("Passphrase is required for secure purge.", nameof(passphrase));
            }

            int messageFiles = 0;
            int outboxFiles = 0;
            int messagesDeleted = 0;
            int queuedDeleted = 0;
            long bytesWiped = 0;

            try
            {
                var messagesDir = AppDataPaths.Combine("messages");
                if (Directory.Exists(messagesDir))
                {
                    foreach (var path in Directory.GetFiles(messagesDir, "*.p2e", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            var items = LoadMessagesFromFile(path, passphrase);
                            messagesDeleted += items.Count;
                            bytesWiped += SecureWipeFile(path);
                            messageFiles++;
                        }
                        catch (Exception ex)
                        {
                            SafeRetentionLog($"Secure purge message file failed: {Path.GetFileName(path)} ({ex.Message})");
                            throw;
                        }
                    }
                    TryResetDirectory(messagesDir);
                }

                var legacyMessagesFile = AppDataPaths.Combine("messages.p2e");
                if (File.Exists(legacyMessagesFile))
                {
                    try
                    {
                        var items = LoadMessagesFromFile(legacyMessagesFile, passphrase);
                        messagesDeleted += items.Count;
                        bytesWiped += SecureWipeFile(legacyMessagesFile);
                        messageFiles++;
                    }
                    catch (Exception ex)
                    {
                        SafeRetentionLog($"Secure purge legacy message file failed: {Path.GetFileName(legacyMessagesFile)} ({ex.Message})");
                        throw;
                    }
                }

                var outboxDir = AppDataPaths.Combine("outbox");
                if (Directory.Exists(outboxDir))
                {
                    foreach (var path in Directory.GetFiles(outboxDir, "*.p2e", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            var queued = LoadQueuedMessagesFromFile(path, passphrase);
                            queuedDeleted += queued.Count;
                            bytesWiped += SecureWipeFile(path);
                            outboxFiles++;
                        }
                        catch (Exception ex)
                        {
                            SafeRetentionLog($"Secure purge outbox file failed: {Path.GetFileName(path)} ({ex.Message})");
                            throw;
                        }
                    }
                    TryResetDirectory(outboxDir);
                }

                var legacyOutboxFile = AppDataPaths.Combine("outbox.p2e");
                if (File.Exists(legacyOutboxFile))
                {
                    try
                    {
                        var queued = LoadQueuedMessagesFromFile(legacyOutboxFile, passphrase);
                        queuedDeleted += queued.Count;
                        bytesWiped += SecureWipeFile(legacyOutboxFile);
                        outboxFiles++;
                    }
                    catch (Exception ex)
                    {
                        SafeRetentionLog($"Secure purge legacy outbox file failed: {Path.GetFileName(legacyOutboxFile)} ({ex.Message})");
                        throw;
                    }
                }

                var summary = new MessagePurgeSummary(messageFiles, outboxFiles, messagesDeleted, queuedDeleted, bytesWiped);
                SafeRetentionLog($"Secure purge complete: messages={messageFiles}, outbox={outboxFiles}, wipedBytes={bytesWiped}");
                try { AppServices.Events.RaiseAllMessagesPurged(summary); } catch { }
                return summary;
            }
            catch
            {
                SafeRetentionLog("Secure purge aborted due to error.");
                throw;
            }
        }

    }
}

