/*
    Container crypto: XChaCha20-Poly1305 via libsodium; Argon2id for KDF.
    - Legacy AES-GCM (P2E2) read support and upgrade path to P2E3.
*/
using System;
using System.Security.Cryptography;

using Konscious.Security.Cryptography;

using Sodium;

namespace ZTalk.Services
{
    public class EncryptionService
    {
        // Formats:
        // P2E3 (XChaCha20-Poly1305): [4 magic 'P2E3'][16 salt][24 nonce][cipher+tag]
        // P2E2 (AES-GCM legacy):     [4 magic 'P2E2'][16 salt][12 nonce][16 tag][cipher]
        private static readonly byte[] MagicX = new byte[] { (byte)'P', (byte)'2', (byte)'E', (byte)'3' };
        private static readonly byte[] MagicA = new byte[] { (byte)'P', (byte)'2', (byte)'E', (byte)'2' };

        public byte[] Encrypt(byte[] data, string passphrase)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (string.IsNullOrEmpty(passphrase)) throw new ArgumentException("Passphrase required", nameof(passphrase));

            var salt = RandomNumberGenerator.GetBytes(16);
            var key = DeriveKey(passphrase, salt);
            var nonce = SecretAeadXChaCha20Poly1305.GenerateNonce(); // 24 bytes
            // Bind header (magic + salt) as AAD
            var aad = new byte[MagicX.Length + salt.Length];
            Buffer.BlockCopy(MagicX, 0, aad, 0, MagicX.Length);
            Buffer.BlockCopy(salt, 0, aad, MagicX.Length, salt.Length);
            var cipher = SecretAeadXChaCha20Poly1305.Encrypt(data, nonce, key, aad);

            var output = new byte[4 + 16 + 24 + cipher.Length];
            Buffer.BlockCopy(MagicX, 0, output, 0, 4);
            Buffer.BlockCopy(salt, 0, output, 4, 16);
            Buffer.BlockCopy(nonce, 0, output, 20, 24);
            Buffer.BlockCopy(cipher, 0, output, 44, cipher.Length);
            CryptographicOperations.ZeroMemory(key);
            return output;
        }

        public byte[] Decrypt(byte[] data, string passphrase)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (string.IsNullOrEmpty(passphrase)) throw new ArgumentException("Passphrase required", nameof(passphrase));
            if (data.Length < 4) throw new CryptographicException("Invalid container");

            if (IsMagic(data, MagicX))
            {
                if (data.Length < 4 + 16 + 24 + 16) throw new CryptographicException("Invalid container");
                var salt = new byte[16];
                var nonce = new byte[24];
                Buffer.BlockCopy(data, 4, salt, 0, 16);
                Buffer.BlockCopy(data, 20, nonce, 0, 24);
                var cipher = new byte[data.Length - (4 + 16 + 24)];
                Buffer.BlockCopy(data, 44, cipher, 0, cipher.Length);

                var key = DeriveKey(passphrase, salt);
                var aad = new byte[MagicX.Length + salt.Length];
                Buffer.BlockCopy(MagicX, 0, aad, 0, MagicX.Length);
                Buffer.BlockCopy(salt, 0, aad, MagicX.Length, salt.Length);
                try
                {
                    var plain = SecretAeadXChaCha20Poly1305.Decrypt(cipher, nonce, key, aad);
                    return plain;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(key);
                }
            }
            else if (IsMagic(data, MagicA))
            {
                if (data.Length < 4 + 16 + 12 + 16) throw new CryptographicException("Invalid container");
                var salt = new byte[16];
                var nonce = new byte[12];
                var tag = new byte[16];
                Buffer.BlockCopy(data, 4, salt, 0, 16);
                Buffer.BlockCopy(data, 20, nonce, 0, 12);
                Buffer.BlockCopy(data, 32, tag, 0, 16);
                var cipher = new byte[data.Length - (4 + 16 + 12 + 16)];
                Buffer.BlockCopy(data, 48, cipher, 0, cipher.Length);

                var key = DeriveKey(passphrase, salt);
                var plain = new byte[cipher.Length];
                using (var aes = new AesGcm(key, 16))
                {
                    aes.Decrypt(nonce, cipher, tag, plain, associatedData: null);
                }
                CryptographicOperations.ZeroMemory(key);
                return plain;
            }

            throw new CryptographicException("Unknown container format");
        }

        private static bool IsMagic(byte[] data, byte[] magic)
        {
            for (int i = 0; i < magic.Length; i++)
            {
                if (data[i] != magic[i]) return false;
            }
            return true;
        }

        private static byte[] DeriveKey(string passphrase, byte[] salt)
        {
            var pwd = System.Text.Encoding.UTF8.GetBytes(passphrase);
            var argon2 = new Argon2id(pwd)
            {
                Salt = salt,
                DegreeOfParallelism = Environment.ProcessorCount >= 2 ? 2 : 1,
                MemorySize = 1 << 16, // 64 MB
                Iterations = 4
            };
            return argon2.GetBytes(32);
        }
    }
}
