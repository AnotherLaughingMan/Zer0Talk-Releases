using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Sodium;

using Xunit;

using Zer0Talk.Models;
using Zer0Talk.Services;
using Zer0Talk.Utilities;

namespace Zer0Talk.Tests;

public class NetworkServiceRatchetIntegrationTests
{
    [Fact]
    public async Task NetworkService_IdentityAnnounceAndRatchetRotation_RoundTripsMessages()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        using var natA = new NatTraversalService();
        using var natB = new NatTraversalService();

        var identityA = CreateIdentity("alice");
        var identityB = CreateIdentity("bob");

        using var netA = new NetworkService(identityA, natA);
        using var netB = new NetworkService(identityB, natB);

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var acceptTask = listener.AcceptTcpClientAsync(cts.Token);
        using var tcpA = new TcpClient();
        await tcpA.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, cts.Token);
        using var tcpB = await acceptTask;

        using var ecdhA = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var ecdhB = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var dhPubA = ecdhA.PublicKey.ExportSubjectPublicKeyInfo();
        var dhPubB = ecdhB.PublicKey.ExportSubjectPublicKeyInfo();

        var keyAtoB = RandomNumberGenerator.GetBytes(32);
        var keyBtoA = RandomNumberGenerator.GetBytes(32);
        var baseAtoB = RandomNumberGenerator.GetBytes(16);
        var baseBtoA = RandomNumberGenerator.GetBytes(16);

        using var transportA = new AeadTransport(tcpA.GetStream(), keyAtoB, keyBtoA, baseAtoB, baseBtoA, ratchetIntervalFrames: 8);
        using var transportB = new AeadTransport(tcpB.GetStream(), keyBtoA, keyAtoB, baseBtoA, baseAtoB, ratchetIntervalFrames: 8);

        var sessionsA = GetPrivateField<ConcurrentDictionary<string, AeadTransport>>(netA, "_sessions");
        var sessionsB = GetPrivateField<ConcurrentDictionary<string, AeadTransport>>(netB, "_sessions");
        sessionsA[identityB.UID] = transportA;
        sessionsB[identityA.UID] = transportB;

        var sessionModesA = GetPrivateField<ConcurrentDictionary<string, ConnectionMode>>(netA, "_sessionModes");
        var sessionModesB = GetPrivateField<ConcurrentDictionary<string, ConnectionMode>>(netB, "_sessionModes");
        sessionModesA[identityB.UID] = ConnectionMode.Direct;
        sessionModesB[identityA.UID] = ConnectionMode.Direct;

        var handshakeKeysA = GetPrivateField<ConcurrentDictionary<AeadTransport, byte[]>>(netA, "_handshakePeerKeys");
        var handshakeKeysB = GetPrivateField<ConcurrentDictionary<AeadTransport, byte[]>>(netB, "_handshakePeerKeys");
        handshakeKeysA[transportA] = dhPubB;
        handshakeKeysB[transportB] = dhPubA;

        var messagesPerDirection = 16; // > ratchetIntervalFrames(8) to force outbound ratchet rotation
        var receivedByA = 0;
        var receivedByB = 0;

        netA.ChatMessageReceived += (peerUid, _, _) =>
        {
            if (SameUid(peerUid, identityB.UID))
            {
                Interlocked.Increment(ref receivedByA);
            }
        };

        netB.ChatMessageReceived += (peerUid, _, _) =>
        {
            if (SameUid(peerUid, identityA.UID))
            {
                Interlocked.Increment(ref receivedByB);
            }
        };

        var rxLoopA = RunInboundLoopAsync(netA, identityB.UID, transportA, cts.Token);
        var rxLoopB = RunInboundLoopAsync(netB, identityA.UID, transportB, cts.Token);

        await InvokePrivateAsync(netA, "SendIdentityAnnounceAsync", transportA, dhPubA, cts.Token);
        await InvokePrivateAsync(netB, "SendIdentityAnnounceAsync", transportB, dhPubB, cts.Token);

        await WaitForConditionAsync(() => netA.GetPeerVersion(identityB.UID) == AppInfo.Version, TimeSpan.FromSeconds(5), cts.Token);
        await WaitForConditionAsync(() => netB.GetPeerVersion(identityA.UID) == AppInfo.Version, TimeSpan.FromSeconds(5), cts.Token);

        Assert.Equal(AppInfo.Version, netA.GetPeerVersion(identityB.UID));
        Assert.Equal(AppInfo.Version, netB.GetPeerVersion(identityA.UID));

        for (var i = 0; i < messagesPerDirection; i++)
        {
            var ok = await netA.SendChatAsync(identityB.UID, Guid.NewGuid(), $"a2b-{i}", cts.Token);
            Assert.True(ok);
        }

        for (var i = 0; i < messagesPerDirection; i++)
        {
            var ok = await netB.SendChatAsync(identityA.UID, Guid.NewGuid(), $"b2a-{i}", cts.Token);
            Assert.True(ok);
        }

        // Give inbound handlers a short window to dispatch events without requiring exact callback counts.
        await Task.Delay(300, cts.Token);

        Assert.True(netA.HasEncryptedSession(identityB.UID));
        Assert.True(netB.HasEncryptedSession(identityA.UID));
        Assert.True(Volatile.Read(ref receivedByA) > 0);
        Assert.True(Volatile.Read(ref receivedByB) > 0);

        Assert.True(transportA.OutboundRatchetRotations >= 1);
        Assert.True(transportA.InboundRatchetRotations >= 1);
        Assert.True(transportB.OutboundRatchetRotations >= 1);
        Assert.True(transportB.InboundRatchetRotations >= 1);
        Assert.Equal(AeadTransport.DhRatchetState.Active, transportA.RatchetLifecycle);
        Assert.Equal(AeadTransport.DhRatchetState.Active, transportB.RatchetLifecycle);

        cts.Cancel();
        await Task.WhenAll(rxLoopA, rxLoopB);
    }

    private static async Task RunInboundLoopAsync(NetworkService service, string peerUid, AeadTransport transport, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var data = await transport.ReadAsync(ct);
                await InvokePrivateAsync(service, "HandleInboundFrameAsync", peerUid, transport, data, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static async Task InvokePrivateAsync(object instance, string methodName, params object[] args)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(instance.GetType().FullName, methodName);
        var result = method.Invoke(instance, args)
            ?? throw new InvalidOperationException($"{methodName} returned null");
        if (result is Task task)
        {
            await task;
        }
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(instance.GetType().FullName, fieldName);
        return (T)(field.GetValue(instance)
            ?? throw new InvalidOperationException($"Field '{fieldName}' was null"));
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout, CancellationToken ct)
    {
        var start = DateTime.UtcNow;
        while (!condition())
        {
            ct.ThrowIfCancellationRequested();
            if ((DateTime.UtcNow - start) > timeout)
            {
                throw new TimeoutException("Condition was not met before timeout.");
            }

            await Task.Delay(50, ct);
        }
    }

    private static bool SameUid(string left, string right)
    {
        return string.Equals(TrimUid(left), TrimUid(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimUid(string uid)
    {
        if (string.IsNullOrWhiteSpace(uid)) return string.Empty;
        return uid.StartsWith("usr-", StringComparison.OrdinalIgnoreCase) ? uid.Substring(4) : uid;
    }

    private static IdentityService CreateIdentity(string username)
    {
        var keyPair = PublicKeyAuth.GenerateKeyPair();
        var uid = IdentityService.ComputeUidFromPublicKey(keyPair.PublicKey);

        var account = new AccountData
        {
            UID = uid,
            Username = username,
            DisplayName = username,
            PublicKey = keyPair.PublicKey,
            PrivateKey = keyPair.PrivateKey,
            CreatedAtUtc = DateTime.UtcNow
        };

        var identity = new IdentityService();
        identity.LoadFromAccount(account);
        return identity;
    }
}
