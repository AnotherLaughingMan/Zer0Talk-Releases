/*
    IP Country Detection Utility: Maps IP addresses and ranges to country codes.
    
    Features:
    - 3-letter country code detection for individual IPs and CIDR ranges
    - Full country name tooltips
    - Built-in mapping for known country IP ranges
    - Support for the IP ranges we have in our blocklist
*/

using System;
using System.Collections.Generic;
using System.Net;

namespace ZTalk.Utilities
{
    public static class IpCountryDetector
    {
        // Country code mappings with full names for tooltips
        private static readonly Dictionary<string, string> CountryNames = new()
        {
            { "CHN", "China" },
            { "RUS", "Russia" },
            { "IND", "India" },
            { "PRK", "North Korea" },
            { "USA", "United States" },
            { "GBR", "United Kingdom" },
            { "DEU", "Germany" },
            { "JPN", "Japan" },
            { "FRA", "France" },
            { "CAN", "Canada" },
            { "AUS", "Australia" },
            { "KOR", "South Korea" },
            { "NLD", "Netherlands" },
            { "SGP", "Singapore" },
            { "HKG", "Hong Kong" },
            { "TWN", "Taiwan" },
            { "BRA", "Brazil" },
            { "MEX", "Mexico" },
            { "ZAF", "South Africa" },
            { "EGY", "Egypt" },
            { "TUR", "Turkey" },
            { "ARE", "United Arab Emirates" },
            { "SAU", "Saudi Arabia" },
            { "IRN", "Iran" },
            { "IRQ", "Iraq" },
            { "AFG", "Afghanistan" },
            { "PAK", "Pakistan" },
            { "BGD", "Bangladesh" },
            { "LKA", "Sri Lanka" },
            { "NPL", "Nepal" },
            { "BTN", "Bhutan" },
            { "MDV", "Maldives" },
            { "UNK", "Unknown" },
            { "PVT", "Private Network" },
            { "LOC", "Localhost" },
            { "DOC", "Documentation Range" }
        };

        // Simplified IP range mappings for our known country blocks
        // In a real implementation, you'd use a comprehensive IP geolocation database
        private static readonly List<(string Network, int PrefixLength, string CountryCode)> CountryRanges = new()
        {
            // China - major known ranges (simplified for demo)
            ("1.0.0.0", 8, "CHN"),
            ("14.0.0.0", 12, "CHN"),
            ("27.0.0.0", 8, "CHN"),
            ("36.0.0.0", 7, "CHN"),
            ("39.0.0.0", 8, "CHN"),
            ("42.0.0.0", 7, "CHN"),
            ("58.0.0.0", 7, "CHN"),
            ("60.0.0.0", 6, "CHN"),
            ("101.0.0.0", 8, "CHN"),
            ("103.0.0.0", 8, "CHN"),
            ("106.0.0.0", 7, "CHN"),
            ("110.0.0.0", 6, "CHN"),
            ("114.0.0.0", 7, "CHN"),
            ("116.0.0.0", 6, "CHN"),
            ("120.0.0.0", 6, "CHN"),
            ("124.0.0.0", 6, "CHN"),
            ("139.0.0.0", 8, "CHN"),
            ("140.0.0.0", 6, "CHN"),
            ("144.0.0.0", 7, "CHN"),
            ("150.0.0.0", 8, "CHN"),
            ("153.0.0.0", 8, "CHN"),
            ("163.0.0.0", 8, "CHN"),
            ("171.0.0.0", 8, "CHN"),
            ("175.0.0.0", 8, "CHN"),
            ("180.76.0.0", 14, "CHN"),
            ("182.0.0.0", 7, "CHN"),
            ("202.0.0.0", 8, "CHN"),
            ("210.0.0.0", 7, "CHN"),
            ("218.0.0.0", 6, "CHN"),
            ("222.0.0.0", 7, "CHN"),

            // Russia - major known ranges
            ("2.60.0.0", 14, "RUS"),
            ("2.92.0.0", 14, "RUS"),
            ("5.1.0.0", 16, "RUS"),
            ("5.16.0.0", 14, "RUS"),
            ("37.0.0.0", 8, "RUS"),
            ("46.0.0.0", 8, "RUS"),
            ("62.76.0.0", 14, "RUS"),
            ("77.88.0.0", 13, "RUS"),
            ("78.108.0.0", 14, "RUS"),
            ("79.110.0.0", 15, "RUS"),
            ("80.64.0.0", 12, "RUS"),
            ("81.0.0.0", 8, "RUS"),
            ("82.0.0.0", 8, "RUS"),
            ("83.0.0.0", 8, "RUS"),
            ("84.0.0.0", 8, "RUS"),
            ("85.0.0.0", 8, "RUS"),
            ("86.0.0.0", 8, "RUS"),
            ("87.0.0.0", 8, "RUS"),
            ("88.0.0.0", 8, "RUS"),
            ("89.0.0.0", 8, "RUS"),
            ("90.0.0.0", 8, "RUS"),
            ("91.0.0.0", 8, "RUS"),
            ("92.0.0.0", 8, "RUS"),
            ("93.0.0.0", 8, "RUS"),
            ("94.0.0.0", 8, "RUS"),
            ("95.24.0.0", 13, "RUS"),
            ("176.0.0.0", 8, "RUS"),
            ("178.0.0.0", 8, "RUS"),
            ("188.0.0.0", 8, "RUS"),
            ("213.0.0.0", 8, "RUS"),

            // India - major known ranges  
            ("1.6.0.0", 15, "IND"),
            ("1.22.0.0", 15, "IND"),
            ("1.38.0.0", 15, "IND"),
            ("14.96.0.0", 11, "IND"),
            ("27.34.0.0", 15, "IND"),
            ("43.224.0.0", 11, "IND"),
            ("49.14.0.0", 15, "IND"),
            ("103.0.0.0", 8, "IND"),
            ("106.0.0.0", 8, "IND"),
            ("117.0.0.0", 8, "IND"),
            ("121.0.0.0", 8, "IND"),
            ("125.0.0.0", 8, "IND"),
            ("157.0.0.0", 8, "IND"),
            ("163.47.0.0", 16, "IND"),
            ("171.0.0.0", 8, "IND"),
            ("182.0.0.0", 8, "IND"),
            ("183.82.0.0", 15, "IND"),
            ("202.0.0.0", 8, "IND"),
            ("203.0.0.0", 8, "IND"),

            // North Korea - known ranges
            ("57.73.214.0", 23, "PRK"),
            ("175.45.176.0", 22, "PRK"),
            ("202.72.96.4", 30, "PRK"),
        };

        /// <summary>
        /// Detect country code for an IP address or CIDR range
        /// </summary>
        public static string DetectCountryCode(string ipOrRange)
        {
            if (string.IsNullOrWhiteSpace(ipOrRange))
                return "UNK";

            try
            {
                // Handle CIDR ranges
                if (ipOrRange.Contains('/'))
                {
                    var parts = ipOrRange.Split('/');
                    if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var networkIp))
                    {
                        return DetectCountryForIp(networkIp);
                    }
                }
                // Handle individual IPs
                else if (IPAddress.TryParse(ipOrRange, out var ip))
                {
                    return DetectCountryForIp(ip);
                }
            }
            catch
            {
                // Fallback for any parsing errors
            }

            return "UNK";
        }

        /// <summary>
        /// Get full country name for tooltip
        /// </summary>
        public static string GetCountryName(string countryCode)
        {
            return CountryNames.TryGetValue(countryCode ?? "UNK", out var name) ? name : "Unknown";
        }

        /// <summary>
        /// Detect country for a specific IP address
        /// </summary>
        private static string DetectCountryForIp(IPAddress ip)
        {
            if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                return "UNK";

            var ipBytes = ip.GetAddressBytes();
            var ipInt = BitConverter.ToUInt32(ipBytes.AsSpan());
            if (BitConverter.IsLittleEndian)
                ipInt = ReverseBytes(ipInt);

            // Check against our known country ranges
            foreach (var (network, prefixLength, countryCode) in CountryRanges)
            {
                if (IPAddress.TryParse(network, out var networkAddr))
                {
                    var networkBytes = networkAddr.GetAddressBytes();
                    var networkInt = BitConverter.ToUInt32(networkBytes.AsSpan());
                    if (BitConverter.IsLittleEndian)
                        networkInt = ReverseBytes(networkInt);

                    var mask = ~(0xFFFFFFFFU >> prefixLength);
                    
                    if ((ipInt & mask) == (networkInt & mask))
                    {
                        return countryCode;
                    }
                }
            }

            // Check for common special ranges
            if (IsPrivateRange(ip))
                return "PVT"; // Private range
            if (IsLoopback(ip))
                return "LOC"; // Localhost
            if (IsDocumentationRange(ip))
                return "DOC"; // Documentation range

            return "UNK";
        }

        private static uint ReverseBytes(uint value)
        {
            return ((value & 0x000000FFU) << 24) |
                   ((value & 0x0000FF00U) << 8) |
                   ((value & 0x00FF0000U) >> 8) |
                   ((value & 0xFF000000U) >> 24);
        }

        private static bool IsPrivateRange(IPAddress ip)
        {
            var bytes = ip.GetAddressBytes();
            return (bytes[0] == 10) ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168);
        }

        private static bool IsLoopback(IPAddress ip)
        {
            return IPAddress.IsLoopback(ip);
        }

        private static bool IsDocumentationRange(IPAddress ip)
        {
            var bytes = ip.GetAddressBytes();
            return (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2) ||      // 192.0.2.0/24 - TEST-NET-1
                   (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) ||   // 198.51.100.0/24 - TEST-NET-2
                   (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);      // 203.0.113.0/24 - TEST-NET-3
        }
    }

    /// <summary>
    /// Model for IP entries with country information
    /// </summary>
    public class IpEntryWithCountry
    {
        public string IpOrRange { get; set; } = "";
        public string CountryCode { get; set; } = "";
        public string CountryName { get; set; } = "";
        public string DisplayText => $"{IpOrRange} ({CountryCode})";
        
        public override string ToString() => DisplayText;
    }
}