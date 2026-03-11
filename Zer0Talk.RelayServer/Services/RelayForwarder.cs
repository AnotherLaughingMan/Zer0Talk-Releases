using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Zer0Talk.RelayServer.Services;

public sealed class RelayForwarder
{
    private readonly int _bufferSize;

    public RelayForwarder(int bufferSize)
    {
        _bufferSize = bufferSize;
    }

    public async Task RunAsync(NetworkStream left, NetworkStream right, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var leftToRight = CopyAsync(left, right, "L→R", cts.Token);
        var rightToLeft = CopyAsync(right, left, "R→L", cts.Token);

        var completed = await Task.WhenAny(leftToRight, rightToLeft);
        var direction = completed == leftToRight ? "L→R" : "R→L";
        
        cts.Cancel();
        try { await Task.WhenAll(leftToRight, rightToLeft); } catch { }
        
        if (completed.IsFaulted)
        {
            throw new IOException($"Relay copy {direction} faulted: {completed.Exception?.GetBaseException().Message}");
        }
    }

    private async Task CopyAsync(Stream source, Stream target, string direction, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(_bufferSize);
        try
        {
        ulong bytesTransferred = 0;
        while (!ct.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await source.ReadAsync(buffer.AsMemory(0, _bufferSize), ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                throw new IOException($"{direction} read failed after {bytesTransferred} bytes: {ex.Message}", ex);
            }

            if (read <= 0)
            {
                if (!ct.IsCancellationRequested && bytesTransferred < 100)
                {
                    throw new IOException($"{direction} closed early ({bytesTransferred} bytes)");
                }
                break;
            }

            bytesTransferred += (ulong)read;

            try
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                // TCP_NODELAY is set on all relay sockets — flushing after every write is redundant
                // (NetworkStream.WriteAsync already sends immediately). Calling FlushAsync here
                // adds a syscall per frame with no throughput benefit.
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                throw new IOException($"{direction} write failed after {bytesTransferred} bytes: {ex.Message}", ex);
            }
        }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
