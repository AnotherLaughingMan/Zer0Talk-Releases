using System;
using System.Globalization;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using Avalonia.Media;

using P2PTalk.Services;

namespace P2PTalk.ViewModels;

public class MonitoringViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
    public int IntervalIndex { get => _intervalIndex; set { if (_intervalIndex != value) { _intervalIndex = value; OnPropertyChanged(); OnIntervalChanged?.Invoke(value); } } }
    public event Action<int>? OnIntervalChanged;
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

    // NAT + ports
    public string NatStatus => AppServices.Nat.Status;
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
    public string NatVerifyTimeLabel => AppServices.Nat.LastVerificationUtc is DateTime t ? t.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "n/a";
    public string NatMapAttemptLabel => AppServices.Nat.LastMappingAttemptUtc is DateTime t ? t.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "n/a";

    // Discovery snapshot (tiny panel)
    public string DiscoveryState { get { try { return AppServices.Discovery.GetSnapshot().StateValue.ToString(); } catch { return "n/a"; } } }
    public int DiscoveryAttempts { get { try { return AppServices.Discovery.GetSnapshot().Attempts; } catch { return 0; } } }
    public int DiscoveryBackoff { get { try { return AppServices.Discovery.GetSnapshot().BackoffSeconds; } catch { return 0; } } }
    public string DiscoveryLastAttemptLabel { get { try { var t = AppServices.Discovery.GetSnapshot().LastAttemptUtc; return t is DateTime dt ? dt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "n/a"; } catch { return "n/a"; } } }
    public string DiscoveryLastSuccessLabel { get { try { var t = AppServices.Discovery.GetSnapshot().LastSuccessUtc; return t is DateTime dt ? dt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "n/a"; } catch { return "n/a"; } } }
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
    public IBrush TcpBrush { get; set; } = Brushes.Gray;
    public IBrush UdpBrush { get; set; } = Brushes.Gray;
    public IBrush OutBrush { get; set; } = Brushes.Gray;
    public System.Collections.Generic.Dictionary<int, (double In, double Out)> CurrentRates { get; private set; } = new();

    // Rolling history for persistent chart rendering. Each sample aggregates per-interval totals into three series.
    public System.Collections.ObjectModel.ObservableCollection<TrafficSample> History { get; } = new();
    public const int MaxHistory = 3600; // ~last hour at 1s; scales with interval

    public record struct TrafficSample(double Tcp, double Udp, double Out);

    // Diagnostics summary (from NetworkDiagnostics)
    private string _diagnosticsSummary = "Sessions: 0  Handshakes: 0/0  Beacons: 0/0";
    public string DiagnosticsSummary { get => _diagnosticsSummary; set { _diagnosticsSummary = value; OnPropertyChanged(); } }

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
    => (AppServices.Theme.CurrentTheme == ZTalk.Models.ThemeOption.Dark)
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
        OnPropertyChanged(nameof(NatVerification));
        OnPropertyChanged(nameof(TcpPortLabel));
        OnPropertyChanged(nameof(UdpPortLabel));
        OnPropertyChanged(nameof(ExternalPortLabel));
        OnPropertyChanged(nameof(NatGateway));
        OnPropertyChanged(nameof(NatExternalIp));
        OnPropertyChanged(nameof(NatService));
        OnPropertyChanged(nameof(NatAvailableServices));
        OnPropertyChanged(nameof(NatPunchSummary));
        OnPropertyChanged(nameof(NatVerifyTimeLabel));
        OnPropertyChanged(nameof(NatMapAttemptLabel));
        // Discovery snapshot refresh
        OnPropertyChanged(nameof(DiscoveryState));
        OnPropertyChanged(nameof(DiscoveryAttempts));
        OnPropertyChanged(nameof(DiscoveryBackoff));
        OnPropertyChanged(nameof(DiscoveryLastAttemptLabel));
        OnPropertyChanged(nameof(DiscoveryLastSuccessLabel));
        EvaluateNatIndicator();
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
        if (AppServices.Network.UdpBoundPort is int up && rates.TryGetValue(up, out var r2)) { udpIn = r2.In; udpOut = r2.Out; }
        if (AppServices.Network.LastAutoClientPort is int ap && rates.TryGetValue(ap, out var r3)) { outIn = r3.In; outOut = r3.Out; }
        TcpRate = $"TCP: {FormatRate(tcpIn + tcpOut)}";
        UdpRate = $"UDP: {FormatRate(udpIn + udpOut)}";
        OutRate = $"Outbound local: {FormatRate(outIn + outOut)}";
        TcpBrush = (tcpIn + tcpOut) > 0 ? Brushes.LimeGreen : Brushes.Gray; OnPropertyChanged(nameof(TcpBrush));
        UdpBrush = (udpIn + udpOut) > 0 ? Brushes.LimeGreen : Brushes.Gray; OnPropertyChanged(nameof(UdpBrush));
        OutBrush = (outIn + outOut) > 0 ? Brushes.LimeGreen : Brushes.Gray; OnPropertyChanged(nameof(OutBrush));

        // Push a sample into rolling history for chart rendering; always add even if zero to keep cadence consistent.
        var tcpTotal = tcpIn + tcpOut;
        var udpTotal = udpIn + udpOut;
        var outTotal = outIn + outOut;
        History.Add(new TrafficSample(tcpTotal, udpTotal, outTotal));
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

    public void UpdateDiagnostics(P2PTalk.Utilities.NetworkDiagnostics.Snapshot s)
    {
        try
        {
            DiagnosticsSummary = $"Sessions: {s.SessionsActive}  Handshakes: {s.HandshakeOk}/{s.HandshakeFail}  Beacons: {s.UdpBeaconsSent}/{s.UdpBeaconsRecv}";
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
        }
        catch { }
    }
}

