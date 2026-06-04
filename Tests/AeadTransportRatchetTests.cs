using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

using Zer0Talk.Utilities;

namespace Zer0Talk.Tests;

public class AeadTransportRatchetTests
{
    [Fact]
    public async Task DhRatchetFrame_RotatesKeysAndPreservesRoundTrip()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var acceptTask = listener.AcceptTcpClientAsync(cts.Token);

        using var clientA = new TcpClient();
        await clientA.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, cts.Token);
        using var clientB = await acceptTask;

        var aToBKey = RandomNumberGenerator.GetBytes(32);
        var bToAKey = RandomNumberGenerator.GetBytes(32);
        var aToBBase = RandomNumberGenerator.GetBytes(16);
        var bToABase = RandomNumberGenerator.GetBytes(16);

        using var transportA = new AeadTransport(clientA.GetStream(), aToBKey, bToAKey, aToBBase, bToABase, ratchetIntervalFrames: 1);
        using var transportB = new AeadTransport(clientB.GetStream(), bToAKey, aToBKey, bToABase, aToBBase, ratchetIntervalFrames: 1);

        Assert.True(transportA.TryEnableDhRatchet(transportB.GetRatchetPublicKey()));
        Assert.True(transportB.TryEnableDhRatchet(transportA.GetRatchetPublicKey()));

        var firstPayload = Encoding.UTF8.GetBytes("first-message");
        await transportA.WriteAsync(firstPayload, cts.Token);

        var ratchetFrameForB = await transportB.ReadAsync(cts.Token);
        Assert.True(ApplyRatchetFrame(transportB, ratchetFrameForB));
        var deliveredToB = await transportB.ReadAsync(cts.Token);
        Assert.Equal(firstPayload, deliveredToB);

        var replyPayload = Encoding.UTF8.GetBytes("reply-message");
        await transportB.WriteAsync(replyPayload, cts.Token);

        var ratchetFrameForA = await transportA.ReadAsync(cts.Token);
        Assert.True(ApplyRatchetFrame(transportA, ratchetFrameForA));
        var deliveredToA = await transportA.ReadAsync(cts.Token);
        Assert.Equal(replyPayload, deliveredToA);
    }

    private static bool ApplyRatchetFrame(AeadTransport transport, byte[] frame)
    {
        if (frame.Length < 3 || frame[0] != AeadTransport.DhRatchetFrameOpcode)
        {
            return false;
        }

        var keyLen = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(1, 2));
        if (keyLen == 0 || frame.Length < 3 + keyLen)
        {
            return false;
        }

        var key = frame.AsSpan(3, keyLen).ToArray();
        return transport.ApplyInboundDhRatchet(key);
    }
}