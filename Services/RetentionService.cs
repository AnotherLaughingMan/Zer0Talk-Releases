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

    public class RetentionService
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

        private static string Trim(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            return s.StartsWith("usr-", StringComparison.Ordinal) ? s.Substring(4) : s;
        }

        private static string Sanitize(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return "unknown";
            return uid.Replace("/", "_").Replace("\\", "_");
        }

        private static List<Message> LoadMessagesFromFile(string path, string passphrase)
        {
            try
            {
                var p2e = new P2EContainer();
                var raw = p2e.LoadFile(path, passphrase);
                var json = System.Text.Encoding.UTF8.GetString(raw);
                var list = JsonSerializer.Deserialize<List<Message>>(json) ?? new List<Message>();
                return list;
            }
            catch { return new List<Message>(); }
        }

        private static List<QueuedMessage> LoadQueuedMessagesFromFile(string path, string passphrase)
        {
            try
            {
                var p2e = new P2EContainer();
                var raw = p2e.LoadFile(path, passphrase);
                var json = System.Text.Encoding.UTF8.GetString(raw);
                return JsonSerializer.Deserialize<List<QueuedMessage>>(json) ?? new List<QueuedMessage>();
            }
            catch { return new List<QueuedMessage>(); }
        }

        private static long SecureWipeFile(string path)
        {
            // Delegate to the new SecureFileWiper utility which handles drive detection
            return Zer0Talk.Utilities.SecureFileWiper.SecureWipeFile(path);
        }

        private static long SecureBurnFile(string path)
        {
            if (!File.Exists(path)) return 0;

            File.SetAttributes(path, FileAttributes.Normal);
            long length = new FileInfo(path).Length;
            if (length <= 0)
            {
                try { File.Delete(path); } catch { }
                return 0;
            }

            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            try
            {
                using var rng = RandomNumberGenerator.Create();
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, buffer.Length, FileOptions.WriteThrough | FileOptions.SequentialScan);
                stream.SetLength(length);

                OverwriteWithRandomBits(stream, rng, buffer, length);
                OverwriteWithLorem(stream, rng, buffer, length);
                OverwriteWithAlternatingBits(stream, buffer, length);
                OverwriteWithZeros(stream, buffer, length);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }

            try { File.SetAttributes(path, FileAttributes.Normal); } catch { }
            try { File.Delete(path); } catch { }
            return length;
        }

        private static long SecureBurnFileEnhanced(string path)
        {
            if (!File.Exists(path)) return 0;

            File.SetAttributes(path, FileAttributes.Normal);
            long length = new FileInfo(path).Length;
            if (length <= 0)
            {
                try { File.Delete(path); } catch { }
                return 0;
            }

            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            try
            {
                using var rng = RandomNumberGenerator.Create();
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, buffer.Length, FileOptions.WriteThrough | FileOptions.SequentialScan);
                stream.SetLength(length);

                // Enhanced 6-pass wipe: random, 0xFF, 0x00, random, 0xAA, 0x55
                OverwriteWithPattern(stream, rng, buffer, length, isRandom: true); // Pass 1: Random
                OverwriteWithPattern(stream, rng, buffer, length, fixedByte: 0xFF); // Pass 2: 0xFF
                OverwriteWithPattern(stream, rng, buffer, length, fixedByte: 0x00); // Pass 3: 0x00
                OverwriteWithPattern(stream, rng, buffer, length, isRandom: true); // Pass 4: Random
                OverwriteWithPattern(stream, rng, buffer, length, fixedByte: 0xAA); // Pass 5: 0xAA
                OverwriteWithPattern(stream, rng, buffer, length, fixedByte: 0x55); // Pass 6: 0x55
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }

            try { File.SetAttributes(path, FileAttributes.Normal); } catch { }
            try { File.Delete(path); } catch { }
            return length;
        }

        private static void OverwriteWithPattern(FileStream stream, RandomNumberGenerator rng, byte[] buffer, long length, bool isRandom = false, byte fixedByte = 0)
        {
            stream.Position = 0;
            long remaining = length;
            while (remaining > 0)
            {
                var chunk = (int)Math.Min(buffer.Length, remaining);
                var span = buffer.AsSpan(0, chunk);
                if (isRandom)
                {
                    rng.GetBytes(span);
                }
                else
                {
                    span.Fill(fixedByte);
                }
                stream.Write(span);
                remaining -= chunk;
            }
            stream.Flush(flushToDisk: true);
        }

        private static void OverwriteWithRandomBits(FileStream stream, RandomNumberGenerator rng, byte[] buffer, long length)
        {
            stream.Position = 0;
            long remaining = length;
            while (remaining > 0)
            {
                var chunk = (int)Math.Min(buffer.Length, remaining);
                var span = buffer.AsSpan(0, chunk);
                rng.GetBytes(span);
                for (int i = 0; i < chunk; i++)
                {
                    span[i] = (span[i] & 0x01) == 0 ? (byte)'0' : (byte)'1';
                }
                stream.Write(span);
                remaining -= chunk;
            }
            stream.Flush(flushToDisk: true);
        }

        private static void OverwriteWithLorem(FileStream stream, RandomNumberGenerator rng, byte[] buffer, long length)
        {
            const string Lorem = "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. ";
            var loremBytes = Encoding.UTF8.GetBytes(Lorem);
            if (loremBytes.Length == 0)
            {
                return;
            }

            stream.Position = 0;
            long remaining = length;
            var offset = RandomNumberGenerator.GetInt32(loremBytes.Length);
            while (remaining > 0)
            {
                var chunk = (int)Math.Min(buffer.Length, remaining);
                var span = buffer.AsSpan(0, chunk);
                for (int i = 0; i < chunk; i++)
                {
                    span[i] = loremBytes[offset];
                    offset++;
                    if (offset >= loremBytes.Length)
                    {
                        offset = RandomNumberGenerator.GetInt32(loremBytes.Length);
                    }
                }
                stream.Write(span);
                remaining -= chunk;
            }
            stream.Flush(flushToDisk: true);
        }

        private static void OverwriteWithAlternatingBits(FileStream stream, byte[] buffer, long length)
        {
            stream.Position = 0;
            long remaining = length;
            bool one = true;
            while (remaining > 0)
            {
                var chunk = (int)Math.Min(buffer.Length, remaining);
                var span = buffer.AsSpan(0, chunk);
                for (int i = 0; i < chunk; i++)
                {
                    span[i] = one ? (byte)'1' : (byte)'0';
                    one = !one;
                }
                stream.Write(span);
                remaining -= chunk;
            }
            stream.Flush(flushToDisk: true);
        }

        private static void OverwriteWithZeros(FileStream stream, byte[] buffer, long length)
        {
            stream.Position = 0;
            long remaining = length;
            while (remaining > 0)
            {
                var chunk = (int)Math.Min(buffer.Length, remaining);
                var span = buffer.AsSpan(0, chunk);
                span.Clear();
                stream.Write(span);
                remaining -= chunk;
            }
            stream.Flush(flushToDisk: true);
        }

        private static void SafeRetentionLog(string message)
        {
            try
            {
                var line = $"[RETENTION] {DateTime.Now:O}: {message}";
                if (Zer0Talk.Utilities.LoggingPaths.Enabled)
                    File.AppendAllText(Zer0Talk.Utilities.LoggingPaths.Retention, line + Environment.NewLine);
            }
            catch { }
        }

        // Debug method to test retention logging
        public static void TestRetentionLogging()
        {
            SafeRetentionLog("Test retention logging called - verifying log functionality");
        }

        private static void TryResetDirectory(string directory)
        {
            try
            {
                if (!Directory.Exists(directory)) return;
                var remaining = Directory.GetFiles(directory);
                foreach (var file in remaining)
                {
                    try { File.Delete(file); } catch { }
                }
            }
            catch { }
            finally
            {
                try { Directory.CreateDirectory(directory); } catch { }
            }
        }

        private static bool IsSimulatedPeer(string uid)
        {
            try
            {
                var norm = Trim(uid);
                var list = Zer0Talk.Services.AppServices.Contacts.Contacts;
                var c = list.FirstOrDefault(x => string.Equals(Trim(x.UID), norm, StringComparison.OrdinalIgnoreCase));
                return c?.IsSimulated == true;
            }
            catch { return false; }
        }
    }
}

