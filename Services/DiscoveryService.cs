using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Zer0Talk.Models;
using Zer0Talk.Utilities;

namespace Zer0Talk.Services
{
    // Orchestrates peer discovery across LAN and WAN without duplicating lower-level logic.
    // Delegates:
    // - LAN multicast/broadcast discovery is handled by NetworkService.
    // - UPnP gateway discovery is handled by NatTraversalService.
    // - WAN bootstrap via Known Major Nodes is handled by PeerCrawler.
    public sealed class DiscoveryService : IDisposable
    {
        public enum State { Idle, Discovering, Completed, Failed }

        private readonly SettingsService _settings;
        private readonly NetworkService _network;
        private readonly NatTraversalService _nat;
        private readonly PeerCrawler _crawler;

        private readonly object _gate = new();
        private CancellationTokenSource? _cts;
        private int _attempt;
        private int _backoffSeconds = 5;
        private DateTime? _lastSuccessUtc;
        private DateTime? _lastAttemptUtc;
        private string _lastError = string.Empty;
        private readonly LinkedList<string> _log = new();
        public const int MaxLog = 400;
    private DateTime _lastRestartUtc = DateTime.MinValue;
    private static readonly TimeSpan RestartCooldown = TimeSpan.FromSeconds(45);

        public State CurrentState { get; private set; } = State.Idle;
        public event Action? Changed; // lightweight state change event

        public DiscoveryService(SettingsService settings, NetworkService network, NatTraversalService nat, PeerCrawler crawler)
        { _settings = settings; _network = network; _nat = nat; _crawler = crawler; }

        public void Start()
        {
            // Honor global SafeMode to avoid network/discovery activity during diagnostics
            if (Zer0Talk.Utilities.RuntimeFlags.SafeMode)
            {
                AppendLog("Start suppressed: SafeMode enabled");
                return;
            }
            lock (_gate)
            {
                Stop();
                _cts = new CancellationTokenSource();
                AppendLog("Trigger: start() invoked");
                _ = Task.Run(() => Loop(_cts.Token));
            }
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            _cts = null;
            if (CurrentState != State.Idle) { CurrentState = State.Idle; TryRaiseChanged(); }
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }

        public void Restart()
        {
            var now = DateTime.UtcNow;
            // Debounce restarts when we're already in a Completed state
            if (CurrentState == State.Completed && _lastRestartUtc != DateTime.MinValue && (now - _lastRestartUtc) < RestartCooldown)
            {
                var remaining = RestartCooldown - (now - _lastRestartUtc);
                AppendLog($"Restart suppressed (cooldown {remaining.TotalSeconds:F0}s remaining)");
                return;
            }
            _lastRestartUtc = now;
            AppendLog("Manual restart requested");
            Start();
        }

        private async Task Loop(CancellationToken ct)
        {
            // Ensure persisted peers are available as a starting point when none are present
            TryLoadPersistedPeers();

            _attempt = 0; _backoffSeconds = 5; _lastError = string.Empty;
            while (!ct.IsCancellationRequested)
            {
                _attempt++; _lastAttemptUtc = DateTime.UtcNow;
                SetState(State.Discovering);
                AppendLog($"Attempt #{_attempt} started");
                AppendLog("Trigger: timer/backoff");
                try
                {
                    // Step 1: Ensure NAT diagnostics are available only if needed
                    await TryNatProbeAsync(ct);

                    // Step 2: Ensure WAN bootstrap crawler is running if seeds exist
                    StartCrawlerIfSeeded();

                    // Step 3: Observe for a short window for any discovery signals
                    var success = await WaitForSignalsAsync(TimeSpan.FromSeconds(12), ct);
                    if (success)
                    {
                        _lastSuccessUtc = DateTime.UtcNow;
                        SetState(State.Completed);
                        AppendLog("Discovery signals observed");
                        // Keep running but slow down re-checks
                        await Task.Delay(TimeSpan.FromSeconds(20), ct);
                        continue;
                    }
                    else
                    {
                        _lastError = "No discovery signals";
                        SetState(State.Failed);
                        AppendLog("No discovery signals; scheduling retry");
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                    SetState(State.Failed);
                    AppendLog($"Error: {ex.Message}");
                }

                // Backoff with cap
                var wait = Math.Min(60, Math.Max(5, _backoffSeconds));
                _backoffSeconds = Math.Min(60, wait * 2);
                await Task.Delay(TimeSpan.FromSeconds(wait), ct);
            }
        }

        private void TryLoadPersistedPeers()
        {
            try
            {
                if (AppServices.Peers.Peers.Count == 0)
                {
                    var list = AppServices.PeersStore.Load(AppServices.Passphrase);
                    if (list.Count > 0)
                    {
                        AppendLog($"Loaded {list.Count} peers from store");
                        AppServices.Peers.SetDiscovered(list);
                    }
                }
            }
            catch { }
        }

        private async Task TryNatProbeAsync(CancellationToken ct)
        {
            try
            {
                // Skip probing if already mapped
                if (_nat.MappedTcpPort.HasValue || _nat.MappedUdpPort.HasValue)
                {
                    return;
                }
                // Skip if a gateway is already known/selected
                if (!string.IsNullOrEmpty(_nat.SelectedServiceType) || _nat.RouterAddress != null)
                {
                    return;
                }
                // Only probe when previous state indicates no gateway/unknown
                AppendLog("NAT: probing gateway");
                await _nat.DiscoverUpnpAsync(TimeSpan.FromSeconds(6), bypassThrottle: false);
            }
            catch { }
        }

        private void StartCrawlerIfSeeded()
        {
            var seeds = _settings.Settings.KnownMajorNodes ?? new List<string>();
            if (seeds.Count == 0)
            {
                AppendLog("Bootstrap: no known major nodes configured");
                return;
            }
            AppendLog($"Bootstrap: {seeds.Count} seed(s)");
            try { _crawler.Start(); } catch { }
        }

        private static async Task<bool> WaitForSignalsAsync(TimeSpan window, CancellationToken ct)
        {
            var start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < window)
            {
                ct.ThrowIfCancellationRequested();
                if (HasDiscoverySignals()) return true;
                await Task.Delay(500, ct);
            }
            return HasDiscoverySignals();
        }

        private static bool HasDiscoverySignals()
        {
            try
            {
                // Signal 1: any discovered peers in PeerManager
                if (AppServices.Peers.Peers.Count > 0) return true;
                // Signal 2: UDP beacons observed
                var snap = AppServices.Network.GetDiagnosticsSnapshot();
                if (snap.UdpBeaconsRecv > 0) return true;
            }
            catch { }
            return false;
        }

        private void SetState(State s)
        {
            if (CurrentState != s)
            {
                var from = CurrentState; var to = s;
                CurrentState = s;
                AppendLog($"State: {from} -> {to}");
                TryRaiseChanged();
            }
        }

        private void AppendLog(string line)
        {
            var msg = $"[Disc] {DateTime.Now:HH:mm:ss} {line}";
            try
            {
                _log.AddLast(msg);
                while (_log.Count > MaxLog) _log.RemoveFirst();
            }
            catch { }
            try { Logger.Log(msg); } catch { }
        }

        // External components can annotate discovery with trigger reasons (e.g., beacon received, peer unreachable)
        public void NoteExternalTrigger(string source, string details)
        {
            try { AppendLog($"Trigger: {source} {details}"); } catch { }
        }

        private void TryRaiseChanged() { try { Changed?.Invoke(); } catch { } }

        public Snapshot GetSnapshot()
        {
            return new Snapshot
            {
                StateValue = CurrentState,
                Attempts = _attempt,
                LastAttemptUtc = _lastAttemptUtc,
                LastSuccessUtc = _lastSuccessUtc,
                LastError = _lastError,
                BackoffSeconds = _backoffSeconds,
                Log = _log.ToArray(),
                Seeds = (_settings.Settings.KnownMajorNodes ?? new List<string>()).ToArray(),
                PeersCount = AppServices.Peers.Peers.Count,
                UdpBeaconsRecv = (int)AppServices.Network.GetDiagnosticsSnapshot().UdpBeaconsRecv
            };
        }

        public readonly record struct Snapshot
        {
            public State StateValue { get; init; }
            public int Attempts { get; init; }
            public int BackoffSeconds { get; init; }
            public DateTime? LastAttemptUtc { get; init; }
            public DateTime? LastSuccessUtc { get; init; }
            public string LastError { get; init; }
            public string[] Log { get; init; }
            public string[] Seeds { get; init; }
            public int PeersCount { get; init; }
            public int UdpBeaconsRecv { get; init; }
        }
    }
}

