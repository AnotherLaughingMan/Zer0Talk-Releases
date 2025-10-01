/*
    HKDF utilities (SHA-256): derives symmetric keys from ECDH shared secrets.
*/
using System;
using System.Security.Cryptography;

namespace ZTalk.Utilities
{
    public static class Hkdf
    {
        // HKDF-Extract(salt, IKM) and HKDF-Expand(PRK, info, L) for SHA-256
        public static byte[] DeriveKey(byte[] ikm, byte[] salt, byte[] info, int length)
        {
            using var hmac = new HMACSHA256(salt ?? Array.Empty<byte>());
            var prk = hmac.ComputeHash(ikm);
            var okm = new byte[length];
            var t = Array.Empty<byte>();
            var pos = 0;
            byte counter = 1;
            while (pos < length)
            {
                hmac.Key = prk;
                var input = new byte[t.Length + info.Length + 1];
                Buffer.BlockCopy(t, 0, input, 0, t.Length);
                Buffer.BlockCopy(info, 0, input, t.Length, info.Length);
                input[^1] = counter++;
                t = hmac.ComputeHash(input);
                var toCopy = Math.Min(t.Length, length - pos);
                Buffer.BlockCopy(t, 0, okm, pos, toCopy);
                pos += toCopy;
            }
            CryptographicOperations.ZeroMemory(prk);
            CryptographicOperations.ZeroMemory(t);
            return okm;
        }
    }
}
