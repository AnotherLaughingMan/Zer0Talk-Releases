/*
    CountingStream: wraps a Stream and reports bytes read/written via callbacks.
    - Used to accumulate per-port traffic stats in NetworkService.
*/
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Zer0Talk.Utilities
{
    public sealed class CountingStream : Stream
    {
        private readonly Stream _inner;
        private readonly Action<long>? _onRead;
        private readonly Action<long>? _onWrite;
        public CountingStream(Stream inner, Action<long>? onRead = null, Action<long>? onWrite = null)
        {
            _inner = inner; _onRead = onRead; _onWrite = onWrite;
        }
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = _inner.Read(buffer, offset, count); if (n > 0) _onRead?.Invoke(n); return n;
        }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var memory = buffer.AsMemory(offset, count);
            var n = await _inner.ReadAsync(memory, cancellationToken).ConfigureAwait(false);
            if (n > 0) _onRead?.Invoke(n);
            return n;
        }
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var n = await _inner.ReadAsync(buffer, cancellationToken); if (n > 0) _onRead?.Invoke(n); return n;
        }
#endif
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count); if (count > 0) _onWrite?.Invoke(count);
        }
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var memory = buffer.AsMemory(offset, count);
            await _inner.WriteAsync(memory, cancellationToken).ConfigureAwait(false);
            if (count > 0) _onWrite?.Invoke(count);
        }
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Length > 0) _onWrite?.Invoke(buffer.Length);
            return _inner.WriteAsync(buffer, cancellationToken);
        }
#endif
        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
