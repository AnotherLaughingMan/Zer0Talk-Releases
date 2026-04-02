using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using Zer0Talk.Models;
using Zer0Talk.Services;
using Zer0Talk.Utilities;

namespace Zer0Talk.ViewModels;

/// <summary>
/// Purpose-built, lightweight ViewModel for the Discovered Peers window.
/// Refreshes in real time via event subscriptions and a periodic byte-counter tick.
/// Does NOT inherit from or depend on the Settings ViewModel.
/// </summary>
public sealed class DiscoveredPeersViewModel : INotifyPropertyChanged
{
    private readonly PeerManager _peerManager = AppServices.Peers;
    private readonly SettingsService _settings = AppServices.Settings;
    private DateTime _lastRefreshErrorLogUtc = DateTime.MinValue;

    /// <summary>The live list of visible, non-simulated discovered peers.</summary>
    public ObservableCollection<Peer> Peers { get; } = new();

    private List<Peer> _selectedPeers = new();
    public List<Peer> SelectedPeers
    {
        get => _selectedPeers;
        set { _selectedPeers = value ?? new(); OnPropertyChanged(); }
    }

    public ICommand BlockSelectedPeersCommand { get; }
    public ICommand UnblockSelectedPeersCommand { get; }
    public ICommand RemoveSelectedPeersCommand { get; }
    public ICommand RefreshCommand { get; }

    private Action<int>? _sessionCountHandler;
    private Action? _peersChangedHandler;

    public DiscoveredPeersViewModel()
    {
        BlockSelectedPeersCommand = new RelayCommand(_ => BlockSelected());
        UnblockSelectedPeersCommand = new RelayCommand(_ => UnblockSelected());
        RemoveSelectedPeersCommand = new RelayCommand(_ => RemoveSelected());
        RefreshCommand = new RelayCommand(_ =>
        {
            try { AppServices.Discovery.Restart(); } catch { }
            Refresh();
        });
    }

    /// <summary>
    /// Called from the window's OnOpened. Subscribes to live events and populates the list.
    /// </summary>
    public void Attach()
    {
        Refresh();

        _sessionCountHandler = _ => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
        _peersChangedHandler = () => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);

        try { AppServices.Network.SessionCountChanged += _sessionCountHandler; } catch { }
        try { AppServices.Events.PeersChanged += _peersChangedHandler; } catch { }
    }

    /// <summary>
    /// Called from the window's OnClosed. Unsubscribes all live event handlers.
    /// </summary>
    public void Detach()
    {
        if (_sessionCountHandler != null)
        {
            try { AppServices.Network.SessionCountChanged -= _sessionCountHandler; } catch { }
            _sessionCountHandler = null;
        }
        if (_peersChangedHandler != null)
        {
            try { AppServices.Events.PeersChanged -= _peersChangedHandler; } catch { }
            _peersChangedHandler = null;
        }
    }

    /// <summary>
    /// Rebuilds the peer list from live state. Must be called on the UI thread.
    /// </summary>
    public void Refresh()
    {
        try
        {
            var allPeers = SnapshotPeers();
            var now = DateTime.UtcNow;

            var contacts = SnapshotContacts();
            var simulatedUids = new HashSet<string>(
                contacts.Where(c => c.IsSimulated).Select(c => NormalizeUid(c.UID)),
                StringComparer.OrdinalIgnoreCase);
            var realContactUids = new HashSet<string>(
                contacts.Where(c => !c.IsSimulated).Select(c => NormalizeUid(c.UID)),
                StringComparer.OrdinalIgnoreCase);

            var blocked = (_settings.Settings.BlockList ?? new List<string>())
                .Select(NormalizeUid)
                .Where(uid => !string.IsNullOrWhiteSpace(uid))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var peers = allPeers
                .Where(p => ShouldDisplay(p, now, realContactUids, simulatedUids))
                .OrderByDescending(p => blocked.Contains(NormalizeUid(p.UID)))
                .ThenByDescending(IsPeerOnline)
                .ThenBy(p => NormalizeUid(p.UID), StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var peer in peers)
            {
                var uid = NormalizeUid(peer.UID);

                peer.IsBlocked = blocked.Contains(uid);
                peer.IsLan = IsLanAddress(peer.Address);
                peer.ModeLabel = AppServices.Network.GetConnectionMode(uid) switch
                {
                    ConnectionMode.Direct => "Direct",
                    ConnectionMode.Relay => "Relay",
                    _ => "—"
                };

                var (bytesIn, bytesOut) = AppServices.Network.GetSessionBytes(uid);
                peer.BytesIn = bytesIn;
                peer.BytesOut = bytesOut;

                if (IsPeerOnline(peer))
                    peer.LastSeenOnline = now;

                var cacheExpired = peer.CountryCodeCachedAt == null ||
                    (peer.LastSeenOnline != null && (now - peer.LastSeenOnline.Value).TotalMinutes > 30);

                if (string.IsNullOrEmpty(peer.CountryCode) || cacheExpired)
                {
                    if (peer.PublicKey != null && peer.PublicKey.Length > 0)
                    {
                        peer.CountryCode = GetCountryCodeFromIp(peer.Address);
                        peer.CountryCodeCachedAt = now;
                    }
                    else
                    {
                        peer.CountryCode = "⚪";
                        peer.CountryCodeCachedAt = null;
                    }
                }
            }

            SyncPeers(peers);
        }
        catch (Exception ex)
        {
            if ((DateTime.UtcNow - _lastRefreshErrorLogUtc) >= TimeSpan.FromSeconds(3))
            {
                _lastRefreshErrorLogUtc = DateTime.UtcNow;
                try { Logger.Log($"DiscoveredPeersViewModel.Refresh failed: {ex.Message}"); } catch { }
            }
        }
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    private void BlockSelected()
    {
        if (SelectedPeers.Count == 0) return;
        foreach (var uid in SelectedPeers.Select(p => p.UID).ToList())
            try { _peerManager.Block(uid); } catch { }
        SelectedPeers.Clear();
        Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
    }

    private void UnblockSelected()
    {
        if (SelectedPeers.Count == 0) return;
        foreach (var uid in SelectedPeers.Select(p => p.UID).ToList())
            try { _peerManager.Unblock(uid); } catch { }
        SelectedPeers.Clear();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try { AppServices.Discovery.Restart(); } catch { }
            Refresh();
        });
    }

    private void RemoveSelected()
    {
        if (SelectedPeers.Count == 0) return;
        var removedAny = false;
        foreach (var peer in SelectedPeers.ToList())
        {
            if (peer == null) continue;
            var uid = NormalizeUid(peer.UID);
            if (string.IsNullOrWhiteSpace(uid)) continue;
            var target = _peerManager.Peers.FirstOrDefault(p =>
                string.Equals(NormalizeUid(p.UID), uid, StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                _peerManager.Peers.Remove(target);
                removedAny = true;
            }
        }
        SelectedPeers.Clear();
        if (removedAny)
        {
            try { AppServices.PeersStore.Save(_peerManager.Peers, AppServices.Passphrase); } catch { }
            try { _peerManager.IncludeContacts(); } catch { }
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
    }

    // ── List sync ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Diff-syncs the target list into <see cref="Peers"/> without replacing the whole collection,
    /// so existing bindings and selection state survive incremental updates.
    /// </summary>
    private void SyncPeers(IReadOnlyList<Peer> target)
    {
        var byUid = new Dictionary<string, Peer>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in target)
        {
            var uid = NormalizeUid(p.UID);
            if (!string.IsNullOrWhiteSpace(uid)) byUid[uid] = p;
        }

        // Remove peers that are no longer in target
        for (var i = Peers.Count - 1; i >= 0; i--)
        {
            if (!byUid.ContainsKey(NormalizeUid(Peers[i].UID)))
                Peers.RemoveAt(i);
        }

        // Insert/re-order peers to match target order
        for (var i = 0; i < target.Count; i++)
        {
            var t = target[i];
            if (i < Peers.Count)
            {
                if (!string.Equals(NormalizeUid(Peers[i].UID), NormalizeUid(t.UID), StringComparison.OrdinalIgnoreCase))
                {
                    var existIdx = IndexOfByUid(NormalizeUid(t.UID));
                    if (existIdx >= 0) Peers.Move(existIdx, i);
                    else Peers.Insert(i, t);
                }
                else if (!ReferenceEquals(Peers[i], t))
                {
                    // Same UID but different object reference — swap in the current object so that
                    // any INPC notifications fired on it by Refresh() are seen by the UI binding.
                    Peers[i] = t;
                }
                // else: same UID, same reference → do nothing; properties already updated via INPC.
            }
            else
            {
                Peers.Add(t);
            }
        }

        while (Peers.Count > target.Count)
            Peers.RemoveAt(Peers.Count - 1);
    }

    private int IndexOfByUid(string uid)
    {
        for (var i = 0; i < Peers.Count; i++)
            if (string.Equals(NormalizeUid(Peers[i].UID), uid, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    // ── Helpers (self-contained) ──────────────────────────────────────────────

    private static bool ShouldDisplay(
        Peer peer,
        DateTime now,
        HashSet<string> realContactUids,
        HashSet<string> simulatedUids)
    {
        if (peer == null) return false;
        var uid = NormalizeUid(peer.UID);
        if (string.IsNullOrWhiteSpace(uid)) return false;
        if (simulatedUids.Contains(uid)) return false;
        if (IsUidLikelyTest(uid)) return false;
        if (realContactUids.Contains(uid)) return true;
        var status = peer.Status?.Trim() ?? string.Empty;
        if (string.Equals(status, "Offline", StringComparison.OrdinalIgnoreCase)) return false;
        if (IsPeerOnline(peer)) return true;
        return peer.LastSeenOnline.HasValue && (now - peer.LastSeenOnline.Value).TotalSeconds <= 8;
    }

    private static bool IsPeerOnline(Peer peer)
    {
        var status = peer.Status?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(status))
            return !string.Equals(status, "Offline", StringComparison.OrdinalIgnoreCase);
        return peer.PublicKey != null && peer.PublicKey.Length > 0;
    }

    private static bool IsLanAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        var host = address.Contains(':') ? address.Split(':')[0] : address;
        if (!System.Net.IPAddress.TryParse(host, out var ip)) return false;
        if (System.Net.IPAddress.IsLoopback(ip)) return true;
        var b = ip.GetAddressBytes();
        if (b.Length != 4) return false;
        return (b[0] == 10)
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254);
    }

    private static string NormalizeUid(string? uid)
    {
        var value = (uid ?? string.Empty).Trim();
        return value.StartsWith("usr-", StringComparison.OrdinalIgnoreCase) && value.Length > 4
            ? value[4..] : value;
    }

    private static bool IsUidLikelyTest(string uid)
    {
        if (string.IsNullOrWhiteSpace(uid)) return true;
        var n = NormalizeUid(uid);
        return n.StartsWith("test", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("sim", StringComparison.OrdinalIgnoreCase)
            || n.Contains("debug", StringComparison.OrdinalIgnoreCase)
            || n.Contains("dummy", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCountryCodeFromIp(string ipAddress)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ipAddress)) return "🌍";

            var addressPart = ipAddress.Contains(':') ? ipAddress.Split(':')[0] : ipAddress;
            if (!System.Net.IPAddress.TryParse(addressPart, out var ip)) return "🌍";

            var b = ip.GetAddressBytes();
            if (b.Length != 4) return "🌍";

            var f = b[0];
            var s = b[1];

            // Private/local — already handled by IsLan, but kept for completeness
            if (f == 10 || (f == 192 && s == 168) || (f == 172 && s >= 16 && s <= 31) || f == 127)
                return "\U0001F5A5\uFE0F";

            if (f >= 3 && f <= 38) return "🇺🇸";
            if (f >= 40 && f <= 50) return "🇺🇸";
            if (f >= 63 && f <= 76) return "🇺🇸";
            if (f >= 77 && f <= 95) return "🇬🇧";
            if (f >= 141 && f <= 145) return "🇩🇪";
            if (f >= 151 && f <= 155) return "🇫🇷";
            if (f >= 185 && f <= 188) return "🇳🇱";
            if (f >= 202 && f <= 203) return "🇨🇳";
            if (f >= 210 && f <= 211) return "🇯🇵";
            if (f >= 119 && f <= 125) return "🇯🇵";
            if (f >= 1 && f <= 2) return "🇨🇳";
            if (f >= 58 && f <= 61) return "🇨🇳";
            if (f >= 112 && f <= 115) return "🇰🇷";
            if (f == 49 || f == 50) return "🇰🇷";
            if (f >= 139 && f <= 140) return "🇮🇳";
            if (f >= 177 && f <= 181) return "🇧🇷";
            if (f >= 200 && f <= 201) return "🇧🇷";
            if (f >= 142 && f <= 143) return "🇨🇦";
            if (f >= 206 && f <= 209) return "🇨🇦";
            if (f >= 27 && f <= 29) return "🇦🇺";
            if (f >= 101 && f <= 103) return "🇦🇺";

            var flags = new[] { "🇺🇸", "🇬🇧", "🇨🇦", "🇩🇪", "🇫🇷", "🇯🇵", "🇦🇺", "🇧🇷", "🇮🇳", "🇨🇳", "🇰🇷", "🇪🇸", "🇮🇹", "🇳🇱", "🇸🇪", "🇨🇭" };
            return flags[Math.Abs(ipAddress.GetHashCode()) % flags.Length];
        }
        catch { return "🌍"; }
    }

    private List<Peer> SnapshotPeers()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { return _peerManager.Peers.ToList(); }
            catch (InvalidOperationException) { }
        }
        return _peerManager.Peers.ToArray().ToList();
    }

    private static List<Contact> SnapshotContacts()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { return AppServices.Contacts.Contacts.ToList(); }
            catch (InvalidOperationException) { }
        }
        return AppServices.Contacts.Contacts.ToArray().ToList();
    }

    // ── INotifyPropertyChanged ────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
