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
    public partial class IpBlockingService
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
    }
}
