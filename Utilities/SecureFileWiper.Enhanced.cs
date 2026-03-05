using System;
using System.Buffers;
using System.IO;
using System.Management;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Zer0Talk.Utilities
{
    public static partial class SecureFileWiper
    {
        public static long SecureWipeFileMaximum(string path)
        {
            if (!File.Exists(path)) return 0;

            try
            {
                File.SetAttributes(path, FileAttributes.Normal);
                var length = new FileInfo(path).Length;
                
                if (length <= 0)
                {
                    File.Delete(path);
                    return 0;
                }

                var driveType = DetectDriveType(path);
                
                if (driveType == DriveType.HDD)
                {
                    // HDD: Use 35-pass Gutmann method
                    PerformGutmannWipe(path, length);
                }
                else
                {
                    // SSD/NVMe/Unknown: Use 6-pass overwrite
                    PerformSixPassWipe(path, length);
                }
                
                // Delete the file
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                
                return length;
            }
            catch
            {
                try { File.SetAttributes(path, FileAttributes.Normal); } catch { }
                throw;
            }
        }

        /// <summary>
        /// Securely wipes a directory and all its contents recursively using maximum security method.
        /// Returns total bytes wiped.
        /// </summary>
        public static long SecureWipeDirectoryMaximum(string directoryPath)
        {
            if (!Directory.Exists(directoryPath)) return 0;

            long totalBytes = 0;

            try
            {
                // Recursively process subdirectories first
                foreach (var subDir in Directory.GetDirectories(directoryPath))
                {
                    totalBytes += SecureWipeDirectoryMaximum(subDir);
                }

                // Wipe all files in this directory
                foreach (var file in Directory.GetFiles(directoryPath))
                {
                    try
                    {
                        totalBytes += SecureWipeFileMaximum(file);
                    }
                    catch { /* Continue wiping other files */ }
                }

                // Remove the directory itself
                try
                {
                    Directory.Delete(directoryPath, false); // Don't recurse since we already cleared it
                }
                catch { }
            }
            catch { }

            return totalBytes;
        }

        /// <summary>
        /// Performs a 6-pass overwrite suitable for SSDs and maximum security deletion:
        /// Pass 1: Random data
        /// Pass 2: All 0xFF
        /// Pass 3: All 0x00
        /// Pass 4: Random data
        /// Pass 5: All 0xAA (alternating bits: 10101010)
        /// Pass 6: All 0x55 (alternating bits: 01010101)
        /// </summary>
        private static void PerformSixPassWipe(string path, long length)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            try
            {
                using var rng = RandomNumberGenerator.Create();
                using var stream = new FileStream(
                    path, 
                    FileMode.Open, 
                    FileAccess.Write, 
                    FileShare.None, 
                    buffer.Length, 
                    FileOptions.WriteThrough | FileOptions.SequentialScan);

                for (var pass = 0; pass < 6; pass++)
                {
                    stream.Position = 0;
                    long remaining = length;
                    
                    while (remaining > 0)
                    {
                        var chunk = (int)Math.Min(buffer.Length, remaining);
                        var span = buffer.AsSpan(0, chunk);
                        
                        switch (pass)
                        {
                            case 0:
                            case 3: // Random data (passes 1 and 4)
                                rng.GetBytes(span);
                                break;
                            case 1: // All 0xFF
                                span.Fill(0xFF);
                                break;
                            case 2: // All 0x00
                                span.Clear();
                                break;
                            case 4: // All 0xAA (10101010)
                                span.Fill(0xAA);
                                break;
                            case 5: // All 0x55 (01010101)
                                span.Fill(0x55);
                                break;
                        }
                        
                        stream.Write(span);
                        remaining -= chunk;
                    }
                    
                    stream.Flush(flushToDisk: true);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }

        /// <summary>
        /// Performs the 35-pass Gutmann method for maximum HDD security.
        /// This method is overkill for modern drives but provides the highest level of data destruction.
        /// </summary>
        private static void PerformGutmannWipe(string path, long length)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            try
            {
                using var rng = RandomNumberGenerator.Create();
                using var stream = new FileStream(
                    path, 
                    FileMode.Open, 
                    FileAccess.Write, 
                    FileShare.None, 
                    buffer.Length, 
                    FileOptions.WriteThrough | FileOptions.SequentialScan);

                // Gutmann method: 35 passes
                for (var pass = 0; pass < 35; pass++)
                {
                    stream.Position = 0;
                    long remaining = length;
                    
                    while (remaining > 0)
                    {
                        var chunk = (int)Math.Min(buffer.Length, remaining);
                        var span = buffer.AsSpan(0, chunk);
                        
                        // Gutmann pattern selection
                        if (pass >= 0 && pass <= 3)
                        {
                            // Passes 1-4: Random
                            rng.GetBytes(span);
                        }
                        else if (pass >= 4 && pass <= 30)
                        {
                            // Passes 5-31: Specific patterns
                            byte pattern = GetGutmannPattern(pass - 4);
                            span.Fill(pattern);
                        }
                        else
                        {
                            // Passes 32-35: Random
                            rng.GetBytes(span);
                        }
                        
                        stream.Write(span);
                        remaining -= chunk;
                    }
                    
                    stream.Flush(flushToDisk: true);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }

        /// <summary>
        /// Returns Gutmann method patterns for passes 5-31
        /// </summary>
        private static byte GetGutmannPattern(int patternIndex)
        {
            // Simplified Gutmann patterns - alternating and specific bit patterns
            byte[] patterns = {
                0x55, 0xAA, 0x92, 0x49, 0x24, 0x00, 0x11, 0x22, 0x33,
                0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC,
                0xDD, 0xEE, 0xFF, 0x92, 0x49, 0x24, 0x6D, 0xB6, 0xDB
            };
            
            return patterns[patternIndex % patterns.Length];
        }

        /// <summary>
        /// Clears the drive type cache (useful for testing or if drives change)
        /// </summary>
        public static void ClearCache()
        {
            lock (_cacheLock)
            {
                _driveTypeCache.Clear();
            }
        }
    }
}
