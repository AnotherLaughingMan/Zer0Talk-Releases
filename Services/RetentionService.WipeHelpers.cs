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
