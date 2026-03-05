/*
    NAT traversal - verification and hole punching.
*/
using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Zer0Talk.Utilities;

namespace Zer0Talk.Services
{
    public partial class NatTraversalService
    {
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
                // Best-effort wait for any response; avoid orphaned ReceiveAsync task on timeout/dispose.
                using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                receiveCts.CancelAfter(TimeSpan.FromMilliseconds(1500));
                try
                {
                    var _ = await udp.ReceiveAsync(receiveCts.Token);
                    Logger.Log("NAT: Received UDP response; hole likely open");
                    Status = "Hole likely open";
                    LastPunchStatus = "Success";
                    try { Changed?.Invoke(); } catch { }
                    return true;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Timed out waiting for punch response.
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
                var relayRole = GetRelayRole(sourceUid, targetUid);
                var hello = System.Text.Encoding.UTF8.GetBytes($"RELAY {sessionKey} {relayRole}\n");
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

        private static string GetRelayRole(string sourceUid, string targetUid)
        {
            var source = NormalizeUid(sourceUid);
            var target = NormalizeUid(targetUid);
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target)) return "I";
            return string.Compare(source, target, StringComparison.OrdinalIgnoreCase) < 0 ? "I" : "R";
        }

        private static string NormalizeUid(string uid)
        {
            var value = (uid ?? string.Empty).Trim();
            if (value.StartsWith("usr-", StringComparison.Ordinal) && value.Length > 4)
            {
                return value.Substring(4);
            }
            return value;
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
