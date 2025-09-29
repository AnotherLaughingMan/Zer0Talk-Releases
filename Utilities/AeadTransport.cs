/*
    Framed transport with AEAD: encrypts/decrypts per-frame payloads over existing streams.
    - Uses derived session keys from ECDH + HKDF.
*/
using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Sodium;

namespace P2PTalk.Utilities
{
    public sealed class AeadTransport : IDisposable
    {
        private readonly System.IO.Stream _stream;
        private readonly byte[] _txKey;
        private readonly byte[] _rxKey;
        private readonly byte[] _txBase; // 16 bytes prefix for nonce
        private readonly byte[] _rxBase; // 16 bytes prefix for nonce
        private ulong _txCounter;
        private ulong _lastRxCounter = ulong.MaxValue; // no frames seen yet
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private const int MaxCipherLen = 1024 * 1024; // 1 MB defensive cap
        private bool _disposed;

        public AeadTransport(System.IO.Stream stream, byte[] txKey, byte[] rxKey, byte[] txBase, byte[] rxBase)
        {
            _stream = stream;
            _txKey = txKey; _rxKey = rxKey; _txBase = txBase; _rxBase = rxBase;
        }

        public async Task WriteAsync(byte[] plain, CancellationToken ct)
        {
            ThrowIfDisposed();
            await _writeLock.WaitAsync(ct);
            try
            {
                var counter = _txCounter++;
                var aad = BuildAad(counter);
                var nonce = BuildNonce(_txBase, counter);
                var cipher = SecretAeadXChaCha20Poly1305.Encrypt(plain, nonce, _txKey, aad);
                var header = new byte[1 + 8 + 4];
                header[0] = 0x01; // data frame
                BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(1, 8), counter);
                BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(9, 4), (uint)cipher.Length);
                await _stream.WriteAsync(header.AsMemory(), ct);
                await _stream.WriteAsync(cipher.AsMemory(), ct);
                await _stream.FlushAsync(ct);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task<byte[]> ReadAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            var header = new byte[1 + 8 + 4];
            await _stream.ReadExactlyAsync(header, 0, header.Length, ct);
            if (header[0] != 0x01) throw new InvalidDataException("Unsupported frame type");
            var counter = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(1, 8));
            var len = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(9, 4));
            if (len <= 0 || len > MaxCipherLen) throw new InvalidDataException("Frame too large");
            // Enforce monotonic counter to prevent replay/out-of-order frames on TCP
            if (_lastRxCounter != ulong.MaxValue && counter <= _lastRxCounter)
                throw new InvalidDataException("Out-of-order/replayed frame detected");
            var cipher = new byte[len];
            await _stream.ReadExactlyAsync(cipher, 0, (int)len, ct);
            var aad = BuildAad(counter);
            var nonce = BuildNonce(_rxBase, counter);
            var plain = SecretAeadXChaCha20Poly1305.Decrypt(cipher, nonce, _rxKey, aad);
            _lastRxCounter = counter;
            return plain;
        }

        private static byte[] BuildAad(ulong counter)
        {
            var aad = new byte[1 + 8];
            aad[0] = 0x01;
            BinaryPrimitives.WriteUInt64BigEndian(aad.AsSpan(1, 8), counter);
            return aad;
        }

        private static byte[] BuildNonce(byte[] basePrefix16, ulong counter)
        {
            if (basePrefix16.Length != 16) throw new ArgumentException("base nonce must be 16 bytes");
            var nonce = new byte[24];
            Buffer.BlockCopy(basePrefix16, 0, nonce, 0, 16);
            BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(16, 8), counter);
            return nonce;
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, nameof(AeadTransport));

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _writeLock.Dispose();
        }
    }
}
