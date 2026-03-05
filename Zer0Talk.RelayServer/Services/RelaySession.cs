using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Zer0Talk.RelayServer.Services;

public sealed class RelaySession
{
    private readonly RelayRequest _leftRequest;
    private readonly RelayRequest _rightRequest;
    private readonly TcpClient _leftClient;
    private readonly TcpClient _rightClient;
    private readonly NetworkStream _leftStream;
    private readonly NetworkStream _rightStream;

    public RelaySession(RelayRequest leftRequest, TcpClient leftClient, NetworkStream leftStream,
        RelayRequest rightRequest, TcpClient rightClient, NetworkStream rightStream)
    {
        _leftRequest = leftRequest;
        _rightRequest = rightRequest;
        _leftClient = leftClient;
        _rightClient = rightClient;
        _leftStream = leftStream;
        _rightStream = rightStream;
        SessionKey = leftRequest.SessionKey;
    }

    public string SessionKey { get; }
    public DateTime StartedUtc { get; } = DateTime.UtcNow;
    public string LeftUid => _leftRequest.Uid;
    public string RightUid => _rightRequest.Uid;
    public bool IsConnected => IsClientConnected(_leftClient) && IsClientConnected(_rightClient);

    internal static bool IsClientConnected(TcpClient client)
    {
        try
        {
            if (client == null || client.Client == null) return false;
            var socket = client.Client;
            if (!socket.Connected) return false;

            // A readable socket with no available bytes indicates remote FIN/close.
            var closed = socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0;
            return !closed;
        }
        catch
        {
            return false;
        }
    }

    public NetworkStream GetOtherStream(NetworkStream current)
    {
        if (current == _leftStream) return _rightStream;
        if (current == _rightStream) return _leftStream;
        throw new InvalidOperationException("Stream not part of this session");
    }

    public async Task RunAsync(RelayForwarder forwarder, CancellationToken ct)
    {
        try
        {
            await forwarder.RunAsync(_leftStream, _rightStream, ct);
        }
        finally
        {
            Close();
        }
    }

    public void Close()
    {
        SafeClose(_leftClient);
        SafeClose(_rightClient);
    }

    private static void SafeClose(TcpClient client)
    {
        try { client.Close(); } catch { }
    }
}
