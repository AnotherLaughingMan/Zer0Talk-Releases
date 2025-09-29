/*
    Identity and profile: keypair-based identity using Ed25519.
    - Loads keys from AccountData after unlock/creation.
    - Derives a compact mixed-case Base32 UID from the public key (prefix "usr-").
    - Provides signing and verification helpers for messages.
*/
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using ZTalk.Models;
using P2PTalk.Utilities;

using Sodium;

namespace P2PTalk.Services
{
    public class IdentityService
    {
        public string UID { get; private set; } = string.Empty;
        public byte[] PublicKey { get; private set; } = Array.Empty<byte>(); // Ed25519 public (32)
        public byte[] PrivateKey { get; private set; } = Array.Empty<byte>(); // Ed25519 secret (64)
        public string Username { get; private set; } = string.Empty; // fixed at creation
        public string DisplayName { get; private set; } = string.Empty;
        public string? Bio { get; private set; }
        public bool ShareAvatar { get; private set; }
        public byte[]? AvatarBytes { get; private set; }

        public event Action? Changed;
        private void RaiseChanged() { try { Changed?.Invoke(); } catch { } }
        public uint AvatarVersion { get; private set; } // increments on avatar change to hint network layer

        public bool IsLoaded => !string.IsNullOrWhiteSpace(UID) && PublicKey.Length == 32 && PrivateKey.Length >= 32;

        public void LoadFromAccount(AccountData account)
        {
            PublicKey = account.PublicKey ?? Array.Empty<byte>();
            PrivateKey = account.PrivateKey ?? Array.Empty<byte>();
            UID = account.UID ?? string.Empty;
            Username = account.Username ?? string.Empty;
            DisplayName = account.DisplayName ?? string.Empty;
            Bio = account.Bio;
            ShareAvatar = account.ShareAvatar;
            AvatarBytes = account.Avatar;
            if (account.Avatar != null) AvatarVersion++;
            SyncAvatarCache();
            RaiseChanged();
        }

        public void UpdateProfileFromAccount(AccountData account)
        {
            DisplayName = account.DisplayName ?? DisplayName;
            Bio = account.Bio;
            ShareAvatar = account.ShareAvatar;
            AvatarBytes = account.Avatar;
            if (account.Avatar != null) AvatarVersion++;
            SyncAvatarCache();
            RaiseChanged();
        }

        public byte[] Sign(byte[] data)
        {
            if (!IsLoaded) throw new InvalidOperationException("Identity not loaded");
            return PublicKeyAuth.SignDetached(data, PrivateKey);
        }

        public static bool Verify(byte[] data, byte[] signature, byte[] publicKey)
        {
            try { return PublicKeyAuth.VerifyDetached(signature, data, publicKey); }
            catch { return false; }
        }

        public static string ComputeUidFromPublicKey(byte[] pub, int minLen = 8)
        {
            // Hash the public key and encode with RFC4648 base32 characters, then flip case deterministically
            // to achieve mixed case while avoiding ambiguous characters. Prefix with "usr-".
            var hash = SHA256.HashData(pub);
            var base32 = Base32Encode(hash); // uppercase A-Z2-7
            // Deterministic case-mix: use next hash bytes as a case mask for letters
            var mask = SHA256.HashData(hash.Reverse().ToArray());
            var chars = base32.ToCharArray();
            int maskBit = 0, maskIdx = 0;
            for (int i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                if (char.IsLetter(c))
                {
                    // Advance mask bit
                    if (maskBit == 0) { maskBit = 8; maskIdx = (maskIdx + 1) % mask.Length; }
                    var bit = (mask[maskIdx] >> (--maskBit)) & 1;
                    chars[i] = bit == 1 ? char.ToLowerInvariant(c) : c;
                }
            }
            // Truncate and prefix
            var min = Math.Max(minLen, 8);
            var id = new string(chars);
            var core = id.Substring(0, min);
            return $"usr-{core}";
        }

        private static string Base32Encode(byte[] data)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"; // RFC 4648
            int outputLength = (int)Math.Ceiling(data.Length / 5d) * 8;
            var result = new StringBuilder(outputLength);
            int buffer = 0, bitsLeft = 0;
            foreach (var b in data)
            {
                buffer = (buffer << 8) | b;
                bitsLeft += 8;
                while (bitsLeft >= 5)
                {
                    int index = (buffer >> (bitsLeft - 5)) & 0x1F;
                    bitsLeft -= 5;
                    result.Append(alphabet[index]);
                }
            }
            if (bitsLeft > 0)
            {
                int index = (buffer << (5 - bitsLeft)) & 0x1F;
                result.Append(alphabet[index]);
            }
            return result.ToString();
        }

        private void SyncAvatarCache()
        {
            try
            {
                var uid = UID;
                if (string.IsNullOrWhiteSpace(uid)) return;

                if (AvatarBytes != null && AvatarBytes.Length > 0)
                {
                    AvatarCache.Save(uid, AvatarBytes);
                }
                else
                {
                    AvatarCache.Delete(uid);
                }
            }
            catch { }
        }
    }
}
