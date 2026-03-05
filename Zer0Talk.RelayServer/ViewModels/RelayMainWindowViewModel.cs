using System;
using System.Diagnostics;
using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.Media;
using Zer0Talk.RelayServer.Services;
using Zer0Talk.RelayServer.Utilities;

namespace Zer0Talk.RelayServer.ViewModels;

public sealed class RelayMainWindowViewModel : INotifyPropertyChanged
{
    private string _statusText = "Starting...";
    private int _pending;
    private int _active;
    private long _totalConnections;
    private long _offerCommands;
    private long _pollCommands;
    private long _waitPollCommands;
    private long _ackCommands;
    private long _ackMisses;
    private int _registeredClients;
    private RelaySessionInfo? _selectedSession;
    private bool _isClientsSidebarDetached;
    private string _copyToastText = string.Empty;
    private bool _isCopyToastVisible;
    private long _copyToastVersion;
    private DateTime? _lastLogTimestamp;
    private DateTime? _lastProbeAuditTimestamp;
    private string? _lastGroupedScannerMessage;
    private int _lastGroupedScannerCount;
    private string? _lastGroupedProbeAuditMessage;
    private int _lastGroupedProbeAuditCount;
    private static readonly IBrush DefaultProbeLogLineBrush = new SolidColorBrush(Color.FromUInt32(0xFFD6D6D6));
    private static readonly IBrush FailureProbeLogLineBrush = new SolidColorBrush(Color.FromUInt32(0xFFFF9A9A));
    private static readonly IBrush WarningProbeLogLineBrush = new SolidColorBrush(Color.FromUInt32(0xFFFFE68A));
    private static readonly IBrush SuccessProbeLogLineBrush = new SolidColorBrush(Color.FromUInt32(0xFFAAFFAA));
    private static readonly IBrush SuspiciousProbeLogLineBrush = new SolidColorBrush(Color.FromUInt32(0xFF8FE6E6));
    private static readonly string[] FailureProbeTokens = new[] { "result=fail", "assert failed", "fail", "failed", "failure", "error", "denied", "reject", "timeout", "unreachable", "exception", "critical" };
    private static readonly string[] WarningProbeTokens = new[] { "warn", "warning", "caution", "degraded", "use-federation-port", "lookupwarn", "postunregwarn", "batchregwarn", "batchofferwarn", "batchinvitewarn", "batchackwarn" };
    private static readonly string[] SuccessProbeTokens = new[] { "result=pass", "success", "succeeded", "passed", "pass", "ok", "accepted", "connected", "healthy", "health " };
    private static readonly string[] ProbeContextTokens = new[] { "probe", "federation", "relay-health", "relay-lookup", "health_bad", "lookup_bad" };
    private static readonly string[] SuspiciousProbeTokens = new[] { "unauthorized", "bad secret", "non-zer0talk", "non zer0talk", "not zer0talk", "client probe", "unknown client", "foreign client", "untrusted client", "spoof" };

    public RelayMainWindowViewModel()
    {
        Logs = new ObservableCollection<string>();
        ProbeAuditLogs = new ObservableCollection<string>();
        ProbeAuditLineEntries = new ObservableCollection<RelayProbeAuditLineEntry>();
        Sessions = new ObservableCollection<RelaySessionInfo>();
        Clients = new ObservableCollection<RelayClientInfo>();
        Settings = new RelaySettingsViewModel();
        RelayAppServices.Host.Log += OnLog;
        RelayAppServices.Host.ProbeAuditLogged += OnProbeAuditLog;
        RelayAppServices.Host.StatsChanged += OnStatsChanged;
        RelayAppServices.Host.SessionsChanged += OnSessionsChanged;
        RelayAppServices.Host.ClientsChanged += OnClientsChanged;
        foreach (var entry in RelayAppServices.Host.GetProbeAuditSnapshot())
        {
            AppendProbeAuditEntry(entry, insertAtTop: false);
        }
        OnClientsChanged(RelayAppServices.Host.GetRegisteredClientsSnapshot());
        StatusText = RelayAppServices.Host.IsRunning ? "Running" : "Stopped";

        StartCommand = new RelayCommand(StartRelay, () => !RelayAppServices.Host.IsRunning);
        StopCommand = new RelayCommand(RequestStopRelay, () => RelayAppServices.Host.IsRunning);
        RestartCommand = new RelayCommand(RestartRelay, () => RelayAppServices.Host.IsRunning);
        PauseResumeCommand = new RelayCommand(RequestPauseResume, () => RelayAppServices.Host.IsRunning);
        DisconnectCommand = new RelayCommand(DisconnectSelected, () => SelectedSession != null);
        OpenConfigGuideCommand = new RelayCommand(OpenConfigGuide);
        OpenConfigCommand = new RelayCommand(OpenConfigFile);
        OpenSettingsCommand = new RelayCommand(() => Settings.Show());
        OpenProbeAuditLogCommand = new RelayCommand(OpenProbeAuditLog);
        ClearProbeAuditLogCommand = new RelayCommand(ClearProbeAuditLog);
        ToggleClientsSidebarCommand = new RelayCommand(ToggleClientsSidebarDetached);
        ShowClientsWindowCommand = new RelayCommand(ShowClientsWindow);
        CopyRelayTokenCommand = new RelayCommand(() => CopyToClipboard(RelayToken, "Relay token copied"));
        CopyClientUidCommand = new RelayCommand(parameter =>
        {
            if (parameter is RelayClientInfo client && !string.IsNullOrWhiteSpace(client.Uid))
            {
                CopyToClipboard(client.Uid, $"Client UID copied: {client.Uid}");
            }
        });
        CopyClientPublicKeyCommand = new RelayCommand(parameter =>
        {
            if (parameter is RelayClientInfo client && !string.IsNullOrWhiteSpace(client.PublicKey))
            {
                CopyToClipboard(client.PublicKey, $"Client public key copied: {client.Uid}");
            }
        });
        CopyClientCombinedCommand = new RelayCommand(parameter =>
        {
            if (parameter is RelayClientInfo client)
            {
                CopyToClipboard($"{client.Uid} | {client.PublicKey}", $"Client entry copied: {client.Uid}");
            }
        });
        BlockClientCommand = new RelayCommand(parameter =>
        {
            if (parameter is not RelayClientInfo client || string.IsNullOrWhiteSpace(client.ModerationHandle))
            {
                return;
            }

            var blocked = RelayAppServices.Host.BlockClientByHandle(client.ModerationHandle);
            if (blocked)
            {
                OnLog($"Operator block applied for {client.ModerationHandle}");
            }
            else
            {
                OnLog($"Operator block skipped: handle not found ({client.ModerationHandle})");
            }
        });
    }

    public string Title => "Zer0Talk Relay";
    public string TagText => RelayAppInfo.PrototypeBadgeText;
    public string RelayToken => RelayAppServices.Config.RelayAddressToken;
    public RelaySettingsViewModel Settings { get; }
    public string PauseResumePath => RelayAppServices.Host.IsPaused ? "M7,5 L17,12 L7,19 Z" : "M6,5 L10,5 L10,19 L6,19 Z M14,5 L18,5 L18,19 L14,19 Z";
    public string PauseResumeTooltip => RelayAppServices.Host.IsPaused ? "Resume" : "Pause";
    public bool IsClientsSidebarAttached => !_isClientsSidebarDetached;
    public string ClientsSidebarToggleText => _isClientsSidebarDetached ? "Attach" : "Detach";
    public string ClientsSidebarToggleTooltip => _isClientsSidebarDetached ? "Attach clients panel to main window" : "Detach clients panel";
    public string CopyToastText
    {
        get => _copyToastText;
        private set => SetField(ref _copyToastText, value);
    }

    public bool IsCopyToastVisible
    {
        get => _isCopyToastVisible;
        private set => SetField(ref _isCopyToastVisible, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public int PendingCount
    {
        get => _pending;
        private set => SetField(ref _pending, value);
    }

    public int ActiveCount
    {
        get => _active;
        private set => SetField(ref _active, value);
    }

    public long TotalConnections
    {
        get => _totalConnections;
        private set => SetField(ref _totalConnections, value);
    }

    public long OfferCommands
    {
        get => _offerCommands;
        private set
        {
            if (SetField(ref _offerCommands, value))
            {
                OnPropertyChanged(nameof(OfferAckRatioText));
                OnPropertyChanged(nameof(OfferAckAnomalyForeground));
                OnPropertyChanged(nameof(OfferAckAnomalyFontWeight));
            }
        }
    }

    public long PollCommands
    {
        get => _pollCommands;
        private set => SetField(ref _pollCommands, value);
    }

    public long WaitPollCommands
    {
        get => _waitPollCommands;
        private set => SetField(ref _waitPollCommands, value);
    }

    public long AckCommands
    {
        get => _ackCommands;
        private set
        {
            if (SetField(ref _ackCommands, value))
            {
                OnPropertyChanged(nameof(OfferAckRatioText));
                OnPropertyChanged(nameof(OfferAckAnomalyForeground));
                OnPropertyChanged(nameof(OfferAckAnomalyFontWeight));
            }
        }
    }

    public long AckMisses
    {
        get => _ackMisses;
        private set
        {
            if (SetField(ref _ackMisses, value))
            {
                OnPropertyChanged(nameof(AckMissForeground));
                OnPropertyChanged(nameof(AckMissFontWeight));
            }
        }
    }

    public int RegisteredClients
    {
        get => _registeredClients;
        private set => SetField(ref _registeredClients, value);
    }

    public IBrush AckMissForeground => AckMisses > 0
        ? new SolidColorBrush(Color.Parse("#FF8A80"))
        : new SolidColorBrush(Color.Parse("#F2F2F2"));

    public FontWeight AckMissFontWeight => AckMisses > 0
        ? FontWeight.SemiBold
        : FontWeight.Normal;

    public string OfferAckRatioText
    {
        get
        {
            if (OfferCommands <= 0) return "n/a";
            var ratio = (double)AckCommands / OfferCommands;
            return $"{ratio * 100:0.#}%";
        }
    }

    public IBrush OfferAckAnomalyForeground => IsOfferAckAnomalous()
        ? new SolidColorBrush(Color.Parse("#FF8A80"))
        : new SolidColorBrush(Color.Parse("#F2F2F2"));

    public FontWeight OfferAckAnomalyFontWeight => IsOfferAckAnomalous()
        ? FontWeight.SemiBold
        : FontWeight.Normal;

    private bool IsOfferAckAnomalous()
    {
        if (OfferCommands < 20) return false;
        return (double)AckCommands / OfferCommands < 0.35;
    }

    public ObservableCollection<string> Logs { get; }
    public ObservableCollection<string> ProbeAuditLogs { get; }
    public ObservableCollection<RelayProbeAuditLineEntry> ProbeAuditLineEntries { get; }
    public ObservableCollection<RelaySessionInfo> Sessions { get; }
    public ObservableCollection<RelayClientInfo> Clients { get; }

    public RelaySessionInfo? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetField(ref _selectedSession, value))
            {
                DisconnectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand RestartCommand { get; }
    public RelayCommand PauseResumeCommand { get; }
    public RelayCommand DisconnectCommand { get; }
    public RelayCommand OpenConfigGuideCommand { get; }
    public RelayCommand OpenConfigCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand OpenProbeAuditLogCommand { get; }
    public RelayCommand ClearProbeAuditLogCommand { get; }
    public RelayCommand ToggleClientsSidebarCommand { get; }
    public RelayCommand ShowClientsWindowCommand { get; }
    public RelayCommand CopyRelayTokenCommand { get; }
    public RelayCommand CopyClientUidCommand { get; }
    public RelayCommand CopyClientPublicKeyCommand { get; }
    public RelayCommand CopyClientCombinedCommand { get; }
    public RelayCommand BlockClientCommand { get; }

    public bool IsClientsSidebarDetached
    {
        get => _isClientsSidebarDetached;
        set
        {
            if (SetField(ref _isClientsSidebarDetached, value))
            {
                OnPropertyChanged(nameof(IsClientsSidebarAttached));
                OnPropertyChanged(nameof(ClientsSidebarToggleText));
                OnPropertyChanged(nameof(ClientsSidebarToggleTooltip));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? ShowClientsWindowRequested;
    public event Action? OpenProbeAuditLogRequested;
    public event Action? StopRelayRequested;
    public event Action? PauseResumeRequested;

    private void OnLog(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var displayMessage = ToOperatorMessage(message);
            var now = DateTime.Now;
            var deltaText = FormatDelta(now, _lastLogTimestamp);
            _lastLogTimestamp = now;

            var severity = GetSeveritySymbol(displayMessage);
            var scannerKey = GetScannerGroupingKey(message);

            if (scannerKey != null &&
                _lastGroupedScannerMessage != null &&
                string.Equals(scannerKey, _lastGroupedScannerMessage, StringComparison.Ordinal) &&
                Logs.Count > 0)
            {
                _lastGroupedScannerCount++;
                Logs[Logs.Count - 1] = FormatLogLine(now, deltaText, severity, displayMessage, _lastGroupedScannerCount);
                return;
            }

            if (scannerKey != null)
            {
                _lastGroupedScannerMessage = scannerKey;
                _lastGroupedScannerCount = 1;
            }
            else
            {
                _lastGroupedScannerMessage = null;
                _lastGroupedScannerCount = 0;
            }

            Logs.Add(FormatLogLine(now, deltaText, severity, displayMessage, 1));
        });
    }

    private static string ToOperatorMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Relay status updated.";
        }

        if (message.StartsWith("OFFER stored:", StringComparison.OrdinalIgnoreCase))
        {
            return "Saved a relay invite for a peer. Waiting for both sides to join.";
        }

        if (message.StartsWith("Relay queued pending session ", StringComparison.OrdinalIgnoreCase))
        {
            return "One side connected. Waiting for the other side to join this relay session.";
        }

        if (message.StartsWith("Relay duplicate pending ignored ", StringComparison.OrdinalIgnoreCase))
        {
            return "Ignored a duplicate join from the same side. Still waiting for the other side.";
        }

        if (message.StartsWith("Relay queue imbalance ", StringComparison.OrdinalIgnoreCase))
        {
            return "The same side keeps arriving first. The counterpart is not joining in time.";
        }

        if (message.StartsWith("Relay paired session ", StringComparison.OrdinalIgnoreCase))
        {
            return "Both sides connected. Relay session is now active.";
        }

        if (message.StartsWith("Relay session ", StringComparison.OrdinalIgnoreCase) &&
            message.Contains(" ended", StringComparison.OrdinalIgnoreCase))
        {
            return "Relay session ended.";
        }

        if (message.StartsWith("Relay rejected ", StringComparison.OrdinalIgnoreCase) &&
            message.Contains(": capacity", StringComparison.OrdinalIgnoreCase))
        {
            return "Relay is at capacity. A new session could not be queued right now.";
        }

        if (message.StartsWith("Relay rejected ", StringComparison.OrdinalIgnoreCase) &&
            message.Contains(": already active", StringComparison.OrdinalIgnoreCase))
        {
            return "Session already active. New join was rejected.";
        }

        if (message.StartsWith("Relay rejected ", StringComparison.OrdinalIgnoreCase) &&
            message.Contains(": cooldown", StringComparison.OrdinalIgnoreCase))
        {
            return "Session is in cooldown after a recent failure. Retry shortly.";
        }

        if (message.StartsWith("Relay rejected ", StringComparison.OrdinalIgnoreCase) &&
            message.Contains(": incompatible roles", StringComparison.OrdinalIgnoreCase))
        {
            return "Both peers tried to join from the same side role. Session pairing rejected.";
        }

        return message;
    }

    private static string FormatLogLine(DateTime timestamp, string deltaText, string severity, string message, int count)
    {
        var countSuffix = count > 1 ? $" (x{count})" : string.Empty;
        return $"[{timestamp:yyyy-MM-dd HH:mm:ss} +{deltaText}] {severity} {message}{countSuffix}";
    }

    private static string FormatDelta(DateTime current, DateTime? previous)
    {
        if (!previous.HasValue)
        {
            return "0.0s";
        }

        var seconds = (current - previous.Value).TotalSeconds;
        if (seconds < 0) seconds = 0;
        return seconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
    }

    private static string? GetScannerGroupingKey(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;

        if (message.Contains("Rejected non-protocol probe from ", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Relay received invalid command from ", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Relay rate limit blocked ", StringComparison.OrdinalIgnoreCase))
        {
            return message.Trim();
        }

        return null;
    }

    private static string GetSeveritySymbol(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "\u00B7";

        var lower = message.ToLowerInvariant();
        if (lower.Contains("failed", StringComparison.Ordinal) ||
            lower.Contains("failure", StringComparison.Ordinal) ||
            lower.Contains("error", StringComparison.Ordinal) ||
            lower.Contains("rejected", StringComparison.Ordinal) ||
            lower.Contains("invalid", StringComparison.Ordinal) ||
            lower.Contains("timeout", StringComparison.Ordinal) ||
            lower.Contains("unauthorized", StringComparison.Ordinal) ||
            lower.Contains("blocked", StringComparison.Ordinal))
        {
            return "!";
        }

        if (lower.Contains("queued", StringComparison.Ordinal) ||
            lower.Contains("paired", StringComparison.Ordinal) ||
            lower.Contains("offer", StringComparison.Ordinal) ||
            lower.Contains("poll", StringComparison.Ordinal) ||
            lower.Contains("waitpoll", StringComparison.Ordinal) ||
            lower.Contains("ack", StringComparison.Ordinal) ||
            lower.Contains("reg", StringComparison.Ordinal) ||
            lower.Contains("unreg", StringComparison.Ordinal) ||
            lower.Contains("session", StringComparison.Ordinal) ||
            lower.Contains("federation", StringComparison.Ordinal))
        {
            return "-";
        }

        return "\u00B7";
    }

    private void OnProbeAuditLog(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AppendProbeAuditEntry(message, insertAtTop: false);
        });
    }

    private void AppendProbeAuditEntry(string message, bool insertAtTop)
    {
        var now = DateTime.Now;
        var deltaText = FormatDelta(now, _lastProbeAuditTimestamp);
        _lastProbeAuditTimestamp = now;

        var severity = GetSeveritySymbol(message);
        var groupingKey = GetScannerGroupingKey(message);

        if (groupingKey != null &&
            _lastGroupedProbeAuditMessage != null &&
            string.Equals(groupingKey, _lastGroupedProbeAuditMessage, StringComparison.Ordinal) &&
            ProbeAuditLineEntries.Count > 0)
        {
            _lastGroupedProbeAuditCount++;
            var formattedGrouped = FormatLogLine(now, deltaText, severity, message, _lastGroupedProbeAuditCount);
            var foreground = GetProbeAuditForeground(formattedGrouped);

            var targetIndex = insertAtTop ? 0 : ProbeAuditLineEntries.Count - 1;
            ProbeAuditLogs[targetIndex] = formattedGrouped;
            ProbeAuditLineEntries[targetIndex] = new RelayProbeAuditLineEntry(formattedGrouped, foreground);
            return;
        }

        if (groupingKey != null)
        {
            _lastGroupedProbeAuditMessage = groupingKey;
            _lastGroupedProbeAuditCount = 1;
        }
        else
        {
            _lastGroupedProbeAuditMessage = null;
            _lastGroupedProbeAuditCount = 0;
        }

        var formatted = FormatLogLine(now, deltaText, severity, message, 1);
        var brush = GetProbeAuditForeground(formatted);
        if (insertAtTop)
        {
            ProbeAuditLogs.Insert(0, formatted);
            ProbeAuditLineEntries.Insert(0, new RelayProbeAuditLineEntry(formatted, brush));
        }
        else
        {
            ProbeAuditLogs.Add(formatted);
            ProbeAuditLineEntries.Add(new RelayProbeAuditLineEntry(formatted, brush));
        }
    }

    private static IBrush GetProbeAuditForeground(string line)
    {
        var text = (line ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text)) return DefaultProbeLogLineBrush;

        var lower = text.ToLowerInvariant();
        var isProbeLine = ContainsAny(lower, ProbeContextTokens);
        if (isProbeLine && ContainsAny(lower, SuspiciousProbeTokens))
            return SuspiciousProbeLogLineBrush;

        if (ContainsAny(lower, FailureProbeTokens))
            return FailureProbeLogLineBrush;

        if (ContainsAny(lower, WarningProbeTokens))
            return WarningProbeLogLineBrush;

        if (ContainsAny(lower, SuccessProbeTokens))
            return SuccessProbeLogLineBrush;

        return DefaultProbeLogLineBrush;
    }

    private static bool ContainsAny(string value, string[] tokens)
    {
        for (var i = 0; i < tokens.Length; i++)
        {
            if (value.Contains(tokens[i], StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private void OnStatsChanged(RelayStats stats)
    {
        Dispatcher.UIThread.Post(() =>
        {
            PendingCount = stats.Pending;
            ActiveCount = stats.Active;
            TotalConnections = stats.TotalConnections;
            OfferCommands = stats.OfferCommands;
            PollCommands = stats.PollCommands;
            WaitPollCommands = stats.WaitPollCommands;
            AckCommands = stats.AckCommands;
            AckMisses = stats.AckMisses;
            RegisteredClients = stats.RegisteredClients;
            StatusText = RelayAppServices.Host.IsPaused ? "Paused" : (RelayAppServices.Host.IsRunning ? "Running" : "Stopped");
            StartCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
            RestartCommand.RaiseCanExecuteChanged();
            PauseResumeCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(PauseResumePath));
            OnPropertyChanged(nameof(PauseResumeTooltip));
        });
    }

    private void OnSessionsChanged(System.Collections.Generic.IReadOnlyList<RelaySessionInfo> sessions)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Sessions.Clear();
            foreach (var session in sessions)
            {
                Sessions.Add(session);
            }
        });
    }

    private void OnClientsChanged(System.Collections.Generic.IReadOnlyList<RelayClientInfo> clients)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Clients.Clear();
            foreach (var client in clients)
            {
                Clients.Add(client);
            }
        });
    }

    private void StartRelay()
    {
        RelayAppServices.Host.Start();
        RefreshCommandStates();
    }

    private void RequestStopRelay()
    {
        try { StopRelayRequested?.Invoke(); } catch { }
    }

    private void RequestPauseResume()
    {
        try { PauseResumeRequested?.Invoke(); } catch { }
    }

    private void StopRelay()
    {
        RelayAppServices.Host.Stop();
        RefreshCommandStates();
    }

    private void RestartRelay()
    {
        if (!RelayAppServices.Host.IsRunning)
        {
            RelayAppServices.Host.Start();
            RefreshCommandStates();
            return;
        }

        RelayAppServices.Host.Stop();
        RelayAppServices.Host.Start();
        RefreshCommandStates();
    }

    public void ExecuteStopRelayFromUi() => StopRelay();

    public void ExecutePauseResumeFromUi() => TogglePauseResume();

    private void TogglePauseResume()
    {
        if (RelayAppServices.Host.IsPaused)
            RelayAppServices.Host.Resume();
        else
            RelayAppServices.Host.Pause();

        RefreshCommandStates();
    }

    private void ToggleClientsSidebarDetached()
    {
        IsClientsSidebarDetached = !IsClientsSidebarDetached;
    }

    private void ShowClientsWindow()
    {
        try { ShowClientsWindowRequested?.Invoke(); } catch { }
    }

    private void OpenProbeAuditLog()
    {
        try { OpenProbeAuditLogRequested?.Invoke(); } catch { }
    }

    private void ClearProbeAuditLog()
    {
        try { RelayAppServices.Host.ClearProbeAuditLogs(); } catch { }

        ProbeAuditLogs.Clear();
        ProbeAuditLineEntries.Clear();
        _lastProbeAuditTimestamp = null;
        _lastGroupedProbeAuditMessage = null;
        _lastGroupedProbeAuditCount = 0;
    }

    private void CopyToClipboard(string text, string successLog)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                var clipboard = desktop?.MainWindow?.Clipboard;
                if (clipboard == null)
                {
                    OnLog("Copy failed: clipboard unavailable");
                    return;
                }

                await clipboard.SetTextAsync(text);
                OnLog(successLog);
                ShowCopyToast(successLog);
            }
            catch (Exception ex)
            {
                OnLog($"Copy failed: {ex.Message}");
            }
        });
    }

    private void ShowCopyToast(string message)
    {
        var version = Interlocked.Increment(ref _copyToastVersion);

        Dispatcher.UIThread.Post(() =>
        {
            CopyToastText = message;
            IsCopyToastVisible = true;
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1800).ConfigureAwait(false);
                if (Interlocked.Read(ref _copyToastVersion) != version) return;

                Dispatcher.UIThread.Post(() =>
                {
                    if (Interlocked.Read(ref _copyToastVersion) != version) return;
                    IsCopyToastVisible = false;
                });
            }
            catch { }
        });
    }

    private void RefreshCommandStates()
    {
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        RestartCommand.RaiseCanExecuteChanged();
        PauseResumeCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(PauseResumePath));
        OnPropertyChanged(nameof(PauseResumeTooltip));
    }

    private void DisconnectSelected()
    {
        if (SelectedSession == null) return;
        RelayAppServices.Host.DisconnectSession(SelectedSession.SessionKey);
    }

    private void OpenConfigFile()
    {
        try
        {
            var configPath = RelayConfigStore.GetConfigPath();
            var configDir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrWhiteSpace(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            if (!File.Exists(configPath))
            {
                RelayConfigStore.Save(RelayAppServices.Config);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = configPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logs.Add($"Failed to open relay config: {ex.Message}");
        }
    }

        private void OpenConfigGuide()
        {
                try
                {
                        var guidePath = RelayConfigStore.GetConfigGuidePath();
                        var guideDir = Path.GetDirectoryName(guidePath);
                        if (!string.IsNullOrWhiteSpace(guideDir))
                        {
                                Directory.CreateDirectory(guideDir);
                        }

                        File.WriteAllText(guidePath, BuildConfigGuideMarkdown());

                        Process.Start(new ProcessStartInfo
                        {
                                FileName = guidePath,
                                UseShellExecute = true
                        });
                }
                catch (Exception ex)
                {
                    Logs.Add($"Failed to open relay config guide: {ex.Message}");
                }
        }

        private static string BuildConfigGuideMarkdown()
        {
                return """
# Zer0Talk Relay - JSON Config Guide

Config file path:
`%APPDATA%\\Zer0TalkRelay\\relay-config.json`

## Example
```json
{
    "Port": 443,
    "DiscoveryPort": 38384,
    "AutoStart": true,
    "DiscoveryEnabled": true,
    "RelayAddressToken": "AbC123!xyz...",
    "MaxPending": 256,
    "MaxSessions": 512,
    "PendingTimeoutSeconds": 60,
    "BufferSize": 16384,
    "MaxConnectionsPerMinute": 120,
    "BanSeconds": 120,
    "ExposeSensitiveClientData": false,
    "OperatorBlockSeconds": 1800,
    "ShowInSystemTray": true,
    "MinimizeToTray": true,
    "StartMinimized": false,
    "RunOnStartup": false,
    "EnableFederation": false,
    "FederationPort": 8443,
    "FederationTrustMode": "AllowList",
    "PeerRelays": [],
    "MaxFederationPeers": 10,
    "FederationSyncIntervalSeconds": 30,
    "FederationSharedSecret": ""
}
```

## Field-by-field reference

### `Port` (number)
- What it does: TCP listen port for relay clients/peers.
- Recommended: `443`.
- Rules:
    - Must be a valid TCP port (`1` to `65535`).
    - If in use by another app, relay cannot bind.
    - Ports below `1024` may require elevated rights on some systems.
    - Internet deployment: forward this port as `TCP` on your router.

### `DiscoveryPort` (number)
- What it does: UDP port used for LAN relay discovery beacons.
- Recommended: `38384`.
- Rules:
    - Must be a valid UDP port (`1` to `65535`).
    - Relay and clients on the same LAN should use the same value.
    - Usually LAN-only; do not internet-forward unless you intentionally need cross-subnet discovery.

### `AutoStart` (boolean)
- What it does: Starts relay host automatically when UI launches.
- Recommended: `true` for dedicated relay machines.
- Rules: `true` or `false` only.

### `DiscoveryEnabled` (boolean)
- What it does: Enables LAN relay discovery broadcast.
- Recommended: `true` on trusted LAN, `false` if you want less network noise.
- Rules: `true` or `false` only.
- Protocol details:
    - Discovery uses UDP port `38384` (multicast + broadcast) on LAN.
    - Do not port-forward UDP `38384` to the internet.

### `RelayAddressToken` (string)
- What it does: Public relay identifier for discovery/connect metadata.
- Recommended: Leave existing generated token unless rotation is needed.
- Rules:
    - Must be exactly 16 characters.
    - No whitespace.
    - Cannot include `|` or `:`.
    - If invalid, app regenerates on load.

### `MaxPending` (number)
- What it does: Max queued pending handshake entries.
- Recommended: `256`.
- Rules:
    - Should be positive (`> 0`).
    - Higher values use more memory under load.

### `MaxSessions` (number)
- What it does: Max simultaneously active paired sessions.
- Recommended: `512` for typical desktop usage.
- Rules:
    - Should be positive (`> 0`).
    - Set based on CPU/RAM/network capacity.

### `PendingTimeoutSeconds` (number)
- What it does: Time before unpaired pending requests expire.
- Recommended: `30` to `60`.
- Rules:
    - Should be positive (`> 0`).
    - Too low can reject slow peers.
    - Too high can keep stale pending entries.

### `BufferSize` (number)
- What it does: Per-stream transfer buffer size in bytes.
- Recommended: `16384` (16 KiB).
- Rules:
    - Should be a positive integer.
    - Typical range: `4096` to `65536`.
    - Larger buffers can improve throughput but consume more memory.

### `MaxConnectionsPerMinute` (number)
- What it does: Rate-limit threshold per source IP.
- Recommended: `120`.
- Rules:
    - Should be positive (`> 0`).
    - Lower values are stricter anti-abuse protection.

### `BanSeconds` (number)
- What it does: Temporary block duration after rate-limit violations.
- Recommended: `120`.
- Rules:
    - Should be positive (`> 0`).
    - Longer values are stricter and may impact legitimate bursty clients.

### `ShowInSystemTray` (boolean)
- What it does: Displays tray icon and tray menu.
- Recommended: `true`.
- Rules: `true` or `false`.

### `MinimizeToTray` (boolean)
- What it does: Window close button hides app instead of fully exiting.
- Recommended: `true`.
- Rules: `true` or `false`.

### `StartMinimized` (boolean)
- What it does: UI starts hidden/minimized to tray.
- Recommended: `false` unless server is unattended.
- Rules: `true` or `false`.

### `RunOnStartup` (boolean)
- What it does: Registers relay UI in Windows startup (current user).
- Recommended: optional, machine role dependent.
- Rules: `true` or `false`.

### `EnableFederation` (boolean)
- What it does: Enables relay-to-relay federation commands.
- Recommended: `false` for single-relay setups; `true` only when intentionally federating trusted relays.
- Rules: `true` or `false`.

### `FederationPort` (number)
- What it does: Dedicated TCP port for relay federation commands.
- Recommended: `8443`.
- Rules:
    - Must be a valid TCP port (`1` to `65535`).
    - If equal to `Port`, federation shares the main relay listener.
    - If different from `Port`, open this port for trusted relay peers only.

### `FederationTrustMode` (string)
- What it does: Controls which relays may issue `RELAY-*` federation commands.
- Recommended: `AllowList`.
- Rules:
    - `AllowList`: only configured `PeerRelays` are trusted.
    - `OpenNetwork`: any relay may connect (less secure).

### `FederationSharedSecret` (string)
- What it does: Optional shared secret required for federation commands.
- Recommended: set a strong shared secret for production federation.
- Rules:
    - Must match exactly across all trusted relays.
    - Leave empty only in controlled test environments.

### `PeerRelays` (array of strings)
- What it does: Trusted relay federation endpoints.
- Recommended format: `host:port` entries using federation port (for example `relay2.example.com:8443`).
- Rules:
    - Use reachable hostnames/IPs.
    - Keep this list aligned on all federated nodes.

## Editing rules
- Keep valid JSON syntax (double quotes, commas, braces).
- Do not duplicate keys.
- Save file as UTF-8 text.
- Restart relay app after edits for full consistency.

## Safety checklist
- Change one setting at a time.
- Keep a backup copy before major tuning.
- If startup fails after edits, restore previous known-good config.
""";
        }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class RelayProbeAuditLineEntry
{
    public RelayProbeAuditLineEntry(string text, IBrush foreground)
    {
        Text = text;
        Foreground = foreground;
    }

    public string Text { get; }
    public IBrush Foreground { get; }
}
