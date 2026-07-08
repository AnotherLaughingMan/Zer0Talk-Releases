/*
    Framed transport with AEAD: encrypts/decrypts per-frame payloads over existing streams.
    - Uses derived session keys from ECDH + HKDF.
*/
using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Sodium;

namespace Zer0Talk.Utilities
{
    public sealed class AeadTransport : IDisposable
    {
        private readonly System.IO.Stream _stream;
        private byte[] _txKey;
        private byte[] _rxKey;
        private byte[] _txBase; // 16 bytes prefix for nonce
        private byte[] _rxBase; // 16 bytes prefix for nonce
        private ulong _txCounter;
        private ulong _lastRxCounter = ulong.MaxValue; // no frames seen yet
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly object _stateGate = new();
        private const int MaxCipherLen = 1024 * 1024; // 1 MB defensive cap
        private bool _disposed;
        private long _totalBytesWritten;
        private long _totalBytesRead;

        /// <summary>Total plaintext bytes written (excluding framing/encryption overhead).</summary>
        public long TotalBytesWritten => System.Threading.Interlocked.Read(ref _totalBytesWritten);
        /// <summary>Total plaintext bytes read (excluding framing/encryption overhead).</summary>
        public long TotalBytesRead => System.Threading.Interlocked.Read(ref _totalBytesRead);
        /// <summary>Best-effort transport liveness probe for stale-session eviction.</summary>
        public bool IsLikelyConnected
        {
            get
            {
                if (_disposed) return false;

                Stream stream;
                try { stream = _stream; }
                catch { return false; }

                try
                {
                    if (!stream.CanRead || !stream.CanWrite) return false;
                }
                catch
                {
                    return false;
                }

                if (!TryGetNetworkStream(stream, out var networkStream)) return true;

                try
                {
                    var socket = networkStream.Socket;
                    if (!socket.Connected) return false;
                    var closed = socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0;
                    return !closed;
                }
                catch
                {
                    return false;
                }
            }
        }

        public AeadTransport(System.IO.Stream stream, byte[] txKey, byte[] rxKey, byte[] txBase, byte[] rxBase)
        {
            _stream = stream;
            _txKey = (byte[])txKey.Clone();
            _rxKey = (byte[])rxKey.Clone();
            _txBase = (byte[])txBase.Clone();
            _rxBase = (byte[])rxBase.Clone();
        }

        public async Task WriteAsync(byte[] plain, CancellationToken ct)
        {
            ThrowIfDisposed();
            EncChatLog($"AeadTransport.WriteAsync: Received cupidatat non proident sunt in culpa");
            EncChatLog($"AeadTransport.WriteAsync: Plaintext qui officia deserunt mollit anim id est laborum");
            
            await _writeLock.WaitAsync(ct);
            try
            {
                await WriteFrameAsync(0x01, plain, ct);
                System.Threading.Interlocked.Add(ref _totalBytesWritten, plain.Length);
                
                EncChatLog($"AeadTransport.WriteAsync: Encrypted frame neque porro quisquam est qui dolorem");
                EncChatLog($"AeadTransport.WriteAsync: *** PLAINTEXT WAS ENCRYPTED - ipsum quia dolor sit amet ***");
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
            var frameType = header[0];
            if (frameType != 0x01) throw new InvalidDataException("Unsupported frame type");
            var counter = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(1, 8));
            var len = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(9, 4));
            if (len <= 0 || len > MaxCipherLen) throw new InvalidDataException("Frame too large");
            // Enforce monotonic counter to prevent replay/out-of-order frames on TCP
            if (_lastRxCounter != ulong.MaxValue && counter <= _lastRxCounter)
                throw new InvalidDataException("Out-of-order/replayed frame detected");
            var cipher = new byte[len];
            await _stream.ReadExactlyAsync(cipher, 0, (int)len, ct);
            byte[] rxKey;
            byte[] rxBase;
            lock (_stateGate)
            {
                rxKey = _rxKey;
                rxBase = _rxBase;
            }
            var aad = BuildAad(frameType, counter);
            if (!TryDecryptWithMaterial(cipher, aad, counter, rxKey, rxBase, out var plain))
            {
                throw new InvalidDataException("Unable to decrypt frame with current session keys");
            }
            _lastRxCounter = counter;
            System.Threading.Interlocked.Add(ref _totalBytesRead, plain.Length);
            return plain;
        }

        private async Task WriteFrameAsync(byte frameType, byte[] plain, CancellationToken ct)
        {
            var counter = _txCounter++;
            EncChatLog($"AeadTransport.WriteAsync: Using counter sed quia non numquam eius modi");

            byte[] txKey;
            byte[] txBase;
            lock (_stateGate)
            {
                txKey = _txKey;
                txBase = _txBase;
            }

            var aad = BuildAad(frameType, counter);
            EncChatLog($"AeadTransport.WriteAsync: AAD built tempora incidunt ut labore");

            var nonce = BuildNonce(txBase, counter);
            EncChatLog($"AeadTransport.WriteAsync: Nonce built et dolore magnam aliquam quaerat voluptatem");

            EncChatLog($"AeadTransport.WriteAsync: Encrypting nemo enim ipsam voluptatem quia voluptas");
            var cipher = SecretAeadXChaCha20Poly1305.Encrypt(plain, nonce, txKey, aad);
            EncChatLog($"AeadTransport.WriteAsync: Encryption complete sit aspernatur aut odit aut fugit");
            EncChatLog($"AeadTransport.WriteAsync: Ciphertext sed quia consequuntur magni dolores");

            var header = new byte[1 + 8 + 4];
            header[0] = frameType;
            BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(1, 8), counter);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(9, 4), (uint)cipher.Length);
            EncChatLog($"AeadTransport.WriteAsync: Writing header eos qui ratione voluptatem sequi nesciunt");

            await _stream.WriteAsync(header.AsMemory(), ct);
            await _stream.WriteAsync(cipher.AsMemory(), ct);
            await _stream.FlushAsync(ct);
        }

        private static byte[] BuildAad(byte frameType, ulong counter)
        {
            var aad = new byte[1 + 8];
            aad[0] = frameType;
            BinaryPrimitives.WriteUInt64BigEndian(aad.AsSpan(1, 8), counter);
            return aad;
        }

        private static void ReplaceKeyMaterial(ref byte[] target, byte[] replacement)
        {
            var previous = target;
            target = replacement;
            if (previous != null && previous.Length > 0)
            {
                CryptographicOperations.ZeroMemory(previous);
            }
        }

        private static bool TryDecryptWithMaterial(byte[] cipher, byte[] aad, ulong counter, byte[] key, byte[] nonceBase, out byte[] plain)
        {
            plain = Array.Empty<byte>();
            try
            {
                var nonce = BuildNonce(nonceBase, counter);
                plain = SecretAeadXChaCha20Poly1305.Decrypt(cipher, nonce, key, aad);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetNetworkStream(Stream stream, out NetworkStream networkStream)
        {
            networkStream = null!;
            if (stream is NetworkStream ns)
            {
                networkStream = ns;
                return true;
            }

            if (stream is CountingStream cs)
            {
                return TryGetNetworkStream(cs.InnerStream, out networkStream);
            }

            return false;
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

        private static void EncChatLog(string message)
        {
            if (!LoggingPaths.Enabled) return;
            
            try
            {
                var line = $"[AEAD] {DateTime.Now:O}: {message}{Environment.NewLine}";
                LoggingPaths.TryWrite(LoggingPaths.EncryptedChat, line);
            }
            catch
            {
                // Best-effort logging
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_stateGate)
            {
                ReplaceKeyMaterial(ref _txKey, Array.Empty<byte>());
                ReplaceKeyMaterial(ref _rxKey, Array.Empty<byte>());
                ReplaceKeyMaterial(ref _txBase, Array.Empty<byte>());
                ReplaceKeyMaterial(ref _rxBase, Array.Empty<byte>());
            }
            _writeLock.Dispose();
        }
    }
}
