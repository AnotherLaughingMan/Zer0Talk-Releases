using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ZTalk.Utilities
{
    // Minimal UPnP IGD discovery and port mapping via SOAP.
    // This is a very small, best-effort client to avoid external dependencies.
    // It discovers IGD via SSDP M-SEARCH and sends SOAP to WANIPConnection/WANPPPConnection service.
    public sealed class UpnpClient
    {
        private static readonly string[] HeaderSplitSeparators = { "\r\n" };
        private Uri? _controlUrl;
        // Prefer WANIPConnection, but some gateways expose only WANPPPConnection.
        private string _serviceType = "urn:schemas-upnp-org:service:WANIPConnection:1";
        private IPAddress? _routerAddress;
        // Track all discovered IGD services so callers can retry with alternates.
        private readonly System.Collections.Generic.Dictionary<string, Uri> _services = new();

        // Expose selected and available service types for diagnostics/retry
        public string? SelectedServiceType => _controlUrl != null ? _serviceType : null;
        public System.Collections.Generic.IReadOnlyList<string> AvailableServiceTypes => _services.Keys.ToList();
        // Expose discovered router/gateway address for diagnostics
        public IPAddress? RouterAddress => _routerAddress;

        public async Task<bool> DiscoverAsync(TimeSpan? timeout = null)
        {
            try
            {
                var overall = timeout ?? TimeSpan.FromSeconds(5);
                var ifaces = GetLocalIPv4Addresses().ToArray();
                if (ifaces.Length == 0) ifaces = new[] { IPAddress.Any };
                // ST variants to maximize compatibility across gateways
                var targets = new[]
                {
                    // Device-level
                    "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
                    "urn:schemas-upnp-org:device:InternetGatewayDevice:2",
                    // Service-level (both v1 and v2)
                    "urn:schemas-upnp-org:service:WANIPConnection:1",
                    "urn:schemas-upnp-org:service:WANIPConnection:2",
                    "urn:schemas-upnp-org:service:WANPPPConnection:1",
                    "urn:schemas-upnp-org:service:WANPPPConnection:2",
                    // Fallbacks
                    "upnp:rootdevice",
                    "ssdp:all"
                };
                // MX specifies seconds a device may wait before replying; ensure we wait at least MX+buffer
                const int MxSeconds = 3; // must match the MX header below
                var minWait = TimeSpan.FromMilliseconds((MxSeconds * 1000) + 800); // MX plus a small buffer
                foreach (var ip in ifaces)
                {
                    try
                    {
                        using var udp = ip.Equals(IPAddress.Any) ? new UdpClient(AddressFamily.InterNetwork)
                                                                 : new UdpClient(new IPEndPoint(ip, 0));
                        try { udp.EnableBroadcast = true; } catch { }
                        udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2);
                        // Allocate at least MX+buffer per interface, even if overall is small
                        var perAttempt = TimeSpan.FromMilliseconds(Math.Max(minWait.TotalMilliseconds, overall.TotalMilliseconds / Math.Max(1, ifaces.Length)));
                        udp.Client.ReceiveTimeout = (int)perAttempt.TotalMilliseconds;
                        var ep = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
                        try { Logger.Log($"SSDP: probing from {ip} for {targets.Length} ST targets; wait {perAttempt.TotalMilliseconds:0}ms"); } catch { }

                        foreach (var st in targets)
                        {
                            var req = "M-SEARCH * HTTP/1.1\r\n" +
                                      "HOST: 239.255.255.250:1900\r\n" +
                                      "MAN: \"ssdp:discover\"\r\n" +
                                      $"MX: {MxSeconds}\r\n" +
                                      "USER-AGENT: ZTalk/1.0 UPnP/1.1 SSDP/1.0\r\n" +
                                      $"ST: {st}\r\n\r\n";
                            var data = Encoding.ASCII.GetBytes(req);
                            // send twice with small pause to improve reliability
                            await udp.SendAsync(data, data.Length, ep);
                            await Task.Delay(120);
                            await udp.SendAsync(data, data.Length, ep);
                            // Also try broadcast variants for noncompliant devices
                            try
                            {
                                foreach (var bc in GetBroadcastAddresses())
                                {
                                    await udp.SendAsync(data, data.Length, new IPEndPoint(bc, 1900));
                                }
                                // global broadcast
                                await udp.SendAsync(data, data.Length, new IPEndPoint(IPAddress.Broadcast, 1900));
                            }
                            catch { }
                        }

                        var start = DateTime.UtcNow;
                        while (DateTime.UtcNow - start < perAttempt)
                        {
                            try
                            {
                                var res = await udp.ReceiveAsync();
                                var text = Encoding.ASCII.GetString(res.Buffer);
                                var loc = GetHeader(text, "LOCATION");
                                if (!string.IsNullOrWhiteSpace(loc) && Uri.TryCreate(loc.Trim(), UriKind.Absolute, out var descUrl))
                                {
                                    _routerAddress = res.RemoteEndPoint.Address;
                                    try { Logger.Log($"SSDP: response from {_routerAddress}; LOCATION={descUrl}"); } catch { }
                                    if (await ParseDeviceDescriptionAsync(descUrl))
                                        return true;
                                }
                            }
                            catch (SocketException) { /* continue until perAttempt expires */ }
                        }

                        // Fallback: some gateways reply only when source port is 1900
                        try
                        {
                            using var udp1900 = new UdpClient(new IPEndPoint(ip.Equals(IPAddress.Any) ? IPAddress.Any : ip, 1900));
                            try { udp1900.EnableBroadcast = true; } catch { }
                            udp1900.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2);
                            udp1900.Client.ReceiveTimeout = 1800;
                            var ep2 = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
                            try { Logger.Log($"SSDP: fallback from {ip} bound to 1900"); } catch { }
                            foreach (var st in targets)
                            {
                                var req = "M-SEARCH * HTTP/1.1\r\n" +
                                          "HOST: 239.255.255.250:1900\r\n" +
                                          "MAN: \"ssdp:discover\"\r\n" +
                                          $"MX: {MxSeconds}\r\n" +
                                          "USER-AGENT: ZTalk/1.0 UPnP/1.1 SSDP/1.0\r\n" +
                                          $"ST: {st}\r\n\r\n";
                                var data = Encoding.ASCII.GetBytes(req);
                                await udp1900.SendAsync(data, data.Length, ep2);
                                // Also broadcast variants
                                foreach (var bc in GetBroadcastAddresses())
                                {
                                    await udp1900.SendAsync(data, data.Length, new IPEndPoint(bc, 1900));
                                }
                                await udp1900.SendAsync(data, data.Length, new IPEndPoint(IPAddress.Broadcast, 1900));
                            }
                            var s2 = DateTime.UtcNow;
                            while (DateTime.UtcNow - s2 < TimeSpan.FromMilliseconds(1800))
                            {
                                try
                                {
                                    var res = await udp1900.ReceiveAsync();
                                    var text = Encoding.ASCII.GetString(res.Buffer);
                                    var loc = GetHeader(text, "LOCATION");
                                    if (!string.IsNullOrWhiteSpace(loc) && Uri.TryCreate(loc.Trim(), UriKind.Absolute, out var descUrl))
                                    {
                                        _routerAddress = res.RemoteEndPoint.Address;
                                        try { Logger.Log($"SSDP: response (1900) from {_routerAddress}; LOCATION={descUrl}"); } catch { }
                                        if (await ParseDeviceDescriptionAsync(descUrl))
                                            return true;
                                    }
                                }
                                catch (SocketException) { /* continue */ }
                            }
                        }
                        catch { }
                    }
                    catch { }
                }
            }
            catch { }
            return false;
        }

        private static System.Collections.Generic.List<IPAddress> GetBroadcastAddresses()
        {
            var list = new System.Collections.Generic.List<IPAddress>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = ni.GetIPProperties();
                    foreach (var ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                        var ip = ua.Address.GetAddressBytes();
                        var mask = ua.IPv4Mask?.GetAddressBytes();
                        if (mask == null) continue;
                        var b = new byte[4];
                        for (int i = 0; i < 4; i++) b[i] = (byte)(ip[i] | (mask[i] ^ 255));
                        list.Add(new IPAddress(b));
                    }
                }
            }
            catch { }
            return list;
        }

        private static System.Collections.Generic.List<IPAddress> GetLocalIPv4Addresses()
        {
            var list = new System.Collections.Generic.List<IPAddress>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = ni.GetIPProperties();
                    foreach (var ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address))
                            list.Add(ua.Address);
                    }
                }
            }
            catch { }
            return list;
        }

        private static string GetHeader(string response, string header)
        {
            var lines = response.Split(HeaderSplitSeparators, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith(header + ":", StringComparison.OrdinalIgnoreCase))
                {
                    var idx = line.IndexOf(':');
                    if (idx >= 0 && idx + 1 < line.Length)
                        return line.Substring(idx + 1).Trim();
                }
            }
            return string.Empty;
        }

        // Returns external public IP using the current service type
        public async Task<IPAddress?> GetExternalIPAddressAsync()
        {
            if (_controlUrl == null) return null;
            var soap =
                "<?xml version=\"1.0\"?>" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                "<s:Body>" +
                $"<u:GetExternalIPAddress xmlns:u=\"{_serviceType}\"></u:GetExternalIPAddress>" +
                "</s:Body></s:Envelope>";
            var xml = await PostSoapAsync("GetExternalIPAddress", soap);
            if (xml == null) return null;
            var m = Regex.Match(xml, @"<NewExternalIPAddress>([^<]+)</NewExternalIPAddress>");
            if (m.Success && IPAddress.TryParse(m.Groups[1].Value, out var ip)) return ip;
            return null;
        }

        // Adds a port mapping. LeaseDuration 0 means permanent.
        public async Task<bool> AddPortMappingAsync(int externalPort, int internalPort, string protocol, IPAddress internalClient, string description)
        {
            if (_controlUrl == null) return false;
            var soap =
                "<?xml version=\"1.0\"?>" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                "<s:Body>" +
                $"<u:AddPortMapping xmlns:u=\"{_serviceType}\">" +
                "<NewRemoteHost></NewRemoteHost>" +
                $"<NewExternalPort>{externalPort}</NewExternalPort>" +
                $"<NewProtocol>{protocol}</NewProtocol>" +
                $"<NewInternalPort>{internalPort}</NewInternalPort>" +
                $"<NewInternalClient>{internalClient}</NewInternalClient>" +
                "<NewEnabled>1</NewEnabled>" +
                $"<NewPortMappingDescription>{EscapeXml(description)}</NewPortMappingDescription>" +
                "<NewLeaseDuration>0</NewLeaseDuration>" +
                "</u:AddPortMapping>" +
                "</s:Body></s:Envelope>";
            var xml = await PostSoapAsync("AddPortMapping", soap);
            return xml != null; // success returns empty SOAP body
        }

        // Deletes a port mapping if present.
        public async Task<bool> DeletePortMappingAsync(int externalPort, string protocol)
        {
            if (_controlUrl == null) return false;
            var soap =
                "<?xml version=\"1.0\"?>" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                "<s:Body>" +
                $"<u:DeletePortMapping xmlns:u=\"{_serviceType}\">" +
                "<NewRemoteHost></NewRemoteHost>" +
                $"<NewExternalPort>{externalPort}</NewExternalPort>" +
                $"<NewProtocol>{protocol}</NewProtocol>" +
                "</u:DeletePortMapping>" +
                "</s:Body></s:Envelope>";
            var xml = await PostSoapAsync("DeletePortMapping", soap);
            return xml != null;
        }

        // Parses the device description and finds controlURL for WANIPConnection or WANPPPConnection, preferring WANIPConnection if available.
        private async Task<bool> ParseDeviceDescriptionAsync(Uri descUrl)
        {
            try
            {
                using var http = new HttpClient();
                var xml = await http.GetStringAsync(descUrl);
                _services.Clear();
                // Resolve relative control URLs against the full description URL for correctness per spec
                var baseUri = descUrl;
                // Collect WANIPConnection services
                var ipMatches = Regex.Matches(xml, @"<service>.*?<serviceType>([^<]+WANIPConnection:[12])</serviceType>.*?<controlURL>([^<]+)</controlURL>.*?</service>", RegexOptions.Singleline);
                foreach (Match m in ipMatches)
                {
                    var st = m.Groups[1].Value;
                    var cu = m.Groups[2].Value.Trim();
                    var url = cu.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? new Uri(cu) : new Uri(baseUri, cu);
                    _services.TryAdd(st, url);
                }
                // Collect WANPPPConnection services
                var pppMatches = Regex.Matches(xml, @"<service>.*?<serviceType>([^<]+WANPPPConnection:[12])</serviceType>.*?<controlURL>([^<]+)</controlURL>.*?</service>", RegexOptions.Singleline);
                foreach (Match m in pppMatches)
                {
                    var st = m.Groups[1].Value;
                    var cu = m.Groups[2].Value.Trim();
                    var url = cu.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? new Uri(cu) : new Uri(baseUri, cu);
                    _services.TryAdd(st, url);
                }
                // Log what we parsed for diagnostics
                try { Logger.Log($"UPnP: device description parsed at {descUrl}; services found: {_services.Count}"); } catch { }
                // Pick preferred service: any WANIPConnection first, else first of PPP, else none
                var preferred = _services.Keys.FirstOrDefault(k => k.Contains("WANIPConnection", StringComparison.OrdinalIgnoreCase))
                                 ?? _services.Keys.FirstOrDefault();
                if (preferred != null)
                {
                    _serviceType = preferred;
                    _controlUrl = _services[preferred];
                    try { Logger.Log($"UPnP: selected service '{_serviceType}' at '{_controlUrl}'"); } catch { }
                    return true;
                }
                try { Logger.Log("UPnP: no suitable IGD service found in description"); } catch { }
            }
            catch (Exception ex) { try { Logger.Log($"UPnP: parse description failed: {ex.Message}"); } catch { } }
            return false;
        }

        // Attempts to switch to a specific service type previously discovered. Returns false if not available.
        public bool TrySwitchService(string serviceType)
        {
            if (_services.TryGetValue(serviceType, out var url))
            {
                _serviceType = serviceType;
                _controlUrl = url;
                return true;
            }
            return false;
        }

        // Queries a specific port mapping entry. Returns tuple (internalClient, internalPort, enabled, description) or null if not found.
        public async Task<(string InternalClient, int InternalPort, bool Enabled, string Description)?> GetSpecificPortMappingEntryAsync(int externalPort, string protocol)
        {
            if (_controlUrl == null) return null;
            var soap =
                "<?xml version=\"1.0\"?>" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                "<s:Body>" +
                $"<u:GetSpecificPortMappingEntry xmlns:u=\"{_serviceType}\">" +
                "<NewRemoteHost></NewRemoteHost>" +
                $"<NewExternalPort>{externalPort}</NewExternalPort>" +
                $"<NewProtocol>{protocol}</NewProtocol>" +
                "</u:GetSpecificPortMappingEntry>" +
                "</s:Body></s:Envelope>";
            var xml = await PostSoapAsync("GetSpecificPortMappingEntry", soap);
            if (xml == null) return null;
            // Parse basic fields if present
            var ic = Regex.Match(xml, @"<NewInternalClient>([^<]+)</NewInternalClient>").Groups;
            var ip = Regex.Match(xml, @"<NewInternalPort>([^<]+)</NewInternalPort>").Groups;
            var en = Regex.Match(xml, @"<NewEnabled>([^<]+)</NewEnabled>").Groups;
            var desc = Regex.Match(xml, @"<NewPortMappingDescription>([^<]*)</NewPortMappingDescription>").Groups;
            if (ic.Count >= 2 && ip.Count >= 2)
            {
                if (!int.TryParse(ip[1].Value, out var iport))
                {
                    try { Logger.Log($"UPnP: failed to parse internal port '{ip[1].Value}'"); } catch { }
                    return null;
                }
                bool enabled = en.Count >= 2 && (en[1].Value == "1" || en[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase));
                var d = desc.Count >= 2 ? desc[1].Value : string.Empty;
                return (ic[1].Value, iport, enabled, d);
            }
            return null;
        }

        private async Task<string?> PostSoapAsync(string action, string body)
        {
            try
            {
                if (_controlUrl == null) return null;
                using var http = new HttpClient();
                var content = new StringContent(body, Encoding.UTF8, "text/xml");
                content.Headers.Clear();
                content.Headers.TryAddWithoutValidation("Content-Type", "text/xml; charset=\"utf-8\"");
                content.Headers.ContentLength = Encoding.UTF8.GetByteCount(body);
                var req = new HttpRequestMessage(HttpMethod.Post, _controlUrl);
                req.Content = content;
                req.Headers.TryAddWithoutValidation("SOAPACTION", $"\"{_serviceType}#{action}\"");
                var res = await http.SendAsync(req);
                var text = await res.Content.ReadAsStringAsync();
                if (!res.IsSuccessStatusCode)
                {
                    // Some IGDs return 500 with SOAP fault for already-exists cases; still return body for diagnostics
                    return text.Length == 0 ? "" : text;
                }
                return text.Length == 0 ? "" : text;
            }
            catch { return null; }
        }

        private static string EscapeXml(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
    }
}
