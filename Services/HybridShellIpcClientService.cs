using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Zer0Talk.Services
{
    /// <summary>
    /// Shell-side IPC client scaffold for hybrid migration.
    /// Connects to the runtime named-pipe host, negotiates capabilities,
    /// and routes snapshot/delta events to typed callbacks.
    /// </summary>
    public sealed class HybridShellIpcClientService : IDisposable
    {
        private const int DefaultConnectTimeoutMs = 5000;
        private const int DefaultRequestTimeoutMs = 5000;

        private static readonly JsonSerializerOptions DeserializeOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly string _pipeName;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingResponses = new(StringComparer.Ordinal);
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        private NamedPipeClientStream? _stream;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private CancellationTokenSource? _cts;
        private Task? _readLoopTask;
        private int _requestSequence;
        private bool _disposed;

        public HybridShellIpcClientService(string? pipeName = null)
        {
            _pipeName = string.IsNullOrWhiteSpace(pipeName) ? HybridIpcHostService.DefaultPipeName : pipeName;
            Capabilities = HybridIpcCapabilities.Empty;
        }

        public bool IsConnected => _stream?.IsConnected == true;

        public HybridIpcCapabilities Capabilities { get; private set; }

        public bool SupportsContactsDelta => Capabilities.Events.Contains(ContactsIpcEndpointService.EventContactsListDeltaChanged);

        public bool SupportsUnreadCountDelta => Capabilities.Events.Contains(UnreadIpcEndpointService.EventCountChanged);

        public event Action<HybridIpcCapabilities>? CapabilitiesNegotiated;
        public event Action<ContactsSnapshotDto>? ContactsSnapshotReceived;
        public event Action<ContactsIpcEndpointService.ContactsListDeltaDto>? ContactsDeltaReceived;
        public event Action<UnreadSnapshotDto>? UnreadSnapshotReceived;
        public event Action<string, int>? UnreadCountDeltaReceived;

        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (IsConnected)
            {
                return true;
            }

            try
            {
                _stream = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                using var connectTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectTimeoutCts.CancelAfter(DefaultConnectTimeoutMs);

                await _stream.ConnectAsync(connectTimeoutCts.Token).ConfigureAwait(false);
                _reader = new StreamReader(_stream);
                _writer = new StreamWriter(_stream) { AutoFlush = true };
                _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _readLoopTask = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);

                var capsResponse = await SendRequestAsync(HybridIpcHostService.CommandGetCapabilities, DefaultRequestTimeoutMs, _cts.Token).ConfigureAwait(false);
                if (!TryParseResponsePayload(capsResponse, out var payloadElement))
                {
                    await DisconnectAsync().ConfigureAwait(false);
                    return false;
                }

                if (!TryParseCapabilities(payloadElement, out var capabilities))
                {
                    await DisconnectAsync().ConfigureAwait(false);
                    return false;
                }

                Capabilities = capabilities;
                try { CapabilitiesNegotiated?.Invoke(capabilities); } catch { }
                return true;
            }
            catch
            {
                await DisconnectAsync().ConfigureAwait(false);
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            var localCts = _cts;
            _cts = null;
            try { localCts?.Cancel(); } catch { }

            foreach (var pending in _pendingResponses)
            {
                try { pending.Value.TrySetCanceled(); } catch { }
            }
            _pendingResponses.Clear();

            try
            {
                if (_readLoopTask != null)
                {
                    await _readLoopTask.ConfigureAwait(false);
                }
            }
            catch { }
            _readLoopTask = null;

            try { _reader?.Dispose(); } catch { }
            try { _writer?.Dispose(); } catch { }
            try { _stream?.Dispose(); } catch { }
            try { localCts?.Dispose(); } catch { }
            _reader = null;
            _writer = null;
            _stream = null;
        }

        public async Task<ContactsSnapshotDto?> RequestContactsSnapshotAsync(CancellationToken cancellationToken = default)
        {
            if (!IsConnected) return null;

            var responseLine = await SendRequestAsync(ContactsIpcEndpointService.CommandGetContactsList, DefaultRequestTimeoutMs, cancellationToken).ConfigureAwait(false);
            if (!TryParseResponsePayload(responseLine, out var payload)) return null;

            try
            {
                return JsonSerializer.Deserialize<ContactsSnapshotDto>(payload.GetRawText(), DeserializeOptions);
            }
            catch
            {
                return null;
            }
        }

        public async Task<UnreadSnapshotDto?> RequestUnreadSnapshotAsync(CancellationToken cancellationToken = default)
        {
            if (!IsConnected) return null;

            var responseLine = await SendRequestAsync(UnreadIpcEndpointService.CommandGetSnapshot, DefaultRequestTimeoutMs, cancellationToken).ConfigureAwait(false);
            if (!TryParseResponsePayload(responseLine, out var payload)) return null;

            try
            {
                return JsonSerializer.Deserialize<UnreadSnapshotDto>(payload.GetRawText(), DeserializeOptions);
            }
            catch
            {
                return null;
            }
        }

        public static bool TryParseCapabilities(JsonElement payload, out HybridIpcCapabilities capabilities)
        {
            capabilities = HybridIpcCapabilities.Empty;
            if (payload.ValueKind != JsonValueKind.Object) return false;

            try
            {
                var protocolVersion = payload.TryGetProperty("protocolVersion", out var p) && p.TryGetInt32(out var parsedProtocol)
                    ? parsedProtocol
                    : 0;

                var commands = ParseStringSet(payload, "commands");
                var events = ParseStringSet(payload, "events");
                var schemas = ParseIntMap(payload, "schemas");

                capabilities = new HybridIpcCapabilities(protocolVersion, commands, events, schemas);
                return true;
            }
            catch
            {
                capabilities = HybridIpcCapabilities.Empty;
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { DisconnectAsync().GetAwaiter().GetResult(); } catch { }
            try { _writeLock.Dispose(); } catch { }
        }

        private async Task ReadLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line;
                try
                {
                    if (_reader == null) break;
                    line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    break;
                }

                if (line == null) break;
                DispatchIncomingLine(line);
            }
        }

        private void DispatchIncomingLine(string line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var messageType = GetString(root, "type");

                if (string.Equals(messageType, "response", StringComparison.OrdinalIgnoreCase))
                {
                    var id = GetString(root, "id");
                    if (!string.IsNullOrWhiteSpace(id) && _pendingResponses.TryRemove(id, out var waiter))
                    {
                        waiter.TrySetResult(line);
                    }
                    return;
                }

                if (string.Equals(messageType, "event", StringComparison.OrdinalIgnoreCase))
                {
                    var eventName = GetString(root, "event");
                    if (!root.TryGetProperty("payload", out var payload)) return;
                    DispatchEvent(eventName, payload);
                }
            }
            catch { }
        }

        private void DispatchEvent(string eventName, JsonElement payload)
        {
            try
            {
                if (string.Equals(eventName, ContactsIpcEndpointService.EventContactsListChanged, StringComparison.OrdinalIgnoreCase))
                {
                    var snapshot = JsonSerializer.Deserialize<ContactsSnapshotDto>(payload.GetRawText(), DeserializeOptions);
                    if (snapshot != null) ContactsSnapshotReceived?.Invoke(snapshot);
                    return;
                }

                if (string.Equals(eventName, ContactsIpcEndpointService.EventContactsListDeltaChanged, StringComparison.OrdinalIgnoreCase))
                {
                    var delta = JsonSerializer.Deserialize<ContactsIpcEndpointService.ContactsListDeltaDto>(payload.GetRawText(), DeserializeOptions);
                    if (delta != null) ContactsDeltaReceived?.Invoke(delta);
                    return;
                }

                if (string.Equals(eventName, UnreadIpcEndpointService.EventSnapshotChanged, StringComparison.OrdinalIgnoreCase))
                {
                    var snapshot = JsonSerializer.Deserialize<UnreadSnapshotDto>(payload.GetRawText(), DeserializeOptions);
                    if (snapshot != null) UnreadSnapshotReceived?.Invoke(snapshot);
                    return;
                }

                if (string.Equals(eventName, UnreadIpcEndpointService.EventCountChanged, StringComparison.OrdinalIgnoreCase))
                {
                    var peerUid = GetString(payload, "peerUid");
                    var unreadCount = payload.TryGetProperty("unreadCount", out var c) && c.TryGetInt32(out var parsedCount) ? parsedCount : 0;
                    if (!string.IsNullOrWhiteSpace(peerUid))
                    {
                        UnreadCountDeltaReceived?.Invoke(peerUid, unreadCount);
                    }
                }
            }
            catch { }
        }

        private async Task<string> SendRequestAsync(string command, int timeoutMs, CancellationToken cancellationToken)
        {
            if (_writer == null) throw new InvalidOperationException("IPC client is not connected.");

            var requestId = Interlocked.Increment(ref _requestSequence).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var request = $"{{\"type\":\"request\",\"id\":\"{requestId}\",\"command\":{JsonSerializer.Serialize(command)}}}";
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingResponses[requestId] = tcs;

            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _writer.WriteLineAsync(request).ConfigureAwait(false);
                await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeoutMs);

            try
            {
                return await tcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            finally
            {
                _pendingResponses.TryRemove(requestId, out _);
            }
        }

        private static bool TryParseResponsePayload(string responseLine, out JsonElement payload)
        {
            payload = default;

            try
            {
                using var doc = JsonDocument.Parse(responseLine);
                var root = doc.RootElement;
                var ok = root.TryGetProperty("ok", out var okValue) && okValue.ValueKind == JsonValueKind.True;
                if (!ok) return false;
                if (!root.TryGetProperty("payload", out var payloadValue)) return false;

                payload = payloadValue.Clone();
                return true;
            }
            catch
            {
                payload = default;
                return false;
            }
        }

        private static HashSet<string> ParseStringSet(JsonElement payload, string propertyName)
        {
            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!payload.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            {
                return values;
            }

            foreach (var item in property.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) continue;
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
            }
            return values;
        }

        private static IReadOnlyDictionary<string, int> ParseIntMap(JsonElement payload, string propertyName)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (!payload.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Object)
            {
                return map;
            }

            foreach (var pair in property.EnumerateObject())
            {
                if (pair.Value.TryGetInt32(out var value))
                {
                    map[pair.Name] = value;
                }
            }

            return map;
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            {
                return string.Empty;
            }

            return property.GetString() ?? string.Empty;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HybridShellIpcClientService));
            }
        }
    }

    public sealed record HybridIpcCapabilities(
        int ProtocolVersion,
        IReadOnlySet<string> Commands,
        IReadOnlySet<string> Events,
        IReadOnlyDictionary<string, int> Schemas)
    {
        public static HybridIpcCapabilities Empty { get; } = new(
            0,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
    }
}
