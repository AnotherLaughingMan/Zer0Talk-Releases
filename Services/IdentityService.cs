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
using System.Threading;

using Zer0Talk.Models;
using Zer0Talk.Utilities;

using Sodium;

namespace Zer0Talk.Services
{
    public class IdentityService
    {
        private static readonly object SodiumInitLock = new();
        private static int _sodiumReady;

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

        public bool IsLoaded => !string.IsNullOrWhiteSpace(UID) && PublicKey.Length == 32 && PrivateKey.Length == 64;

        public void LoadFromAccount(AccountData account)
        {
            var loadedPublic = account.PublicKey ?? Array.Empty<byte>();
            var loadedPrivate = account.PrivateKey ?? Array.Empty<byte>();

            NormalizeKeyMaterial(ref loadedPublic, ref loadedPrivate);

            PublicKey = loadedPublic;
            PrivateKey = loadedPrivate;
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
            EnsureSodiumReady();
            if (!IsLoaded) throw new InvalidOperationException("Identity not loaded");
            return PublicKeyAuth.SignDetached(data, PrivateKey);
        }

        public static bool Verify(byte[] data, byte[] signature, byte[] publicKey)
        {
            try
            {
                EnsureSodiumReady();
                return PublicKeyAuth.VerifyDetached(signature, data, publicKey);
            }
            catch { return false; }
        }

        private static void NormalizeKeyMaterial(ref byte[] publicKey, ref byte[] privateKey)
        {
            try
            {
                if (privateKey.Length == 32)
                {
                    try
                    {
                        EnsureSodiumReady();
                        var keyPair = PublicKeyAuth.GenerateKeyPair(privateKey);
                        var derivedPublic = keyPair.PublicKey;
                        var derivedPrivate = keyPair.PrivateKey;

                        if (publicKey.Length == 32 && !publicKey.SequenceEqual(derivedPublic))
                        {
                            Logger.Log("Identity key normalization detected public/private mismatch; using stored public key and derived private key.");
                        }
                        else
                        {
                            publicKey = derivedPublic;
                        }

                        privateKey = derivedPrivate;
                        Logger.Log("Identity key normalization upgraded 32-byte Ed25519 seed to 64-byte secret key.");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Identity key normalization failed for 32-byte seed: {ex.Message}");
                    }
                }
                else if (privateKey.Length > 64)
                {
                    var trimmed = new byte[64];
                    Buffer.BlockCopy(privateKey, 0, trimmed, 0, 64);
                    privateKey = trimmed;
                    Logger.Log("Identity key normalization trimmed oversized private key to 64 bytes.");
                }
            }
            catch { }
        }

        private static void EnsureSodiumReady()
        {
            if (Volatile.Read(ref _sodiumReady) == 1) return;
            lock (SodiumInitLock)
            {
                if (_sodiumReady == 1) return;
                SodiumCore.Init();
                Volatile.Write(ref _sodiumReady, 1);
                try { Logger.Log("IdentityService: Sodium initialized on-demand."); } catch { }
            }
        }

        public static string ComputeUidFromPublicKey(byte[] pub, int minLen = 8)
        {
            // Hash the public key and encode with RFC4648 base32 characters, then flip case deterministically
            // to achieve mixed case while avoiding ambiguous characters. Prefix with "usr-".
            var hash = SHA256.HashData(pub);
            var base32 = Base32Encode(hash); // uppercase A-Z2-7
            // Deterministic case-mix: use next hash bytes as a case mask for letters
            var mask = SHA256.HashData(hash.AsEnumerable().Reverse().ToArray());
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

