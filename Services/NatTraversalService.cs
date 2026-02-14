/*
    NAT traversal stubs: STUN-like discovery, UPnP/PCP attempts, and candidate gathering.
    - Prepares endpoints for NetworkService; specifics can vary by platform.
*/
// TODO[ANCHOR]: NatTraversalService - STUN/UPnP discovery and mapping
using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Zer0Talk.Utilities;

namespace Zer0Talk.Services
{
    // Minimal NAT traversal helper. Real hole punching requires a rendezvous/mediator.
    public class NatTraversalService : IDisposable
    {
        // Unified mapping state machine for consistent UI across view models
        public enum MappingState
        {
            Idle,
            Discovering,
            GatewayDiscovered,
            Mapping,
            Mapped,          // Mapped but not yet verified
            Verified,        // Mapping verified + (optionally) hairpin reachable
            HairpinFailed,   // Mapping ok but hairpin test failed
            Unmapped,
            Failed,
            NoGateway,
            Error
        }

        public string Status { get; private set; } = "Idle";
        public MappingState State { get; private set; } = MappingState.Idle;
    // Separate hairpin result surfaced independently of the combined MappingVerification string
    public string HairpinStatus { get; private set; } = "n/a";
        public event Action? Changed; // Raised when significant NAT state changes
        private Utilities.UpnpClient? _upnp;
        private int? _mappedTcpPort;
        private int? _mappedUdpPort;
        public int? MappedTcpPort => _mappedTcpPort;
        public int? MappedUdpPort => _mappedUdpPort;
        private DateTime _lastDiscoverUtc = DateTime.MinValue; // throttle UPnP discovery
        private readonly System.Threading.SemaphoreSlim _sync = new(1, 1); // serialize NAT ops to avoid races
        // Expose last verification result for UI
        public string MappingVerification { get; private set; } = string.Empty;
        // Diagnostics surface for Monitoring
        public string? SelectedServiceType => _upnp?.SelectedServiceType;
        public System.Collections.Generic.IReadOnlyList<string> AvailableServiceTypes => _upnp?.AvailableServiceTypes ?? System.Array.Empty<string>();
        public IPAddress? RouterAddress => _upnp?.RouterAddress;
        public IPAddress? ExternalIPAddress { get; private set; }
        public DateTime? LastMappingAttemptUtc { get; private set; }
        public DateTime? LastVerificationUtc { get; private set; }
        public string LastPunchStatus { get; private set; } = string.Empty;
        public DateTime? LastPunchAttemptUtc { get; private set; }

        private System.Threading.Timer? _autoVerifyTimer;
        private readonly TimeSpan _autoVerifyInterval = TimeSpan.FromMinutes(7); // periodic hairpin re-check
        private DateTime _nextAutoVerifyUtc = DateTime.MinValue;
        // Opportunistic auto-mapping retry loop (for unmapped peers)
        private System.Threading.Timer? _autoMapTimer;
        private bool _autoMapEnabled;
        private int _desiredTcpPort;
        private int _desiredUdpPort;
        private int _autoMapBackoffSeconds = 15; // start quickly, then back off
        private DateTime _nextAutoMapUtc = DateTime.MinValue;

        public void ConfigureDesiredPorts(int tcpPort, int udpPort)
        {
            _desiredTcpPort = tcpPort;
            _desiredUdpPort = udpPort;
        }

        // Enable periodic attempts to obtain a mapping even for non-major peers.
        // Uses exponential backoff and resets on network changes.
        public void EnableAutoMapping(bool enabled, bool forceKick = false)
        {
            _autoMapEnabled = enabled;
            if (!enabled)
            {
                try { _autoMapTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite); } catch { }
                try { Logger.Log("AutoMap disabled"); } catch { }
                return;
            }
            if (forceKick)
            {
                _autoMapBackoffSeconds = 15;
                _nextAutoMapUtc = DateTime.MinValue;
                try { Logger.Log("AutoMap enabled (force kick)"); } catch { }
            }
            ScheduleAutoMapIfNeeded(initial: true);
            // Subscribe once to network change to nudge the loop
            try
            {
                System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
                System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += OnNetworkChanged;
            }
            catch { }
        }

        private void OnNetworkChanged(object? sender, EventArgs e)
        {
            try
            {
                if (!_autoMapEnabled) return;
                _autoMapBackoffSeconds = 10; // speed up after change
                _nextAutoMapUtc = DateTime.MinValue;
                try { Logger.Log("Network change detected: nudging AutoMap loop (backoff=10s)"); } catch { }
                ScheduleAutoMapIfNeeded(initial: true);
            }
            catch { }
        }

        private void ScheduleAutoMapIfNeeded(bool initial = false)
        {
            try
            {
                if (!_autoMapEnabled) return;
                // Do not schedule if already mapped/verified
                if (_mappedTcpPort.HasValue || _mappedUdpPort.HasValue)
                {
                    // Still allow verify timer to handle validation; pause auto-map
                    return;
                }
                var now = DateTime.UtcNow;
                if (!initial && now < _nextAutoMapUtc) return;
                _nextAutoMapUtc = now.Add(TimeSpan.FromSeconds(Math.Clamp(_autoMapBackoffSeconds, 10, 20 * 60)));
                try { Logger.Log($"AutoMap schedule | next={_nextAutoMapUtc:o} | backoff={_autoMapBackoffSeconds}s"); } catch { }
                _autoMapTimer ??= new System.Threading.Timer(_ => { try { _ = Task.Run(async () => await SafeAutoMapAttemptAsync()); } catch { } }, null, System.Threading.Timeout.InfiniteTimeSpan, System.Threading.Timeout.InfiniteTimeSpan);
                _autoMapTimer.Change(TimeSpan.FromSeconds(Math.Max(1, _autoMapBackoffSeconds)), System.Threading.Timeout.InfiniteTimeSpan);
            }
            catch { }
        }

        private async Task SafeAutoMapAttemptAsync()
        {
            try
            {
                if (!_autoMapEnabled) return;
                if (_desiredTcpPort <= 0 && _desiredUdpPort <= 0) return;
                // Skip if we already have a mapping adopted or verified
                if (_mappedTcpPort.HasValue || _mappedUdpPort.HasValue) return;
                SetState(MappingState.Mapping, "Auto-mapping (retry)");
                // Light discovery to (re)locate gateway
                await DiscoverUpnpAsync(TimeSpan.FromSeconds(5), bypassThrottle: true);
                if (_upnp is null)
                {
                    // No gateway; back off more
                    _autoMapBackoffSeconds = Math.Min(_autoMapBackoffSeconds * 2, 20 * 60);
                    try { Logger.Log($"AutoMap attempt: no gateway, backoff={_autoMapBackoffSeconds}s"); } catch { }
                    ScheduleAutoMapIfNeeded();
                    return;
                }
                var ok = await TryMapPortsAsync(_desiredTcpPort, _desiredUdpPort);
                if (!ok)
                {
                    _autoMapBackoffSeconds = Math.Min(_autoMapBackoffSeconds * 2, 20 * 60);
                    try { Logger.Log($"AutoMap attempt: mapping failed, backoff={_autoMapBackoffSeconds}s"); } catch { }
                    ScheduleAutoMapIfNeeded();
                }
                else
                {
                    // Success: reset backoff; verification timer will keep an eye on it
                    _autoMapBackoffSeconds = 60;
                    try { Logger.Log("AutoMap attempt: mapping succeeded, backoff reset to 60s"); } catch { }
                }
            }
            catch
            {
                _autoMapBackoffSeconds = Math.Min(_autoMapBackoffSeconds * 2, 20 * 60);
                try { Logger.Log($"AutoMap attempt: exception, backoff={_autoMapBackoffSeconds}s"); } catch { }
            }
        }
        private void SetState(MappingState st, string? statusOverride = null)
        {
            var changed = st != State;
            State = st;
            if (!string.IsNullOrWhiteSpace(statusOverride)) Status = statusOverride!;
            else
            {
                Status = st switch
                {
                    MappingState.Idle => "Idle",
                    MappingState.Discovering => "Discovering gateway",
                    MappingState.GatewayDiscovered => "Gateway discovered",
                    MappingState.Mapping => "Attempting mapping via UPnP...",
                    MappingState.Mapped => "Mapped (pending verification)",
                    MappingState.Verified => "Mapped (verified)",
                    MappingState.HairpinFailed => "Mapped (hairpin failed)",
                    MappingState.Unmapped => "Unmapped",
                    MappingState.Failed => "Mapping failed",
                    MappingState.NoGateway => "No gateway",
                    MappingState.Error => "Error",
                    _ => Status
                };
            }
            if (changed)
            {
                try { Logger.Log($"[NAT] State: {State} | {Status}"); } catch { }
                try { Changed?.Invoke(); } catch { }
            }
        }

        public async Task DiscoverUpnpAsync(TimeSpan? timeout = null, bool bypassThrottle = false, bool lockAlreadyHeld = false)
        {
            if (!lockAlreadyHeld) await _sync.WaitAsync();
            try
            {
                // If we already have an active mapping, don't rediscover unless explicitly unmapped
                if ((_mappedTcpPort.HasValue || _mappedUdpPort.HasValue) && _upnp != null)
                {
                    Logger.Log("UPnP discovery skipped: already mapped");
                    return;
                }
                // Throttle discovery calls to avoid hammering on app start or rapid retries
                if (!bypassThrottle && (DateTime.UtcNow - _lastDiscoverUtc) < TimeSpan.FromSeconds(5))
                {
                    return;
                }
                _lastDiscoverUtc = DateTime.UtcNow;
                SetState(MappingState.Discovering);
                _upnp = new Utilities.UpnpClient();
                var window = timeout ?? TimeSpan.FromSeconds(5);
                var ok = await _upnp.DiscoverAsync(window);
                // If the first pass didn't find a usable service, try once more with a slightly longer window
                if (!ok)
                {
                    ok = await _upnp.DiscoverAsync(TimeSpan.FromSeconds(Math.Min(8, window.TotalSeconds + 2)));
                }
                if (ok)
                {
                    // Log service type chosen and available options
                    var sel = _upnp.SelectedServiceType ?? "(none)";
                    var avail = string.Join(", ", _upnp.AvailableServiceTypes);
                    Logger.Log($"UPnP gateway discovered. Selected service: {sel}. Available: [{avail}]");
                    SetState(MappingState.GatewayDiscovered);
                }
                else
                {
                    SetState(MappingState.NoGateway);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"UPnP discovery failed: {ex.Message}");
                SetState(MappingState.NoGateway);
            }
            finally { if (!lockAlreadyHeld) { try { _sync.Release(); } catch { } } }
        }

        // Attempts to map TCP/UDP ports via UPnP. Returns true on any successful mapping.
        public async Task<bool> TryMapPortsAsync(int tcpPort, int udpPort)
        {
            await _sync.WaitAsync();
            try
            {
                // UI hint: mapping begins — set searching status and clear prior verification
                SetState(MappingState.Mapping);
                MappingVerification = string.Empty;
                LastMappingAttemptUtc = DateTime.UtcNow;
                if (_upnp is null)
                {
                    await DiscoverUpnpAsync(bypassThrottle: true, lockAlreadyHeld: true);
                    if (_upnp is null) return false;
                }
                var ip = await _upnp!.GetExternalIPAddressAsync();
                ExternalIPAddress = ip;
                if (ip != null) Logger.Log($"UPnP external IP: {ip}");
                var localIp = GetLocalIPv4() ?? IPAddress.Loopback;

                // Idempotency: if mappings already exist and point to us for the same ports, adopt and treat as success
                bool existingOkTcp = false, existingOkUdp = false;
                try
                {
                    if (tcpPort > 0)
                    {
                        var e = await _upnp.GetSpecificPortMappingEntryAsync(tcpPort, "TCP");
                        if (e.HasValue && string.Equals(e.Value.InternalClient, localIp.ToString(), StringComparison.OrdinalIgnoreCase)
                            && e.Value.InternalPort == tcpPort && e.Value.Enabled)
                        { existingOkTcp = true; }
                    }
                    if (udpPort > 0)
                    {
                        var e = await _upnp.GetSpecificPortMappingEntryAsync(udpPort, "UDP");
                        if (e.HasValue && string.Equals(e.Value.InternalClient, localIp.ToString(), StringComparison.OrdinalIgnoreCase)
                            && e.Value.InternalPort == udpPort && e.Value.Enabled)
                        { existingOkUdp = true; }
                    }
                }
                catch { }

                if (existingOkTcp || existingOkUdp)
                {
                    _mappedTcpPort = existingOkTcp ? tcpPort : null;
                    _mappedUdpPort = existingOkUdp ? udpPort : null;
                    SetState(MappingState.Mapped, $"Mapped TCP {(existingOkTcp ? tcpPort : 0)}, UDP {(existingOkUdp ? udpPort : 0)} (adopted)");
                    // Verify adopted mappings inline (we hold _sync)
                    try { await VerifyMappingsAsync(ip, localIp); } catch { }
                    return true;
                }

                // First attempt with the selected service
                Logger.Log($"UPnP mapping via service: {_upnp.SelectedServiceType}");
                // Preemptively delete any stale entries to avoid conflicts
                try { await _upnp.DeletePortMappingAsync(tcpPort, "TCP"); } catch { }
                try { await _upnp.DeletePortMappingAsync(udpPort, "UDP"); } catch { }
                var okTcp = await _upnp.AddPortMappingAsync(tcpPort, tcpPort, "TCP", localIp, "Zer0Talk TCP");
                var okUdp = await _upnp.AddPortMappingAsync(udpPort, udpPort, "UDP", localIp, "Zer0Talk UDP");

                // If both failed, try alternates if any
                if (!okTcp && !okUdp)
                {
                    foreach (var svc in _upnp.AvailableServiceTypes)
                    {
                        if (string.Equals(svc, _upnp.SelectedServiceType, StringComparison.Ordinal)) continue;
                        if (_upnp.TrySwitchService(svc))
                        {
                            Logger.Log($"Retry UPnP mapping via alternate service: {svc}");
                            try { await _upnp.DeletePortMappingAsync(tcpPort, "TCP"); } catch { }
                            try { await _upnp.DeletePortMappingAsync(udpPort, "UDP"); } catch { }
                            okTcp = okTcp || await _upnp.AddPortMappingAsync(tcpPort, tcpPort, "TCP", localIp, "Zer0Talk TCP");
                            okUdp = okUdp || await _upnp.AddPortMappingAsync(udpPort, udpPort, "UDP", localIp, "Zer0Talk UDP");
                            if (okTcp || okUdp) break;
                        }
                    }
                }

                if (!okTcp && !okUdp)
                {
                    SetState(MappingState.Failed, "Mapping failed - enable UPnP or forward ports manually.");
                    MappingVerification = "Mapping failed";
                    Logger.Log("UPnP mapping failed across all discovered services. Suggest enabling UPnP or manual port forwarding.");
                    return false;
                }
                _mappedTcpPort = tcpPort; _mappedUdpPort = udpPort;
                SetState(MappingState.Mapped, $"Mapped TCP {tcpPort}, UDP {udpPort} via {_upnp.SelectedServiceType}");
                // Verify mappings inline (we already hold _sync)
                try { await VerifyMappingsAsync(ip, localIp); } catch { }
                // Keep auto-verify loop active
                try { ScheduleAutoVerifyIfNeeded(); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"UPnP mapping failed: {ex.Message}");
                SetState(MappingState.Failed, "Mapping failed - enable UPnP or forward ports manually.");
                return false;
            }
            finally { try { _sync.Release(); } catch { } }
        }

        public async Task UnmapAsync()
        {
            await _sync.WaitAsync();
            try
            {
                if (_upnp is null) return;
                if (_mappedTcpPort is int tp) try { await _upnp.DeletePortMappingAsync(tp, "TCP"); } catch { }
                if (_mappedUdpPort is int up) try { await _upnp.DeletePortMappingAsync(up, "UDP"); } catch { }
                _mappedTcpPort = null; _mappedUdpPort = null;
                SetState(MappingState.Unmapped);
                MappingVerification = string.Empty; // Clear stale verification to avoid green indicator with 'Unmapped'
            }
            catch (Exception ex)
            {
                Logger.Log($"UPnP unmap error: {ex.Message}");
            }
            finally { try { _sync.Release(); } catch { } }
        }
        public async Task<bool> TryUdpHolePunchAsync(IPEndPoint peerPublicEndpoint, int localPort, CancellationToken ct)
        {
            try
            {
                // Allow caller to pass 0 to request an ephemeral local port.
                // If preferred local port is busy (common when discovery already binds UDP), fall back to ephemeral.
                UdpClient udp;
                if (localPort > 0)
                {
                    try
                    {
                        udp = new UdpClient(new IPEndPoint(IPAddress.Any, localPort));
                    }
                    catch
                    {
                        udp = new UdpClient(AddressFamily.InterNetwork);
                        try { udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0)); } catch { }
                    }
                }
                else
                {
                    udp = new UdpClient(AddressFamily.InterNetwork);
                    try { udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0)); } catch { }
                }
                using (udp)
                {
                udp.Client.ReceiveTimeout = 1500;
                var payload = System.Text.Encoding.ASCII.GetBytes("Zer0Talk-punch");
                await udp.SendAsync(payload, payload.Length, peerPublicEndpoint);
                Logger.Log($"NAT: Sent UDP punch to {peerPublicEndpoint}");
                Status = "Attempting punch";
                LastPunchAttemptUtc = DateTime.UtcNow;
                LastPunchStatus = "Attempting";
                try { Changed?.Invoke(); } catch { }
                // Best-effort wait for any response
                var vt = udp.ReceiveAsync(ct);
                var t = vt.AsTask();
                var completed = await Task.WhenAny(t, Task.Delay(1500, ct));
                if (completed == t && t.IsCompletedSuccessfully)
                {
                    Logger.Log("NAT: Received UDP response; hole likely open");
                    Status = "Hole likely open";
                    LastPunchStatus = "Success";
                    try { Changed?.Invoke(); } catch { }
                    return true;
                }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"NAT punch error: {ex.Message}");
                Status = "Error";
                LastPunchStatus = "Error";
                try { Changed?.Invoke(); } catch { }
            }
            if (Status == "Attempting punch") { Status = "No response"; LastPunchStatus = "No response"; try { Changed?.Invoke(); } catch { } }
            return false;
        }

        // Simple relay fallback stub: in a real system, this would connect to a relay service.
        // Phase 2 relay protocol now uses session-key-only preface: "RELAY <sessionKey>".
        // Returns a connected TcpClient kept alive by the caller; NetworkStream is obtained from it.
        public async Task<System.Net.Sockets.TcpClient?> TryRelayAsync(string relayHost, int relayPort, string sourceUid, string targetUid, string sessionKey, CancellationToken ct)
        {
            try
            {
                var client = new System.Net.Sockets.TcpClient();
                await client.ConnectAsync(relayHost, relayPort, ct);
                try { client.NoDelay = true; } catch { }
                try { client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true); } catch { }
                var ns = client.GetStream();
            var hello = System.Text.Encoding.UTF8.GetBytes($"RELAY {sessionKey}\n");
                await ns.WriteAsync(hello.AsMemory(0, hello.Length), ct);
                await ns.FlushAsync(ct);
                Status = "Using relay";
                try { Changed?.Invoke(); } catch { }
                return client; // Caller owns the client/stream
            }
            catch (Exception ex)
            {
                Logger.Log($"Relay connect failed: {ex.Message}");
                return null;
            }
        }

        private static IPAddress? GetLocalIPv4()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    var props = ni.GetIPProperties();
                    foreach (var ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address))
                            return ua.Address;
                    }
                }
            }
            catch { }
            return null;
        }

        // Verifies the presence of the UPnP port mappings using two strategies:
        // 1) Query GetSpecificPortMappingEntry for TCP/UDP.
        // 2) Best-effort loopback connect to external IP:tcpPort (hairpin NAT must be supported to succeed).
        private async Task VerifyMappingsAsync(IPAddress? externalIp, IPAddress localIp)
        {
            if (_upnp == null) { MappingVerification = "No UPnP"; SetState(MappingState.NoGateway, Status); return; }
            var sb = new System.Text.StringBuilder();
            bool tcpListed = false, udpListed = false;
            bool hairpinAttempted = false, hairpinReachable = false;
            try
            {
                if (_mappedTcpPort is int tp)
                {
                    var e = await _upnp.GetSpecificPortMappingEntryAsync(tp, "TCP");
                    if (e != null)
                    {
                        sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"TCP {tp} → {e.Value.InternalClient}:{e.Value.InternalPort} {(e.Value.Enabled ? "EN" : "DIS")} ");
                        tcpListed = true;
                    }
                    else sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"TCP {tp} not listed ");
                }
                if (_mappedUdpPort is int up)
                {
                    var e = await _upnp.GetSpecificPortMappingEntryAsync(up, "UDP");
                    if (e != null)
                    {
                        sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"| UDP {up} → {e.Value.InternalClient}:{e.Value.InternalPort} {(e.Value.Enabled ? "EN" : "DIS")} ");
                        udpListed = true;
                    }
                    else sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"| UDP {up} not listed ");
                }

                // Optional TCP hairpin check (only if TCP mapping is still listed)
                if (externalIp != null && _mappedTcpPort is int checkTcp && tcpListed)
                {
                    hairpinAttempted = true;
                    try
                    {
                        // Hairpin support varies by router/ISP. Retry once before classifying as unavailable.
                        bool connected = false;
                        for (var attempt = 0; attempt < 2 && !connected; attempt++)
                        {
                            using var c = new System.Net.Sockets.TcpClient();
                            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(5000));
                            try { await c.ConnectAsync(new IPEndPoint(externalIp, checkTcp), cts.Token); } catch { }
                            connected = c.Connected;
                            if (!connected && attempt == 0)
                            {
                                try { await Task.Delay(250); } catch { }
                            }
                        }

                        if (connected)
                        {
                            sb.Append("| TCP hairpin: reachable");
                            HairpinStatus = "reachable";
                            hairpinReachable = true;
                        }
                        else
                        {
                            sb.Append("| TCP hairpin: unavailable");
                            HairpinStatus = "unavailable";
                        }
                    }
                    catch { sb.Append("| TCP hairpin: unavailable"); HairpinStatus = "unavailable"; }
                }
                else
                {
                    // No attempt (missing external IP or mapping) – only reset if we previously had a different value
                    if (HairpinStatus != "n/a") HairpinStatus = "n/a";
                }
            }
            catch { }
            var result = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(result)) result = "Verification not available";
            MappingVerification = result;
            Logger.Log($"UPnP verify: {result}");
            // If previously mapped ports are no longer listed, attempt to re-create them before giving up.
            if ((_mappedTcpPort.HasValue && !tcpListed) || (_mappedUdpPort.HasValue && !udpListed))
            {
                Logger.Log($"UPnP mapping(s) disappeared: tcpListed={tcpListed} udpListed={udpListed}. Attempting re-map.");
                bool remapped = false;
                try
                {
                    var localIpForRemap = localIp;
                    if (!tcpListed && _mappedTcpPort is int reTcp)
                    {
                        try { await _upnp.DeletePortMappingAsync(reTcp, "TCP"); } catch { }
                        var ok = await _upnp.AddPortMappingAsync(reTcp, reTcp, "TCP", localIpForRemap, "Zer0Talk TCP");
                        if (ok) { tcpListed = true; remapped = true; Logger.Log($"UPnP re-mapped TCP {reTcp}"); }
                    }
                    if (!udpListed && _mappedUdpPort is int reUdp)
                    {
                        try { await _upnp.DeletePortMappingAsync(reUdp, "UDP"); } catch { }
                        var ok = await _upnp.AddPortMappingAsync(reUdp, reUdp, "UDP", localIpForRemap, "Zer0Talk UDP");
                        if (ok) { udpListed = true; remapped = true; Logger.Log($"UPnP re-mapped UDP {reUdp}"); }
                    }
                }
                catch (Exception ex) { Logger.Log($"UPnP re-map attempt failed: {ex.Message}"); }

                if (!remapped)
                {
                    _mappedTcpPort = tcpListed ? _mappedTcpPort : null;
                    _mappedUdpPort = udpListed ? _mappedUdpPort : null;
                    // Nudge auto-map regardless of partial state so it can recover the missing mapping
                    if (!_mappedTcpPort.HasValue || !_mappedUdpPort.HasValue)
                    {
                        if (!_mappedTcpPort.HasValue && !_mappedUdpPort.HasValue)
                            SetState(MappingState.Unmapped, "Unmapped (mappings disappeared)");
                        try { _autoMapBackoffSeconds = 10; _nextAutoMapUtc = DateTime.MinValue; ScheduleAutoMapIfNeeded(initial: true); } catch { }
                    }
                }
            }
            // Derive state refinement (Verified vs HairpinFailed)
            if (hairpinReachable) SetState(MappingState.Verified, $"Mapped ({result})");
            else if (tcpListed || udpListed) SetState(MappingState.Mapped, $"Mapped ({result})");
            else if (hairpinAttempted) SetState(MappingState.HairpinFailed, $"Mapped ({result})");
            else SetState(MappingState.Mapped, $"Mapped ({result})");
            LastVerificationUtc = DateTime.UtcNow;
            ScheduleAutoVerifyIfNeeded();
            try { Changed?.Invoke(); } catch { }
        }

        private void ScheduleAutoVerifyIfNeeded()
        {
            try
            {
                // Only schedule periodic re-verification if we have a mapping
                if (!(_mappedTcpPort.HasValue || _mappedUdpPort.HasValue)) return;
                var now = DateTime.UtcNow;
                if (now < _nextAutoVerifyUtc) return; // already scheduled
                _nextAutoVerifyUtc = now.Add(_autoVerifyInterval);
                _autoVerifyTimer ??= new System.Threading.Timer(_ =>
                {
                    try { _ = Task.Run(async () => await SafeReverifyAsync()); } catch { }
                }, null, _autoVerifyInterval, System.Threading.Timeout.InfiniteTimeSpan);
                _autoVerifyTimer.Change(_autoVerifyInterval, System.Threading.Timeout.InfiniteTimeSpan);
            }
            catch { }
        }

        private async Task SafeReverifyAsync()
        {
            if (!await _sync.WaitAsync(TimeSpan.FromSeconds(5))) return; // skip if another op is running
            try
            {
                if (_upnp == null) return;
                // Always refresh external IP to detect ISP DHCP changes / router reboots
                IPAddress? ip;
                try { ip = await _upnp.GetExternalIPAddressAsync(); } catch { ip = ExternalIPAddress; }
                ExternalIPAddress = ip;
                var local = GetLocalIPv4() ?? IPAddress.Loopback;
                await VerifyMappingsAsync(ip, local);
            }
            catch { }
            finally { try { _sync.Release(); } catch { } }
        }

        public async Task RetryVerificationAsync()
        {
            await _sync.WaitAsync();
            try
            {
                // Always refresh external IP for manual retries
                IPAddress? ip = null;
                if (_upnp != null)
                {
                    try { ip = await _upnp.GetExternalIPAddressAsync(); ExternalIPAddress = ip; } catch { ip = ExternalIPAddress; }
                }
                var local = GetLocalIPv4() ?? IPAddress.Loopback;
                await VerifyMappingsAsync(ip, local);
            }
            finally { try { _sync.Release(); } catch { } }
        }

        public void Dispose()
        {
            try { _autoVerifyTimer?.Dispose(); } catch { }
            try { _autoMapTimer?.Dispose(); } catch { }
            try { System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged -= OnNetworkChanged; } catch { }
            GC.SuppressFinalize(this);
        }
    }
}

