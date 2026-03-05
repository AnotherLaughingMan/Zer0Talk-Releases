using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Zer0Talk.Utilities
{
    public static class TrustCeremonyFormatter
    {
        public static string FingerprintFromPublicKeyHex(string? publicKeyHex)
        {
            if (string.IsNullOrWhiteSpace(publicKeyHex)) return "Unavailable";

            var normalized = NormalizeHex(publicKeyHex);
            if (string.IsNullOrWhiteSpace(normalized)) return "Unavailable";

            byte[] keyBytes;
            try
            {
                keyBytes = Convert.FromHexString(normalized);
            }
            catch
            {
                keyBytes = Encoding.UTF8.GetBytes(normalized);
            }

            var digest = SHA256.HashData(keyBytes);
            var digestHex = Convert.ToHexString(digest);
            return GroupHex(digestHex, 4);
        }

        private static string NormalizeHex(string hex)
        {
            var sb = new StringBuilder(hex.Length);
            foreach (var ch in hex)
            {
                if (Uri.IsHexDigit(ch)) sb.Append(ch);
            }

            var normalized = sb.ToString();
            if (normalized.Length < 2 || (normalized.Length % 2) != 0) return string.Empty;
            return normalized.ToUpperInvariant();
        }

        private static string GroupHex(string hex, int groupSize)
        {
            if (string.IsNullOrWhiteSpace(hex) || groupSize <= 0) return string.Empty;
            var sb = new StringBuilder(hex.Length + (hex.Length / groupSize));
            for (var i = 0; i < hex.Length; i++)
            {
                if (i > 0 && (i % groupSize) == 0) sb.Append(' ');
                sb.Append(hex[i]);
            }

            return sb.ToString();
        }
    }
}
