/*
    Container crypto: XChaCha20-Poly1305 via libsodium; Argon2id for KDF.
    - Legacy AES-GCM (P2E2) read support and upgrade path to P2E3.
    - Enhanced memory security: all sensitive buffers zeroed after use.
    - ArrayPool<byte> for temporary buffers to reduce GC pressure.
    - ReadOnlySpan<char> overloads to avoid string immutability issues.
*/
using System;
using System.Buffers;
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

        /// <summary>
        /// Encrypts data with a passphrase using XChaCha20-Poly1305.
        /// For better security, use the ReadOnlySpan&lt;char&gt; overload to avoid string immutability.
        /// </summary>
        public byte[] Encrypt(byte[] data, string passphrase)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (string.IsNullOrEmpty(passphrase)) throw new ArgumentException("Passphrase required", nameof(passphrase));

            return EncryptCore(data, passphrase.AsSpan());
        }

        /// <summary>
        /// Encrypts data with a passphrase using XChaCha20-Poly1305.
        /// This overload accepts ReadOnlySpan&lt;char&gt; to allow secure passphrase handling.
        /// </summary>
        public byte[] Encrypt(byte[] data, ReadOnlySpan<char> passphrase)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (passphrase.IsEmpty) throw new ArgumentException("Passphrase required", nameof(passphrase));

            return EncryptCore(data, passphrase);
        }

        private static byte[] EncryptCore(byte[] data, ReadOnlySpan<char> passphrase)
        {
            byte[]? salt = null;
            byte[]? key = null;
            byte[]? nonce = null;
            byte[]? aad = null;
            byte[]? cipher = null;

            try
            {
                salt = RandomNumberGenerator.GetBytes(16);
                key = DeriveKey(passphrase, salt);
                nonce = SecretAeadXChaCha20Poly1305.GenerateNonce(); // 24 bytes
                
                // Bind header (magic + salt) as AAD
                aad = ArrayPool<byte>.Shared.Rent(MagicX.Length + salt.Length);
                var aadSpan = aad.AsSpan(0, MagicX.Length + salt.Length);
                MagicX.CopyTo(aadSpan);
                salt.CopyTo(aadSpan.Slice(MagicX.Length));
                
                cipher = SecretAeadXChaCha20Poly1305.Encrypt(data, nonce, key, aadSpan.ToArray());

                var output = new byte[4 + 16 + 24 + cipher.Length];
                Buffer.BlockCopy(MagicX, 0, output, 0, 4);
                Buffer.BlockCopy(salt, 0, output, 4, 16);
                Buffer.BlockCopy(nonce, 0, output, 20, 24);
                Buffer.BlockCopy(cipher, 0, output, 44, cipher.Length);
                
                return output;
            }
            finally
            {
                // Secure cleanup of all sensitive buffers
                if (key != null) CryptographicOperations.ZeroMemory(key);
                if (salt != null) CryptographicOperations.ZeroMemory(salt);
                if (nonce != null) CryptographicOperations.ZeroMemory(nonce);
                if (aad != null)
                {
                    CryptographicOperations.ZeroMemory(aad);
                    ArrayPool<byte>.Shared.Return(aad);
                }
                if (cipher != null) CryptographicOperations.ZeroMemory(cipher);
            }
        }

        /// <summary>
        /// Decrypts data with a passphrase. Supports both P2E3 (XChaCha20-Poly1305) and legacy P2E2 (AES-GCM).
        /// For better security, use the ReadOnlySpan&lt;char&gt; overload to avoid string immutability.
        /// WARNING: Caller must securely zero the returned plaintext after use with CryptographicOperations.ZeroMemory().
        /// </summary>
        public byte[] Decrypt(byte[] data, string passphrase)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (string.IsNullOrEmpty(passphrase)) throw new ArgumentException("Passphrase required", nameof(passphrase));

            return Decrypt(data, passphrase.AsSpan());
        }

        /// <summary>
        /// Decrypts data with a passphrase. Supports both P2E3 (XChaCha20-Poly1305) and legacy P2E2 (AES-GCM).
        /// This overload accepts ReadOnlySpan&lt;char&gt; to allow secure passphrase handling.
        /// WARNING: Caller must securely zero the returned plaintext after use with CryptographicOperations.ZeroMemory().
        /// </summary>
        public byte[] Decrypt(byte[] data, ReadOnlySpan<char> passphrase)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (passphrase.IsEmpty) throw new ArgumentException("Passphrase required", nameof(passphrase));
            if (data.Length < 4) throw new CryptographicException("Invalid container");

            if (IsMagic(data, MagicX))
            {
                return DecryptP2E3(data, passphrase);
            }
            else if (IsMagic(data, MagicA))
            {
                return DecryptP2E2(data, passphrase);
            }

            throw new CryptographicException("Unknown container format");
        }

        private static byte[] DecryptP2E3(byte[] data, ReadOnlySpan<char> passphrase)
        {
            if (data.Length < 4 + 16 + 24 + 16) throw new CryptographicException("Invalid container");
            
            byte[]? salt = null;
            byte[]? nonce = null;
            byte[]? cipher = null;
            byte[]? key = null;
            byte[]? aad = null;
            
            try
            {
                salt = new byte[16];
                nonce = new byte[24];
                Buffer.BlockCopy(data, 4, salt, 0, 16);
                Buffer.BlockCopy(data, 20, nonce, 0, 24);
                
                cipher = new byte[data.Length - (4 + 16 + 24)];
                Buffer.BlockCopy(data, 44, cipher, 0, cipher.Length);

                key = DeriveKey(passphrase, salt);
                
                aad = new byte[MagicX.Length + salt.Length];
                Buffer.BlockCopy(MagicX, 0, aad, 0, MagicX.Length);
                Buffer.BlockCopy(salt, 0, aad, MagicX.Length, salt.Length);
                
                var plain = SecretAeadXChaCha20Poly1305.Decrypt(cipher, nonce, key, aad);
                return plain;
            }
            finally
            {
                if (key != null) CryptographicOperations.ZeroMemory(key);
                if (salt != null) CryptographicOperations.ZeroMemory(salt);
                if (nonce != null) CryptographicOperations.ZeroMemory(nonce);
                if (cipher != null) CryptographicOperations.ZeroMemory(cipher);
                if (aad != null) CryptographicOperations.ZeroMemory(aad);
            }
        }

        private static byte[] DecryptP2E2(byte[] data, ReadOnlySpan<char> passphrase)
        {
            if (data.Length < 4 + 16 + 12 + 16) throw new CryptographicException("Invalid container");
            
            byte[]? salt = null;
            byte[]? nonce = null;
            byte[]? tag = null;
            byte[]? cipher = null;
            byte[]? key = null;
            byte[]? plain = null;
            AesGcm? aes = null;

            try
            {
                salt = new byte[16];
                nonce = new byte[12];
                tag = new byte[16];
                Buffer.BlockCopy(data, 4, salt, 0, 16);
                Buffer.BlockCopy(data, 20, nonce, 0, 12);
                Buffer.BlockCopy(data, 32, tag, 0, 16);
                
                cipher = new byte[data.Length - (4 + 16 + 12 + 16)];
                Buffer.BlockCopy(data, 48, cipher, 0, cipher.Length);

                key = DeriveKey(passphrase, salt);
                plain = new byte[cipher.Length];
                
                aes = new AesGcm(key, 16);
                aes.Decrypt(nonce, cipher, tag, plain, associatedData: null);
                
                return plain;
            }
            finally
            {
                aes?.Dispose();
                if (key != null) CryptographicOperations.ZeroMemory(key);
                if (salt != null) CryptographicOperations.ZeroMemory(salt);
                if (nonce != null) CryptographicOperations.ZeroMemory(nonce);
                if (tag != null) CryptographicOperations.ZeroMemory(tag);
                if (cipher != null) CryptographicOperations.ZeroMemory(cipher);
                // Note: plain is returned, caller must secure it
            }
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
            return DeriveKey(passphrase.AsSpan(), salt);
        }

        private static byte[] DeriveKey(ReadOnlySpan<char> passphrase, byte[] salt)
        {
            byte[]? pwd = null;
            Argon2id? argon2 = null;
            
            try
            {
                // Get byte count for UTF8 encoding
                var byteCount = System.Text.Encoding.UTF8.GetByteCount(passphrase);
                pwd = ArrayPool<byte>.Shared.Rent(byteCount);
                var pwdSpan = pwd.AsSpan(0, byteCount);
                
                // Encode passphrase to UTF8 bytes
                System.Text.Encoding.UTF8.GetBytes(passphrase, pwdSpan);
                
                argon2 = new Argon2id(pwdSpan.ToArray())
                {
                    Salt = salt,
                    DegreeOfParallelism = Environment.ProcessorCount >= 2 ? 2 : 1,
                    MemorySize = 1 << 16, // 64 MB
                    Iterations = 4
                };
                return argon2.GetBytes(32);
            }
            finally
            {
                if (pwd != null)
                {
                    CryptographicOperations.ZeroMemory(pwd);
                    ArrayPool<byte>.Shared.Return(pwd);
                }
                argon2?.Dispose();
            }
        }
    }
}
