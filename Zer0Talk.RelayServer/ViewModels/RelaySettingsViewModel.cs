using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Zer0Talk.RelayServer.Services;
using Zer0Talk.RelayServer.Utilities;

namespace Zer0Talk.RelayServer.ViewModels;

public sealed class RelaySettingsViewModel : INotifyPropertyChanged
{
    private bool _showInSystemTray;
    private bool _minimizeToTray;
    private bool _startMinimized;
    private bool _runOnStartup;
    private bool _enableSmoothScrolling;
    private bool _isVisible;
    private string _networkInfoMessage = string.Empty;
    private string _networkErrorMessage = string.Empty;

    public RelaySettingsViewModel()
    {
        LoadFromConfig();
        CloseCommand = new RelayCommand(() => IsVisible = false);
        SaveCommand = new RelayCommand(Save);
        RunFirewallTroubleshooterCommand = new RelayCommand(RunFirewallTroubleshooter);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetField(ref _isVisible, value);
    }

    public bool ShowInSystemTray
    {
        get => _showInSystemTray;
        set => SetField(ref _showInSystemTray, value);
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => SetField(ref _minimizeToTray, value);
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set => SetField(ref _startMinimized, value);
    }

    public bool RunOnStartup
    {
        get => _runOnStartup;
        set => SetField(ref _runOnStartup, value);
    }

    public bool EnableSmoothScrolling
    {
        get => _enableSmoothScrolling;
        set => SetField(ref _enableSmoothScrolling, value);
    }

    public string NetworkInfoMessage
    {
        get => _networkInfoMessage;
        private set
        {
            if (SetField(ref _networkInfoMessage, value))
            {
                OnPropertyChanged(nameof(HasNetworkInfoMessage));
            }
        }
    }

    public string NetworkErrorMessage
    {
        get => _networkErrorMessage;
        private set
        {
            if (SetField(ref _networkErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasNetworkErrorMessage));
            }
        }
    }

    public bool HasNetworkInfoMessage => !string.IsNullOrWhiteSpace(NetworkInfoMessage);
    public bool HasNetworkErrorMessage => !string.IsNullOrWhiteSpace(NetworkErrorMessage);

    public RelayCommand CloseCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand RunFirewallTroubleshooterCommand { get; }

    public void Show()
    {
        LoadFromConfig();
        IsVisible = true;
    }

    private void LoadFromConfig()
    {
        var cfg = RelayAppServices.Config;
        ShowInSystemTray = cfg.ShowInSystemTray;
        MinimizeToTray = cfg.MinimizeToTray;
        StartMinimized = cfg.StartMinimized;
        RunOnStartup = cfg.RunOnStartup;
        EnableSmoothScrolling = cfg.EnableSmoothScrolling;
    }

    private void Save()
    {
        var cfg = RelayAppServices.Config;
        cfg.ShowInSystemTray = ShowInSystemTray;
        cfg.MinimizeToTray = MinimizeToTray;
        cfg.StartMinimized = StartMinimized;
        cfg.RunOnStartup = RunOnStartup;
        cfg.EnableSmoothScrolling = EnableSmoothScrolling;

        RelayConfigStore.Save(cfg);
        RelayStartupManager.ApplyStartupSetting(RunOnStartup);
        RelayTrayHost.ApplyTrayVisibility(ShowInSystemTray);

        IsVisible = false;
    }

    private void RunFirewallTroubleshooter()
    {
        _ = RunFirewallTroubleshooterAsync();
    }

    private async Task RunFirewallTroubleshooterAsync()
    {
        try
        {
            NetworkErrorMessage = string.Empty;
            NetworkInfoMessage = "Requesting administrator approval for firewall repair...";

            var cfg = RelayAppServices.Config;
            var result = await RelayWindowsFirewallRuleManager.RefreshRulesAsync(
                cfg.Port,
                cfg.DiscoveryPort,
                cfg.EnableFederation,
                cfg.FederationPort,
                force: true).ConfigureAwait(true);

            switch (result)
            {
                case RelayFirewallRuleRefreshResult.SkippedNonWindows:
                    NetworkInfoMessage = "Network troubleshooter is available on Windows only.";
                    return;

                case RelayFirewallRuleRefreshResult.MissingExecutablePath:
                    NetworkErrorMessage = "Unable to determine executable path for firewall repair.";
                    NetworkInfoMessage = string.Empty;
                    return;

                case RelayFirewallRuleRefreshResult.Canceled:
                    NetworkInfoMessage = "Network troubleshooter canceled.";
                    return;

                case RelayFirewallRuleRefreshResult.Success:
                case RelayFirewallRuleRefreshResult.UpToDate:
                    TryRestartRelayHost();
                    NetworkErrorMessage = string.Empty;
                    NetworkInfoMessage = "Firewall rules refreshed. Relay listener restarted.";
                    return;

                default:
                    NetworkErrorMessage = "Network troubleshooter failed. Try running Zer0Talk Relay as Administrator.";
                    NetworkInfoMessage = string.Empty;
                    return;
            }
        }
        catch
        {
            NetworkErrorMessage = "Network troubleshooter failed. Check permissions and try again.";
            NetworkInfoMessage = string.Empty;
        }
    }

    private static void TryRestartRelayHost()
    {
        try
        {
            var host = RelayAppServices.Host;
            if (!host.IsRunning) return;

            var wasPaused = host.IsPaused;
            host.Stop();
            host.Start();
            if (wasPaused)
            {
                host.Pause();
            }
        }
        catch
        {
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
