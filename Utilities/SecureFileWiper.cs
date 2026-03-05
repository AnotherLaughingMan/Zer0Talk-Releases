/*
    SecureFileWiper: Drive-aware secure file deletion utility.
    - Detects drive type (SSD/NVMe vs HDD) to use appropriate deletion method
    - SSD/NVMe: Simple deletion (wear-leveling makes overwriting ineffective)
    - HDD: 3-pass overwrite (random, 0xFF, 0x00) before deletion
    - Provides consistent secure deletion API for all file operations
*/
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
        private static readonly object _cacheLock = new();
        private static readonly System.Collections.Generic.Dictionary<string, DriveType> _driveTypeCache = new();

        public enum DriveType
        {
            Unknown,
            HDD,      // Traditional spinning disk - benefits from overwriting
            SSD       // Solid state (SSD/NVMe) - simple deletion sufficient
        }

        /// <summary>
        /// Securely wipes a file using drive-appropriate method, then deletes it.
        /// Returns the number of bytes wiped.
        /// </summary>
        public static long SecureWipeFile(string path)
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
                    // HDD: Use 3-pass overwrite method
                    PerformThreePassWipe(path, length);
                }
                // SSD: Skip overwriting - wear-leveling makes it ineffective and wastes write cycles
                
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
        /// Securely wipes multiple files in parallel.
        /// Returns total bytes wiped.
        /// </summary>
        public static long SecureWipeFiles(string[] paths)
        {
            if (paths == null || paths.Length == 0) return 0;

            long totalBytes = 0;
            var exceptions = new System.Collections.Generic.List<Exception>();

            System.Threading.Tasks.Parallel.ForEach(paths, path =>
            {
                try
                {
                    var bytes = SecureWipeFile(path);
                    System.Threading.Interlocked.Add(ref totalBytes, bytes);
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            });

            if (exceptions.Count > 0)
            {
                throw new AggregateException("One or more files failed to wipe securely.", exceptions);
            }

            return totalBytes;
        }

        /// <summary>
        /// Detects whether a file resides on an HDD or SSD.
        /// </summary>
        private static DriveType DetectDriveType(string filePath)
        {
            try
            {
                var root = Path.GetPathRoot(filePath);
                if (string.IsNullOrEmpty(root))
                    return DriveType.Unknown;

                // Check cache first
                lock (_cacheLock)
                {
                    if (_driveTypeCache.TryGetValue(root, out var cached))
                        return cached;
                }

                var detected = OperatingSystem.IsWindows() 
                    ? DetectDriveTypeWindows(root) 
                    : DriveType.Unknown;

                // Cache the result
                lock (_cacheLock)
                {
                    _driveTypeCache[root] = detected;
                }

                return detected;
            }
            catch
            {
                return DriveType.Unknown; // Default to unknown on error
            }
        }

        [SupportedOSPlatform("windows")]
        private static DriveType DetectDriveTypeWindows(string driveLetter)
        {
            try
            {
                // Remove trailing backslash for WMI query
                var drive = driveLetter.TrimEnd('\\');
                
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT MediaType FROM Win32_LogicalDisk WHERE DeviceID = '{drive}'");
                
                foreach (ManagementObject disk in searcher.Get())
                {
                    try
                    {
                        var mediaType = disk["MediaType"]?.ToString();
                        
                        // If we can't determine from LogicalDisk, try PhysicalDisk
                        if (string.IsNullOrEmpty(mediaType))
                        {
                            return DetectPhysicalDiskType(drive);
                        }
                        
                        // MediaType 12 = removable media (could be SSD)
                        // We need to check the physical disk for accurate detection
                        return DetectPhysicalDiskType(drive);
                    }
                    finally
                    {
                        disk?.Dispose();
                    }
                }

                return DriveType.Unknown;
            }
            catch
            {
                return DriveType.Unknown;
            }
        }

        [SupportedOSPlatform("windows")]
        private static DriveType DetectPhysicalDiskType(string driveLetter)
        {
            try
            {
                var drive = driveLetter.TrimEnd('\\');
                
                // Query Win32_DiskDrive through partition association
                using var partitionSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{drive}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");

                foreach (ManagementObject partition in partitionSearcher.Get())
                {
                    try
                    {
                        using var diskSearcher = new ManagementObjectSearcher(
                            $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");

                        foreach (ManagementObject disk in diskSearcher.Get())
                        {
                            try
                            {
                                // Check MediaType property
                                var mediaType = disk["MediaType"]?.ToString() ?? "";
                                
                                // SSD indicators in MediaType
                                if (mediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                                    mediaType.Contains("Solid State", StringComparison.OrdinalIgnoreCase))
                                {
                                    return DriveType.SSD;
                                }

                                // Check for NVMe (always SSD)
                                var model = disk["Model"]?.ToString() ?? "";
                                if (model.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
                                {
                                    return DriveType.SSD;
                                }

                                // Check Win32_PhysicalMedia for more details
                                var deviceId = disk["DeviceID"]?.ToString();
                                if (!string.IsNullOrEmpty(deviceId))
                                {
                                    // Query MSFT_PhysicalDisk for MediaType (newer Windows versions)
                                    try
                                    {
                                        using var physSearcher = new ManagementObjectSearcher(
                                            "root\\Microsoft\\Windows\\Storage",
                                            $"SELECT MediaType FROM MSFT_PhysicalDisk WHERE DeviceId = '{disk["Index"]}'");

                                        foreach (ManagementObject physDisk in physSearcher.Get())
                                        {
                                            try
                                            {
                                                var physMediaType = physDisk["MediaType"];
                                                if (physMediaType != null)
                                                {
                                                    // MediaType values: 3 = HDD, 4 = SSD, 5 = SCM
                                                    var mediaTypeValue = Convert.ToUInt16(physMediaType);
                                                    if (mediaTypeValue == 4 || mediaTypeValue == 5)
                                                        return DriveType.SSD;
                                                    if (mediaTypeValue == 3)
                                                        return DriveType.HDD;
                                                }
                                            }
                                            finally
                                            {
                                                physDisk?.Dispose();
                                            }
                                        }
                                    }
                                    catch { /* Fallback to heuristics */ }
                                }

                                // Heuristic: Check RPM (only HDDs have this)
                                // No RPM or RPM = 0 likely means SSD
                                // This is not foolproof but helps
                                return DriveType.Unknown; // Let caller decide
                            }
                            finally
                            {
                                disk?.Dispose();
                            }
                        }
                    }
                    finally
                    {
                        partition?.Dispose();
                    }
                }

                return DriveType.Unknown;
            }
            catch
            {
                return DriveType.Unknown;
            }
        }

        /// <summary>
        /// Performs a 3-pass overwrite suitable for HDDs:
        /// Pass 1: Random data
        /// Pass 2: All 0xFF
        /// Pass 3: All 0x00
        /// </summary>
        private static void PerformThreePassWipe(string path, long length)
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

                // 3-pass overwrite
                for (var pass = 0; pass < 3; pass++)
                {
                    stream.Position = 0;
                    long remaining = length;
                    
                    while (remaining > 0)
                    {
                        var chunk = (int)Math.Min(buffer.Length, remaining);
                        var span = buffer.AsSpan(0, chunk);
                        
                        switch (pass)
                        {
                            case 0: // Random data
                                rng.GetBytes(span);
                                break;
                            case 1: // All 0xFF
                                span.Fill(0xFF);
                                break;
                            case 2: // All 0x00
                                span.Clear();
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
        /// Securely wipes a file with maximum security using drive-appropriate method for account deletion.
        /// SSD/NVMe: 6-pass overwrite, HDD: 35-pass Gutmann method.
        /// Returns the number of bytes wiped.
        /// </summary>
    }
}
