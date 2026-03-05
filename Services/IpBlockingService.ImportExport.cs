using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using Zer0Talk.Models;
using Zer0Talk.Utilities;

namespace Zer0Talk.Services
{
    public partial class IpBlockingService
    {
        public async Task<int> ImportIpListFromFileAsync(string filePath)
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(filePath);
                var importedCount = 0;
                
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    
                    // Skip empty lines and comments
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith("//", StringComparison.Ordinal))
                        continue;
                    
                    // Check if it's a CIDR range or individual IP
                    if (trimmed.Contains('/'))
                    {
                        if (IsValidCidrFormat(trimmed))
                        {
                            AddBlockedIpRange(trimmed, false);
                            importedCount++;
                        }
                    }
                    else if (IPAddress.TryParse(trimmed, out _))
                    {
                        AddBadActorIp(trimmed, false);
                        importedCount++;
                    }
                }
                
                if (importedCount > 0)
                {
                    _settings.Save(Zer0Talk.Services.AppServices.Passphrase);
                    Logger.Log($"[IP-BLOCK] Imported {importedCount} entries from {filePath}");
                }
                
                return importedCount;
            }
            catch (Exception ex)
            {
                Logger.Log($"[IP-BLOCK] Error importing from {filePath}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Export current IP block lists to a text file
        /// </summary>
        public async Task ExportIpListToFileAsync(string filePath)
        {
            try
            {
                var settings = _settings.Settings;
                var lines = new List<string>
                {
                    $"# Zer0Talk IP Block List - Exported {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    "# Lines starting with # are comments",
                    "# Individual IP addresses:",
                    ""
                };
                
                // Add individual IPs
                if (settings.BlockedIpAddresses?.Count > 0)
                {
                    lines.AddRange(settings.BlockedIpAddresses);
                    lines.Add("");
                }
                
                if (settings.CustomBadActorIps?.Count > 0)
                {
                    lines.Add("# Custom bad actor IPs:");
                    lines.AddRange(settings.CustomBadActorIps);
                    lines.Add("");
                }
                
                // Add CIDR ranges
                if (settings.BlockedIpRanges?.Count > 0)
                {
                    lines.Add("# CIDR ranges:");
                    lines.AddRange(settings.BlockedIpRanges);
                }
                
                await File.WriteAllLinesAsync(filePath, lines);
                Logger.Log($"[IP-BLOCK] Exported IP lists to {filePath}");
            }
            catch (Exception ex)
            {
                Logger.Log($"[IP-BLOCK] Error exporting to {filePath}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Get statistics about current IP blocking
        /// </summary>
        public (int IndividualIps, int CustomBadActorIps, int CidrRanges) GetBlockingStats()
        {
            var settings = _settings.Settings;
            return (
                settings.BlockedIpAddresses?.Count ?? 0,
                settings.CustomBadActorIps?.Count ?? 0,
                settings.BlockedIpRanges?.Count ?? 0
            );
        }



        #region Private Methods

        private bool IsIpInBlockedRanges(string ipAddress, List<string>? cidrRanges)
        {
            if (cidrRanges == null || cidrRanges.Count == 0) return false;
            if (!IPAddress.TryParse(ipAddress, out var ip)) return false;
            
            foreach (var cidr in cidrRanges)
            {
                if (IsIpInCidrRange(ip, cidr)) return true;
            }
            
            return false;
        }

        private bool IsIpInCidrRange(IPAddress ip, string cidrNotation)
        {
            try
            {
                var parts = cidrNotation.Split('/');
                if (parts.Length != 2) return false;
                
                if (!IPAddress.TryParse(parts[0], out var networkAddress)) return false;
                if (!int.TryParse(parts[1], out var prefixLength)) return false;
                
                if (ip.AddressFamily != networkAddress.AddressFamily) return false;
                
                var ipBytes = ip.GetAddressBytes();
                var networkBytes = networkAddress.GetAddressBytes();
                
                if (ipBytes.Length != networkBytes.Length) return false;
                
                var maskBytes = prefixLength / 8;
                var maskBits = prefixLength % 8;
                
                // Check full bytes
                for (int i = 0; i < maskBytes; i++)
                {
                    if (ipBytes[i] != networkBytes[i]) return false;
                }
                
                // Check partial byte if exists
                if (maskBits > 0 && maskBytes < ipBytes.Length)
                {
                    var mask = (byte)(0xFF << (8 - maskBits));
                    if ((ipBytes[maskBytes] & mask) != (networkBytes[maskBytes] & mask))
                        return false;
                }
                
                return true;
            }
            catch { return false; }
        }

        private bool IsValidCidrFormat(string cidr)
        {
            try
            {
                var parts = cidr.Split('/');
                if (parts.Length != 2) return false;
                
                if (!IPAddress.TryParse(parts[0], out var ip)) return false;
                if (!int.TryParse(parts[1], out var prefix)) return false;
                
                var maxPrefix = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
                return prefix >= 0 && prefix <= maxPrefix;
            }
            catch { return false; }
        }



        /// <summary>
        /// Initialize the IP blocking service with the default embedded blocklist
        /// </summary>
        public async Task InitializeDefaultBlocklistAsync()
        {
            try
            {
                // Load embedded default blocklist
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "Zer0Talk.Assets.Security.default-ip-blocklist.txt";
                
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    var content = await reader.ReadToEndAsync();
                    
                    // Parse content line by line (similar to ImportIpListFromFileAsync)
                    var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    var importedCount = 0;
                    
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        
                        // Skip empty lines and comments
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith("//", StringComparison.Ordinal))
                            continue;
                        
                        // Check if it's a CIDR range or individual IP
                        if (trimmed.Contains('/'))
                        {
                            if (IsValidCidrFormat(trimmed))
                            {
                                AddBlockedIpRange(trimmed, false);
                                importedCount++;
                            }
                        }
                        else if (IPAddress.TryParse(trimmed, out _))
                        {
                            AddBadActorIp(trimmed, false);
                            importedCount++;
                        }
                    }
                    
                    // Save to persistent storage if any entries were loaded
                    if (importedCount > 0)
                    {
                        _settings.Save(Zer0Talk.Services.AppServices.Passphrase);
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"Loaded {importedCount} IP ranges from default blocklist");
                }
            }
            catch (Exception ex)
            {
                // Log but don't throw - this is initialization, app should still work without default list
                System.Diagnostics.Debug.WriteLine($"Warning: Failed to load default IP blocklist: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear all bad actor IP addresses (individual IPs only, not ranges)
        /// </summary>
        public void ClearAllBadActorIps()
        {
            try
            {
                var settings = _settings.Settings;
                var removedCount = 0;
                
                // Clear BlockedIpAddresses
                if (settings.BlockedIpAddresses?.Count > 0)
                {
                    removedCount += settings.BlockedIpAddresses.Count;
                    settings.BlockedIpAddresses.Clear();
                }
                
                // Clear CustomBadActorIps  
                if (settings.CustomBadActorIps?.Count > 0)
                {
                    removedCount += settings.CustomBadActorIps.Count;
                    settings.CustomBadActorIps.Clear();
                }
                
                if (removedCount > 0)
                {
                    _settings.Save(Zer0Talk.Services.AppServices.Passphrase);
                    Logger.Log($"[IP-BLOCK] Cleared {removedCount} bad actor IP addresses");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[IP-BLOCK] Error clearing bad actor IPs: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Clear all blocked IP ranges (CIDR blocks only, not individual IPs)
        /// </summary>
        public void ClearAllBlockedRanges()
        {
            try
            {
                var settings = _settings.Settings;
                var removedCount = 0;
                
                // Clear BlockedIpRanges
                if (settings.BlockedIpRanges?.Count > 0)
                {
                    removedCount = settings.BlockedIpRanges.Count;
                    settings.BlockedIpRanges.Clear();
                    
                    _settings.Save(Zer0Talk.Services.AppServices.Passphrase);
                    Logger.Log($"[IP-BLOCK] Cleared {removedCount} blocked IP ranges");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[IP-BLOCK] Error clearing blocked IP ranges: {ex.Message}");
                throw;
            }
        }

        #endregion
    }
}
