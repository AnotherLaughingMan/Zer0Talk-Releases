using System;
using System.Globalization;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using Avalonia.Media;

using Zer0Talk.Services;

namespace Zer0Talk.ViewModels;

public class MonitoringViewModel : INotifyPropertyChanged
{
    private const int SessionSyntheticRateKey = -1;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // Localized strings
    public string LocalizedTitle => Services.AppServices.Localization.GetString("Monitoring.Title", "Monitoring");
    public string LocalizedNAT => Services.AppServices.Localization.GetString("Monitoring.NAT", "NAT:");
    public string LocalizedRetry => Services.AppServices.Localization.GetString("Monitoring.Retry", "Retry");
    public string LocalizedViewMode => Services.AppServices.Localization.GetString("Monitoring.ViewMode", "View");
    public string LocalizedViewSummary => Services.AppServices.Localization.GetString("Monitoring.ViewSummary", "Summary");
    public string LocalizedViewNetwork => Services.AppServices.Localization.GetString("Monitoring.ViewNetwork", "Network");
    public string LocalizedViewAdvanced => Services.AppServices.Localization.GetString("Monitoring.ViewAdvanced", "Advanced");
    public string LocalizedRetryTooltip => Services.AppServices.Localization.GetString("Monitoring.RetryTooltip", "Re-run mapping verification & hairpin test");
    public string LocalizedStatusLegend => Services.AppServices.Localization.GetString("Monitoring.StatusLegend", "Status Legend");
    public string LocalizedSearching => Services.AppServices.Localization.GetString("Monitoring.Searching", "Searching");
    public string LocalizedFailure => Services.AppServices.Localization.GetString("Monitoring.Failure", "Failure");
    public string LocalizedSuccess => Services.AppServices.Localization.GetString("Monitoring.Success", "Success");
    public string LocalizedTrafficBytesPerSec => Services.AppServices.Localization.GetString("Monitoring.TrafficBytesPerSec", "Traffic (bytes/sec)");
    public string LocalizedRefresh => Services.AppServices.Localization.GetString("Monitoring.Refresh", "Refresh");
    public string LocalizedGraphStyle => Services.AppServices.Localization.GetString("Monitoring.GraphStyle", "Style");
    public string LocalizedGraphLine => Services.AppServices.Localization.GetString("Monitoring.GraphLine", "Line");
    public string LocalizedGraphBar => Services.AppServices.Localization.GetString("Monitoring.GraphBar", "Bar");
    public string LocalizedGraphShaded => Services.AppServices.Localization.GetString("Monitoring.GraphShaded", "Shaded");
    public string LocalizedGraphSolid => LocalizedGraphShaded;
    public string LocalizedLegendSide => Services.AppServices.Localization.GetString("Monitoring.LegendSide", "Legend");
    public string LocalizedLegendLeft => Services.AppServices.Localization.GetString("Monitoring.LegendLeft", "Left");
    public string LocalizedLegendRight => Services.AppServices.Localization.GetString("Monitoring.LegendRight", "Right");
    public string LocalizedRecvRate => Services.AppServices.Localization.GetString("Monitoring.RecvRate", "Received");
    public string LocalizedPorts => Services.AppServices.Localization.GetString("Monitoring.Ports", "Ports:");
    public string LocalizedGateway => Services.AppServices.Localization.GetString("Monitoring.Gateway", "Gateway:");
    public string LocalizedExternalIP => Services.AppServices.Localization.GetString("Monitoring.ExternalIP", "External IP:");
    public string LocalizedService => Services.AppServices.Localization.GetString("Monitoring.Service", "Service:");
    public string LocalizedAltServices => Services.AppServices.Localization.GetString("Monitoring.AltServices", "Alt Services:");
    public string LocalizedMapPing => Services.AppServices.Localization.GetString("Monitoring.MapPing", "Map/Ping:");
    public string LocalizedMapAt => Services.AppServices.Localization.GetString("Monitoring.MapAt", "Map @");
    public string LocalizedVerifyAt => Services.AppServices.Localization.GetString("Monitoring.VerifyAt", "· Verify @");
    public string LocalizedPunch => Services.AppServices.Localization.GetString("Monitoring.Punch", "Punch:");
    public string LocalizedHairpin => Services.AppServices.Localization.GetString("Monitoring.Hairpin", "| Hairpin:");
    public string LocalizedDiscovery => Services.AppServices.Localization.GetString("Monitoring.Discovery", "Discovery:");
    public string LocalizedAttempts => Services.AppServices.Localization.GetString("Monitoring.Attempts", "Attempts:");
    public string LocalizedBackoff => Services.AppServices.Localization.GetString("Monitoring.Backoff", "Backoff:");
    public string LocalizedRestartDiscovery => Services.AppServices.Localization.GetString("Monitoring.RestartDiscovery", "Restart Discovery");
    public string LocalizedLastAttempt => Services.AppServices.Localization.GetString("Monitoring.LastAttempt", "Last attempt:");
    public string LocalizedLastSuccess => Services.AppServices.Localization.GetString("Monitoring.LastSuccess", "· Last success:");
    public string LocalizedPresencePipeline => Services.AppServices.Localization.GetString("Monitoring.PresencePipeline", "Presence pipeline:");
    public string LocalizedDiagnosticsLog => Services.AppServices.Localization.GetString("Monitoring.DiagnosticsLog", "Diagnostics log");
    public string LocalizedShowLog => Services.AppServices.Localization.GetString("Monitoring.ShowLog", "Show log");
    public string LocalizedTextSizeForLog => Services.AppServices.Localization.GetString("Monitoring.TextSizeForLog", "Text Size for Log");
    public string LocalizedClose => Services.AppServices.Localization.GetString("Monitoring.Close", "Close");

    public MonitoringViewModel()
    {
        try
        {
            // Live-refresh when NAT state or listening port changes
            AppServices.Events.NatChanged += OnNatChanged;
            AppServices.Nat.Changed += OnNatChanged;
            AppServices.Events.NetworkListeningChanged += OnListeningChanged;
        }
        catch { }

        // Subscribe to language changes
        try
        {
            Action languageChangedHandler = () => { Avalonia.Threading.Dispatcher.UIThread.Post(RefreshLocalizedStrings); };
            AppServices.Localization.LanguageChanged += languageChangedHandler;
        }
        catch { }
    }

    private void RefreshLocalizedStrings()
    {
        OnPropertyChanged(nameof(LocalizedTitle));
        OnPropertyChanged(nameof(LocalizedNAT));
        OnPropertyChanged(nameof(LocalizedRetry));
        OnPropertyChanged(nameof(LocalizedViewMode));
        OnPropertyChanged(nameof(LocalizedViewSummary));
        OnPropertyChanged(nameof(LocalizedViewNetwork));
        OnPropertyChanged(nameof(LocalizedViewAdvanced));
        OnPropertyChanged(nameof(LocalizedRetryTooltip));
        OnPropertyChanged(nameof(LocalizedStatusLegend));
        OnPropertyChanged(nameof(LocalizedSearching));
        OnPropertyChanged(nameof(LocalizedFailure));
        OnPropertyChanged(nameof(LocalizedSuccess));
        OnPropertyChanged(nameof(LocalizedTrafficBytesPerSec));
        OnPropertyChanged(nameof(LocalizedRefresh));
        OnPropertyChanged(nameof(LocalizedGraphStyle));
        OnPropertyChanged(nameof(LocalizedGraphLine));
        OnPropertyChanged(nameof(LocalizedGraphBar));
        OnPropertyChanged(nameof(LocalizedGraphShaded));
        OnPropertyChanged(nameof(LocalizedLegendSide));
        OnPropertyChanged(nameof(LocalizedLegendLeft));
        OnPropertyChanged(nameof(LocalizedLegendRight));
        OnPropertyChanged(nameof(LocalizedRecvRate));
        OnPropertyChanged(nameof(LocalizedPorts));
        OnPropertyChanged(nameof(LocalizedGateway));
        OnPropertyChanged(nameof(LocalizedExternalIP));
        OnPropertyChanged(nameof(LocalizedService));
        OnPropertyChanged(nameof(LocalizedAltServices));
        OnPropertyChanged(nameof(LocalizedMapPing));
        OnPropertyChanged(nameof(LocalizedMapAt));
        OnPropertyChanged(nameof(LocalizedVerifyAt));
        OnPropertyChanged(nameof(LocalizedPunch));
        OnPropertyChanged(nameof(LocalizedHairpin));
        OnPropertyChanged(nameof(LocalizedDiscovery));
        OnPropertyChanged(nameof(LocalizedAttempts));
        OnPropertyChanged(nameof(LocalizedBackoff));
        OnPropertyChanged(nameof(LocalizedRestartDiscovery));
        OnPropertyChanged(nameof(LocalizedLastAttempt));
        OnPropertyChanged(nameof(LocalizedLastSuccess));
        OnPropertyChanged(nameof(LocalizedPresencePipeline));
        OnPropertyChanged(nameof(LocalizedDiagnosticsLog));
        OnPropertyChanged(nameof(LocalizedShowLog));
        OnPropertyChanged(nameof(LocalizedTextSizeForLog));
        OnPropertyChanged(nameof(LocalizedClose));
    }

    private void OnNatChanged()
    {
        try { Avalonia.Threading.Dispatcher.UIThread.Post(() => NotifyNetworkStatus()); } catch { }
    }
    private void OnListeningChanged(bool _isListening, int? _port)
    {
        try { Avalonia.Threading.Dispatcher.UIThread.Post(() => NotifyNetworkStatus()); } catch { }
    }

    // Interval selection and persistence
    private int _intervalIndex;
    public int IntervalIndex
    {
        get => _intervalIndex;
        set
        {
            if (_intervalIndex != value)
            {
                _intervalIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsRealTimeRefresh));
                OnIntervalChanged?.Invoke(value);
            }
        }
    }
    public event Action<int>? OnIntervalChanged;
    public bool IsRealTimeRefresh => IntervalIndex == 0;
    public void SetIntervalFromMilliseconds(int ms)
    {
        IntervalIndex = ms switch
        {
            <= 250 => 0,
            <= 500 => 1,
            <= 1000 => 2,
            <= 2000 => 3,
            <= 5000 => 4,
            <= 10000 => 5,
            <= 30000 => 6,
            <= 60000 => 7,
            <= 120000 => 8,
            <= 300000 => 9,
            <= 600000 => 10,
            _ => 11 // 20m
        };
    }
    public static int IndexToMs(int idx) => idx switch { 0 => 250, 1 => 500, 2 => 1000, 3 => 2000, 4 => 5000, 5 => 10000, 6 => 30000, 7 => 60000, 8 => 120000, 9 => 300000, 10 => 600000, 11 => 1200000, _ => 500 };

    private int _graphStyleIndex;
    public int GraphStyleIndex
    {
        get => _graphStyleIndex;
        set
        {
            var normalized = Math.Clamp(value, 0, 2);
            if (_graphStyleIndex != normalized)
            {
                _graphStyleIndex = normalized;
                OnPropertyChanged();
                try
                {
                    AppServices.Settings.Settings.MonitoringGraphStyleIndex = _graphStyleIndex;
                    _ = System.Threading.Tasks.Task.Run(() => AppServices.Settings.Save(AppServices.Passphrase));
                }
                catch { }
            }
        }
    }

    private int _legendPositionIndex = 1;
    public int LegendPositionIndex
    {
        get => _legendPositionIndex;
        set
        {
            var normalized = Math.Clamp(value, 0, 1);
            if (_legendPositionIndex != normalized)
            {
                _legendPositionIndex = normalized;
                OnPropertyChanged();
                try
                {
                    AppServices.Settings.Settings.MonitoringLegendPositionIndex = _legendPositionIndex;
                    _ = System.Threading.Tasks.Task.Run(() => AppServices.Settings.Save(AppServices.Passphrase));
                }
                catch { }
            }
        }
    }

    // View mode selection: Summary (default), Network, Advanced
    private int _viewModeIndex = 0;
    public int ViewModeIndex
    {
        get => _viewModeIndex;
        set
        {
            if (_viewModeIndex != value)
            {
                _viewModeIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSummaryMode));
                OnPropertyChanged(nameof(IsNetworkMode));
                OnPropertyChanged(nameof(IsAdvancedMode));
                OnPropertyChanged(nameof(IsNetworkOrAdvancedMode));
            }
        }
    }
    public bool IsSummaryMode => ViewModeIndex == 0;
    public bool IsNetworkMode => ViewModeIndex == 1;
    public bool IsAdvancedMode => ViewModeIndex == 2;
    public bool IsNetworkOrAdvancedMode => ViewModeIndex >= 1;

    // NAT + ports
    public string NatStatus => AppServices.Nat.Status;
    public string NatStatusShort
    {
        get
        {
            var s = (NatStatus ?? string.Empty).ToLowerInvariant();
            var v = (NatVerification ?? string.Empty).ToLowerInvariant();
            if (s.Contains("discovering") || (s.Contains("gateway discovered") && string.IsNullOrWhiteSpace(v)))
                return "Searching...";
            if (s.Contains("unmapped") || s.Contains("no gateway"))
                return "Disconnected";
            if (s.Contains("failed") || v.Contains("unreachable") || v.Contains("failed"))
                return "Failed";
            if (v.Contains("reachable") || v.Contains("ok") || (s.Contains("mapped") && !s.Contains("unmapped")))
                return "Connected";
            return "Unknown";
        }
    }
    public string NatVerification => AppServices.Nat.MappingVerification;
    public string HairpinStatus => AppServices.Nat.HairpinStatus;
    public string TcpPortLabel => AppServices.Network.ListeningPort is int p ? $"TCP: {p}" : "TCP: n/a";
    public string UdpPortLabel => AppServices.Network.UdpBoundPort is int p ? $"UDP: {p}" : "UDP: n/a";
    public string ExternalPortLabel => AppServices.Nat.MappedTcpPort is int tp && AppServices.Nat.MappedUdpPort is int up ? $"External: {tp} -> {up}" : "External: n/a";
    // NAT diagnostics details
    public string NatGateway => AppServices.Nat.RouterAddress?.ToString() ?? "n/a";
    public string NatExternalIp => AppServices.Nat.ExternalIPAddress?.ToString() ?? "n/a";
    public string NatService => AppServices.Nat.SelectedServiceType ?? "n/a";
    public string NatAvailableServices => string.Join(", ", AppServices.Nat.AvailableServiceTypes);
    public string NatPunchSummary
    {
        get
        {
            var s = AppServices.Nat.LastPunchStatus;
            var t = AppServices.Nat.LastPunchAttemptUtc;
            if (string.IsNullOrWhiteSpace(s) && t is null) return "n/a";
            var ts = t is DateTime dt ? $" @ {dt.ToLocalTime():HH:mm:ss}" : string.Empty;
            return string.IsNullOrWhiteSpace(s) ? $"(no status){ts}" : s + ts;
        }
    }
    public string NatPunchTooltip
    {
        get
        {
            try
            {
                var status = (AppServices.Nat.LastPunchStatus ?? string.Empty).Trim();
                var attemptedAt = AppServices.Nat.LastPunchAttemptUtc;
                var stamp = attemptedAt is DateTime t ? $" Last attempt: {t.ToLocalTime():HH:mm:ss}." : string.Empty;
                if (string.IsNullOrWhiteSpace(status) && attemptedAt is null)
                {
                    return "No UDP punch attempt yet. Punching only runs when establishing a peer path that needs UDP hole-punching.";
                }

                if (status.Equals("Success", StringComparison.OrdinalIgnoreCase))
                {
                    return "UDP punch succeeded. A return UDP packet was observed, so NAT hole-punch is likely open." + stamp;
                }

                if (status.Equals("No response", StringComparison.OrdinalIgnoreCase))
                {
                    return "Punch timed out waiting for a UDP response. This usually means symmetric NAT, strict firewall, or peer-side punch not coordinated yet." + stamp;
                }

                if (status.Equals("Error", StringComparison.OrdinalIgnoreCase))
                {
                    return "Punch failed with an error. Check UDP binding, local firewall rules, and whether a valid peer public endpoint was available." + stamp;
                }

                return $"Punch status: {status}.{stamp}";
            }
            catch
            {
                return "Punch diagnostics unavailable.";
            }
        }
    }
    public string HairpinTooltip
    {
        get
        {
            try
            {
                var hairpin = (HairpinStatus ?? string.Empty).Trim().ToLowerInvariant();
                var verify = (NatVerification ?? string.Empty).Trim();
                var verifyAt = AppServices.Nat.LastVerificationUtc;
                var stamp = verifyAt is DateTime t ? $" Last verification: {t.ToLocalTime():HH:mm:ss}." : string.Empty;

                if (hairpin == "reachable")
                {
                    return "Hairpin is reachable. Your router supports NAT loopback to your own external mapping." + stamp;
                }

                if (hairpin == "unavailable")
                {
                    return "Hairpin is unavailable. Many routers/ISPs do not support NAT loopback; this is common and not necessarily a connection failure." + stamp;
                }

                if (hairpin == "n/a" || string.IsNullOrWhiteSpace(hairpin))
                {
                    if (string.IsNullOrWhiteSpace(verify) || verify.Contains("not available", StringComparison.OrdinalIgnoreCase))
                    {
                        return "Hairpin not tested yet. Mapping verification has not completed, or required external mapping details are not available." + stamp;
                    }
                    if (verify.Contains("not listed", StringComparison.OrdinalIgnoreCase) || verify.Contains("failed", StringComparison.OrdinalIgnoreCase))
                    {
                        return "Hairpin not tested because TCP mapping is missing/invalid. Verify UPnP mapping first, then retry verification." + stamp;
                    }
                    return "Hairpin check was skipped for this verification cycle. It requires an external IP and a valid listed TCP mapping." + stamp;
                }

                return $"Hairpin status: {HairpinStatus}.{stamp}";
            }
            catch
            {
                return "Hairpin diagnostics unavailable.";
            }
        }
    }
    public string NatVerifyTimeLabel => AppServices.Nat.LastVerificationUtc is DateTime t ? t.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "n/a";
    public string NatMapAttemptLabel => AppServices.Nat.LastMappingAttemptUtc is DateTime t ? t.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "n/a";

    // Discovery snapshot (tiny panel)
    public string DiscoveryState { get { try { return AppServices.Discovery.GetSnapshot().StateValue.ToString(); } catch { return "n/a"; } } }
    public int DiscoveryAttempts { get { try { return AppServices.Discovery.GetSnapshot().Attempts; } catch { return 0; } } }
    public int DiscoveryBackoff { get { try { return AppServices.Discovery.GetSnapshot().BackoffSeconds; } catch { return 0; } } }
    public string DiscoveryLastAttemptLabel { get { try { var t = AppServices.Discovery.GetSnapshot().LastAttemptUtc; return t is DateTime dt ? dt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "n/a"; } catch { return "n/a"; } } }
    public string DiscoveryLastSuccessLabel { get { try { var t = AppServices.Discovery.GetSnapshot().LastSuccessUtc; return t is DateTime dt ? dt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "n/a"; } catch { return "n/a"; } } }
    public string PresencePipelineSummary
    {
        get
        {
            try
            {
                var s = AppServices.GetPresencePipelineSnapshot();
                return $"seen={s.Seen} executed={s.Executed} coalesced={s.Coalesced} in-flight={s.InFlight} connect={s.ConnectAttempts} outbox q/sk={s.OutboxQueued}/{s.OutboxSkipped}";
            }
            catch { return "n/a"; }
        }
    }
    public ICommand RestartDiscoveryCommand => new RelayCommand(_ => { try { AppServices.Discovery.Restart(); AppendLog($"{DateTime.Now:HH:mm:ss} - DISCOVERY restart requested"); } catch { } });
    public ICommand RestoreCheckpointCommand => new RelayCommand(_ => { try { AppServices.Guard.RestoreLastCheckpoint(); AppendLog($"{DateTime.Now:HH:mm:ss} - Restore Checkpoint requested"); } catch { } });
    public ICommand SaveCheckpointCommand => new RelayCommand(_ => { try { AppServices.Guard.SaveCurrentCheckpoint(); AppendLog($"{DateTime.Now:HH:mm:ss} - Save Checkpoint requested"); } catch { } });

    // NAT indicator
    private IBrush _natIndicatorBrush = Brushes.Gray;
    public IBrush NatIndicatorBrush { get => _natIndicatorBrush; set { _natIndicatorBrush = value; OnPropertyChanged(); } }
    private double _natIndicatorOpacity = 1.0;
    public double NatIndicatorOpacity { get => _natIndicatorOpacity; set { _natIndicatorOpacity = value; OnPropertyChanged(); } }
    public bool NatIndicatorBlink { get; private set; }

    // Rates
    private string _tcpRate = "TCP: 0 B/s";
    public string TcpRate { get => _tcpRate; set { _tcpRate = value; OnPropertyChanged(); } }
    private string _udpRate = "UDP: 0 B/s";
    public string UdpRate { get => _udpRate; set { _udpRate = value; OnPropertyChanged(); } }
    private string _outRate = "Outbound local: 0 B/s";
    public string OutRate { get => _outRate; set { _outRate = value; OnPropertyChanged(); } }
    private string _recvRate = "Received: 0 B/s";
    public string RecvRate { get => _recvRate; set { _recvRate = value; OnPropertyChanged(); } }
    public IBrush TcpBrush { get; set; } = Brushes.Gray;
    public IBrush UdpBrush { get; set; } = Brushes.Gray;
    public IBrush OutBrush { get; set; } = Brushes.Gray;
    public System.Collections.Generic.Dictionary<int, (double In, double Out)> CurrentRates { get; private set; } = new();

    // Rolling history for persistent chart rendering. Each sample aggregates per-interval totals into three series.
    public System.Collections.ObjectModel.ObservableCollection<TrafficSample> History { get; } = new();
    public const int MaxHistory = 3600; // ~last hour at 1s; scales with interval

    public record struct TrafficSample(double Tcp, double Udp, double Out, double Recv);

    // Diagnostics summary (from NetworkDiagnostics)
    private string _diagnosticsSummary = "Sessions: 0  Handshakes: 0/0  Beacons: 0/0";
    public string DiagnosticsSummary { get => _diagnosticsSummary; set { _diagnosticsSummary = value; OnPropertyChanged(); } }

    // Phase 4: Connection telemetry summary
    private string _connectionStatsSummary = "Direct: 0/0  NAT: 0/0  Relay: 0/0  Mismatch: 0";
    public string ConnectionStatsSummary { get => _connectionStatsSummary; set { _connectionStatsSummary = value; OnPropertyChanged(); } }

    // Phase 4: Relay health summary
    private string _relayHealthSummary = "No relays configured";
    public string RelayHealthSummary { get => _relayHealthSummary; set { _relayHealthSummary = value; OnPropertyChanged(); } }

    private int _healthScore = 100;
    public int HealthScore { get => _healthScore; private set { if (_healthScore != value) { _healthScore = value; OnPropertyChanged(); OnPropertyChanged(nameof(HealthScoreSummary)); } } }
    public string HealthScoreSummary => $"Health Score: {HealthScore}/100";

    private string _connectionDoctorSummary = "Connection Doctor: Network looks healthy.";
    public string ConnectionDoctorSummary { get => _connectionDoctorSummary; private set { if (_connectionDoctorSummary != value) { _connectionDoctorSummary = value; OnPropertyChanged(); } } }

    public void RefreshConnectionStats()
    {
        try
        {
            var snap = AppServices.Network.GetDiagnosticsSnapshot();
            var directTotal = snap.DirectSuccess + snap.DirectFail;
            var natTotal = snap.NatSuccess + snap.NatFail;
            var relayTotal = snap.RelaySuccess + snap.RelayFail;

            var directPct = directTotal > 0 ? $" ({snap.DirectSuccess * 100 / directTotal}%)" : "";
            var natPct = natTotal > 0 ? $" ({snap.NatSuccess * 100 / natTotal}%)" : "";
            var relayPct = relayTotal > 0 ? $" ({snap.RelaySuccess * 100 / relayTotal}%)" : "";

            ConnectionStatsSummary = $"Direct: {snap.DirectSuccess}/{directTotal}{directPct}  NAT: {snap.NatSuccess}/{natTotal}{natPct}  Relay: {snap.RelaySuccess}/{relayTotal}{relayPct}  Mismatch: {snap.UidMismatch}";

            var evaluation = Utilities.ConnectionHealthScoring.Evaluate(snap);
            HealthScore = evaluation.Score;
            ConnectionDoctorSummary = "Connection Doctor: " + evaluation.DoctorSummary;
        }
        catch { }

        try
        {
            var relays = AppServices.Network.GetRelayHealthSnapshots();
            if (relays.Count == 0)
            {
                RelayHealthSummary = "No relays configured";
            }
            else
            {
                var parts = new System.Collections.Generic.List<string>(relays.Count);
                foreach (var r in relays)
                {
                    var total = r.SuccessCount + r.FailureCount;
                    var status = total == 0 ? "untested"
                        : r.LastSuccessUtc > r.LastFailureUtc ? $"ok ({r.LatencyMs:F0}ms)"
                        : "failing";
                    parts.Add($"{r.Endpoint}: {status} [{r.SuccessCount}/{total}]");
                }
                RelayHealthSummary = string.Join("  |  ", parts);
            }
        }
        catch { RelayHealthSummary = "n/a"; }
    }

    // Log (rolling buffer): keep a compact history to bound memory while avoiding UI churn
    public const int MaxLogEntries = 800; // between 500-1000 is acceptable; 800 is a balanced default
    private readonly System.Collections.Generic.LinkedList<string> _log = new();
    public System.Collections.ObjectModel.ObservableCollection<string> LogItems { get; } = new();
    public bool IsLogEmpty => LogItems.Count == 0;
    private bool _showLog = true;
    public bool ShowLog { get => _showLog; set { if (_showLog != value) { _showLog = value; OnPropertyChanged(); } } }

    // Filtering: ALL / NET / WARNING / INFO
    private string _filterMode = "ALL";
    public string FilterMode { get => _filterMode; set { if (_filterMode != value) { _filterMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(FilteredLogItems)); } } }
    // Index mapping to ensure ComboBox default is set reliably on load
    public int FilterIndex
    {
        get => _filterMode switch { "ALL" => 0, "NET" => 1, "WARNING" => 2, "INFO" => 3, _ => 0 };
        set
        {
            var v = value switch { 0 => "ALL", 1 => "NET", 2 => "WARNING", 3 => "INFO", _ => "ALL" };
            if (_filterMode != v) { _filterMode = v; OnPropertyChanged(nameof(FilterMode)); OnPropertyChanged(nameof(FilteredLogItems)); OnPropertyChanged(); }
        }
    }
    public System.Collections.Generic.IEnumerable<string> FilteredLogItems
    {
        get
        {
            var mode = (FilterMode ?? "ALL").ToUpperInvariant();
            if (mode == "ALL") return _log.ToArray();
            bool IsMatch(string s) => mode switch
            {
                "NET" => s.StartsWith("NET:", StringComparison.OrdinalIgnoreCase) || s.Contains(" TCP ", StringComparison.Ordinal),
                "WARNING" => s.StartsWith("WARN:", StringComparison.OrdinalIgnoreCase) || s.Contains("WARNING", StringComparison.OrdinalIgnoreCase),
                "INFO" => !(s.StartsWith("NET:", StringComparison.OrdinalIgnoreCase) || s.StartsWith("WARN:", StringComparison.OrdinalIgnoreCase)),
                _ => true
            };
            return _log.Where(IsMatch).ToArray();
        }
    }
    public void AppendLog(string line)
    {
        _log.AddLast(line); LogItems.Add(line);
        // Smooth pruning to avoid flicker: trim only when exceeding the cap
        while (_log.Count > MaxLogEntries)
        {
            _log.RemoveFirst(); if (LogItems.Count > 0) LogItems.RemoveAt(0);
        }
        OnPropertyChanged(nameof(IsLogEmpty));
        OnPropertyChanged(nameof(FilteredLogItems));
    }
    public ICommand ClearLogCommand => new RelayCommand(_ => { _log.Clear(); LogItems.Clear(); OnPropertyChanged(nameof(IsLogEmpty)); OnPropertyChanged(nameof(FilteredLogItems)); });

    // Theme-aware log text color for contrast with current theme (use app theme service colors)
    public IBrush LogForeground
    => (AppServices.Theme.CurrentTheme == Zer0Talk.Models.ThemeOption.Dark)
            ? new SolidColorBrush(Color.FromUInt32(0xFFCCCCCC))
            : new SolidColorBrush(Color.FromUInt32(0xFF222222));
    public ICommand CopyAllCommand => new RelayCommand(async _ => { try { var txt = string.Join(Environment.NewLine, LogItems); var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime; if (lifetime?.MainWindow?.Clipboard != null) await lifetime.MainWindow.Clipboard.SetTextAsync(txt); } catch { } });
    public ICommand CloseCommand => new RelayCommand(_ =>
    {
        try
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var win = lifetime?.Windows?.OfType<Views.MonitoringWindow>().FirstOrDefault();
            win?.Close();
        }
        catch { }
    });

    public ICommand RetryNatVerificationCommand => new RelayCommand(async _ => { try { await AppServices.Nat.RetryVerificationAsync(); } catch { } });

    public void NotifyNetworkStatus()
    {
        OnPropertyChanged(nameof(NatStatus));
        OnPropertyChanged(nameof(NatStatusShort));
        OnPropertyChanged(nameof(NatVerification));
        OnPropertyChanged(nameof(TcpPortLabel));
        OnPropertyChanged(nameof(UdpPortLabel));
        OnPropertyChanged(nameof(ExternalPortLabel));
        OnPropertyChanged(nameof(NatGateway));
        OnPropertyChanged(nameof(NatExternalIp));
        OnPropertyChanged(nameof(NatService));
        OnPropertyChanged(nameof(NatAvailableServices));
        OnPropertyChanged(nameof(NatPunchSummary));
        OnPropertyChanged(nameof(NatPunchTooltip));
        OnPropertyChanged(nameof(HairpinTooltip));
        OnPropertyChanged(nameof(NatVerifyTimeLabel));
        OnPropertyChanged(nameof(NatMapAttemptLabel));
        // Discovery snapshot refresh
        OnPropertyChanged(nameof(DiscoveryState));
        OnPropertyChanged(nameof(DiscoveryAttempts));
        OnPropertyChanged(nameof(DiscoveryBackoff));
        OnPropertyChanged(nameof(DiscoveryLastAttemptLabel));
        OnPropertyChanged(nameof(DiscoveryLastSuccessLabel));
        OnPropertyChanged(nameof(PresencePipelineSummary));
        EvaluateNatIndicator();
        RefreshConnectionStats();
    }

    private void EvaluateNatIndicator()
    {
        try
        {
            var s = (NatStatus ?? string.Empty).ToLowerInvariant();
            var v = (NatVerification ?? string.Empty).ToLowerInvariant();
            if (s.Contains("discovering") || (s.Contains("gateway discovered") && string.IsNullOrWhiteSpace(v)))
            { NatIndicatorBrush = Brushes.Goldenrod; NatIndicatorBlink = true; if (NatIndicatorOpacity <= 0) NatIndicatorOpacity = 1.0; return; }
            // Explicit unmapped or no-gateway states should be neutral gray, not green
            if (s.Contains("unmapped") || v.Contains("unmapped") || s.Contains("no gateway"))
            { NatIndicatorBrush = Brushes.Gray; NatIndicatorBlink = false; NatIndicatorOpacity = 1.0; return; }
            if (s.Contains("failed") || v.Contains("unreachable") || v.Contains("failed"))
            { NatIndicatorBrush = Brushes.IndianRed; NatIndicatorBlink = false; NatIndicatorOpacity = 1.0; return; }
            if (v.Contains("reachable") || v.Contains("ok") || (s.Contains("mapped") && !s.Contains("unmapped")))
            { NatIndicatorBrush = Brushes.LimeGreen; NatIndicatorBlink = false; NatIndicatorOpacity = 1.0; return; }
            NatIndicatorBrush = Brushes.Gray; NatIndicatorBlink = false; NatIndicatorOpacity = 1.0;
        }
        catch { }
    }

    public void UpdateRates(System.Collections.Generic.Dictionary<int, (double In, double Out)> rates)
    {
        CurrentRates = rates;
        // Summaries
        double tcpIn = 0, tcpOut = 0, udpIn = 0, udpOut = 0, outIn = 0, outOut = 0;
        if (AppServices.Network.ListeningPort is int lp && rates.TryGetValue(lp, out var r1)) { tcpIn = r1.In; tcpOut = r1.Out; }
        else if (rates.TryGetValue(SessionSyntheticRateKey, out var rs)) { tcpIn = rs.In; tcpOut = rs.Out; }
        if (AppServices.Network.UdpBoundPort is int up && rates.TryGetValue(up, out var r2)) { udpIn = r2.In; udpOut = r2.Out; }
        if (AppServices.Network.LastAutoClientPort is int ap && rates.TryGetValue(ap, out var r3)) { outIn = r3.In; outOut = r3.Out; }
        TcpRate = $"TCP: {FormatRate(tcpIn + tcpOut)}";
        UdpRate = $"UDP: {FormatRate(udpIn + udpOut)}";
        OutRate = $"Outbound local: {FormatRate(outIn + outOut)}";
        RecvRate = $"{LocalizedRecvRate}: {FormatRate(tcpIn + udpIn + outIn)}";
        TcpBrush = (tcpIn + tcpOut) > 0 ? Brushes.LimeGreen : Brushes.Gray; OnPropertyChanged(nameof(TcpBrush));
        UdpBrush = (udpIn + udpOut) > 0 ? Brushes.LimeGreen : Brushes.Gray; OnPropertyChanged(nameof(UdpBrush));
        OutBrush = (outIn + outOut) > 0 ? Brushes.LimeGreen : Brushes.Gray; OnPropertyChanged(nameof(OutBrush));

        // Push a sample into rolling history for chart rendering; always add even if zero to keep cadence consistent.
        var tcpTotal = tcpIn + tcpOut;
        var udpTotal = udpIn + udpOut;
        var outTotal = outIn + outOut;
        var recvTotal = tcpIn + udpIn + outIn;
        History.Add(new TrafficSample(tcpTotal, udpTotal, outTotal, recvTotal));
        if (History.Count > MaxHistory)
        {
            // remove oldest in small batches to avoid per-frame churn when overshooting
            while (History.Count > MaxHistory) History.RemoveAt(0);
        }
    }

    private static string FormatRate(double bytesPerSec)
    {
        string[] units = new[] { "B/s", "KB/s", "MB/s", "GB/s" };
        int i = 0; while (bytesPerSec >= 1024 && i < units.Length - 1) { bytesPerSec /= 1024; i++; }
        return $"{bytesPerSec:0.#} {units[i]}";
    }

    public void UpdateDiagnostics(Zer0Talk.Utilities.NetworkDiagnostics.Snapshot s)
    {
        try
        {
            DiagnosticsSummary = $"Sessions: {s.SessionsActive}  Handshakes: {s.HandshakeOk}/{s.HandshakeFail}  Beacons: {s.UdpBeaconsSent}/{s.UdpBeaconsRecv}";
            OnPropertyChanged(nameof(PresencePipelineSummary));
            RefreshConnectionStats();
        }
        catch { }
    }

    // Diagnostics log: lightweight observable list with adjustable font size
    private double _logFontSize = 11; // default small size; user adjustable
    public double LogFontSize { get => _logFontSize; set { if (Math.Abs(_logFontSize - value) > 0.1) { _logFontSize = value; OnPropertyChanged(); PersistFontSize(); } } }

    public ICommand IncreaseLogFontCommand => new RelayCommand(_ => { LogFontSize = Math.Min(24, LogFontSize + 1); PersistFontSize(); });
    public ICommand DecreaseLogFontCommand => new RelayCommand(_ => { LogFontSize = Math.Max(8, LogFontSize - 1); PersistFontSize(); });

    private void PersistFontSize()
    {
        try
        {
            // Persist with Monitoring settings if available; safe no-op if settings not loaded yet.
            var s = AppServices.Settings.Settings;
            s.MonitoringLogFontSize = LogFontSize;
            _ = System.Threading.Tasks.Task.Run(() => AppServices.Settings.Save(AppServices.Passphrase));
        }
        catch { }
    }

    public void LoadPersistedPreferences()
    {
        try
        {
            var s = AppServices.Settings.Settings;
            if (s.MonitoringLogFontSize > 0) LogFontSize = s.MonitoringLogFontSize;
            GraphStyleIndex = Math.Clamp(s.MonitoringGraphStyleIndex, 0, 2);
            LegendPositionIndex = Math.Clamp(s.MonitoringLegendPositionIndex, 0, 1);
        }
        catch { }
    }
}

