/*
    Peer discovery: crawls known/seed nodes, maintains discovered list, and respects blocklist.
    - Feeds PeerManager with candidate peers.
*/
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Zer0Talk.Models;
using Zer0Talk.Utilities;

namespace Zer0Talk.Services
{
    // [DISCOVERY] Optional WAN/seed discovery helper: remains as a fallback/diagnostics anchor.
    // LAN discovery is now peer-to-peer and independent of Major Node status.
    public class PeerCrawler : IDisposable
    {
        private readonly NetworkService _net;
        private readonly SettingsService _settings;
        private readonly IdentityService _identity;
        private CancellationTokenSource? _cts;
        private readonly ConcurrentDictionary<string, (Peer Peer, DateTime LastSeen)> _discovered = new();

        public PeerCrawler(NetworkService net, SettingsService settings, IdentityService identity)
        {
            _net = net; _settings = settings; _identity = identity;
        }

        public event Action<IReadOnlyCollection<Peer>>? DiscoveredChanged;

        public void Start()
        {
            Stop();
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => Loop(_cts.Token));
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            _cts = null;
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }

        private async Task Loop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await ScanOnce(ct);
                    PruneExpired();
                    RaiseChanged();
                }
                catch (Exception ex)
                {
                    Logger.Log($"Crawler error: {ex.Message}");
                }
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
        }

        private async Task ScanOnce(CancellationToken ct)
        {
            var blocked = new HashSet<string>(_settings.Settings.BlockList ?? new());
            foreach (var entry in _settings.Settings.KnownMajorNodes ?? new())
            {
                var parts = entry.Split(':');
                if (parts.Length != 2) continue;
                var host = parts[0];
                if (!int.TryParse(parts[1], out var port)) continue;
                try
                {
                    TcpClient? client = null;
                    try
                    {
                        client = CreatePreferredClient();
                        await client.ConnectAsync(host, port, ct);
                        using var ns = client.GetStream();
                        // Simple plaintext request for discovery. This is a minimal bootstrap path for public major nodes.
                        var req = Encoding.UTF8.GetBytes("DISCOVER\n");
                        await ns.WriteAsync(req.AsMemory(0, req.Length), ct);
                        await ns.FlushAsync(ct);
                        // Read lines of "UID,host,port" terminated by blank line
                        using var reader = new System.IO.StreamReader(ns, Encoding.UTF8, false, 1024, leaveOpen: true);
                        while (!reader.EndOfStream)
                        {
                            var line = await reader.ReadLineAsync(ct);
                            if (string.IsNullOrWhiteSpace(line)) break;
                            var fields = line.Split(',');
                            if (fields.Length < 3) continue;
                            var uid = fields[0].Trim(); var phost = fields[1].Trim(); var pportStr = fields[2].Trim();
                            if (blocked.Contains(uid)) continue;
                            if (!int.TryParse(pportStr, out var pport)) continue;
                            // Validate via handshake (UID confirmation)
                            var ok = await ValidatePeerAsync(phost, pport, uid, ct);
                            if (ok)
                            {
                                var peer = new Peer { UID = uid, Address = phost, Port = pport };
                                _discovered.AddOrUpdate(uid, _ => (peer, DateTime.UtcNow), (_, __) => (peer, DateTime.UtcNow));
                            }
                        }
                    }
                    finally
                    {
                        try { client?.Dispose(); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Discovery to {entry} failed: {ex.Message}");
                }
            }
        }

        private async Task<bool> ValidatePeerAsync(string host, int port, string expectedUid, CancellationToken ct)
        {
            try
            {
                TcpClient? client = null;
                try
                {
                    client = CreatePreferredClient();
                    await client.ConnectAsync(host, port, ct);
                    using var ns = client.GetStream();
                    // Minimal UID handshake: echo our UID request, peer returns its UID signed (future). For now, read claimed UID.
                    var hello = Encoding.UTF8.GetBytes("UID?\n");
                    await ns.WriteAsync(hello.AsMemory(0, hello.Length), ct);
                    await ns.FlushAsync(ct);
                    using var reader = new System.IO.StreamReader(ns, Encoding.UTF8, false, 1024, leaveOpen: true);
                    var uid = (await reader.ReadLineAsync(ct))?.Trim() ?? string.Empty;
                    if (uid != expectedUid)
                    {
                        Logger.Log($"UID mismatch from {host}:{port}. Expected {expectedUid}, got {uid}");
                        BlockForMisbehavior(uid);
                        return false;
                    }
                    return true;
                }
                finally
                {
                    try { client?.Dispose(); } catch { }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Peer validate error {host}:{port} - {ex.Message}");
                return false;
            }
        }

        private void PruneExpired()
        {
            var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(1);
            foreach (var kv in _discovered.ToArray())
            {
                if (kv.Value.LastSeen < cutoff)
                {
                    _discovered.TryRemove(kv.Key, out _);
                }
            }
        }

        private void RaiseChanged()
        {
            DiscoveredChanged?.Invoke(_discovered.Values.Select(v => v.Peer).ToList());
        }

        public void BlockForMisbehavior(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return;
            var list = _settings.Settings.BlockList ??= new();
            if (!list.Contains(uid))
            {
                list.Add(uid);
                _settings.Save(AppServices.Passphrase);
                // Remove from discovered immediately
                _discovered.TryRemove(uid, out _);
                RaiseChanged();
            }
        }

        private TcpClient CreatePreferredClient()
        {
            var bindIp = SelectPreferredBindAddress();
            TcpClient client;
            if (!bindIp.Equals(IPAddress.Any))
            {
                client = new TcpClient(bindIp.AddressFamily);
                try { client.Client.Bind(new IPEndPoint(bindIp, 0)); }
                catch (Exception ex) { Logger.Log($"Crawler local bind {bindIp}:0 failed, continuing without: {ex.Message}"); }
            }
            else
            {
                client = new TcpClient();
            }
            return client;
        }

        private IPAddress SelectPreferredBindAddress()
        {
            try
            {
                var order = _settings.Settings.AdapterPriorityIds ?? new List<string>();
                var nics = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                foreach (var id in order)
                {
                    var ni = nics.FirstOrDefault(n => n.Id == id);
                    if (ni == null) continue;
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    var ipProps = ni.GetIPProperties();
                    foreach (var ua in ipProps.UnicastAddresses)
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                            return ua.Address;
                }
                foreach (var ni in nics)
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    var ipProps = ni.GetIPProperties();
                    foreach (var ua in ipProps.UnicastAddresses)
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                            return ua.Address;
                }
            }
            catch { }
            return IPAddress.Any;
        }
    }
}

