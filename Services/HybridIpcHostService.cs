using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Zer0Talk.Services
{
    /// <summary>
    /// Runtime IPC host for hybrid shell integrations.
    /// Uses newline-delimited JSON over a named pipe.
    /// </summary>
    public sealed class HybridIpcHostService : IDisposable
    {
        public const string DefaultPipeName = "zer0talk.hybrid.ipc.v1";
        public const string CommandGetCapabilities = "ipc.capabilities.get";
        public const int ProtocolVersion = 1;
        private const int MaxConnectedClients = 8;
        private const int MaxFrameChars = 32768;
        private const int RequestsPerMinutePerClient = 240;

        private readonly ContactsIpcEndpointService _contactsEndpoint;
        private readonly UnreadIpcEndpointService _unreadEndpoint;
        private readonly string _pipeName;
        private readonly ConcurrentDictionary<Guid, ClientConnection> _clients = new();

        private readonly Action<string, string> _contactsEventHandler;
        private readonly Action<string, string> _unreadEventHandler;

        private CancellationTokenSource? _cts;
        private Task? _acceptLoopTask;
        private int _started;

        public HybridIpcHostService(
            ContactsIpcEndpointService contactsEndpoint,
            UnreadIpcEndpointService unreadEndpoint,
            string? pipeName = null)
        {
            _contactsEndpoint = contactsEndpoint ?? throw new ArgumentNullException(nameof(contactsEndpoint));
            _unreadEndpoint = unreadEndpoint ?? throw new ArgumentNullException(nameof(unreadEndpoint));
            _pipeName = string.IsNullOrWhiteSpace(pipeName) ? DefaultPipeName : pipeName;

            _contactsEventHandler = OnEndpointNotification;
            _unreadEventHandler = OnEndpointNotification;
        }

        public bool IsRunning => Volatile.Read(ref _started) == 1;

        public bool Start()
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            {
                return true;
            }

            try
            {
                _cts = new CancellationTokenSource();
                _contactsEndpoint.NotificationJsonReady += _contactsEventHandler;
                _unreadEndpoint.NotificationJsonReady += _unreadEventHandler;
                _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
                return true;
            }
            catch
            {
                Stop();
                return false;
            }
        }

        public void Stop()
        {
            if (Interlocked.CompareExchange(ref _started, 0, 1) != 1)
            {
                return;
            }

            try { _contactsEndpoint.NotificationJsonReady -= _contactsEventHandler; } catch { }
            try { _unreadEndpoint.NotificationJsonReady -= _unreadEventHandler; } catch { }

            try { _cts?.Cancel(); } catch { }

            foreach (var kvp in _clients)
            {
                try { kvp.Value.Dispose(); } catch { }
            }
            _clients.Clear();

            try { _acceptLoopTask?.Wait(TimeSpan.FromSeconds(1)); } catch { }

            try { _cts?.Dispose(); } catch { }
            _cts = null;
            _acceptLoopTask = null;
        }

        public void Dispose()
        {
            Stop();
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                NamedPipeServerStream? server = null;
                try
                {
                    server = CreateServer();
                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                    _ = Task.Run(() => HandleClientAsync(server, token), token);
                }
                catch (OperationCanceledException)
                {
                    try { server?.Dispose(); } catch { }
                    break;
                }
                catch
                {
                    try { server?.Dispose(); } catch { }
                    await Task.Delay(250, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        private NamedPipeServerStream CreateServer()
        {
            return new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
        }

        private async Task HandleClientAsync(NamedPipeServerStream server, CancellationToken token)
        {
            var clientId = Guid.NewGuid();
            ClientConnection? client = null;
            try
            {
                var reader = new StreamReader(server);
                var writer = new StreamWriter(server) { AutoFlush = true };
                client = new ClientConnection(server, writer);

                if (_clients.Count >= MaxConnectedClients)
                {
                    await client.WriteLineAsync(BuildErrorResponse(string.Empty, string.Empty, "server-busy"), token).ConfigureAwait(false);
                    return;
                }

                _clients[clientId] = client;

                while (!token.IsCancellationRequested && server.IsConnected)
                {
                    string? line;
                    try
                    {
                        line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        break;
                    }

                    if (line == null)
                    {
                        break;
                    }

                    if (line.Length > MaxFrameChars)
                    {
                        await client.WriteLineAsync(BuildErrorResponse(string.Empty, string.Empty, "frame-too-large"), token).ConfigureAwait(false);
                        break;
                    }

                    if (!client.TryAcceptRequest())
                    {
                        await client.WriteLineAsync(BuildErrorResponse(string.Empty, string.Empty, "rate-limit"), token).ConfigureAwait(false);
                        continue;
                    }

                    var responseLine = ProcessRequestLine(line);
                    await client.WriteLineAsync(responseLine, token).ConfigureAwait(false);
                }
            }
            finally
            {
                _clients.TryRemove(clientId, out _);
                try { client?.Dispose(); } catch { }
            }
        }

        private string ProcessRequestLine(string line)
        {
            string requestId = string.Empty;
            string command = string.Empty;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                var messageType = GetString(root, "type");
                if (!string.Equals(messageType, "request", StringComparison.OrdinalIgnoreCase))
                {
                    return BuildErrorResponse(requestId, command, "invalid-message-type");
                }

                requestId = GetString(root, "id");
                command = GetString(root, "command");

                if (string.IsNullOrWhiteSpace(command))
                {
                    return BuildErrorResponse(requestId, command, "missing-command");
                }

                if (string.Equals(command, CommandGetCapabilities, StringComparison.OrdinalIgnoreCase))
                {
                    return BuildOkResponse(requestId, command, BuildCapabilitiesPayloadJson());
                }

                if (_contactsEndpoint.TryHandleRequest(command, out var contactsPayload))
                {
                    return BuildOkResponse(requestId, command, contactsPayload);
                }

                if (_unreadEndpoint.TryHandleRequest(command, out var unreadPayload))
                {
                    return BuildOkResponse(requestId, command, unreadPayload);
                }

                return BuildErrorResponse(requestId, command, "unknown-command");
            }
            catch
            {
                return BuildErrorResponse(requestId, command, "invalid-json");
            }
        }

        private void OnEndpointNotification(string eventName, string payloadJson)
        {
            var line = BuildEventEnvelope(eventName, payloadJson);
            _ = Task.Run(() => BroadcastAsync(line));
        }

        private async Task BroadcastAsync(string line)
        {
            var snapshot = _clients.Values;
            foreach (var client in snapshot)
            {
                try
                {
                    await client.WriteLineAsync(line, CancellationToken.None).ConfigureAwait(false);
                }
                catch { }
            }
        }

        private static string BuildOkResponse(string requestId, string command, string payloadJson)
        {
            var safeId = JsonSerializer.Serialize(requestId ?? string.Empty);
            var safeCommand = JsonSerializer.Serialize(command ?? string.Empty);
            var safePayload = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson;
            return $"{{\"type\":\"response\",\"id\":{safeId},\"ok\":true,\"command\":{safeCommand},\"payload\":{safePayload}}}";
        }

        private static string BuildErrorResponse(string requestId, string command, string error)
        {
            var safeId = JsonSerializer.Serialize(requestId ?? string.Empty);
            var safeCommand = JsonSerializer.Serialize(command ?? string.Empty);
            var safeError = JsonSerializer.Serialize(error ?? "error");
            return $"{{\"type\":\"response\",\"id\":{safeId},\"ok\":false,\"command\":{safeCommand},\"error\":{safeError}}}";
        }

        private static string BuildEventEnvelope(string eventName, string payloadJson)
        {
            var safeEventName = JsonSerializer.Serialize(eventName ?? string.Empty);
            var safePayload = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson;
            return $"{{\"type\":\"event\",\"event\":{safeEventName},\"payload\":{safePayload}}}";
        }

        private static string BuildCapabilitiesPayloadJson()
        {
            var payload = new
            {
                protocolVersion = ProtocolVersion,
                command = CommandGetCapabilities,
                commands = new[]
                {
                    CommandGetCapabilities,
                    ContactsIpcEndpointService.CommandGetContactsList,
                    UnreadIpcEndpointService.CommandGetSnapshot
                },
                events = new[]
                {
                    ContactsIpcEndpointService.EventContactsListChanged,
                    ContactsIpcEndpointService.EventContactsListDeltaChanged,
                    UnreadIpcEndpointService.EventSnapshotChanged,
                    UnreadIpcEndpointService.EventCountChanged
                },
                schemas = new
                {
                    contactsSnapshot = ContactsBridgeService.SnapshotSchemaVersion,
                    contactsDelta = ContactsIpcEndpointService.DeltaSchemaVersion,
                    unreadSnapshot = UnreadBridgeService.SnapshotSchemaVersion,
                    unreadCountDelta = UnreadIpcEndpointService.CountDeltaSchemaVersion
                },
                limits = new
                {
                    maxConnectedClients = MaxConnectedClients,
                    maxFrameChars = MaxFrameChars,
                    requestsPerMinutePerClient = RequestsPerMinutePerClient
                }
            };

            return JsonSerializer.Serialize(payload);
        }

        private static string GetString(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var value)) return string.Empty;
            if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? string.Empty;
            return value.ToString() ?? string.Empty;
        }

        private sealed class ClientConnection : IDisposable
        {
            private readonly Stream _stream;
            private readonly StreamWriter _writer;
            private readonly SemaphoreSlim _writeLock = new(1, 1);
            private readonly object _rateLock = new();
            private DateTime _windowStartUtc = DateTime.UtcNow;
            private int _requestsInWindow;

            public ClientConnection(Stream stream, StreamWriter writer)
            {
                _stream = stream;
                _writer = writer;
            }

            public async Task WriteLineAsync(string line, CancellationToken token)
            {
                await _writeLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    await _writer.WriteLineAsync(line).ConfigureAwait(false);
                    await _writer.FlushAsync(token).ConfigureAwait(false);
                }
                finally
                {
                    _writeLock.Release();
                }
            }

            public bool TryAcceptRequest()
            {
                lock (_rateLock)
                {
                    var now = DateTime.UtcNow;
                    if ((now - _windowStartUtc) >= TimeSpan.FromMinutes(1))
                    {
                        _windowStartUtc = now;
                        _requestsInWindow = 0;
                    }

                    if (_requestsInWindow >= RequestsPerMinutePerClient)
                    {
                        return false;
                    }

                    _requestsInWindow++;
                    return true;
                }
            }

            public void Dispose()
            {
                try { _writer.Dispose(); } catch { }
                try { _stream.Dispose(); } catch { }
                try { _writeLock.Dispose(); } catch { }
            }
        }
    }
}
