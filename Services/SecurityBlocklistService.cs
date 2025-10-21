/*
    Security Blocklist Service: Pre-emptive blocking of known hostile actors and regions.
    
    IMPORTANT LEGAL & ETHICAL NOTICE:
    - This service implements defensive security measures against known threat actors
    - Geo-blocking is based on documented patterns of abuse, not discrimination
    - Users can disable geo-blocking in settings if desired
    - Hardcoded blocklists target specific hostile infrastructure, not individuals
    - All blocking is logged for transparency and audit purposes
*/

using System;
using System.Collections.Generic;
using System.Linq;
using Zer0Talk.Utilities;
using Zer0Talk.Models;

namespace Zer0Talk.Services
{
    public class SecurityBlocklistService
    {
        // [GEO-BLOCKING] Country codes with historically high rates of state-sponsored cyber attacks
        // Based on public threat intelligence reports from Microsoft, Google, Cloudflare, etc.
        // Source references: APT groups, state-sponsored hacking operations, botnet C&C infrastructure
        private static readonly HashSet<string> DEFAULT_BLOCKED_COUNTRIES = new(StringComparer.OrdinalIgnoreCase)
        {
            // NOTE: These defaults are DISABLED by default. Users must explicitly enable via settings.
            // Kept here for reference and optional use by security-conscious users.
            
            // North Korea - documented state cyber operations (Lazarus Group, APT38)
            // "KP",
            
            // Countries with high botnet/spam infrastructure (based on Spamhaus, abuse.ch data)
            // Note: Many legitimate users also come from these regions
            // This is why geo-blocking is OPT-IN and configurable
        };

        // [HARDCODED-BLOCKLIST] Known malicious infrastructure (requires strong evidence)
        // Only add entries with documented proof of persistent hostile behavior
        // Format: SHA256 hash of public key (Base64) or IP address
        private static readonly HashSet<string> HARDCODED_BLOCKLIST = new(StringComparer.Ordinal)
        {
            // Example format (DO NOT ADD WITHOUT VERIFICATION):
            // "ABC123...XYZ", // Description: [Date] [Incident] [Evidence URL]
            
            // Currently empty - requires community reporting mechanism and verification process
            // Future: Implement signed blocklist updates from trusted security researchers
        };

        // [IP-RANGES] Known malicious IP ranges (CIDR notation)
        // Source: Public threat feeds (abuse.ch, Spamhaus DROP/EDROP lists, etc.)
        private static readonly List<(string Cidr, string Reason)> BLOCKED_IP_RANGES = new()
        {
            // Example: ("192.0.2.0/24", "Reserved for documentation - should never be seen in production")
            
            // Future: Integrate with public threat feeds
            // Consider: Spamhaus DROP, FireHOL blocklists, abuse.ch feeds
        };

        /// <summary>
        /// Check if a country code should be blocked based on user settings
        /// </summary>
        public static bool IsCountryBlocked(string countryCode, AppSettings settings)
        {
            if (string.IsNullOrWhiteSpace(countryCode)) return false;
            if (!settings.EnableGeoBlocking) return false;
            
            try
            {
                // Check user-configured blocked countries
                if (settings.BlockedCountryCodes?.Contains(countryCode, StringComparer.OrdinalIgnoreCase) == true)
                {
                    if (settings.LogGeoBlockEvents)
                    {
                        Logger.Log($"[GEO-BLOCK] Connection blocked from country: {countryCode}");
                    }
                    return true;
                }
                
                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// Check if a public key fingerprint is on the hardcoded blocklist
        /// </summary>
        public static bool IsPublicKeyOnHardcodedBlocklist(string publicKeyFingerprint)
        {
            if (string.IsNullOrWhiteSpace(publicKeyFingerprint)) return false;
            
            try
            {
                if (HARDCODED_BLOCKLIST.Contains(publicKeyFingerprint))
                {
                    Logger.Log($"[SECURITY] ALERT: Hardcoded blocklist match detected! Fingerprint: {publicKeyFingerprint}");
                    Logger.Log($"[SECURITY] This connection attempt matches known hostile infrastructure.");
                    return true;
                }
                
                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// Check if an IP address is in a blocked range
        /// </summary>
        public static bool IsIpInBlockedRange(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress)) return false;
            if (BLOCKED_IP_RANGES.Count == 0) return false;
            
            try
            {
                if (!System.Net.IPAddress.TryParse(ipAddress, out var ip)) return false;
                
                foreach (var (cidr, reason) in BLOCKED_IP_RANGES)
                {
                    if (IsIpInCidrRange(ip, cidr))
                    {
                        Logger.Log($"[SECURITY] IP {ipAddress} blocked by range {cidr}: {reason}");
                        return true;
                    }
                }
                
                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// Derive country code from IP address (basic heuristic)
        /// NOTE: This is NOT accurate geolocation - just educated guesses based on IP ranges
        /// For production use, integrate with a proper GeoIP database (MaxMind, IP2Location, etc.)
        /// </summary>
        public static string? DeriveCountryCodeFromIp(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress)) return null;
            
            try
            {
                if (!System.Net.IPAddress.TryParse(ipAddress, out var ip)) return null;
                
                var bytes = ip.GetAddressBytes();
                if (bytes.Length != 4) return null; // Only IPv4 for now
                
                var firstOctet = bytes[0];
                
                // Private/Local networks - not geolocatable
                if (firstOctet == 10 || firstOctet == 127) return null;
                if (firstOctet == 192 && bytes[1] == 168) return null;
                if (firstOctet == 172 && bytes[1] >= 16 && bytes[1] <= 31) return null;
                
                // Basic heuristics for major regions (VERY approximate)
                // Real implementation should use GeoIP database
                // Format: 2-letter ISO 3166-1 alpha-2 code
                
                // This is intentionally left minimal because:
                // 1. IP-to-country mapping requires up-to-date databases
                // 2. Inaccurate geo-blocking causes false positives
                // 3. VPNs make this unreliable anyway
                
                // For now, return null (unknown) rather than guess wrong
                return null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Check if an IP address falls within a CIDR range
        /// </summary>
        private static bool IsIpInCidrRange(System.Net.IPAddress ip, string cidrNotation)
        {
            try
            {
                var parts = cidrNotation.Split('/');
                if (parts.Length != 2) return false;
                
                if (!System.Net.IPAddress.TryParse(parts[0], out var networkAddress)) return false;
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

        /// <summary>
        /// Get statistics about currently active blocklists
        /// </summary>
        public static (int HardcodedEntries, int IpRanges, int CountriesConfigured) GetBlocklistStats(AppSettings settings)
        {
            return (
                HARDCODED_BLOCKLIST.Count,
                BLOCKED_IP_RANGES.Count,
                settings.BlockedCountryCodes?.Count ?? 0
            );
        }

        /// <summary>
        /// Get a user-friendly explanation of geo-blocking status
        /// </summary>
        public static string GetGeoBlockingStatus(AppSettings settings)
        {
            if (!settings.EnableGeoBlocking)
                return "Disabled";
            
            var count = settings.BlockedCountryCodes?.Count ?? 0;
            if (count == 0)
                return "Enabled (no countries blocked)";
            
            return $"Enabled ({count} {(count == 1 ? "country" : "countries")} blocked)";
        }

        /// <summary>
        /// Validate and sanitize a country code
        /// </summary>
        public static bool IsValidCountryCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            code = code.Trim().ToUpperInvariant();
            
            // Must be exactly 2 letters (ISO 3166-1 alpha-2)
            if (code.Length != 2) return false;
            
            return code.All(c => c >= 'A' && c <= 'Z');
        }
    }
}

