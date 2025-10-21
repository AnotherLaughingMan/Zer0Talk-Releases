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

using Zer0Talk.Utilities;

namespace Zer0Talk.Services
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

            CryptoLog($"Encrypt(string): Starting encryption of {data.Length} bytes");
            var result = EncryptCore(data, passphrase.AsSpan());
            CryptoLog($"Encrypt(string): Successfully encrypted to {result.Length} bytes (format: P2E3)");
            return result;
        }

        /// <summary>
        /// Encrypts data with a passphrase using XChaCha20-Poly1305.
        /// This overload accepts ReadOnlySpan&lt;char&gt; to allow secure passphrase handling.
        /// </summary>
        public byte[] Encrypt(byte[] data, ReadOnlySpan<char> passphrase)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (passphrase.IsEmpty) throw new ArgumentException("Passphrase required", nameof(passphrase));

            CryptoLog($"Encrypt(span): Starting encryption of {data.Length} bytes");
            var result = EncryptCore(data, passphrase);
            CryptoLog($"Encrypt(span): Successfully encrypted to {result.Length} bytes (format: P2E3)");
            return result;
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
                CryptoLog("EncryptCore: Generating 16-byte salt");
                salt = RandomNumberGenerator.GetBytes(16);
                CryptoLog($"EncryptCore: Salt generated (first 4 bytes: {BitConverter.ToString(salt, 0, Math.Min(4, salt.Length))})");
                
                CryptoLog("EncryptCore: Deriving 32-byte key using Argon2id");
                key = DeriveKey(passphrase, salt);
                CryptoLog("EncryptCore: Key derived successfully");
                
                CryptoLog("EncryptCore: Generating 24-byte nonce for XChaCha20-Poly1305");
                nonce = SecretAeadXChaCha20Poly1305.GenerateNonce(); // 24 bytes
                CryptoLog($"EncryptCore: Nonce generated (first 4 bytes: {BitConverter.ToString(nonce, 0, Math.Min(4, nonce.Length))})");
                
                // Bind header (magic + salt) as AAD
                CryptoLog("EncryptCore: Preparing AAD (magic + salt)");
                aad = ArrayPool<byte>.Shared.Rent(MagicX.Length + salt.Length);
                var aadSpan = aad.AsSpan(0, MagicX.Length + salt.Length);
                MagicX.CopyTo(aadSpan);
                salt.CopyTo(aadSpan.Slice(MagicX.Length));
                CryptoLog($"EncryptCore: AAD prepared ({aadSpan.Length} bytes)");
                
                CryptoLog($"EncryptCore: Encrypting {data.Length} bytes with XChaCha20-Poly1305");
                cipher = SecretAeadXChaCha20Poly1305.Encrypt(data, nonce, key, aadSpan.ToArray());
                CryptoLog($"EncryptCore: Encryption complete, ciphertext+tag = {cipher.Length} bytes");

                var output = new byte[4 + 16 + 24 + cipher.Length];
                Buffer.BlockCopy(MagicX, 0, output, 0, 4);
                Buffer.BlockCopy(salt, 0, output, 4, 16);
                Buffer.BlockCopy(nonce, 0, output, 20, 24);
                Buffer.BlockCopy(cipher, 0, output, 44, cipher.Length);
                CryptoLog($"EncryptCore: Final container assembled: {output.Length} bytes (4 magic + 16 salt + 24 nonce + {cipher.Length} cipher+tag)");
                
                return output;
            }
            finally
            {
                // Secure cleanup of all sensitive buffers
                CryptoLog("EncryptCore: Zeroing sensitive buffers");
                if (key != null) CryptographicOperations.ZeroMemory(key);
                if (salt != null) CryptographicOperations.ZeroMemory(salt);
                if (nonce != null) CryptographicOperations.ZeroMemory(nonce);
                if (aad != null)
                {
                    CryptographicOperations.ZeroMemory(aad);
                    ArrayPool<byte>.Shared.Return(aad);
                }
                if (cipher != null) CryptographicOperations.ZeroMemory(cipher);
                CryptoLog("EncryptCore: Cleanup complete");
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

            CryptoLog($"Decrypt(string): Starting decryption of {data.Length} bytes");
            var result = Decrypt(data, passphrase.AsSpan());
            CryptoLog($"Decrypt(string): Successfully decrypted to {result.Length} bytes");
            return result;
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

            CryptoLog($"Decrypt(span): Starting decryption of {data.Length} bytes");
            CryptoLog($"Decrypt(span): Detecting format - magic bytes: {BitConverter.ToString(data, 0, Math.Min(4, data.Length))}");
            
            if (IsMagic(data, MagicX))
            {
                CryptoLog("Decrypt(span): Detected P2E3 (XChaCha20-Poly1305) format");
                return DecryptP2E3(data, passphrase);
            }
            else if (IsMagic(data, MagicA))
            {
                CryptoLog("Decrypt(span): Detected P2E2 (AES-GCM legacy) format");
                return DecryptP2E2(data, passphrase);
            }

            CryptoLog("Decrypt(span): ERROR - Unknown container format");
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
                CryptoLog($"DecryptP2E3: Parsing P2E3 container ({data.Length} bytes)");
                salt = new byte[16];
                nonce = new byte[24];
                Buffer.BlockCopy(data, 4, salt, 0, 16);
                Buffer.BlockCopy(data, 20, nonce, 0, 24);
                CryptoLog($"DecryptP2E3: Extracted salt (first 4 bytes: {BitConverter.ToString(salt, 0, Math.Min(4, salt.Length))})");
                CryptoLog($"DecryptP2E3: Extracted nonce (first 4 bytes: {BitConverter.ToString(nonce, 0, Math.Min(4, nonce.Length))})");
                
                cipher = new byte[data.Length - (4 + 16 + 24)];
                Buffer.BlockCopy(data, 44, cipher, 0, cipher.Length);
                CryptoLog($"DecryptP2E3: Extracted ciphertext+tag ({cipher.Length} bytes)");

                CryptoLog("DecryptP2E3: Deriving key using Argon2id");
                key = DeriveKey(passphrase, salt);
                CryptoLog("DecryptP2E3: Key derived successfully");
                
                CryptoLog("DecryptP2E3: Reconstructing AAD");
                aad = new byte[MagicX.Length + salt.Length];
                Buffer.BlockCopy(MagicX, 0, aad, 0, MagicX.Length);
                Buffer.BlockCopy(salt, 0, aad, MagicX.Length, salt.Length);
                CryptoLog($"DecryptP2E3: AAD reconstructed ({aad.Length} bytes)");
                
                CryptoLog("DecryptP2E3: Decrypting with XChaCha20-Poly1305");
                var plain = SecretAeadXChaCha20Poly1305.Decrypt(cipher, nonce, key, aad);
                CryptoLog($"DecryptP2E3: Decryption successful, plaintext = {plain.Length} bytes");
                return plain;
            }
            catch (Exception ex)
            {
                CryptoLog($"DecryptP2E3: ERROR - Decryption failed: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
            finally
            {
                CryptoLog("DecryptP2E3: Zeroing sensitive buffers");
                if (key != null) CryptographicOperations.ZeroMemory(key);
                if (salt != null) CryptographicOperations.ZeroMemory(salt);
                if (nonce != null) CryptographicOperations.ZeroMemory(nonce);
                if (cipher != null) CryptographicOperations.ZeroMemory(cipher);
                if (aad != null) CryptographicOperations.ZeroMemory(aad);
                CryptoLog("DecryptP2E3: Cleanup complete");
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
                CryptoLog($"DecryptP2E2: Parsing P2E2 legacy container ({data.Length} bytes)");
                salt = new byte[16];
                nonce = new byte[12];
                tag = new byte[16];
                Buffer.BlockCopy(data, 4, salt, 0, 16);
                Buffer.BlockCopy(data, 20, nonce, 0, 12);
                Buffer.BlockCopy(data, 32, tag, 0, 16);
                CryptoLog($"DecryptP2E2: Extracted salt (first 4 bytes: {BitConverter.ToString(salt, 0, Math.Min(4, salt.Length))})");
                CryptoLog($"DecryptP2E2: Extracted 12-byte nonce (first 4 bytes: {BitConverter.ToString(nonce, 0, Math.Min(4, nonce.Length))})");
                CryptoLog($"DecryptP2E2: Extracted 16-byte auth tag (first 4 bytes: {BitConverter.ToString(tag, 0, Math.Min(4, tag.Length))})");
                
                cipher = new byte[data.Length - (4 + 16 + 12 + 16)];
                Buffer.BlockCopy(data, 48, cipher, 0, cipher.Length);
                CryptoLog($"DecryptP2E2: Extracted ciphertext ({cipher.Length} bytes)");

                CryptoLog("DecryptP2E2: Deriving key using Argon2id");
                key = DeriveKey(passphrase, salt);
                CryptoLog("DecryptP2E2: Key derived successfully");
                plain = new byte[cipher.Length];
                
                CryptoLog("DecryptP2E2: Initializing AES-GCM with 16-byte tag");
                aes = new AesGcm(key, 16);
                CryptoLog("DecryptP2E2: Decrypting with AES-GCM");
                aes.Decrypt(nonce, cipher, tag, plain, associatedData: null);
                CryptoLog($"DecryptP2E2: Decryption successful, plaintext = {plain.Length} bytes");
                
                return plain;
            }
            catch (Exception ex)
            {
                CryptoLog($"DecryptP2E2: ERROR - Decryption failed: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
            finally
            {
                CryptoLog("DecryptP2E2: Zeroing sensitive buffers");
                aes?.Dispose();
                if (key != null) CryptographicOperations.ZeroMemory(key);
                if (salt != null) CryptographicOperations.ZeroMemory(salt);
                if (nonce != null) CryptographicOperations.ZeroMemory(nonce);
                if (tag != null) CryptographicOperations.ZeroMemory(tag);
                if (cipher != null) CryptographicOperations.ZeroMemory(cipher);
                CryptoLog("DecryptP2E2: Cleanup complete (plaintext returned to caller)");
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
                CryptoLog($"DeriveKey: Passphrase UTF8 byte count = {byteCount}");
                pwd = ArrayPool<byte>.Shared.Rent(byteCount);
                var pwdSpan = pwd.AsSpan(0, byteCount);
                
                // Encode passphrase to UTF8 bytes
                System.Text.Encoding.UTF8.GetBytes(passphrase, pwdSpan);
                CryptoLog("DeriveKey: Passphrase encoded to UTF8");
                
                var parallelism = Environment.ProcessorCount >= 2 ? 2 : 1;
                CryptoLog($"DeriveKey: Configuring Argon2id (parallelism={parallelism}, memory=64MB, iterations=4)");
                argon2 = new Argon2id(pwdSpan.ToArray())
                {
                    Salt = salt,
                    DegreeOfParallelism = parallelism,
                    MemorySize = 1 << 16, // 64 MB
                    Iterations = 4
                };
                CryptoLog("DeriveKey: Running Argon2id KDF to derive 32-byte key");
                var key = argon2.GetBytes(32);
                CryptoLog("DeriveKey: Key derivation complete");
                return key;
            }
            finally
            {
                if (pwd != null)
                {
                    CryptographicOperations.ZeroMemory(pwd);
                    ArrayPool<byte>.Shared.Return(pwd);
                }
                argon2?.Dispose();
                CryptoLog("DeriveKey: Cleanup complete");
            }
        }

        /// <summary>
        /// Internal logging helper that writes to the crypto.log file
        /// </summary>
        private static void CryptoLog(string message)
        {
            if (!LoggingPaths.Enabled) return;
            
            try
            {
                var line = $"[CRYPTO] {DateTime.Now:O}: {message}{Environment.NewLine}";
                LoggingPaths.TryWrite(LoggingPaths.Crypto, line);
            }
            catch
            {
                // Best-effort logging, don't throw
            }
        }
    }
}

