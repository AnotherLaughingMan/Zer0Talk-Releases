/*
    Quick validation test for IP country detection functionality.
    Run this to verify country codes are being detected correctly.
*/

using System;
using ZTalk.Utilities;

namespace ZTalk.Tests
{
    public static class IpCountryDetectionTest
    {
        public static void RunTests()
        {
            Console.WriteLine("=== IP Country Detection Tests ===");
            
            // Test known IP ranges from our blocklist
            TestIpCountry("1.0.1.0", "CHN", "China range"); 
            TestIpCountry("5.16.0.1", "RUS", "Russia range");
            TestIpCountry("1.22.0.1", "IND", "India range");
            TestIpCountry("175.45.176.1", "PRK", "North Korea range");
            
            // Test CIDR ranges
            TestIpCountry("1.0.1.0/24", "CHN", "China CIDR");
            TestIpCountry("5.16.0.0/14", "RUS", "Russia CIDR");
            TestIpCountry("175.45.176.0/22", "PRK", "North Korea CIDR");
            
            // Test special ranges
            TestIpCountry("192.168.1.1", "PVT", "Private range");
            TestIpCountry("127.0.0.1", "LOC", "Localhost");
            TestIpCountry("192.0.2.1", "DOC", "Documentation range");
            
            // Test unknown ranges
            TestIpCountry("8.8.8.8", "UNK", "Google DNS (unknown in our mappings)");
            
            Console.WriteLine("=== Tests Complete ===");
        }
        
        private static void TestIpCountry(string ip, string expectedCode, string description)
        {
            var actualCode = IpCountryDetector.DetectCountryCode(ip);
            var countryName = IpCountryDetector.GetCountryName(actualCode);
            var status = actualCode == expectedCode ? "✅ PASS" : "❌ FAIL";
            
            Console.WriteLine($"{status} {description}:");
            Console.WriteLine($"  IP: {ip}");
            Console.WriteLine($"  Expected: {expectedCode}");
            Console.WriteLine($"  Actual: {actualCode} ({countryName})");
            Console.WriteLine();
        }
    }
}