/*
    Framed transport with AEAD: encrypts/decrypts per-frame payloads over existing streams.
    - Uses derived session keys from ECDH + HKDF.
*/
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
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
        private readonly object _ratchetGate = new();
        private const int MaxCipherLen = 1024 * 1024; // 1 MB defensive cap
        private const int DefaultRatchetIntervalFrames = 64;
        private const int MaxSkippedInboundEpochs = 8;
        private static readonly TimeSpan SkippedInboundEpochTtl = TimeSpan.FromMinutes(5);
        private static readonly byte[] DhRatchetInfo = Encoding.UTF8.GetBytes("Zer0Talk-dh-ratchet");
        private bool _disposed;
        private long _totalBytesWritten;
        private long _totalBytesRead;
        private readonly int _ratchetIntervalFrames;
        private int _outboundFramesSinceRatchet;
        private bool _dhRatchetEnabled;
        private bool _outboundRatchetPending;
        private DhRatchetState _dhRatchetState;
        private readonly List<SkippedInboundEpoch> _skippedInboundEpochs = new();
        private ECDiffieHellman _localRatchet;
        private byte[] _localRatchetPublicKey;
        private byte[]? _remoteRatchetPublicKey;
        private int _outboundRatchetRotations;
        private int _inboundRatchetRotations;

        public const byte DhRatchetFrameOpcode = 0xA3;

        public enum DhRatchetState
        {
            Disabled,
            Negotiating,
            Active
        }

        /// <summary>Total plaintext bytes written (excluding framing/encryption overhead).</summary>
        public long TotalBytesWritten => System.Threading.Interlocked.Read(ref _totalBytesWritten);
        /// <summary>Total plaintext bytes read (excluding framing/encryption overhead).</summary>
        public long TotalBytesRead => System.Threading.Interlocked.Read(ref _totalBytesRead);
        /// <summary>Current DH ratchet lifecycle state.</summary>
        public DhRatchetState RatchetLifecycle { get { lock (_ratchetGate) { return _dhRatchetState; } } }
        /// <summary>Number of outbound DH ratchet rotations applied on this transport.</summary>
        public int OutboundRatchetRotations => Volatile.Read(ref _outboundRatchetRotations);
        /// <summary>Number of inbound DH ratchet rotations applied on this transport.</summary>
        public int InboundRatchetRotations => Volatile.Read(ref _inboundRatchetRotations);
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

        public AeadTransport(System.IO.Stream stream, byte[] txKey, byte[] rxKey, byte[] txBase, byte[] rxBase, int ratchetIntervalFrames = DefaultRatchetIntervalFrames)
        {
            _stream = stream;
            _txKey = (byte[])txKey.Clone();
            _rxKey = (byte[])rxKey.Clone();
            _txBase = (byte[])txBase.Clone();
            _rxBase = (byte[])rxBase.Clone();
            _ratchetIntervalFrames = ratchetIntervalFrames > 0 ? ratchetIntervalFrames : DefaultRatchetIntervalFrames;
            _localRatchet = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            _localRatchetPublicKey = _localRatchet.PublicKey.ExportSubjectPublicKeyInfo();
        }

        public byte[] GetRatchetPublicKey()
        {
            ThrowIfDisposed();
            lock (_ratchetGate)
            {
                return (byte[])_localRatchetPublicKey.Clone();
            }
        }

        public bool TryEnableDhRatchet(byte[] remoteRatchetPublicKey)
        {
            ThrowIfDisposed();
            if (!TryCloneRatchetPublicKey(remoteRatchetPublicKey, out var remoteClone))
            {
                return false;
            }

            lock (_ratchetGate)
            {
                _remoteRatchetPublicKey = remoteClone;
                _dhRatchetEnabled = true;
                _outboundRatchetPending = true;
                if (_dhRatchetState == DhRatchetState.Disabled)
                {
                    _dhRatchetState = DhRatchetState.Negotiating;
                }
            }
            return true;
        }

        public bool ApplyInboundDhRatchet(byte[] remoteRatchetPublicKey)
        {
            ThrowIfDisposed();
            if (!TryCloneRatchetPublicKey(remoteRatchetPublicKey, out var remoteClone))
            {
                return false;
            }

            DirectionKeyMaterial nextState;
            lock (_ratchetGate)
            {
                nextState = DeriveDirectionKeyMaterial(_localRatchet, remoteClone);
                AddSkippedInboundEpochNoLock(_rxKey, _rxBase);
                _rxKey = nextState.Key;
                _rxBase = nextState.Base;
                ReplaceOptionalKeyMaterial(ref _remoteRatchetPublicKey, remoteClone);
                _dhRatchetEnabled = true;
                _dhRatchetState = DhRatchetState.Active;
                PruneSkippedInboundEpochsNoLock();
            }
            Interlocked.Increment(ref _inboundRatchetRotations);
            return true;
        }

        public async Task WriteAsync(byte[] plain, CancellationToken ct)
        {
            ThrowIfDisposed();
            EncChatLog($"AeadTransport.WriteAsync: Received cupidatat non proident sunt in culpa");
            EncChatLog($"AeadTransport.WriteAsync: Plaintext qui officia deserunt mollit anim id est laborum");
            
            await _writeLock.WaitAsync(ct);
            try
            {
                await MaybeWriteRatchetFrameAsync(ct);
                await WriteFrameAsync(0x01, plain, ct);
                System.Threading.Interlocked.Add(ref _totalBytesWritten, plain.Length);
                if (plain.Length > 0)
                {
                    lock (_ratchetGate)
                    {
                        _outboundFramesSinceRatchet++;
                    }
                }
                
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
            List<SkippedInboundEpoch>? skippedEpochs = null;
            lock (_ratchetGate)
            {
                rxKey = _rxKey;
                rxBase = _rxBase;
                if (_skippedInboundEpochs.Count > 0)
                {
                    PruneSkippedInboundEpochsNoLock();
                    skippedEpochs = new List<SkippedInboundEpoch>(_skippedInboundEpochs);
                }
            }
            var aad = BuildAad(frameType, counter);
            var nonce = BuildNonce(rxBase, counter);
            byte[] plain;
            if (!TryDecryptWithMaterial(cipher, aad, counter, rxKey, rxBase, out plain))
            {
                plain = TryDecryptWithSkippedInboundEpochs(cipher, aad, counter, skippedEpochs);
            }
            _lastRxCounter = counter;
            System.Threading.Interlocked.Add(ref _totalBytesRead, plain.Length);
            return plain;
        }

        private async Task MaybeWriteRatchetFrameAsync(CancellationToken ct)
        {
            ECDiffieHellman? nextRatchet = null;
            byte[]? nextPub = null;
            byte[]? remotePub = null;

            lock (_ratchetGate)
            {
                if (!_dhRatchetEnabled || _remoteRatchetPublicKey == null)
                {
                    return;
                }

                if (!_outboundRatchetPending && _outboundFramesSinceRatchet < _ratchetIntervalFrames)
                {
                    return;
                }

                nextRatchet = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
                nextPub = nextRatchet.PublicKey.ExportSubjectPublicKeyInfo();
                remotePub = (byte[])_remoteRatchetPublicKey.Clone();
            }

            try
            {
                await WriteFrameAsync(0x01, BuildRatchetPayload(nextPub!), ct);
                var nextState = DeriveDirectionKeyMaterial(nextRatchet!, remotePub!);
                lock (_ratchetGate)
                {
                    ReplaceKeyMaterial(ref _txKey, nextState.Key);
                    ReplaceKeyMaterial(ref _txBase, nextState.Base);
                    ReplaceRatchetState(nextRatchet!, nextPub!);
                    _outboundFramesSinceRatchet = 0;
                    _outboundRatchetPending = false;
                    _dhRatchetState = DhRatchetState.Active;
                }
                Interlocked.Increment(ref _outboundRatchetRotations);
                nextRatchet = null;
                nextPub = null;
            }
            finally
            {
                if (nextPub != null)
                {
                    CryptographicOperations.ZeroMemory(nextPub);
                }
                if (remotePub != null)
                {
                    CryptographicOperations.ZeroMemory(remotePub);
                }
                nextRatchet?.Dispose();
            }
        }

        private async Task WriteFrameAsync(byte frameType, byte[] plain, CancellationToken ct)
        {
            var counter = _txCounter++;
            EncChatLog($"AeadTransport.WriteAsync: Using counter sed quia non numquam eius modi");

            byte[] txKey;
            byte[] txBase;
            lock (_ratchetGate)
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

        private static byte[] BuildRatchetPayload(byte[] ratchetPublicKey)
        {
            var frame = new byte[1 + 2 + ratchetPublicKey.Length];
            frame[0] = DhRatchetFrameOpcode;
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(1, 2), (ushort)ratchetPublicKey.Length);
            Buffer.BlockCopy(ratchetPublicKey, 0, frame, 3, ratchetPublicKey.Length);
            return frame;
        }

        private static bool TryCloneRatchetPublicKey(byte[] remoteRatchetPublicKey, out byte[] clone)
        {
            clone = Array.Empty<byte>();
            if (remoteRatchetPublicKey == null || remoteRatchetPublicKey.Length == 0)
            {
                return false;
            }

            try
            {
                using var peer = ECDiffieHellman.Create();
                peer.ImportSubjectPublicKeyInfo(remoteRatchetPublicKey, out _);
                clone = (byte[])remoteRatchetPublicKey.Clone();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static DirectionKeyMaterial DeriveDirectionKeyMaterial(ECDiffieHellman localRatchet, byte[] remoteRatchetPublicKey)
        {
            using var peer = ECDiffieHellman.Create();
            peer.ImportSubjectPublicKeyInfo(remoteRatchetPublicKey, out _);
            var secret = localRatchet.DeriveKeyMaterial(peer.PublicKey);
            try
            {
                var okm = Hkdf.DeriveKey(secret, Array.Empty<byte>(), DhRatchetInfo, 48);
                var key = new byte[32];
                var nonceBase = new byte[16];
                Buffer.BlockCopy(okm, 0, key, 0, 32);
                Buffer.BlockCopy(okm, 32, nonceBase, 0, 16);
                CryptographicOperations.ZeroMemory(okm);
                return new DirectionKeyMaterial(key, nonceBase);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);
            }
        }

        private void ReplaceRatchetState(ECDiffieHellman nextRatchet, byte[] nextPub)
        {
            var previousRatchet = _localRatchet;
            var previousPub = _localRatchetPublicKey;
            _localRatchet = nextRatchet;
            _localRatchetPublicKey = nextPub;
            previousRatchet.Dispose();
            CryptographicOperations.ZeroMemory(previousPub);
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

        private static void ReplaceOptionalKeyMaterial(ref byte[]? target, byte[] replacement)
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

        private static byte[] TryDecryptWithSkippedInboundEpochs(byte[] cipher, byte[] aad, ulong counter, List<SkippedInboundEpoch>? skippedEpochs)
        {
            if (skippedEpochs == null || skippedEpochs.Count == 0)
            {
                throw new InvalidDataException("Unable to decrypt frame with active ratchet state");
            }

            foreach (var skipped in skippedEpochs)
            {
                if (TryDecryptWithMaterial(cipher, aad, counter, skipped.Key, skipped.NonceBase, out var plain))
                {
                    return plain;
                }
            }

            throw new InvalidDataException("Unable to decrypt frame with active or skipped ratchet keys");
        }

        private void AddSkippedInboundEpochNoLock(byte[] key, byte[] nonceBase)
        {
            if (key.Length == 0 || nonceBase.Length == 0)
            {
                return;
            }

            _skippedInboundEpochs.Add(new SkippedInboundEpoch(key, nonceBase, DateTime.UtcNow));
            PruneSkippedInboundEpochsNoLock();
        }

        private void PruneSkippedInboundEpochsNoLock()
        {
            if (_skippedInboundEpochs.Count == 0)
            {
                return;
            }

            var cutoff = DateTime.UtcNow - SkippedInboundEpochTtl;
            for (var i = _skippedInboundEpochs.Count - 1; i >= 0; i--)
            {
                if (_skippedInboundEpochs[i].AddedUtc >= cutoff)
                {
                    continue;
                }

                var stale = _skippedInboundEpochs[i];
                CryptographicOperations.ZeroMemory(stale.Key);
                CryptographicOperations.ZeroMemory(stale.NonceBase);
                _skippedInboundEpochs.RemoveAt(i);
            }

            while (_skippedInboundEpochs.Count > MaxSkippedInboundEpochs)
            {
                var oldest = _skippedInboundEpochs[0];
                CryptographicOperations.ZeroMemory(oldest.Key);
                CryptographicOperations.ZeroMemory(oldest.NonceBase);
                _skippedInboundEpochs.RemoveAt(0);
            }
        }

        private readonly record struct DirectionKeyMaterial(byte[] Key, byte[] Base);
        private readonly record struct SkippedInboundEpoch(byte[] Key, byte[] NonceBase, DateTime AddedUtc);

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
            lock (_ratchetGate)
            {
                ReplaceKeyMaterial(ref _txKey, Array.Empty<byte>());
                ReplaceKeyMaterial(ref _rxKey, Array.Empty<byte>());
                ReplaceKeyMaterial(ref _txBase, Array.Empty<byte>());
                ReplaceKeyMaterial(ref _rxBase, Array.Empty<byte>());
                ReplaceOptionalKeyMaterial(ref _remoteRatchetPublicKey, Array.Empty<byte>());
                foreach (var skipped in _skippedInboundEpochs)
                {
                    CryptographicOperations.ZeroMemory(skipped.Key);
                    CryptographicOperations.ZeroMemory(skipped.NonceBase);
                }
                _skippedInboundEpochs.Clear();
                _localRatchet.Dispose();
                CryptographicOperations.ZeroMemory(_localRatchetPublicKey);
                _localRatchetPublicKey = Array.Empty<byte>();
                _dhRatchetState = DhRatchetState.Disabled;
            }
            _writeLock.Dispose();
        }
    }
}
