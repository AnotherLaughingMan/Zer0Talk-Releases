/*
    IP Blocking Service: Bad-actor IP blocking with user-provided lists.
    
    Features:
    - Individual IP address blocking
    - CIDR range blocking  
    - Custom user-defined IP blocks
    - Import from security provider lists (Spamhaus, FireHOL, abuse.ch, etc.)
    - Export/backup functionality
    - Integration with existing SecurityBlocklistService
*/

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
    public class IpBlockingService
    {
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
        private readonly SettingsService _settings;
        
        // Note: Users provide their own IP block lists from various sources
        // Common sources: Spamhaus, FireHOL, abuse.ch, various security vendors

        public IpBlockingService(SettingsService settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Check if an IP address should be blocked
        /// </summary>
        public bool IsIpBlocked(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress)) return false;
            
            try
            {
                var settings = _settings.Settings;
                
                // Check direct IP blocks
                if (settings.BlockedIpAddresses?.Contains(ipAddress) == true)
                {
                    Logger.Log($"[IP-BLOCK] Direct IP block: {ipAddress}");
                    return true;
                }
                
                // Check custom bad actor IPs
                if (settings.CustomBadActorIps?.Contains(ipAddress) == true)
                {
                    Logger.Log($"[IP-BLOCK] Custom bad actor IP: {ipAddress}");
                    return true;
                }
                
                // Check CIDR ranges
                if (IsIpInBlockedRanges(ipAddress, settings.BlockedIpRanges))
                {
                    Logger.Log($"[IP-BLOCK] IP in blocked range: {ipAddress}");
                    return true;
                }
                
                // Check hardcoded security blocklist
                if (SecurityBlocklistService.IsIpInBlockedRange(ipAddress))
                {
                    Logger.Log($"[IP-BLOCK] Security blocklist match: {ipAddress}");
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Logger.Log($"[IP-BLOCK] Error checking IP {ipAddress}: {ex.Message}");
                return false; // Fail open for availability
            }
        }

        /// <summary>
        /// Add an IP address to the custom bad actor list
        /// </summary>
        public void AddBadActorIp(string ipAddress, bool save = true)
        {
            if (string.IsNullOrWhiteSpace(ipAddress)) return;
            
            try
            {
                if (!IPAddress.TryParse(ipAddress, out _))
                {
                    throw new ArgumentException($"Invalid IP address format: {ipAddress}");
                }
                
                var settings = _settings.Settings;
                settings.CustomBadActorIps ??= new HashSet<string>();
                
                if (settings.CustomBadActorIps.Add(ipAddress))
                {
                    Logger.Log($"[IP-BLOCK] Added bad actor IP: {ipAddress}");
                    if (save) _settings.Save(Zer0Talk.Services.AppServices.Passphrase);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[IP-BLOCK] Error adding bad actor IP {ipAddress}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Remove an IP address from the custom bad actor list
        /// </summary>
        public void RemoveBadActorIp(string ipAddress, bool save = true)
        {
            if (string.IsNullOrWhiteSpace(ipAddress)) return;
            
            try
            {
                var settings = _settings.Settings;
                if (settings.CustomBadActorIps?.Remove(ipAddress) == true)
                {
                    Logger.Log($"[IP-BLOCK] Removed bad actor IP: {ipAddress}");
                    if (save) _settings.Save(Zer0Talk.Services.AppServices.Passphrase);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[IP-BLOCK] Error removing bad actor IP {ipAddress}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Add a CIDR range to blocked ranges
        /// </summary>
        public void AddBlockedIpRange(string cidrRange, bool save = true)
        {
            if (string.IsNullOrWhiteSpace(cidrRange)) return;
            
            try
            {
                // Validate CIDR format
                if (!IsValidCidrFormat(cidrRange))
                {
                    throw new ArgumentException($"Invalid CIDR format: {cidrRange}");
                }
                
                var settings = _settings.Settings;
                settings.BlockedIpRanges ??= new List<string>();
                
                if (!settings.BlockedIpRanges.Contains(cidrRange))
                {
                    settings.BlockedIpRanges.Add(cidrRange);
                    Logger.Log($"[IP-BLOCK] Added CIDR range: {cidrRange}");
                    if (save) _settings.Save(Zer0Talk.Services.AppServices.Passphrase);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[IP-BLOCK] Error adding CIDR range {cidrRange}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Remove a CIDR range from blocked ranges
        /// </summary>
        public void RemoveBlockedIpRange(string cidrRange, bool save = true)
        {
            try
            {
                var settings = _settings.Settings;
                if (settings.BlockedIpRanges?.Remove(cidrRange) == true)
                {
                    Logger.Log($"[IP-BLOCK] Removed CIDR range: {cidrRange}");
                    if (save) _settings.Save(Zer0Talk.Services.AppServices.Passphrase);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[IP-BLOCK] Error removing CIDR range {cidrRange}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Manual refresh of IP lists from settings (no external feeds)
        /// </summary>
        public void RefreshIpLists()
        {
            try
            {
                Logger.Log("[IP-BLOCK] Refreshing IP block lists from settings");
                // This method can be used to trigger UI refresh or validation
                // All IPs are managed through manual addition and file import only
            }
            catch (Exception ex)
            {
                Logger.Log($"[IP-BLOCK] Error refreshing IP lists: {ex.Message}");
            }
        }

        /// <summary>
        /// Import IP addresses from a text file
        /// </summary>
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
