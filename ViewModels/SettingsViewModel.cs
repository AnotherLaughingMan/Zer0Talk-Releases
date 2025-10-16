using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using ZTalk.Models;
using Models = ZTalk.Models;
using ZTalk.Services;
using ZTalk.Utilities;

namespace ZTalk.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SettingsService _settings;
    private readonly ThemeService _themeService = AppServices.Theme;
    private string _errorMessage = string.Empty;
    private bool _rememberPassphrase;
    // Performance: detected CPU/GPU capabilities (static for session)
    private int _detectedCcdCount = 1; // 1 = single-CCD assumed by default
    private bool _isAmdX3D;
    private string _cpuModel = string.Empty;
    public string CpuModel { get => _cpuModel; private set { _cpuModel = value; OnPropertyChanged(); } }
    private string _vCacheInfo = string.Empty;
    public string VCacheInfo { get => _vCacheInfo; private set { _vCacheInfo = value; OnPropertyChanged(); } }
#if DEBUG
    // Debug-only CCD simulation state (0=Auto,1=None,2=Single X3D,3=Dual)
    private int _debugCcdModeIndex = 0;
    public int DebugCcdModeIndex
    {
        get => _debugCcdModeIndex;
        set
        {
            if (_debugCcdModeIndex != value)
            {
                _debugCcdModeIndex = value < 0 ? 0 : (value > 3 ? 3 : value);
                try { WriteDebugLog($"CCD debug mode set: {_debugCcdModeIndex}"); } catch { }
                // Recompute dependent UI flags
                OnPropertyChanged(nameof(CcdAffinityEnabled));
                OnPropertyChanged(nameof(CcdAffinityNoticeVisible));
                OnPropertyChanged(nameof(CcdAffinityNotice));
            }
        }
    }
    private static void WriteDebugLog(string line)
    {
        try
        {
            if (!Utilities.LoggingPaths.Enabled) return;
            var path = Utilities.LoggingPaths.Debug;
            System.IO.File.AppendAllText(path, $"{DateTime.Now:O} {line}{Environment.NewLine}");
        }
        catch { }
    }
#endif
    // Performance settings (backed by AppSettings)
    private int _ccdAffinityIndex;
    private int _lastApplicableCcdAffinityIndex;
    private bool _disableGpuAcceleration;
    public bool EnableGpuAcceleration
    {
        get => !_disableGpuAcceleration;
        set
        {
            var newDisable = !value;
            if (_disableGpuAcceleration != newDisable)
            {
                _disableGpuAcceleration = newDisable;
                OnPropertyChanged(nameof(EnableGpuAcceleration));
                OnPropertyChanged(nameof(DisableGpuAcceleration));
                ApplyGpuModeImmediate(_disableGpuAcceleration);
            }
        }
    }
    private int _fpsThrottle;
    private int _refreshRateThrottle;
    private int _backgroundFramerateFps;
    private int _ramUsageLimitMb;
    private int _vramUsageLimitMb;
    private bool _enforceRamLimit;
    private bool _enforceVramLimit;
    // Audio settings
    private double _mainVolume = 1.0;
    private double _notificationVolume = 0.8;
    private double _chatVolume = 0.7;
    private int _baseVramUsageLimitMb;
    // Baseline for dirty tracking
    private int _baseCcdAffinityIndex;
    private bool _baseDisableGpuAcceleration;
    private int _baseFpsThrottle;
    private int _baseRefreshRateThrottle;
    private int _baseBackgroundFramerateFps;
    private int _baseRamUsageLimitMb;
    private bool _baseEnforceRamLimit;
    private bool _baseEnforceVramLimit;
    private double _baseMainVolume;
    private double _baseNotificationVolume;
    private double _baseChatVolume;
    private bool _suppressDirtyCheck = true;
    private bool _hasUnsavedChanges;
    private bool _unsavedWarningVisible;
    private string _unsavedWarningText = "You Have Unsaved Changes";
    private const string UnsavedChangesMessage = "You Have Unsaved Changes";
    private static readonly System.Collections.Generic.HashSet<string> DirtyTrackedProperties = new(System.StringComparer.Ordinal)
    {
        nameof(DisplayName),
        nameof(ShareAvatar),
        nameof(Bio),
        nameof(AvatarPreview),
        nameof(ThemeIndex),
        nameof(RememberPassphrase),
        nameof(UiFontFamily),
        nameof(Language),
        nameof(DefaultPresenceIndex),
        nameof(SuppressNotificationsInDnd),
        nameof(NotificationDurationSeconds),
        nameof(AutoLockEnabled),
        nameof(AutoLockMinutes),
        nameof(LockOnMinimize),
        nameof(LockBlurRadius),
        nameof(BlockScreenCapture),
        nameof(ShowPublicKeys),
        nameof(ShowKeyboardFocus),
        nameof(EnhancedKeyboardNavigation),
        nameof(ShowInSystemTray),
        nameof(MinimizeToTray),
        nameof(RunOnStartup),
        nameof(LockHotkeyDisplay),
        nameof(ClearInputHotkeyDisplay),
        nameof(CcdAffinityIndex),
        nameof(DisableGpuAcceleration),
        nameof(FpsThrottle),
        nameof(RefreshRateThrottle),
        nameof(BackgroundFramerateFps),
        nameof(RamUsageLimitMb),
        nameof(VramUsageLimitMb),
        nameof(EnforceRamLimit),
        nameof(EnforceVramLimit),
        nameof(EnableDebugLogAutoTrim),
        nameof(DebugUiLogMaxLines),
        nameof(DebugLogRetentionDays),
        nameof(DebugLogMaxMegabytes),
        nameof(EnableLogging),
        nameof(MainVolume),
        nameof(NotificationVolume),
        nameof(ChatVolume)
    };

    private bool _saveToastVisible;
    public bool SaveToastVisible { get => _saveToastVisible; set { _saveToastVisible = value; OnPropertyChanged(); } }
    private string _saveToastText = string.Empty;
    public string SaveToastText
    {
        get => _saveToastText;
        set
        {
            _saveToastText = value;
            OnPropertyChanged();
            // If toast text becomes empty/whitespace, ensure toast is hidden to avoid blank toasts.
            try { if (string.IsNullOrWhiteSpace(_saveToastText)) SaveToastVisible = false; } catch { }
        }
    }
    private System.Threading.CancellationTokenSource? _saveToastCts;
    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set
        {
            if (_hasUnsavedChanges == value) return;
            _hasUnsavedChanges = value;
            OnPropertyChanged();
            UpdateUnsavedWarningVisual();
        }
    }
    public bool UnsavedWarningVisible
    {
        get => _unsavedWarningVisible;
        private set
        {
            if (_unsavedWarningVisible != value)
            {
                _unsavedWarningVisible = value;
                OnPropertyChanged();
            }
        }
    }
    public string UnsavedWarningText
    {
        get => _unsavedWarningText;
        private set
        {
            if (!string.Equals(_unsavedWarningText, value, StringComparison.Ordinal))
            {
                _unsavedWarningText = value;
                OnPropertyChanged();
            }
        }
    }
    private bool _disposed;
    private bool _enableDebugLogAutoTrim;
    private int _debugUiLogMaxLines;
    private int _debugLogRetentionDays;
    private int _debugLogMaxMegabytes;
    private bool _baseEnableDebugLogAutoTrim;
    private int _baseDebugUiLogMaxLines;
    private int _baseDebugLogRetentionDays;
    private int _baseDebugLogMaxMegabytes;
    private bool _enableLogging;
    private bool _baseEnableLogging;
    private string _logMaintenanceStatus = "Log maintenance hasn't run yet.";
    private DateTime? _lastLogMaintenanceUtc;

    private async System.Threading.Tasks.Task ShowSaveToastAsync(string message, int durationMs = 2000)
    {
        var previous = System.Threading.Interlocked.Exchange(ref _saveToastCts, null);
        try { previous?.Cancel(); } catch { }
        previous?.Dispose();

        if (_disposed) return;

        var cts = new System.Threading.CancellationTokenSource();
        _saveToastCts = cts;
        try
        {
            var token = cts.Token;
            UnsavedWarningVisible = false;
            // Force retrigger by toggling visibility off first
            SaveToastVisible = false;
            SaveToastText = message;
            SaveToastVisible = true;
            await System.Threading.Tasks.Task.Delay(durationMs, token);
            if (!token.IsCancellationRequested)
                SaveToastVisible = false;
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch { }
        finally
        {
            if (System.Threading.Interlocked.CompareExchange(ref _saveToastCts, null, cts) == cts)
            {
                // cleared
            }
            cts.Dispose();
        }
    }

    // Reset non-persisted UI affordances (toasts/banners) without affecting data.
    public void ResetTransientUi()
    {
        try
        {
            DismissSaveToast();
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { AppServices.LogMaintenance.MaintenanceCompleted -= OnLogMaintenanceCompleted; } catch { }
        DismissSaveToast();
        GC.SuppressFinalize(this);
    }



    private string _baseDisplayName = string.Empty;
    private int _baseThemeIndex;
    private bool _baseShareAvatar;
    private string _baseBio = string.Empty;
    private string _baseAvatarSig = string.Empty;
    private bool _baseRememberPassphrase;
    private string? _baseUiFontFamily; // Theme Engine baseline
    private string _baseLanguage = "English (US)";
    // Baseline: General additions
    private int _baseDefaultPresenceIndex;
    private bool _baseSuppressNotificationsInDnd;
    private double _baseNotificationDurationSeconds;
    private bool _baseAutoLockEnabled;
    private int _baseAutoLockMinutes;
    private bool _baseLockOnMinimize;
    private int _baseLockBlurRadius;
    private bool _baseBlockScreenCapture;
    private bool _baseShowPublicKeys;
    private Avalonia.Input.Key _baseLockHotkeyKey;
    private Avalonia.Input.KeyModifiers _baseLockHotkeyModifiers;
    // Accessibility properties removed - these are OS-level settings that the app cannot control
    // High Contrast, Reduce Motion, Cursor settings must be configured through Windows Settings
    private bool _baseShowKeyboardFocus;
    private bool _baseEnhancedKeyboardNavigation;
    private bool _baseShowInSystemTray;
    private bool _baseMinimizeToTray;
    private bool _baseRunOnStartup;
    private bool _suppressThemeBinding = true;

    public SettingsViewModel()
    {
        _settings = AppServices.Settings;
        _suppressDirtyCheck = true;
        _suppressThemeBinding = true;

        try { DetectCpuGpuCapabilities(); }
        catch { }

        try
        {
            var identity = AppServices.Identity;
            if (identity != null)
            {
                UID = identity.UID ?? string.Empty;
                Username = identity.Username ?? string.Empty;
                try { SelfPublicKeyHex = identity.PublicKey is { Length: > 0 } key ? Convert.ToHexString(key) : string.Empty; }
                catch { SelfPublicKeyHex = string.Empty; }
            }
        }
        catch { }

    Models.AccountData? account = null;
        try { account = AppServices.Accounts.LoadAccount(AppServices.Passphrase); }
        catch { }

        if (account != null)
        {
            if (!string.IsNullOrWhiteSpace(account.Username))
                Username = account.Username;
            if (!string.IsNullOrWhiteSpace(account.UID))
                UID = account.UID;

            if (!string.IsNullOrWhiteSpace(account.DisplayName))
                DisplayName = account.DisplayName;
            else if (!string.IsNullOrWhiteSpace(Username))
                DisplayName = Username;
            else
                DisplayName = UID;

            ShareAvatar = account.ShareAvatar;
            Bio = account.Bio ?? string.Empty;
            _avatarBytes = account.Avatar;
            RefreshAvatarPreview();
            DisplayNameChangeCount = account.DisplayNameChangeCount;
            try
            {
                if (account.DisplayNameHistory != null)
                {
                    DisplayNameHistory = new System.Collections.ObjectModel.ObservableCollection<DisplayNameRecord>(
                        account.DisplayNameHistory.OrderByDescending(h => h.ChangedAtUtc));
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(SelfPublicKeyHex))
            {
                try { SelfPublicKeyHex = account.PublicKey is { Length: > 0 } pk ? Convert.ToHexString(pk) : string.Empty; }
                catch { SelfPublicKeyHex = string.Empty; }
            }
        }

        AppSettings? settings = null;
        try { settings = _settings.Settings; }
        catch { }

        if (settings != null)
        {
            RememberPassphrase = settings.RememberPassphrase;

            UiFontFamily = string.IsNullOrWhiteSpace(settings.UiFontFamily) ? null : settings.UiFontFamily;
            Language = string.IsNullOrWhiteSpace(settings.Language) ? "English (US)" : settings.Language;
            LockBlurRadius = ClampRange(settings.LockBlurRadius, 0, 10);
            ShowKeyboardFocus = settings.ShowKeyboardFocus;
            EnhancedKeyboardNavigation = settings.EnhancedKeyboardNavigation;
            DefaultPresenceIndex = PresenceToIndex(settings.Status);
            SuppressNotificationsInDnd = settings.SuppressNotificationsInDnd;
            NotificationDurationSeconds = Math.Clamp(settings.NotificationDurationSeconds, 0.5, 30.0);
            AutoLockEnabled = settings.AutoLockEnabled;
            AutoLockMinutes = Math.Max(0, settings.AutoLockMinutes);
            LockOnMinimize = settings.LockOnMinimize;
            BlockScreenCapture = settings.BlockScreenCapture;
            ShowPublicKeys = settings.ShowPublicKeys;
            ShowInSystemTray = settings.ShowInSystemTray;
            MinimizeToTray = settings.MinimizeToTray;
            RunOnStartup = settings.RunOnStartup;

            // Verify Windows startup registry matches saved setting
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var actuallyEnabled = WindowsStartupManager.IsRunOnStartupEnabled();
                    if (actuallyEnabled != RunOnStartup)
                    {
                        // Settings and registry mismatch - sync registry to match settings
                        WindowsStartupManager.ApplyStartupSetting(RunOnStartup);
                    }
                }
            }
            catch { }

            // Load hotkey settings (with validation to ensure valid enum values)
            try
            {
                _lockHotkeyKey = (Avalonia.Input.Key)settings.LockHotkeyKey;
                _lockHotkeyModifiers = (Avalonia.Input.KeyModifiers)settings.LockHotkeyModifiers;
            }
            catch
            {
                // Fallback to defaults if invalid
                _lockHotkeyKey = Avalonia.Input.Key.L;
                _lockHotkeyModifiers = Avalonia.Input.KeyModifiers.Control;
            }
            
            try
            {
                _clearInputHotkeyKey = (Avalonia.Input.Key)settings.ClearInputHotkeyKey;
                _clearInputHotkeyModifiers = (Avalonia.Input.KeyModifiers)settings.ClearInputHotkeyModifiers;
            }
            catch
            {
                // Fallback to defaults if invalid
                _clearInputHotkeyKey = Avalonia.Input.Key.Q;
                _clearInputHotkeyModifiers = Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift;
            }

            CcdAffinityIndex = ClampRange(settings.CcdAffinityIndex, 0, 3);
            DisableGpuAcceleration = settings.DisableGpuAcceleration;
            FpsThrottle = Math.Max(0, settings.FpsThrottle);
            RefreshRateThrottle = Math.Max(0, settings.RefreshRateThrottle);
            BackgroundFramerateFps = Math.Max(1, settings.BackgroundFramerateFps);
            RamUsageLimitMb = Math.Max(0, settings.RamUsageLimitMb);
            VramUsageLimitMb = Math.Max(0, settings.VramUsageLimitMb);
            EnforceRamLimit = settings.EnforceRamLimit;
            EnforceVramLimit = settings.EnforceVramLimit;
            EnableDebugLogAutoTrim = settings.EnableDebugLogAutoTrim;
            DebugUiLogMaxLines = ClampRange(settings.DebugUiLogMaxLines <= 0 ? 1000 : settings.DebugUiLogMaxLines, 100, 20000);
            DebugLogRetentionDays = settings.DebugLogRetentionDays < 0 ? 0 : (settings.DebugLogRetentionDays > 30 ? 30 : settings.DebugLogRetentionDays);
            DebugLogMaxMegabytes = ClampRange(settings.DebugLogMaxMegabytes <= 0 ? 16 : settings.DebugLogMaxMegabytes, 1, 512);
            EnableLogging = settings.EnableLogging;

            var themeIndex = settings.Theme switch
            {
                ThemeOption.Light => 1,
                ThemeOption.Sandy => 2,
                ThemeOption.Butter => 3,
                _ => 0
            };
            _themeIndex = themeIndex;
            _baseThemeIndex = themeIndex;
        }
        else
        {
            _themeIndex = 0;
            _baseThemeIndex = 0;
            EnableDebugLogAutoTrim = true;
            DebugUiLogMaxLines = 1000;
            DebugLogRetentionDays = 1;
            DebugLogMaxMegabytes = 16;
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            try
            {
                var fallback = settings?.DisplayName;
                if (!string.IsNullOrWhiteSpace(fallback))
                    DisplayName = fallback;
                else if (!string.IsNullOrWhiteSpace(Username))
                    DisplayName = Username;
                else
                    DisplayName = UID;
            }
            catch { }
        }

        SaveCommand = new RelayCommand(async _ => await SaveAsync(showToast: true, close: false), _ => true);
        CloseApplyCommand = new RelayCommand(async _ => await SaveAsync(showToast: false, close: true), _ => true);
        CancelCommand = new RelayCommand(_ => { DiscardChanges(); CloseRequested?.Invoke(this, EventArgs.Empty); });
    PurgeStoredPassphraseCommand = new RelayCommand(_ => PurgeStoredPassphrase(), _ => true);
    PurgeAllMessagesCommand = new RelayCommand(async _ => await PurgeAllMessagesAsync(), _ => CanPurgeAllMessages);
        LogoutCommand = new RelayCommand(_ => Logout(), _ => true);
        CopyUidCommand = new RelayCommand(async _ => await CopyUidAsync(), _ => !string.IsNullOrWhiteSpace(UID));
        ChooseAvatarCommand = new RelayCommand(async _ => await ChooseAvatarAsync(), _ => true);
        ClearAvatarCommand = new RelayCommand(_ => { _avatarBytes = null; RefreshAvatarPreview(); }, _ => _avatarBytes != null && _avatarBytes.Length > 0);
        DeleteAccountCommand = new RelayCommand(_ => DeleteAccount(), _ => !string.IsNullOrWhiteSpace(DeleteConfirmText) && string.Equals(DeleteConfirmText.Trim(), GeneratedDeleteCode, StringComparison.Ordinal));
        ResetLayoutCommand = new RelayCommand(_ => ResetLayout(), _ => true);
        RunLogMaintenanceCommand = new RelayCommand(async _ => await RunLogMaintenanceAsync(), _ => true);
        CopyPublicKeyCommand = new RelayCommand(async _ => await CopyPublicKeyAsync(), _ => !string.IsNullOrWhiteSpace(SelfPublicKeyHex));

        try
        {
            AppServices.LogMaintenance.MaintenanceCompleted += OnLogMaintenanceCompleted;
            UpdateLogMaintenanceStatus(AppServices.LogMaintenance.LastSummary, AppServices.LogMaintenance.LastRunUtc);
        }
        catch { }

        try { _themeService.ApplyThemeEngine(UiFontFamily, 1.0); }
        catch { }
        try { StartOrUpdateResourceMonitors(); }
        catch { }

        try
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _suppressThemeBinding = false;
                OnPropertyChanged(nameof(ThemeIndex));
            });
        }
        catch
        {
            _suppressThemeBinding = false;
            OnPropertyChanged(nameof(ThemeIndex));
        }

        CaptureBaseline();
        _suppressDirtyCheck = false;
        UpdateUnsavedChangesState();
    }

    private void DiscardChanges()
    {
        var prevSuppress = _suppressDirtyCheck;
        _suppressDirtyCheck = true;
        try
        {
            var s = _settings.Settings;
            if (!string.IsNullOrWhiteSpace(s.DisplayName))
                DisplayName = s.DisplayName;
            else
            {
                try
                {
                    var acct = AppServices.Accounts.LoadAccount(AppServices.Passphrase);
                    if (!string.IsNullOrWhiteSpace(acct.DisplayName)) DisplayName = acct.DisplayName;
                }
                catch { }
            }

            ThemeIndex = _baseThemeIndex;
            RememberPassphrase = _baseRememberPassphrase;
            UiFontFamily = string.IsNullOrWhiteSpace(s.UiFontFamily) ? null : s.UiFontFamily;
            LockBlurRadius = ClampRange(s.LockBlurRadius, 0, 10);
            DefaultPresenceIndex = PresenceToIndex(s.Status);
            SuppressNotificationsInDnd = s.SuppressNotificationsInDnd;
            NotificationDurationSeconds = Math.Clamp(s.NotificationDurationSeconds, 0.5, 30.0);
            AutoLockEnabled = s.AutoLockEnabled;
            AutoLockMinutes = Math.Max(0, s.AutoLockMinutes);
            LockOnMinimize = s.LockOnMinimize;
            ShowPublicKeys = s.ShowPublicKeys;
            BlockScreenCapture = s.BlockScreenCapture;
            ShowInSystemTray = s.ShowInSystemTray;
            MinimizeToTray = s.MinimizeToTray;
            RunOnStartup = s.RunOnStartup;
            EnableDebugLogAutoTrim = s.EnableDebugLogAutoTrim;
            DebugUiLogMaxLines = ClampRange(s.DebugUiLogMaxLines <= 0 ? 1000 : s.DebugUiLogMaxLines, 100, 20000);
            DebugLogRetentionDays = s.DebugLogRetentionDays < 0 ? 0 : (s.DebugLogRetentionDays > 30 ? 30 : s.DebugLogRetentionDays);
            DebugLogMaxMegabytes = ClampRange(s.DebugLogMaxMegabytes <= 0 ? 16 : s.DebugLogMaxMegabytes, 1, 512);
            EnableLogging = s.EnableLogging;

            CcdAffinityIndex = ClampRange(s.CcdAffinityIndex, 0, 3);
            DisableGpuAcceleration = s.DisableGpuAcceleration;
            FpsThrottle = Math.Max(0, s.FpsThrottle);
            RefreshRateThrottle = Math.Max(0, s.RefreshRateThrottle);
            BackgroundFramerateFps = Math.Max(1, s.BackgroundFramerateFps);
            RamUsageLimitMb = Math.Max(0, s.RamUsageLimitMb);
            VramUsageLimitMb = Math.Max(0, s.VramUsageLimitMb);
            EnforceRamLimit = s.EnforceRamLimit;
            EnforceVramLimit = s.EnforceVramLimit;
            MainVolume = Math.Clamp(s.MainVolume, 0.0, 1.0);
            NotificationVolume = Math.Clamp(s.NotificationVolume, 0.0, 1.0);
            ChatVolume = Math.Clamp(s.ChatVolume, 0.0, 1.0);

            try
            {
                var acct = AppServices.Accounts.LoadAccount(AppServices.Passphrase);
                ShareAvatar = acct.ShareAvatar;
                Bio = acct.Bio ?? string.Empty;
                _avatarBytes = acct.Avatar;
                RefreshAvatarPreview();
            }
            catch { }

            CaptureBaseline();
            try { LogSettingsEvent($"Discarded changes (ThemeIndex back to {_baseThemeIndex})"); }
            catch { }
        }
        catch { }
        finally
        {
            _suppressDirtyCheck = prevSuppress;
            if (!_suppressDirtyCheck)
            {
                UpdateUnsavedChangesState();
            }
        }
    }

    // About panel properties
    public string AppName => "ZTalk";
    public string AppVersion => ZTalk.AppInfo.Version;
    public string AvaloniaVersion
    {
        get
        {
            try
            {
                var asm = typeof(Avalonia.Application).Assembly;
                var ver = asm.GetName().Version?.ToString() ?? string.Empty;
                return $"Avalonia {ver}";
            }
            catch { return "Avalonia (unknown)"; }
        }
    }
    public string DotNetVersion
    {
        get
        {
            try
            {
                return $".NET {System.Environment.Version}";
            }
            catch { return ".NET (unknown)"; }
        }
    }
    public string Author => "AnotherLaughingMan";

    // Performance UI properties and helpers
    public int CcdAffinityIndex
    {
        get => _ccdAffinityIndex;
        set
        {
            var v = value; if (v < -1) v = -1; if (v > 3) v = 3;
            if (_ccdAffinityIndex != v)
            {
                _ccdAffinityIndex = v;
                OnPropertyChanged();
                if (_ccdAffinityIndex >= 0)
                {
                    _lastApplicableCcdAffinityIndex = _ccdAffinityIndex;
                    try { ApplyCcdAffinityImmediate(_ccdAffinityIndex); } catch { }
                }
                OnPropertyChanged(nameof(BothCcdWarningVisible));
                OnPropertyChanged(nameof(CcdAffinityTooltip));
            }
        }
    }
    public bool BothCcdWarningVisible => _ccdAffinityIndex == 3 && CcdAffinityEnabled;
    public bool CcdAffinityEnabled
    {
        get
        {
#if DEBUG
            return GetCcdCountForUi() >= 2;
#else
            return _detectedCcdCount >= 2;
#endif
        }
    }
    public bool CcdAffinityNoticeVisible => !CcdAffinityEnabled;
    public string CcdAffinityNotice
    {
        get
        {
            if (CcdAffinityEnabled) return string.Empty;
            bool x3d = _isAmdX3D;
#if DEBUG
            // In Debug, reflect simulated single-CCD X3D state
            if (_debugCcdModeIndex == 2) x3d = true; else if (_debugCcdModeIndex == 1 || _debugCcdModeIndex == 3) x3d = false;
#endif
            if (x3d) return "This CPU appears to be a single-CCD X3D; CCD selection isn’t applicable.";
            return "CCD affinity requires a multi-CCD CPU.";
        }
    }
    public string? CcdAffinityTooltip => CcdAffinityEnabled ? null : "CCD Affinity not applicable for this configuration";
#if DEBUG
    private int GetCcdCountForUi()
        => _debugCcdModeIndex switch
        {
            1 => 0, // No CCDs
            2 => 1, // Single X3D
            3 => 2, // Dual
            _ => _detectedCcdCount
        };
#endif
    public bool DisableGpuAcceleration
    {
        get => _disableGpuAcceleration;
        set
        {
            if (_disableGpuAcceleration != value)
            {
                _disableGpuAcceleration = value;
                OnPropertyChanged(nameof(EnableGpuAcceleration));
                ApplyGpuModeImmediate(_disableGpuAcceleration);
                OnPropertyChanged();
                try { WritePerformanceLog($"Change DisableGpuAcceleration={_disableGpuAcceleration}"); } catch { }
            }
        }
    }
    public int FpsThrottle
    {
        get => _fpsThrottle;
        set
        {
            var v = Math.Max(0, value);
            if (_fpsThrottle != v)
            {
                _fpsThrottle = v;
                ApplyFpsThrottleImmediate(_fpsThrottle);
                OnPropertyChanged();
                try { WritePerformanceLog($"Change FpsThrottle={_fpsThrottle}"); } catch { }
            }
        }
    }
    public int RefreshRateThrottle
    {
        get => _refreshRateThrottle;
        set
        {
            var v = Math.Max(0, value);
            if (_refreshRateThrottle != v)
            {
                _refreshRateThrottle = v;
                ApplyRefreshRateThrottleImmediate(_refreshRateThrottle);
                OnPropertyChanged();
                try { WritePerformanceLog($"Change RefreshRateThrottle={_refreshRateThrottle}"); } catch { }
            }
        }
    }
    public int BackgroundFramerateFps
    {
        get => _backgroundFramerateFps;
        set
        {
            var v = Math.Max(1, value);
            if (_backgroundFramerateFps != v)
            {
                _backgroundFramerateFps = v;
                OnPropertyChanged();
                try { ZTalk.Services.FocusFramerateService.ApplyCurrentPolicy(); } catch { }
                try { WritePerformanceLog($"Change BackgroundFramerateFps={_backgroundFramerateFps}"); } catch { }
            }
        }
    }
    public int RamUsageLimitMb
    {
        get => _ramUsageLimitMb;
        set
        {
            var v = Math.Max(0, value);
            if (_ramUsageLimitMb != v)
            {
                _ramUsageLimitMb = v;
                OnPropertyChanged();
                try { WritePerformanceLog($"Change RamUsageLimitMb={_ramUsageLimitMb}"); } catch { }
            }
        }
    }
    public int VramUsageLimitMb
    {
        get => _vramUsageLimitMb;
        set
        {
            var v = Math.Max(0, value);
            if (_vramUsageLimitMb != v)
            {
                _vramUsageLimitMb = v;
                OnPropertyChanged();
                try { StartOrUpdateResourceMonitors(); } catch { }
                try { WritePerformanceLog($"Change VramUsageLimitMb={_vramUsageLimitMb}"); } catch { }
            }
        }
    }
    public bool EnforceRamLimit
    {
        get => _enforceRamLimit;
        set
        {
            if (_enforceRamLimit != value)
            {
                _enforceRamLimit = value;
                OnPropertyChanged();
                try { StartOrUpdateResourceMonitors(); } catch { }
                try { WritePerformanceLog($"Change EnforceRamLimit={_enforceRamLimit}"); } catch { }
            }
        }
    }
    public bool EnforceVramLimit
    {
        get => _enforceVramLimit;
        set
        {
            if (_enforceVramLimit != value)
            {
                _enforceVramLimit = value;
                OnPropertyChanged();
                try { StartOrUpdateResourceMonitors(); } catch { }
                try { WritePerformanceLog($"Change EnforceVramLimit={_enforceVramLimit}"); } catch { }
            }
        }
    }

    // Audio volume settings (0.0 to 1.0)
    public double MainVolume
    {
        get => _mainVolume;
        set
        {
            var v = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_mainVolume - v) > 0.001)
            {
                _mainVolume = v;
                OnPropertyChanged();
                try { AppServices.AudioNotifications.MainVolume = (float)v; } catch { }
            }
        }
    }

    public double NotificationVolume
    {
        get => _notificationVolume;
        set
        {
            var v = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_notificationVolume - v) > 0.001)
            {
                _notificationVolume = v;
                OnPropertyChanged();
                try { AppServices.AudioNotifications.NotificationVolume = (float)v; } catch { }
            }
        }
    }

    public double ChatVolume
    {
        get => _chatVolume;
        set
        {
            var v = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_chatVolume - v) > 0.001)
            {
                _chatVolume = v;
                OnPropertyChanged();
                try { AppServices.AudioNotifications.ChatVolume = (float)v; } catch { }
            }
        }
    }

    private static int ClampRange(int value, int min, int max) => value < min ? min : (value > max ? max : value);
    private void DetectCpuGpuCapabilities()
    {
        try
        {
            CpuModel = GetCpuModelString();
            var ident = string.IsNullOrWhiteSpace(CpuModel)
                ? (Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? string.Empty)
                : CpuModel;
            _isAmdX3D = ident.Contains("AMD", StringComparison.OrdinalIgnoreCase) && ident.Contains("X3D", StringComparison.OrdinalIgnoreCase);
        }
        catch { _isAmdX3D = false; }
        try
        {
            // Heuristic: treat >=16 logical cores as likely multi-CCD (2 CCD). Otherwise assume single-CCD.
            _detectedCcdCount = Environment.ProcessorCount >= 16 ? 2 : 1;
        }
        catch { _detectedCcdCount = 1; }
        // Attempt V-Cache detection (Windows: via L3 cache size per mask)
        try
        {
            var res = DetectVCache();
            if (res.HasVCache)
            {
                _isAmdX3D = true; // refine based on cache evidence
                if (res.CcdIndex >= 0)
                    VCacheInfo = $"3D V-Cache detected on CCD {res.CcdIndex}";
                else
                    VCacheInfo = "3D V-Cache detected";
            }
            else
            {
                VCacheInfo = "No 3D V-Cache present on this CPU";
            }
            WritePerformanceLogSafe($"CPU model: {CpuModel}");
            WritePerformanceLogSafe($"V-Cache: {VCacheInfo}");
        }
        catch { }
        // Notify dependant properties once
        OnPropertyChanged(nameof(CcdAffinityEnabled));
        OnPropertyChanged(nameof(CcdAffinityNoticeVisible));
        OnPropertyChanged(nameof(CcdAffinityNotice));
        OnPropertyChanged(nameof(CcdAffinityTooltip));
        try { UpdateFontSmoothingNotice(); } catch { }
    }

    // Windows ClearType / font smoothing detection and UI notice
    private bool _osFontSmoothingEnabled;
    public bool OsFontSmoothingEnabled { get => _osFontSmoothingEnabled; private set { _osFontSmoothingEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(GpuTextNotice)); } }
    public string GpuTextNotice
        => OsFontSmoothingEnabled ? "OS-level font smoothing is enabled; GPU acceleration won’t change text rendering." : string.Empty;
    private void UpdateFontSmoothingNotice()
    {
        try
        {
            bool enabled = false;
            if (OperatingSystem.IsWindows())
            {
                try { using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\\Desktop"); enabled = string.Equals(k?.GetValue("FontSmoothing")?.ToString(), "2", StringComparison.OrdinalIgnoreCase); } catch { }
                if (!enabled)
                {
                    try { using var k2 = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\\Microsoft\\Avalon.Graphics"); enabled = string.Equals(k2?.GetValue("ClearTypeLevel")?.ToString(), "100", StringComparison.OrdinalIgnoreCase); } catch { }
                }
            }
            OsFontSmoothingEnabled = enabled;
            if (enabled) WritePerformanceLogSafe("[GPU] OS font smoothing detected; skipping GPU text tweaks");
        }
        catch { OsFontSmoothingEnabled = false; }
    }

    private static string GetCpuModelString()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Registry read is cheap and available without extra packages
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                var name = key?.GetValue("ProcessorNameString") as string;
                if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
            }
            else if (OperatingSystem.IsLinux())
            {
                try
                {
                    var txt = System.IO.File.ReadAllText("/proc/cpuinfo");
                    var line = txt.Split('\n').FirstOrDefault(l => l.Contains("model name", StringComparison.OrdinalIgnoreCase));
                    if (line != null)
                    {
                        var parts = line.Split(':');
                        if (parts.Length > 1) return parts[1].Trim();
                    }
                }
                catch { }
            }
            else if (OperatingSystem.IsMacOS())
            {
                try
                {
                    // Fallback: sysctl (best-effort)
                    return RunProcessAndCapture("/usr/sbin/sysctl", "-n machdep.cpu.brand_string");
                }
                catch { }
            }
        }
        catch { }
        return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? string.Empty;
    }

    private static string RunProcessAndCapture(string fileName, string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(fileName, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return string.Empty;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(1000);
            return (output ?? string.Empty).Trim();
        }
        catch { return string.Empty; }
    }

    private readonly struct VCacheDetectionResult
    {
        public VCacheDetectionResult(bool has, int idx) { HasVCache = has; CcdIndex = idx; }
        public bool HasVCache { get; }
        public int CcdIndex { get; } // -1 unknown
    }

    private VCacheDetectionResult DetectVCache()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                // Heuristic: rely on model string for non-Windows
                var model = CpuModel ?? string.Empty;
                if (model.Contains("X3D", StringComparison.OrdinalIgnoreCase))
                {
                    // Single-CCD X3D (e.g., 7800X3D) → CCD 0; dual-CCD varies → unknown
                    var isSingle = Environment.ProcessorCount < 16;
                    return new VCacheDetectionResult(true, isSingle ? 0 : -1);
                }
                return new VCacheDetectionResult(false, -1);
            }

            // Windows: enumerate L3 caches and pick the largest
            var infos = GetLogicalProcessorInfos();
            if (infos == null || infos.Length == 0)
                return new VCacheDetectionResult(_isAmdX3D, -1);
            uint largestL3 = 0;
            ulong largestMask = 0;
            foreach (var info in infos)
            {
                if (info.Relationship == LOGICAL_PROCESSOR_RELATIONSHIP.RelationCache && info.ProcessorInformation.Cache.Level == 3)
                {
                    var size = info.ProcessorInformation.Cache.Size;
                    if (size > largestL3)
                    {
                        largestL3 = size;
                        largestMask = (ulong)info.ProcessorMask;
                    }
                }
            }
            if (largestL3 >= 48 * 1024 * 1024) // >= 48MB implies 3D V-Cache CCD (96MB typical)
            {
                int logical = Math.Max(1, Environment.ProcessorCount);
                int half = Math.Max(1, logical / 2);
                int lower = CountBits(largestMask & BuildMask(0, half));
                int upper = CountBits(largestMask & BuildMask(half, Math.Min(half, logical - half)));
                int idx = lower >= upper ? 0 : 1;
                return new VCacheDetectionResult(true, idx);
            }
            return new VCacheDetectionResult(false, -1);
        }
        catch { return new VCacheDetectionResult(_isAmdX3D, -1); }
    }

    private static int CountBits(ulong mask)
    {
        int c = 0; while (mask != 0) { mask &= (mask - 1); c++; }
        return c;
    }

    // PInvoke for GetLogicalProcessorInformation (legacy but sufficient for L3 cache sizes/masks up to 64 procs)
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformation(IntPtr Buffer, ref uint ReturnLength);

    private enum LOGICAL_PROCESSOR_RELATIONSHIP : uint
    {
        RelationProcessorCore = 0,
        RelationNumaNode = 1,
        RelationCache = 2,
        RelationProcessorPackage = 3,
        RelationGroup = 4,
        RelationAll = 0xFFFF
    }

    private enum PROCESSOR_CACHE_TYPE : uint { Unified = 0, Instruction = 1, Data = 2, Trace = 3 }

    [StructLayout(LayoutKind.Sequential)]
    private struct CACHE_DESCRIPTOR
    {
        public byte Level;
        public byte Associativity;
        public ushort LineSize;
        public uint Size;
        public PROCESSOR_CACHE_TYPE Type;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PROCESSOR_INFO_UNION
    {
        [FieldOffset(0)] public byte ProcessorCoreFlags; // not used
        [FieldOffset(0)] public uint NumaNodeNumber; // not used
        [FieldOffset(0)] public CACHE_DESCRIPTOR Cache;
        [FieldOffset(0)] public ulong Reserved1;
        [FieldOffset(8)] public ulong Reserved2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION
    {
        public UIntPtr ProcessorMask;
        public LOGICAL_PROCESSOR_RELATIONSHIP Relationship;
        public PROCESSOR_INFO_UNION ProcessorInformation;
    }

    private static SYSTEM_LOGICAL_PROCESSOR_INFORMATION[] GetLogicalProcessorInfos()
    {
        try
        {
            uint len = 0;
            GetLogicalProcessorInformation(IntPtr.Zero, ref len);
            if (len == 0) return Array.Empty<SYSTEM_LOGICAL_PROCESSOR_INFORMATION>();
            var ptr = Marshal.AllocHGlobal((int)len);
            try
            {
                if (!GetLogicalProcessorInformation(ptr, ref len)) return Array.Empty<SYSTEM_LOGICAL_PROCESSOR_INFORMATION>();
                int size = Marshal.SizeOf<SYSTEM_LOGICAL_PROCESSOR_INFORMATION>();
                int count = (int)(len / size);
                var arr = new SYSTEM_LOGICAL_PROCESSOR_INFORMATION[count];
                for (int i = 0; i < count; i++)
                {
                    arr[i] = Marshal.PtrToStructure<SYSTEM_LOGICAL_PROCESSOR_INFORMATION>(ptr + i * size);
                }
                return arr;
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        catch { return Array.Empty<SYSTEM_LOGICAL_PROCESSOR_INFORMATION>(); }
    }

    // Apply CPU affinity for the current process based on CCD selection.
    // index: 0 = Auto (best CCD: prefer V-Cache, else CCD 0), 1 = CCD 0, 2 = CCD 1, 3 = Both
    private void ApplyCcdAffinityImmediate(int index)
    {
        try
        {
            // Determine CCD topology
            int logicalCores = Math.Max(1, Environment.ProcessorCount);
            int ccds = 1;
#if DEBUG
            bool simulate = false;
#endif
#if DEBUG
            // Simulate CCD config when debug toggle is set
            if (DebugCcdModeIndex == 1) { ccds = 0; simulate = true; }
            else if (DebugCcdModeIndex == 2) { ccds = 1; simulate = true; }
            else if (DebugCcdModeIndex == 3) { ccds = 2; simulate = true; }
            else { ccds = _detectedCcdCount; }
#else
            ccds = _detectedCcdCount;
#endif

            // Build masks per CCD using a simple split model: lower half = CCD 0, upper half = CCD 1
            ulong allMask = BuildMask(0, logicalCores);
            if (index == 0)
            {
                // Auto: prefer V-Cache CCD if present, otherwise prefer CCD 0 on multi-CCD CPUs
                int preferredCcd = -1;
                try
                {
                    var res = DetectVCache();
                    if (res.HasVCache && ccds >= 2 && res.CcdIndex >= 0) preferredCcd = res.CcdIndex;
                }
                catch { }
                if (ccds >= 2 && preferredCcd >= 0)
                {
                    int halfA = logicalCores / 2; if (halfA <= 0) halfA = logicalCores;
                    ulong maskAuto = preferredCcd == 0 ? BuildMask(0, halfA) : BuildMask(halfA, Math.Min(halfA, logicalCores - halfA));
                    if (maskAuto != 0)
                    {
                        SetProcessAffinity(maskAuto);
                        WritePerformanceLogSafe($"[Auto] CCD {preferredCcd} selected due to 3D V-Cache presence → mask=0x{maskAuto:X}");
                        return;
                    }
                }
                if (ccds >= 2)
                {
                    int halfA = logicalCores / 2; if (halfA <= 0) halfA = logicalCores;
                    ulong maskCcd0 = BuildMask(0, Math.Min(halfA, logicalCores));
                    if (maskCcd0 != 0)
                    {
                        SetProcessAffinity(maskCcd0);
                        WritePerformanceLogSafe($"[Auto] Defaulting to CCD 0 (no V-Cache detected) → mask=0x{maskCcd0:X}");
                        return;
                    }
                }
                // Single CCD or failed to compute a half: allow all cores
                SetProcessAffinity(allMask);
                WritePerformanceLogSafe($"[Auto] Single-CCD or fallback → mask=0x{allMask:X}");
                return;
            }

            if (ccds < 2)
            {
                // No CCDs or single CCD: fallback to Auto
                SetProcessAffinity(allMask);
#if DEBUG
                if (simulate)
                    WritePerformanceLogSafe($"CCD Affinity: simulated ccds={ccds}; selection={index} not applicable → fallback mask=0x{allMask:X}");
                else
#endif
                    WritePerformanceLogSafe($"CCD Affinity: ccds={ccds}; selection={index} not applicable → fallback mask=0x{allMask:X}");
                return;
            }

            int half = logicalCores / 2;
            if (half <= 0)
            {
                SetProcessAffinity(allMask);
                WritePerformanceLogSafe($"CCD Affinity: invalid half ({half}) → fallback mask=0x{allMask:X}");
                return;
            }
            // Special case: Both CCDs
            if (index == 3)
            {
                SetProcessAffinity(allMask);
                WritePerformanceLogSafe($"[Affinity] Both CCDs selected → mask=0x{allMask:X}");
                return;
            }
            // CCD 0 = [0, half-1]; CCD 1 = [half, 2*half-1]
            ulong mask = index == 1 ? BuildMask(0, half) : BuildMask(half, Math.Min(half, logicalCores - half));
            if (mask == 0)
            {
                SetProcessAffinity(allMask);
                WritePerformanceLogSafe("CCD Affinity: computed empty mask → fallback to all cores");
                return;
            }
            SetProcessAffinity(mask);
#if DEBUG
            if (simulate)
                WritePerformanceLogSafe($"CCD Affinity: simulated ccds={ccds}, selection={(index == 1 ? "CCD0" : "CCD1")} → mask=0x{mask:X}");
            else
#endif
                WritePerformanceLogSafe($"CCD Affinity: selection={(index == 1 ? "CCD0" : "CCD1")} → mask=0x{mask:X}");
        }
        catch (Exception ex)
        {
            try { WritePerformanceLogSafe($"CCD Affinity: error applying affinity → {ex.Message}"); } catch { }
        }
    }

    private static ulong BuildMask(int start, int count)
    {
        try
        {
            if (start < 0) start = 0; if (count <= 0) return 0UL;
            ulong m = 0UL;
            for (int i = 0; i < count; i++)
            {
                int bit = start + i;
                if (bit >= 64) break; // limit to 64-bit mask for ProcessorAffinity
                m |= (1UL << bit);
            }
            return m;
        }
        catch { return 0UL; }
    }

    private static void SetProcessAffinity(ulong mask)
    {
        try
        {
            // Only apply on Windows; on other OS, quietly skip
            if (!OperatingSystem.IsWindows()) return;
            using var p = System.Diagnostics.Process.GetCurrentProcess();
            var m = (nint)unchecked((long)mask);
            if (m == 0)
            {
                // 0 is not a valid mask; don't change
                return;
            }
            p.ProcessorAffinity = m;
        }
        catch { }
    }
    private string _displayName = string.Empty;
    public string DisplayName { get => _displayName; set { _displayName = value; OnPropertyChanged(); } }
    private string _username = string.Empty;
    public string Username { get => _username; private set { _username = value; OnPropertyChanged(); } }
    private string _uid = string.Empty;
    public string UID { get => _uid; private set { _uid = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayUID)); (CopyUidCommand as RelayCommand)?.RaiseCanExecuteChanged(); } }
    public string DisplayUID => TrimUidPrefix(UID);
    private static string TrimUidPrefix(string uid)
    {
        if (string.IsNullOrWhiteSpace(uid)) return string.Empty;
        return uid.StartsWith("usr-", StringComparison.Ordinal) && uid.Length > 4 ? uid.Substring(4) : uid;
    }
    private bool _shareAvatar;
    public bool ShareAvatar { get => _shareAvatar; set { _shareAvatar = value; OnPropertyChanged(); } }
    private string _bio = string.Empty;
    public string Bio { get => _bio; set { _bio = value?.Length > 280 ? value.Substring(0, 280) : value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(BioCounter)); } }
    public string BioCounter => $"{(Bio?.Length ?? 0)}/280";
    private System.Collections.ObjectModel.ObservableCollection<DisplayNameRecord> _displayNameHistory = new();
    public System.Collections.ObjectModel.ObservableCollection<DisplayNameRecord> DisplayNameHistory { get => _displayNameHistory; set { _displayNameHistory = value; OnPropertyChanged(); } }
    private DisplayNameRecord? _selectedPreviousDisplayName;
    public DisplayNameRecord? SelectedPreviousDisplayName
    {
        get => _selectedPreviousDisplayName;
        set
        {
            if (_selectedPreviousDisplayName != value)
            {
                _selectedPreviousDisplayName = value;
                OnPropertyChanged();
                // When the user picks a previous name, stage it into DisplayName immediately (manual save persists later)
                try
                {
                    var name = value?.Name;
                    if (!string.IsNullOrWhiteSpace(name) && !string.Equals(DisplayName, name, StringComparison.Ordinal))
                    {
                        DisplayName = name!;
                    }
                }
                catch { }
            }
        }
    }
    private int _displayNameChangeCount;
    public int DisplayNameChangeCount { get => _displayNameChangeCount; set { _displayNameChangeCount = value; OnPropertyChanged(); } }

    private byte[]? _avatarBytes;
    private Avalonia.Media.IImage? _avatarPreview;
    public Avalonia.Media.IImage? AvatarPreview { get => _avatarPreview; private set { _avatarPreview = value; OnPropertyChanged(); } }
    private void RefreshAvatarPreview()
    {
        try
        {
            if (_avatarBytes == null || _avatarBytes.Length == 0) { AvatarPreview = null; }
            else { using var ms = new System.IO.MemoryStream(_avatarBytes); AvatarPreview = new Avalonia.Media.Imaging.Bitmap(ms); }
        }
        catch { AvatarPreview = null; }
        (ClearAvatarCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private int _themeIndex;
    public int ThemeIndex
    {
        get => _themeIndex;
        set
        {
            if (_suppressThemeBinding) return;
            if (_themeIndex != value)
            {
                _themeIndex = value;
                OnPropertyChanged();
                try { LogSettingsEvent($"ThemeIndex changed to {_themeIndex}"); } catch { }
            }
        }
    }

    private string _language = "English (US)";
    public string Language
    {
        get => _language;
        set
        {
            if (_language != value)
            {
                _language = value;
                OnPropertyChanged();
                try { LogSettingsEvent($"Language changed to {_language}"); } catch { }
            }
        }
    }

    // Sync ThemeIndex from persisted settings without marking Unsaved.
    // Useful on overlay open when settings may have been loaded in the background.
    public void SyncThemeFromPersisted()
    {
        try
        {
            var idx = (_settings.Settings.Theme) switch
            {
                ThemeOption.Dark => 0,
                ThemeOption.Light => 1,
                ThemeOption.Sandy => 2,
                ThemeOption.Butter => 3,
                _ => 0
            };
            var prevSuppress = _suppressThemeBinding;
            _suppressThemeBinding = true;
            _themeIndex = idx;
            OnPropertyChanged(nameof(ThemeIndex));
            _baseThemeIndex = idx; // align baseline with persisted
            _suppressThemeBinding = prevSuppress;
        }
        catch { }
    }

    // Backfill DisplayName from account if settings value is missing; align VM and baseline.
    public void SyncProfileFromPersisted()
    {
        try
        {
            var s = _settings.Settings;
            var acct = AppServices.Accounts.LoadAccount(AppServices.Passphrase);
            if (string.IsNullOrWhiteSpace(s.DisplayName) && !string.IsNullOrWhiteSpace(acct.DisplayName))
            {
                s.DisplayName = acct.DisplayName;
                DisplayName = acct.DisplayName;
                try { _settings.Save(AppServices.Passphrase); } catch { }
                CaptureBaseline();
            }
        }
        catch { }
    }

    private static ThemeOption IndexToTheme(int idx)
        => idx switch { 0 => ThemeOption.Dark, 1 => ThemeOption.Light, 2 => ThemeOption.Sandy, 3 => ThemeOption.Butter, _ => ThemeOption.Dark };

    // General: Default Presence (Offline is automatic only)
    private static int PresenceToIndex(PresenceStatus s)
        => s switch
        {
            PresenceStatus.Online => 0,
            PresenceStatus.Idle => 1,
            PresenceStatus.DoNotDisturb => 2,
            PresenceStatus.Invisible => 3,
            PresenceStatus.Offline => 0,
            _ => 0
        };
    private static PresenceStatus IndexToPresence(int i)
        => i switch
        {
            0 => PresenceStatus.Online,
            1 => PresenceStatus.Idle,
            2 => PresenceStatus.DoNotDisturb,
            3 => PresenceStatus.Invisible,
            _ => PresenceStatus.Online
        };
    private int _defaultPresenceIndex;
    public int DefaultPresenceIndex { get => _defaultPresenceIndex; set { var v = value; if (v < 0) v = 0; if (v > 3) v = 3; if (_defaultPresenceIndex != v) { _defaultPresenceIndex = v; OnPropertyChanged(); } } }
    
    private bool _suppressNotificationsInDnd;
    public bool SuppressNotificationsInDnd { get => _suppressNotificationsInDnd; set { if (_suppressNotificationsInDnd != value) { _suppressNotificationsInDnd = value; OnPropertyChanged(); } } }
    
    private double _notificationDurationSeconds;
    public double NotificationDurationSeconds { get => _notificationDurationSeconds; set { var v = Math.Clamp(value, 0.5, 30.0); if (Math.Abs(_notificationDurationSeconds - v) > 0.01) { _notificationDurationSeconds = v; OnPropertyChanged(); } } }

    // General: Auto-Lock controls
    private bool _autoLockEnabled;
    public bool AutoLockEnabled { get => _autoLockEnabled; set { if (_autoLockEnabled != value) { _autoLockEnabled = value; OnPropertyChanged(); } } }
    private int _autoLockMinutes;
    public int AutoLockMinutes { get => _autoLockMinutes; set { var v = Math.Max(0, value); if (_autoLockMinutes != v) { _autoLockMinutes = v; OnPropertyChanged(); } } }
    private bool _lockOnMinimize;
    public bool LockOnMinimize { get => _lockOnMinimize; set { if (_lockOnMinimize != value) { _lockOnMinimize = value; OnPropertyChanged(); } } }

    public bool RememberPassphrase
    {
        get => _rememberPassphrase;
        set
        {
            if (_rememberPassphrase != value)
            {
                _rememberPassphrase = value;
                OnPropertyChanged();
                // Stage change; actual DPAPI update occurs in SaveAsync
            }
        }
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand CloseApplyCommand { get; }
    public ICommand PurgeStoredPassphraseCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand CopyUidCommand { get; }
    public ICommand ChooseAvatarCommand { get; }
    public ICommand ClearAvatarCommand { get; }
    public ICommand DeleteAccountCommand { get; }
    public ICommand PurgeAllMessagesCommand { get; }
    public ICommand ResetLayoutCommand { get; }
    public ICommand RunLogMaintenanceCommand { get; }
    public ICommand RetryNatVerificationCommand => new RelayCommand(async _ => { try { await AppServices.Nat.RetryVerificationAsync(); } catch { } });
    public ICommand CopyPublicKeyCommand { get; }

    private bool _copyToastVisible;
    public bool CopyToastVisible { get => _copyToastVisible; set { _copyToastVisible = value; OnPropertyChanged(); } }
    private string _copyToastText = string.Empty;
    public string CopyToastText { get => _copyToastText; set { _copyToastText = value; OnPropertyChanged(); } }

    public event EventHandler? CloseRequested;

    public string ErrorMessage { get => _errorMessage; set { _errorMessage = value; OnPropertyChanged(); } }

    // Lock blur radius (for dimming effect). Clamp to [0..10].
    private int _lockBlurRadius = 6;
    public int LockBlurRadius
    {
        get => _lockBlurRadius;
        set
        {
            var v = ClampRange(value, 0, 10);
            if (_lockBlurRadius != v)
            {
                _lockBlurRadius = v;
                OnPropertyChanged();
            }
        }
    }

    private async System.Threading.Tasks.Task SaveAsync(bool showToast, bool close)
    {
        try
        {
            var s = _settings.Settings;
            string? passphraseAction = null; // "cleared" | "retained" | null
            // Do not allow empty display name to be persisted; fall back to previous/account value
            var newDisplay = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim();
            if (string.IsNullOrWhiteSpace(newDisplay))
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(s.DisplayName)) newDisplay = s.DisplayName;
                    else
                    {
                        var acct0 = AppServices.Accounts.LoadAccount(AppServices.Passphrase);
                        if (!string.IsNullOrWhiteSpace(acct0.DisplayName)) newDisplay = acct0.DisplayName;
                        else if (!string.IsNullOrWhiteSpace(AppServices.Identity.Username)) newDisplay = AppServices.Identity.Username;
                        else newDisplay = UID; // ultimate fallback
                    }
                }
                catch { newDisplay = _baseDisplayName ?? UID; }
            }
            s.DisplayName = newDisplay!;
            // Include theme in manual Save
            s.Theme = IndexToTheme(ThemeIndex);
            // Performance settings
            s.CcdAffinityIndex = ClampRange(CcdAffinityIndex, 0, 3);
            s.DisableGpuAcceleration = DisableGpuAcceleration;
            s.FpsThrottle = Math.Max(0, FpsThrottle);
            s.RefreshRateThrottle = Math.Max(0, RefreshRateThrottle);
            s.BackgroundFramerateFps = Math.Max(1, BackgroundFramerateFps);
            s.RamUsageLimitMb = Math.Max(0, RamUsageLimitMb);
            s.VramUsageLimitMb = Math.Max(0, VramUsageLimitMb);
            s.EnforceRamLimit = EnforceRamLimit;
            s.EnforceVramLimit = EnforceVramLimit;
            s.MainVolume = Math.Clamp(MainVolume, 0.0, 1.0);
            s.NotificationVolume = Math.Clamp(NotificationVolume, 0.0, 1.0);
            s.ChatVolume = Math.Clamp(ChatVolume, 0.0, 1.0);
            s.BlockScreenCapture = BlockScreenCapture;
            // Theme Engine persistence
            s.UiFontFamily = UiFontFamily;
            // Language persistence
            s.Language = string.IsNullOrWhiteSpace(Language) ? "English (US)" : Language;
            // General additions
            s.Status = IndexToPresence(DefaultPresenceIndex);
            s.SuppressNotificationsInDnd = SuppressNotificationsInDnd;
            s.NotificationDurationSeconds = Math.Clamp(NotificationDurationSeconds, 0.5, 30.0);
            s.AutoLockEnabled = AutoLockEnabled;
            s.AutoLockMinutes = Math.Max(0, AutoLockMinutes);
            s.LockOnMinimize = LockOnMinimize;
            s.LockBlurRadius = ClampRange(LockBlurRadius, 0, 10);
            s.ShowPublicKeys = ShowPublicKeys;
            // Persist system tray settings
            s.ShowInSystemTray = ShowInSystemTray;
            s.MinimizeToTray = MinimizeToTray;
            s.RunOnStartup = RunOnStartup;
            // Persist accessibility settings
            s.ShowKeyboardFocus = ShowKeyboardFocus;
            s.EnhancedKeyboardNavigation = EnhancedKeyboardNavigation;
            // Persist hotkey settings
            s.LockHotkeyKey = (int)_lockHotkeyKey;
            s.LockHotkeyModifiers = (int)_lockHotkeyModifiers;
            s.ClearInputHotkeyKey = (int)_clearInputHotkeyKey;
            s.ClearInputHotkeyModifiers = (int)_clearInputHotkeyModifiers;
            s.EnableDebugLogAutoTrim = EnableDebugLogAutoTrim;
            s.DebugUiLogMaxLines = DebugUiLogMaxLines;
            s.DebugLogRetentionDays = DebugLogRetentionDays;
            s.DebugLogMaxMegabytes = DebugLogMaxMegabytes;
            s.EnableLogging = EnableLogging;
            // Apply staged Security change: RememberPassphrase
            if (RememberPassphrase != s.RememberPassphrase)
            {
                if (RememberPassphrase)
                {
                    var pass = AppServices.Passphrase;
                    if (!string.IsNullOrWhiteSpace(pass) && pass != "dev")
                    {
                        _settings.SetRememberedPassphrase(pass);
                        s.RememberPassphrase = true;
                        _settings.SetRememberPreference(true);
                        passphraseAction = "retained";
                    }
                }
                else
                {
                    _settings.ClearRememberedPassphrase();
                    s.RememberPassphrase = false;
                    _settings.SetRememberPreference(false);
                    // Defer clearing AppServices.Passphrase until after we save settings
                    passphraseAction = "cleared";
                }
            }
            _settings.Save(AppServices.Passphrase);
            try { LogSettingsEvent($"Saved settings (Theme={s.Theme})"); } catch { }
            try { WritePerformanceLog($"Saved perf: CcdAffinityIndex={s.CcdAffinityIndex}, DisableGPU={s.DisableGpuAcceleration}, FPS={s.FpsThrottle}, Refresh={s.RefreshRateThrottle}, RAMmb={s.RamUsageLimitMb}, VRAMmb={s.VramUsageLimitMb}"); } catch { }
            try { ApplyGpuModeImmediate(s.DisableGpuAcceleration); } catch { }
            try { ApplyFpsThrottleImmediate(s.FpsThrottle); } catch { }
            try { ApplyRefreshRateThrottleImmediate(s.RefreshRateThrottle); } catch { }
            try { ZTalk.Services.FocusFramerateService.ApplyCurrentPolicy(); } catch { }
            try { ApplyCcdAffinityImmediate(s.CcdAffinityIndex); } catch { }
            // Apply theme + theme engine live
            try { _themeService.SetTheme(s.Theme); _themeService.ApplyThemeEngine(s.UiFontFamily, 1.0); } catch { }
            // Apply accessibility settings immediately
            // Accessibility settings removed - these are OS-level settings
            // Persist profile-related items
            try
            {
                var acct = AppServices.Accounts.LoadAccount(AppServices.Passphrase);
                if (acct.DisplayName != newDisplay)
                {
                    acct.DisplayName = newDisplay!;
                    (acct.DisplayNameHistory ??= new()).Add(new DisplayNameRecord { Name = newDisplay!, ChangedAtUtc = DateTime.UtcNow });
                    acct.DisplayNameChangeCount = acct.DisplayNameChangeCount <= 0 ? 1 : acct.DisplayNameChangeCount + 1;
                }
                acct.Bio = string.IsNullOrWhiteSpace(Bio) ? null : Bio;
                acct.ShareAvatar = ShareAvatar;
                acct.Avatar = _avatarBytes;
                AppServices.Accounts.SaveAccount(acct, AppServices.Passphrase);
                try { AppServices.Identity.UpdateProfileFromAccount(acct); } catch { }
            }
            catch { }
            // Apply screen capture protection immediately to all open windows (Windows)
            try
            {
                if (OperatingSystem.IsWindows() &&
                    Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
                {
                    foreach (var w in life.Windows.OfType<Window>())
                        ZTalk.Services.ScreenCaptureProtection.SetExcludeFromCapture(w, BlockScreenCapture);
                }
            }
            catch { }
            // Apply system tray settings immediately
            try
            {
                if (ShowInSystemTray)
                {
                    AppServices.TrayIcon.Initialize();
                    AppServices.TrayIcon.SetVisible(true);
                }
                else
                {
                    AppServices.TrayIcon.SetVisible(false);
                }
            }
            catch (Exception ex)
            {
                try { Utilities.Logger.Log($"Failed to apply tray icon settings: {ex.Message}"); } catch { }
            }
            // Apply Windows startup setting
            try
            {
                WindowsStartupManager.ApplyStartupSetting(RunOnStartup);
            }
            catch (Exception ex)
            {
                try { Utilities.Logger.Log($"Failed to apply startup setting: {ex.Message}"); } catch { }
            }

            // Update hotkey registration if changed
            try
            {
                HotkeyManager.Instance.UpdateKeyBinding("app.lock", _lockHotkeyKey, _lockHotkeyModifiers);
                HotkeyManager.Instance.UpdateKeyBinding("app.clearInput", _clearInputHotkeyKey, _clearInputHotkeyModifiers);
            }
            catch { }

            // Refresh baseline (including theme now)
            CaptureBaseline();
            if (showToast)
            {
                if (string.Equals(passphraseAction, "cleared", StringComparison.Ordinal))
                {
                    try { AppServices.Passphrase = string.Empty; } catch { }
                    await ShowSaveToastAsync("Passphrase cleared (Save Passphrase is off)");
                }
                else if (string.Equals(passphraseAction, "retained", StringComparison.Ordinal))
                {
                    await ShowSaveToastAsync("Passphrase retained (Save Passphrase is on)");
                }
                else
                {
                    await ShowSaveToastAsync("Changes Saved");
                }
            }
            if (close)
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Settings save error: {ex.Message}");
            ErrorMessage = "Failed to save settings. Please try again.";
        }
    }

    // Lightweight UI logging
    private static bool UiLoggingEnabled => ZTalk.Utilities.LoggingPaths.Enabled;
    private static void WriteUiLog(string line)
    {
        try
        {
            if (!UiLoggingEnabled) return;
            System.IO.File.AppendAllText(ZTalk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} {line}{Environment.NewLine}");
        }
        catch { }
    }

    // ApplyAccessibilitySettings removed - all settings were OS-level (High Contrast, Reduce Motion, Cursor)
    // These cannot be controlled by the application and must be configured through Windows Settings

    private static void LogSettingsEvent(string line)
    {
        try
        {
            if (!Utilities.LoggingPaths.Enabled) return;
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "logs", "settings.log");
            System.IO.File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}\n");
        }
        catch { }
    }

    private async System.Threading.Tasks.Task CopyUidAsync()
    {
        try
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            if (lifetime?.MainWindow?.Clipboard != null)
                await lifetime.MainWindow.Clipboard.SetTextAsync(DisplayUID);
            _ = ShowToastAsync("UID copied!");
        }
        catch { }
    }

    private async System.Threading.Tasks.Task ChooseAvatarAsync()
    {
        try
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var owner = lifetime?.MainWindow;
            if (owner == null) return;
            var files = await owner.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Choose avatar image",
                AllowMultiple = false,
                FileTypeFilter = new System.Collections.Generic.List<Avalonia.Platform.Storage.FilePickerFileType>
                {
                    new("Images") { Patterns = new System.Collections.Generic.List<string> { "*.png", "*.jpg", "*.jpeg", "*.bmp" } }
                }
            });
            var f = files is { Count: > 0 } list ? list[0] : null;
            if (f != null)
            {
                await using var s = await f.OpenReadAsync();
                using var ms = new System.IO.MemoryStream();
                await s.CopyToAsync(ms);
                _avatarBytes = ms.ToArray();
                RefreshAvatarPreview();
            }
        }
        catch { }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        MaybeUpdateUnsavedChanges(name);
    }

    private void MaybeUpdateUnsavedChanges(string? propertyName)
    {
        if (_suppressDirtyCheck) return;
        if (propertyName != null && !DirtyTrackedProperties.Contains(propertyName)) return;
        UpdateUnsavedChangesState();
    }

    private void UpdateUnsavedChangesState()
    {
        HasUnsavedChanges = ComputeHasUnsavedChanges();
    }

    private bool ComputeHasUnsavedChanges()
    {
        try
        {
            if (!string.Equals(_baseDisplayName, DisplayName ?? string.Empty, StringComparison.Ordinal)) return true;
            if (_baseThemeIndex != _themeIndex) return true;
            if (_baseShareAvatar != _shareAvatar) return true;
            if (!string.Equals(_baseBio, Bio ?? string.Empty, StringComparison.Ordinal)) return true;
            if (!string.Equals(_baseAvatarSig, GetAvatarSignature(_avatarBytes), StringComparison.Ordinal)) return true;
            if (_baseRememberPassphrase != _rememberPassphrase) return true;
            if (!string.Equals(_baseUiFontFamily ?? string.Empty, UiFontFamily ?? string.Empty, StringComparison.Ordinal)) return true;
            if (!string.Equals(_baseLanguage ?? "English (US)", Language ?? "English (US)", StringComparison.Ordinal)) return true;
            if (_baseDefaultPresenceIndex != _defaultPresenceIndex) return true;
            if (_baseSuppressNotificationsInDnd != _suppressNotificationsInDnd) return true;
            if (Math.Abs(_baseNotificationDurationSeconds - _notificationDurationSeconds) > 0.01) return true;
            if (_baseAutoLockEnabled != _autoLockEnabled) return true;
            if (_baseAutoLockMinutes != _autoLockMinutes) return true;
            if (_baseLockOnMinimize != _lockOnMinimize) return true;
            if (_baseLockBlurRadius != _lockBlurRadius) return true;
            if (_baseBlockScreenCapture != _blockScreenCapture) return true;
            if (_baseShowPublicKeys != _showPublicKeys) return true;
            if (_baseLockHotkeyKey != _lockHotkeyKey || _baseLockHotkeyModifiers != _lockHotkeyModifiers) return true;
            if (_baseShowKeyboardFocus != _showKeyboardFocus) return true;
            if (_baseEnhancedKeyboardNavigation != _enhancedKeyboardNavigation) return true;
            if (_baseShowInSystemTray != _showInSystemTray) return true;
            if (_baseMinimizeToTray != _minimizeToTray) return true;
            if (_baseRunOnStartup != _runOnStartup) return true;
            if (_baseCcdAffinityIndex != _ccdAffinityIndex) return true;
            if (_baseDisableGpuAcceleration != _disableGpuAcceleration) return true;
            if (_baseFpsThrottle != _fpsThrottle) return true;
            if (_baseRefreshRateThrottle != _refreshRateThrottle) return true;
            if (_baseBackgroundFramerateFps != _backgroundFramerateFps) return true;
            if (_baseRamUsageLimitMb != _ramUsageLimitMb) return true;
            if (_baseVramUsageLimitMb != _vramUsageLimitMb) return true;
            if (_baseEnforceRamLimit != _enforceRamLimit) return true;
            if (_baseEnforceVramLimit != _enforceVramLimit) return true;
            if (Math.Abs(_baseMainVolume - _mainVolume) > 0.001) return true;
            if (Math.Abs(_baseNotificationVolume - _notificationVolume) > 0.001) return true;
            if (Math.Abs(_baseChatVolume - _chatVolume) > 0.001) return true;
            if (_baseEnableDebugLogAutoTrim != _enableDebugLogAutoTrim) return true;
            if (_baseDebugUiLogMaxLines != _debugUiLogMaxLines) return true;
            if (_baseDebugLogRetentionDays != _debugLogRetentionDays) return true;
            if (_baseDebugLogMaxMegabytes != _debugLogMaxMegabytes) return true;
            if (_baseEnableLogging != _enableLogging) return true;
            return false;
        }
        catch
        {
            return true;
        }
    }

    private void UpdateUnsavedWarningVisual()
    {
        if (_hasUnsavedChanges)
        {
            UnsavedWarningText = UnsavedChangesMessage;
            UnsavedWarningVisible = true;
            DismissSaveToast();
        }
        else
        {
            UnsavedWarningVisible = false;
        }
    }

    private void DismissSaveToast()
    {
        var previous = System.Threading.Interlocked.Exchange(ref _saveToastCts, null);
        try { previous?.Cancel(); } catch { }
        previous?.Dispose();
        SaveToastVisible = false;
        SaveToastText = string.Empty;
    }

    private void CaptureBaseline()
    {
        var prevSuppress = _suppressDirtyCheck;
        _suppressDirtyCheck = true;
        try
        {
            _baseDisplayName = DisplayName ?? string.Empty;
            _baseThemeIndex = ThemeIndex;
            _baseShareAvatar = ShareAvatar;
            _baseBio = Bio ?? string.Empty;
            _baseAvatarSig = GetAvatarSignature(_avatarBytes);
            _baseRememberPassphrase = _rememberPassphrase;
        _baseUiFontFamily = UiFontFamily;
            _baseLanguage = Language ?? "English (US)";
            _baseDefaultPresenceIndex = _defaultPresenceIndex;
            _baseSuppressNotificationsInDnd = _suppressNotificationsInDnd;
            _baseNotificationDurationSeconds = _notificationDurationSeconds;
            _baseAutoLockEnabled = _autoLockEnabled;
            _baseAutoLockMinutes = _autoLockMinutes;
            _baseLockOnMinimize = _lockOnMinimize;
            _baseCcdAffinityIndex = _ccdAffinityIndex;
            _baseDisableGpuAcceleration = _disableGpuAcceleration;
            _baseFpsThrottle = _fpsThrottle;
            _baseRefreshRateThrottle = _refreshRateThrottle;
            _baseBackgroundFramerateFps = _backgroundFramerateFps;
            _baseRamUsageLimitMb = _ramUsageLimitMb;
            _baseVramUsageLimitMb = _vramUsageLimitMb;
            _baseEnforceRamLimit = _enforceRamLimit;
            _baseEnforceVramLimit = _enforceVramLimit;
            _baseMainVolume = _mainVolume;
            _baseNotificationVolume = _notificationVolume;
            _baseChatVolume = _chatVolume;
            _baseLockBlurRadius = _lockBlurRadius;
            _baseBlockScreenCapture = _blockScreenCapture;
            _baseShowPublicKeys = _showPublicKeys;
            _baseLockHotkeyKey = _lockHotkeyKey;
            _baseLockHotkeyModifiers = _lockHotkeyModifiers;
            _baseShowKeyboardFocus = _showKeyboardFocus;
            _baseEnhancedKeyboardNavigation = _enhancedKeyboardNavigation;
            _baseShowInSystemTray = _showInSystemTray;
            _baseMinimizeToTray = _minimizeToTray;
            _baseRunOnStartup = _runOnStartup;
            _baseEnableDebugLogAutoTrim = _enableDebugLogAutoTrim;
            _baseDebugUiLogMaxLines = _debugUiLogMaxLines;
            _baseDebugLogRetentionDays = _debugLogRetentionDays;
            _baseDebugLogMaxMegabytes = _debugLogMaxMegabytes;
            _baseEnableLogging = _enableLogging;
            HasUnsavedChanges = false;
        }
        catch { }
        finally
        {
            _suppressDirtyCheck = prevSuppress;
            if (!_suppressDirtyCheck)
            {
                UpdateUnsavedChangesState();
            }
        }
    }

    // Theme Engine properties
    private string? _uiFontFamily;
    public string? UiFontFamily { get => _uiFontFamily; set { if (_uiFontFamily != value) { _uiFontFamily = value; OnPropertyChanged(); } } }
    
    // UI Scaling removed - use OS-level display scaling instead
    // DO NOT re-add: RenderTransform scaling causes unsolvable layout/window sizing issues,
    // content clipping, and buttons disappearing off-screen. Use Windows Display Settings or
    // equivalent OS accessibility features for proper DPI/scaling support.

    // Privacy
    private bool _blockScreenCapture;
    private bool _showPublicKeys;
    public bool BlockScreenCapture
    {
        get => _blockScreenCapture;
        set { if (_blockScreenCapture != value) { _blockScreenCapture = value; OnPropertyChanged(); } }
    }
    public bool ShowPublicKeys
    {
        get => _showPublicKeys;
        set { if (_showPublicKeys != value) { _showPublicKeys = value; OnPropertyChanged(); } }
    }

    // System Tray settings
    private bool _showInSystemTray;
    private bool _minimizeToTray;
    private bool _runOnStartup;

    public bool ShowInSystemTray
    {
        get => _showInSystemTray;
        set { if (_showInSystemTray != value) { _showInSystemTray = value; OnPropertyChanged(); } }
    }
    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set { if (_minimizeToTray != value) { _minimizeToTray = value; OnPropertyChanged(); } }
    }
    public bool RunOnStartup
    {
        get => _runOnStartup;
        set { if (_runOnStartup != value) { _runOnStartup = value; OnPropertyChanged(); } }
    }

    // Accessibility - OS-controlled settings removed (High Contrast, Reduce Motion, Cursor Blink/Width)
    // These must be configured through Windows Settings > Accessibility
    private bool _showKeyboardFocus = true;
    private bool _enhancedKeyboardNavigation = true;

    public bool ShowKeyboardFocus
    {
        get => _showKeyboardFocus;
        set { if (_showKeyboardFocus != value) { _showKeyboardFocus = value; OnPropertyChanged(); } }
    }
    public bool EnhancedKeyboardNavigation
    {
        get => _enhancedKeyboardNavigation;
        set { if (_enhancedKeyboardNavigation != value) { _enhancedKeyboardNavigation = value; OnPropertyChanged(); } }
    }

    // Hotkey configuration
    private Avalonia.Input.Key _lockHotkeyKey = Avalonia.Input.Key.L;
    private Avalonia.Input.KeyModifiers _lockHotkeyModifiers = Avalonia.Input.KeyModifiers.Control;
    private bool _isCapturingLockHotkey;

    private Avalonia.Input.Key _clearInputHotkeyKey = Avalonia.Input.Key.Q;
    private Avalonia.Input.KeyModifiers _clearInputHotkeyModifiers = Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift;
    private bool _isCapturingClearInputHotkey;

    public string LockHotkeyDisplay => HotkeyManager.FormatKeyBinding(_lockHotkeyKey, _lockHotkeyModifiers);
    public bool IsCapturingLockHotkey
    {
        get => _isCapturingLockHotkey;
        set
        {
            if (_isCapturingLockHotkey != value)
            {
                _isCapturingLockHotkey = value;
                OnPropertyChanged();
            }
        }
    }

    public string ClearInputHotkeyDisplay => HotkeyManager.FormatKeyBinding(_clearInputHotkeyKey, _clearInputHotkeyModifiers);
    public bool IsCapturingClearInputHotkey
    {
        get => _isCapturingClearInputHotkey;
        set
        {
            if (_isCapturingClearInputHotkey != value)
            {
                _isCapturingClearInputHotkey = value;
                OnPropertyChanged();
            }
        }
    }

    private bool IsReservedHotkey(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers)
    {
        // Block Ctrl+Alt+Delete (system reserved)
        if (key == Avalonia.Input.Key.Delete && 
            (modifiers & Avalonia.Input.KeyModifiers.Control) == Avalonia.Input.KeyModifiers.Control &&
            (modifiers & Avalonia.Input.KeyModifiers.Alt) == Avalonia.Input.KeyModifiers.Alt)
        {
            return true;
        }

        // Block common text editor shortcuts (Ctrl+Letter combinations)
        if ((modifiers & Avalonia.Input.KeyModifiers.Control) == Avalonia.Input.KeyModifiers.Control &&
            modifiers != (Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift) &&
            modifiers != (Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Alt))
        {
            // Block Ctrl+C, Ctrl+V, Ctrl+X, Ctrl+Z, Ctrl+Y, Ctrl+A, Ctrl+S, Ctrl+F, Ctrl+H, Ctrl+N, Ctrl+O, Ctrl+P, Ctrl+W, Ctrl+T
            if (key == Avalonia.Input.Key.C || key == Avalonia.Input.Key.V || key == Avalonia.Input.Key.X ||
                key == Avalonia.Input.Key.Z || key == Avalonia.Input.Key.Y || key == Avalonia.Input.Key.A ||
                key == Avalonia.Input.Key.S || key == Avalonia.Input.Key.F || key == Avalonia.Input.Key.H ||
                key == Avalonia.Input.Key.N || key == Avalonia.Input.Key.O || key == Avalonia.Input.Key.P ||
                key == Avalonia.Input.Key.W || key == Avalonia.Input.Key.T || key == Avalonia.Input.Key.B ||
                key == Avalonia.Input.Key.I || key == Avalonia.Input.Key.U || key == Avalonia.Input.Key.K)
            {
                return true;
            }
        }

        // Block Alt+F4 (close window)
        if (key == Avalonia.Input.Key.F4 && 
            (modifiers & Avalonia.Input.KeyModifiers.Alt) == Avalonia.Input.KeyModifiers.Alt)
        {
            return true;
        }

        // Block Win+L (system lock)
        if (key == Avalonia.Input.Key.L && 
            (modifiers & Avalonia.Input.KeyModifiers.Meta) == Avalonia.Input.KeyModifiers.Meta)
        {
            return true;
        }

        return false;
    }

    public void StartCapturingLockHotkey()
    {
        IsCapturingLockHotkey = true;
        try
        {
            // Attach a key down handler to the main window
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
            {
                var mainWindow = life.MainWindow;
                if (mainWindow != null)
                {
                    // Remove previous handler if any
                    mainWindow.KeyDown -= OnCaptureKeyDown;
                    mainWindow.KeyDown += OnCaptureKeyDown;
                }
            }
        }
        catch { IsCapturingLockHotkey = false; }
    }

    public void StartCapturingClearInputHotkey()
    {
        IsCapturingClearInputHotkey = true;
        try
        {
            // Attach a key down handler to the main window
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
            {
                var mainWindow = life.MainWindow;
                if (mainWindow != null)
                {
                    // Remove previous handler if any
                    mainWindow.KeyDown -= OnCaptureClearInputKeyDown;
                    mainWindow.KeyDown += OnCaptureClearInputKeyDown;
                }
            }
        }
        catch { IsCapturingClearInputHotkey = false; }
    }

    private void OnCaptureKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        try
        {
            if (!IsCapturingLockHotkey) return;

            // Ignore modifier-only keys
            if (e.Key == Avalonia.Input.Key.LeftCtrl || e.Key == Avalonia.Input.Key.RightCtrl ||
                e.Key == Avalonia.Input.Key.LeftAlt || e.Key == Avalonia.Input.Key.RightAlt ||
                e.Key == Avalonia.Input.Key.LeftShift || e.Key == Avalonia.Input.Key.RightShift ||
                e.Key == Avalonia.Input.Key.LWin || e.Key == Avalonia.Input.Key.RWin)
            {
                return;
            }

            // Check if hotkey is reserved
            if (IsReservedHotkey(e.Key, e.KeyModifiers))
            {
                _ = ShowSaveToastAsync("This hotkey is reserved by the system or conflicts with common text editor shortcuts.", 2500);
                IsCapturingLockHotkey = false;
                return;
            }

            // Check if trying to set to the default value (Ctrl+L)
            bool isSettingToDefault = (e.Key == Avalonia.Input.Key.L && e.KeyModifiers == Avalonia.Input.KeyModifiers.Control);

            // Check for conflicts (but allow setting to default even if it conflicts with itself)
            if (!isSettingToDefault && HotkeyManager.Instance.HasConflict(e.Key, e.KeyModifiers, "app.lock"))
            {
                // Show error and don't accept
                _ = ShowSaveToastAsync("Hotkey conflict! This combination is already in use.", 2000);
                IsCapturingLockHotkey = false;
                return;
            }

            // Accept the new hotkey
            _lockHotkeyKey = e.Key;
            _lockHotkeyModifiers = e.KeyModifiers;
            OnPropertyChanged(nameof(LockHotkeyDisplay));
            IsCapturingLockHotkey = false;

            // Unregister from window
            if (sender is Window w)
            {
                w.KeyDown -= OnCaptureKeyDown;
            }

            e.Handled = true;
        }
        catch
        {
            IsCapturingLockHotkey = false;
        }
    }

    private void OnCaptureClearInputKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        try
        {
            if (!IsCapturingClearInputHotkey) return;

            // Ignore modifier-only keys
            if (e.Key == Avalonia.Input.Key.LeftCtrl || e.Key == Avalonia.Input.Key.RightCtrl ||
                e.Key == Avalonia.Input.Key.LeftAlt || e.Key == Avalonia.Input.Key.RightAlt ||
                e.Key == Avalonia.Input.Key.LeftShift || e.Key == Avalonia.Input.Key.RightShift ||
                e.Key == Avalonia.Input.Key.LWin || e.Key == Avalonia.Input.Key.RWin)
            {
                return;
            }

            // Check if hotkey is reserved
            if (IsReservedHotkey(e.Key, e.KeyModifiers))
            {
                _ = ShowSaveToastAsync("This hotkey is reserved by the system or conflicts with common text editor shortcuts.", 2500);
                IsCapturingClearInputHotkey = false;
                return;
            }

            // Check if trying to set to the default value (Ctrl+Shift+Q)
            bool isSettingToDefault = (e.Key == Avalonia.Input.Key.Q && 
                                       e.KeyModifiers == (Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift));

            // Check for conflicts (but allow setting to default even if it conflicts with itself)
            if (!isSettingToDefault && HotkeyManager.Instance.HasConflict(e.Key, e.KeyModifiers, "app.clearInput"))
            {
                // Show error and don't accept
                _ = ShowSaveToastAsync("Hotkey conflict! This combination is already in use.", 2000);
                IsCapturingClearInputHotkey = false;
                return;
            }

            // Accept the new hotkey
            _clearInputHotkeyKey = e.Key;
            _clearInputHotkeyModifiers = e.KeyModifiers;
            OnPropertyChanged(nameof(ClearInputHotkeyDisplay));
            IsCapturingClearInputHotkey = false;

            // Unregister from window
            if (sender is Window w)
            {
                w.KeyDown -= OnCaptureClearInputKeyDown;
            }

            e.Handled = true;
        }
        catch
        {
            IsCapturingClearInputHotkey = false;
        }
    }

    public ICommand ResetLockHotkeyCommand => new RelayCommand(_ =>
    {
        _lockHotkeyKey = Avalonia.Input.Key.L;
        _lockHotkeyModifiers = Avalonia.Input.KeyModifiers.Control;
        OnPropertyChanged(nameof(LockHotkeyDisplay));
    }, _ => true);

    public ICommand ResetClearInputHotkeyCommand => new RelayCommand(_ =>
    {
        _clearInputHotkeyKey = Avalonia.Input.Key.Q;
        _clearInputHotkeyModifiers = Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift;
        OnPropertyChanged(nameof(ClearInputHotkeyDisplay));
    }, _ => true);

    public bool EnableDebugLogAutoTrim
    {
        get => _enableDebugLogAutoTrim;
        set
        {
            if (_enableDebugLogAutoTrim != value)
            {
                _enableDebugLogAutoTrim = value;
                OnPropertyChanged();
            }
        }
    }

    public bool EnableLogging
    {
        get => _enableLogging;
        set
        {
            if (_enableLogging != value)
            {
                _enableLogging = value;
                OnPropertyChanged();
                // Update LoggingPaths immediately when toggle changes
                try
                {
                    ZTalk.Utilities.LoggingPaths.SetEnabled(value);
                }
                catch { }
                // Update Logs button visibility in MainWindow
#if DEBUG
                try
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            var desktop = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                            var mainWindow = desktop?.Windows?.OfType<Views.MainWindow>().FirstOrDefault();
                            var logsButton = mainWindow?.FindControl<Avalonia.Controls.Button>("LogsButton");
                            if (logsButton != null)
                            {
                                logsButton.IsVisible = value;
                            }
                        }
                        catch { }
                    });
                }
                catch { }
#endif
            }
        }
    }

    public int DebugUiLogMaxLines
    {
        get => _debugUiLogMaxLines;
        set
        {
            var v = ClampRange(value <= 0 ? 1000 : value, 100, 20000);
            if (_debugUiLogMaxLines != v)
            {
                _debugUiLogMaxLines = v;
                OnPropertyChanged();
            }
        }
    }
    public int DebugLogRetentionDays
    {
        get => _debugLogRetentionDays;
        set
        {
            var v = value;
            if (v < 0) v = 0;
            if (v > 30) v = 30;
            if (_debugLogRetentionDays != v)
            {
                _debugLogRetentionDays = v;
                OnPropertyChanged();
            }
        }
    }
    public int DebugLogMaxMegabytes
    {
        get => _debugLogMaxMegabytes;
        set
        {
            var v = ClampRange(value <= 0 ? 16 : value, 1, 512);
            if (_debugLogMaxMegabytes != v)
            {
                _debugLogMaxMegabytes = v;
                OnPropertyChanged();
            }
        }
    }
    public string LogMaintenanceStatus
    {
        get => _logMaintenanceStatus;
        private set
        {
            var text = value ?? string.Empty;
            if (!string.Equals(_logMaintenanceStatus, text, StringComparison.Ordinal))
            {
                _logMaintenanceStatus = text;
                OnPropertyChanged();
            }
        }
    }
    public string LogMaintenanceLastRun
        => _lastLogMaintenanceUtc.HasValue
            ? _lastLogMaintenanceUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
            : "Never";
    private string _selfPublicKeyHex = string.Empty;
    public string SelfPublicKeyHex { get => _selfPublicKeyHex; private set { _selfPublicKeyHex = value; OnPropertyChanged(); } }

    // Runtime application of GPU and throttles
    private void ApplyGpuModeImmediate(bool disable)
    {
        try
        {
            var app = Avalonia.Application.Current;
            if (app == null) return;
            // Toggle avatar smoothing across the app via global resource key
            var val = disable ? "None" : "HighQuality";
            app.Resources["App.AvatarInterpolation"] = val;
            // Optional: signal a UI pulse so visuals update
            try { Services.AppServices.Events.RaiseUiPulse(); } catch { }
            WritePerformanceLogSafe($"GPU {(disable ? "Disabled" : "Enabled")} (runtime)");
        }
        catch { }
    }

    private void ApplyFpsThrottleImmediate(int fps)
    {
        try
        {
            // FPS throttle caps rendering callbacks by reducing UI pulse frequency baseline.
            // We use UpdateManager UI pulse as the central loop; lower frequency when FPS is set.
            var interval = fps <= 0 ? 16 : Math.Max(5, 1000 / Math.Max(1, fps));
            Services.AppServices.Updates.UpdateUiInterval("App.UI.Pulse", interval);
            WritePerformanceLogSafe($"FPS throttle set to {fps} fps (interval {interval}ms)");
        }
        catch { }
    }

    private void ApplyRefreshRateThrottleImmediate(int hz)
    {
        try
        {
            // Internal redraw frequency separate from FPS: drive a secondary UI timer for animations/graphs.
            // We'll register (or update) a dedicated key.
            const string key = "App.UI.Refresh";
            if (hz <= 0)
            {
                Services.AppServices.Updates.UnregisterUi(key);
                WritePerformanceLogSafe("Refresh throttle off");
                return;
            }
            var interval = Math.Max(5, 1000 / Math.Max(1, hz));
            Services.AppServices.Updates.RegisterUiInterval(key, interval, () =>
            {
                try { Services.AppServices.Events.RaiseUiPulse(); } catch { }
            });
            WritePerformanceLogSafe($"Refresh throttle set to {hz} Hz (interval {interval}ms)");
        }
        catch { }
    }

    private void WritePerformanceLogSafe(string message)
    {
        try { WritePerformanceLog(message); } catch { }
    }
    // Resource monitoring and enforcement
    private const string RamMonitorKey = "Perf.RAM.Monitor";
    private const string VramMonitorKey = "Perf.VRAM.Monitor";
    private void StartOrUpdateResourceMonitors()
    {
        try
        {
            if (!_enforceRamLimit || _ramUsageLimitMb <= 0)
            {
                AppServices.Updates.UnregisterBg(RamMonitorKey);
            }
            else
            {
                // Clamp to safe minimum of 288 MiB
                var limitMb = Math.Max(288, _ramUsageLimitMb);
                AppServices.Updates.RegisterBgInterval(RamMonitorKey, 1000, () => EnforceRam(limitMb));
                WritePerformanceLogSafe($"[RAM] Enforcement enabled at {limitMb} MB");
            }

            if (!_enforceVramLimit || _vramUsageLimitMb <= 0)
            {
                AppServices.Updates.UnregisterBg(VramMonitorKey);
            }
            else
            {
                var limitMb = Math.Max(64, _vramUsageLimitMb); // minimal meaningful VRAM cap
                AppServices.Updates.RegisterBgInterval(VramMonitorKey, 1500, () => EnforceVram(limitMb));
                WritePerformanceLogSafe($"[VRAM] Enforcement enabled at {limitMb} MB");
            }
        }
        catch { }
    }

    private void EnforceRam(int limitMb)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetCurrentProcess();
            long working = 0, privateBytes = 0;
            try { working = p.WorkingSet64; } catch { }
            try { privateBytes = p.PrivateMemorySize64; } catch { }
            // Also consider managed heap
            long managed = 0; try { managed = GC.GetTotalMemory(forceFullCollection: false); } catch { }
            var usedMb = (int)Math.Max(working, Math.Max(privateBytes, managed)) / (1024 * 1024);
            if (usedMb > limitMb)
            {
                // Paging fallback when limit < 1024 MB, otherwise trim/light GC
                bool low = limitMb < 1024;
                if (low)
                {
                    try { WritePerformanceLogSafe($"[RAM] Over limit ({usedMb}>{limitMb} MB). Low cap: initiating paging fallback"); } catch { }
                    try { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); } catch { }
                }
                else
                {
                    try { WritePerformanceLogSafe($"[RAM] Over limit ({usedMb}>{limitMb} MB). Trimming caches"); } catch { }
                }
                // Hint caches to trim
                try { TrimCaches(); } catch { }
            }
        }
        catch { }
   
    }

    private static void TrimCaches()
    {
        try { AvatarCache.Stop(); AvatarCache.Start(); } catch { }
        // Potential extension: add other caches here as they appear
    }

    private void EnforceVram(int limitMb)
    {
        try
        {
            // We don't have direct VRAM counters; approximate by trimming large bitmaps cache when ticks occur.
            // Strategy: if VRAM enforcement is on, periodically reload avatar cache to drop decoded images.
            try { AvatarCache.Stop(); AvatarCache.Start(); } catch { }
        }
        catch { }
    }
    private static string GetAvatarSignature(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return "0:0";
        unchecked
        {
            int hash = 17;
            for (int i = 0; i < bytes.Length; i += Math.Max(1, bytes.Length / 32)) hash = hash * 31 + bytes[i];
            return $"{bytes.Length}:{hash}";
        }
    }


    private async System.Threading.Tasks.Task ShowToastAsync(string text)
    {
        try
        {
            CopyToastText = text;
            CopyToastVisible = true;
            await System.Threading.Tasks.Task.Delay(1200);
            CopyToastVisible = false;
        }
        catch { }
    }

    private async System.Threading.Tasks.Task CopyPublicKeyAsync()
    {
        try
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            if (lifetime?.MainWindow?.Clipboard != null && !string.IsNullOrWhiteSpace(SelfPublicKeyHex))
                await lifetime.MainWindow.Clipboard.SetTextAsync(SelfPublicKeyHex);
            _ = ShowToastAsync("Public key copied!");
        }
        catch { }
    }

    private async System.Threading.Tasks.Task RunLogMaintenanceAsync()
    {
        try
        {
            var summary = await System.Threading.Tasks.Task.Run(() => AppServices.LogMaintenance.RunMaintenanceNow("manual"));
            UpdateLogMaintenanceStatus(summary, AppServices.LogMaintenance.LastRunUtc);
            await ShowSaveToastAsync("Log maintenance complete", 1600);
        }
        catch { }
    }

    private void OnLogMaintenanceCompleted(string summary)
    {
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    UpdateLogMaintenanceStatus(summary, AppServices.LogMaintenance.LastRunUtc);
                }
                catch { }
            });
        }
        catch { }
    }

    private void UpdateLogMaintenanceStatus(string summary, DateTime? whenUtc)
    {
        try
        {
            _lastLogMaintenanceUtc = whenUtc;
            LogMaintenanceStatus = summary ?? string.Empty;
            OnPropertyChanged(nameof(LogMaintenanceLastRun));
        }
        catch { }
    }

    private void PurgeStoredPassphrase()
    {
        try
        {
            if (RememberPassphrase)
                _settings.PurgeRememberedPassphraseKeepPreference();
            else
                _settings.ClearRememberedPassphrase();
            LockServiceSingleton.Instance.LockNow();
        }
        catch (Exception ex)
        {
            Logger.Log($"PurgeStoredPassphrase error: {ex.Message}");
        }
    }

    // Optional gated logs for performance/accessibility work
    private static bool PerformanceLoggingEnabled
    {
        get
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                var flagPath = System.IO.Path.Combine(baseDir, "logs", "performance.logging.enabled");
                return System.IO.File.Exists(flagPath);
            }
            catch { return false; }
        }
    }
    private static void WritePerformanceLog(string line)
    {
        try
        {
            if (!PerformanceLoggingEnabled || !Utilities.LoggingPaths.Enabled) return;
            var path = Utilities.LoggingPaths.Performance;
            System.IO.File.AppendAllText(path, $"{DateTime.Now:O} {line}{Environment.NewLine}");
        }
        catch { }
    }
    private static bool AccessibilityLoggingEnabled => ZTalk.Utilities.LoggingPaths.Enabled;
    private static void WriteAccessibilityLog(string line)
    {
        try
        {
            if (!AccessibilityLoggingEnabled) return;
            System.IO.File.AppendAllText(ZTalk.Utilities.LoggingPaths.Debug, $"[ACCESS] {DateTime.Now:O} {line}{Environment.NewLine}");
        }
        catch { }
    }

    private bool _wipeLocalSettings;
    public bool WipeLocalSettings { get => _wipeLocalSettings; set { _wipeLocalSettings = value; OnPropertyChanged(); } }
    private bool _isPurgingAllMessages;
    public bool IsPurgingAllMessages
    {
        get => _isPurgingAllMessages;
        private set
        {
            if (_isPurgingAllMessages != value)
            {
                _isPurgingAllMessages = value;
                OnPropertyChanged();
                if (PurgeAllMessagesCommand is RelayCommand relay)
                {
                    relay.RaiseCanExecuteChanged();
                }
            }
        }
    }

    private string _purgeConfirmText = string.Empty;
    public string PurgeConfirmText
    {
        get => _purgeConfirmText;
        set
        {
            if (_purgeConfirmText != value)
            {
                _purgeConfirmText = value;
                OnPropertyChanged();
                if (PurgeAllMessagesCommand is RelayCommand relay)
                {
                    relay.RaiseCanExecuteChanged();
                }
            }
        }
    }

    public bool CanPurgeAllMessages => !IsPurgingAllMessages && 
        string.Equals(PurgeConfirmText?.Trim(), "PURGE-ALL-DATA", StringComparison.Ordinal);

    private string _deleteConfirmText = string.Empty;
    public string DeleteConfirmText { get => _deleteConfirmText; set { _deleteConfirmText = value; OnPropertyChanged(); (DeleteAccountCommand as RelayCommand)?.RaiseCanExecuteChanged(); } }
    public string GeneratedDeleteCode { get; private set; } = GenerateDeleteCode();
    private static string GenerateDeleteCode()
    {
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz2346789";
        Span<byte> bytes = stackalloc byte[6];
        Span<char> code = stackalloc char[6];
        using var rnd = System.Security.Cryptography.RandomNumberGenerator.Create();
        rnd.GetBytes(bytes);
        for (int i = 0; i < bytes.Length; i++)
        {
            code[i] = alphabet[bytes[i] % alphabet.Length];
        }

        return string.Concat("DEL-".AsSpan(), code);
    }
    private void DeleteAccount()
    {
        try
        {
            if (!string.Equals(DeleteConfirmText?.Trim(), GeneratedDeleteCode, StringComparison.Ordinal)) return;
            TryDelete(AppServices.Accounts.GetPath());
            TryDelete(GetContactsPath());
            TryDelete(GetMessagesPath());
            TryDelete(GetPeersPath());
            AppServices.Settings.ClearRememberedPassphrase();
            if (WipeLocalSettings)
            {
                TryDelete(AppServices.Settings.GetSettingsPath());
                TryDelete(GetThemesFolder());
            }
            Logger.Log("Account deletion completed (local only).");
            (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        catch (System.Exception ex)
        {
            Logger.Log($"Account deletion error: {ex.Message}");
        }
    }
    private static void TryDelete(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (System.IO.Directory.Exists(path)) System.IO.Directory.Delete(path, true);
            else if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
        catch { }
    }
    private static string GetContactsPath()
    {
    return ZTalk.Utilities.AppDataPaths.Combine("contacts.p2e");
    }
    private static string GetMessagesPath()
    {
    return ZTalk.Utilities.AppDataPaths.Combine("messages.p2e");
    }
    private static string GetPeersPath()
    {
    return ZTalk.Utilities.AppDataPaths.Combine("peers.p2e");
    }
    private static string GetThemesFolder()
    {
    return ZTalk.Utilities.AppDataPaths.Combine("Themes");
    }

    private async Task PurgeAllMessagesAsync()
    {
        if (IsPurgingAllMessages || !CanPurgeAllMessages) return;

        try
        {

            IsPurgingAllMessages = true;

            MessagePurgeSummary summary = await Task.Run(() => AppServices.Retention.PurgeAllMessagesSecurely(AppServices.Passphrase));

            var message = summary.MessagesDeleted > 0
                ? $"Purged {summary.MessagesDeleted} messages across {summary.MessageFileCount} conversation files."
                : "All conversation files were already empty.";

            if (summary.QueuedMessagesDeleted > 0)
            {
                message += $" Removed {summary.QueuedMessagesDeleted} pending outbox items.";
            }

            message += summary.BytesWiped > 0 ? $" ({summary.BytesWiped:N0} bytes wiped)." : string.Empty;

            await AppServices.Dialogs.ShowInfoAsync("Messages Purged", message.Trim(), 3500);
        }
        catch (Exception ex)
        {
            Logger.Log($"PurgeAllMessages error: {ex.Message}");
            await AppServices.Dialogs.ShowInfoAsync("Purge Failed", "We couldn't purge your messages. Check logs for details.", 4000);
        }
        finally
        {
            IsPurgingAllMessages = false;
        }
    }

    private void ResetLayout()
    {
        try
        {
            var s = _settings.Settings;
            s.MainWindow = new WindowStateSettings();
            s.SettingsWindow = new WindowStateSettings();
            s.NetworkWindow = new WindowStateSettings();
            s.MonitoringWindow = new WindowStateSettings();
            _settings.Save(AppServices.Passphrase);
            _ = ShowToastAsync("Layout reset");
        }
        catch { }
    }

    private void Logout()
    {
        try
        {
            // Do not clear passphrase; simply lock the app.
            try { Logger.Log("User logout requested (lock only; passphrase retained)"); } catch { }
            try { CloseRequested?.Invoke(this, EventArgs.Empty); } catch { }
            try { new ZTalk.Services.LockService().Lock(); } catch { }
        }
        catch { }
    }

    // Network functionality - delegate to NetworkViewModel instance
    private NetworkViewModel? _networkVm;
    private NetworkViewModel NetworkVm => _networkVm ??= new NetworkViewModel();
    
    // Network properties exposed from NetworkViewModel
    public int Port { get => NetworkVm.Port; set => NetworkVm.PortText = value.ToString(System.Globalization.CultureInfo.InvariantCulture); }
    public bool MajorNode { get => NetworkVm.MajorNode; set => NetworkVm.MajorNode = value; }
    public bool EnableGeoBlocking { get => NetworkVm.EnableGeoBlocking; set => NetworkVm.EnableGeoBlocking = value; }
    public string GeoBlockingStatus => NetworkVm.GeoBlockingStatus;
    public bool RelayFallbackEnabled { get; set; } // TODO: Implement in NetworkViewModel
    public string RelayServer { get; set; } = string.Empty; // TODO: Implement in NetworkViewModel
    public string NewMajorNode { get => NetworkVm.NewMajorNode; set => NetworkVm.NewMajorNode = value; }
    public string NewBlockedPeer { get; set; } = string.Empty; // TODO: Implement in NetworkViewModel

    // Network collections exposed from NetworkViewModel
    public System.Collections.ObjectModel.ObservableCollection<string> KnownMajorNodes => NetworkVm.KnownMajorNodes;
    public System.Collections.ObjectModel.ObservableCollection<string> BlockedPeers => NetworkVm.BlockedPeers;
    public System.Collections.ObjectModel.ObservableCollection<Peer> DiscoveredPeers => NetworkVm.DiscoveredPeers;
    public System.Collections.ObjectModel.ObservableCollection<NetworkViewModel.AdapterItem> NetworkAdapters => NetworkVm.Adapters;
    
    // IP Blocking Properties (proxied from NetworkViewModel)
    public System.Collections.ObjectModel.ObservableCollection<IpEntryWithCountry> BadActorIps => NetworkVm.BadActorIps;
    public System.Collections.ObjectModel.ObservableCollection<IpEntryWithCountry> IpRanges => NetworkVm.IpRanges;
    public string NewBadActorIp { get => NetworkVm.NewBadActorIp; set => NetworkVm.NewBadActorIp = value; }
    public string NewIpRange { get => NetworkVm.NewIpRange; set => NetworkVm.NewIpRange = value; }
    public string IpBlockingStats => NetworkVm.IpBlockingStats;
    
    public IpEntryWithCountry? SelectedBadActorIp
    {
        get => NetworkVm.SelectedBadActorIp;
        set => NetworkVm.SelectedBadActorIp = value;
    }
    public IpEntryWithCountry? SelectedIpRange 
    {
        get => NetworkVm.SelectedIpRange;
        set => NetworkVm.SelectedIpRange = value;
    }    public Peer? SelectedDiscoveredPeer
    {
        get => NetworkVm.SelectedDiscoveredPeer;
        set => NetworkVm.SelectedDiscoveredPeer = value;
    }
    public string? SelectedBlockedPeer
    {
        get => NetworkVm.SelectedBlockedPeer;
        set => NetworkVm.SelectedBlockedPeer = value;
    }
    public NetworkViewModel.AdapterItem? SelectedNetworkAdapter 
    { 
        get => NetworkVm.SelectedAdapter; 
        set => NetworkVm.SelectedAdapter = value; 
    }

    // Network commands exposed from NetworkViewModel
    public ICommand AddMajorNodeCommand => NetworkVm.AddMajorNodeCommand;
    public ICommand RemoveMajorNodeCommand => NetworkVm.RemoveMajorNodeCommand;
    public ICommand BlockPeerCommand => NetworkVm.BlockPeerCommand;
    public ICommand UnblockPeerCommand => NetworkVm.UnblockPeerCommand;
    public ICommand BlockSelectedPeersCommand => NetworkVm.BlockSelectedPeersCommand;
    public ICommand UnblockSelectedPeersCommand => NetworkVm.UnblockSelectedPeersCommand;
    public ICommand ClearAllBlocksCommand => NetworkVm.ClearAllBlocksCommand;
    public ICommand RefreshPeersCommand => NetworkVm.RefreshPeersCommand;
    public ICommand MoveAdapterUpCommand => NetworkVm.MoveAdapterUpCommand;
    public ICommand MoveAdapterDownCommand => NetworkVm.MoveAdapterDownCommand;
    public ICommand SaveAdapterOrderCommand => NetworkVm.SaveAdaptersCommand;
    
    // IP Blocking commands exposed from NetworkViewModel
    public ICommand AddBadActorIpCommand => NetworkVm.AddBadActorIpCommand;
    public ICommand RemoveBadActorIpCommand => NetworkVm.RemoveBadActorIpCommand;
    public ICommand AddIpRangeCommand => NetworkVm.AddIpRangeCommand;
    public ICommand RemoveIpRangeCommand => NetworkVm.RemoveIpRangeCommand;
    public ICommand ImportIpListCommand => NetworkVm.ImportIpListCommand;
    public ICommand ExportIpListCommand => NetworkVm.ExportIpListCommand;
    public ICommand ClearAllIpsCommand => NetworkVm.ClearAllIpsCommand;
    public ICommand ClearAllRangesCommand => NetworkVm.ClearAllRangesCommand;
    
    // Additional debug properties that were referenced in XAML
    public int DebugLogSizeValue { get; set; } = 16;
    public int DebugLogSizeMaxValue { get; set; } = 512;
    public string DebugLogSizeUnit { get; set; } = "MB";
    public ICommand? ClearErrorLogCommand { get; set; } // TODO: Implement
}

internal sealed class LockServiceSingleton
{
    private LockServiceSingleton() { Service = new ZTalk.Services.LockService(); }
    public static LockServiceSingleton Instance { get; } = new LockServiceSingleton();
    public ZTalk.Services.LockService Service { get; }
    public void LockNow() => Service.Lock();
}

public class NetworkViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _settings;
    private string _errorMessage = string.Empty;
    private string _infoMessage = string.Empty;
    private readonly PeerManager _peerManager = AppServices.Peers;
    public ICommand RetryNatVerificationCommand { get; }

    public NetworkViewModel()
    {
        _settings = AppServices.Settings;
        var s = _settings.Settings;
        Port = s.Port;
        PortText = s.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        MajorNode = s.MajorNode;
        EnableGeoBlocking = s.EnableGeoBlocking;
    RetryNatVerificationCommand = new RelayCommand(async _ => { try { await AppServices.Nat.RetryVerificationAsync(); } catch { } });
        SaveCommand = new RelayCommand(async _ => await SaveAsync(showToast: true, close: false), _ => Port >= 1 && Port <= 65535);
        CloseApplyCommand = new RelayCommand(async _ => await SaveAsync(showToast: false, close: true), _ => Port >= 1 && Port <= 65535);
        CancelCommand = new RelayCommand(_ => { DiscardNetworkChanges(); CloseRequested?.Invoke(this, EventArgs.Empty); });
        BlockPeerCommand = new RelayCommand(p => { if (p is string uid) { _peerManager.Block(uid); RefreshLists(); } });
        UnblockPeerCommand = new RelayCommand(p => { if (p is string uid) ConfirmUnblock(uid); });
        BlockSelectedPeersCommand = new RelayCommand(_ => BlockSelectedPeers());
        UnblockSelectedPeersCommand = new RelayCommand(_ => UnblockSelectedPeers());
        TrustPeerCommand = new RelayCommand(p => { if (p is string uid) { _peerManager.SetTrusted(uid, true); RefreshLists(); } });
        UntrustPeerCommand = new RelayCommand(p => { if (p is string uid) { _peerManager.SetTrusted(uid, false); RefreshLists(); } });
        ClearAllBlocksCommand = new RelayCommand(_ => ConfirmClearAll());
        RefreshPeersCommand = new RelayCommand(_ => { 
            try { ZTalk.Utilities.Logger.Log("[NetworkViewModel] Manual refresh peers triggered"); } catch { }
            try { AppServices.Discovery.Restart(); } catch { }
            RefreshLists(); 
        });
        AddMajorNodeCommand = new RelayCommand(_ => AddMajorNode(), _ => !string.IsNullOrWhiteSpace(NewMajorNode));
        RemoveMajorNodeCommand = new RelayCommand(n => { if (n is string s) RemoveMajorNode(s); });
        
        // IP Blocking Commands
        AddBadActorIpCommand = new RelayCommand(_ => AddBadActorIp(), _ => !string.IsNullOrWhiteSpace(NewBadActorIp));
        RemoveBadActorIpCommand = new RelayCommand(ip => { if (ip is IpEntryWithCountry entry) RemoveBadActorIp(entry.IpOrRange); });
        AddIpRangeCommand = new RelayCommand(_ => AddIpRange(), _ => !string.IsNullOrWhiteSpace(NewIpRange));
        RemoveIpRangeCommand = new RelayCommand(range => { if (range is IpEntryWithCountry entry) RemoveIpRange(entry.IpOrRange); });
        ImportIpListCommand = new RelayCommand(async _ => await ImportIpListAsync());
        ExportIpListCommand = new RelayCommand(async _ => await ExportIpListAsync());
        ClearAllIpsCommand = new RelayCommand(_ => ClearAllIps(), _ => BadActorIps.Count > 0);
        ClearAllRangesCommand = new RelayCommand(_ => ClearAllRanges(), _ => IpRanges.Count > 0);
        
        RefreshLists();
        RefreshIpLists();

        LoadAdapters();
        MoveAdapterUpCommand = new RelayCommand(_ => MoveAdapter(-1), _ => SelectedAdapter != null);
        MoveAdapterDownCommand = new RelayCommand(_ => MoveAdapter(1), _ => SelectedAdapter != null);
        SaveAdaptersCommand = new RelayCommand(_ => SaveAdapters());

        // Logging commands (used by NetworkWindow Logging tab)
        ClearLogCommand = new RelayCommand(_ => ClearLog());
        CopyAllCommand = new RelayCommand(async _ => await CopyAllAsync());
        CopySelectedCommand = new RelayCommand(async sel => await CopySelectedAsync(sel));

        try
        {
            _uiThrottled = AppServices.Updates.GetUiThrottled("NetworkViewModel.UI.throttle", 250, () =>
            {
                try { Avalonia.Threading.Dispatcher.UIThread.Post(() => NotifyNetworkStatus()); } catch { }
            });
            AppServices.Events.NatChanged += () => _uiThrottled?.Invoke();
            AppServices.Events.NetworkListeningChanged += (_, __) => _uiThrottled?.Invoke();
            AppServices.Events.PeersChanged += () => { 
                try { ZTalk.Utilities.Logger.Log("[NetworkViewModel] PeersChanged event received"); } catch { }
                _uiThrottled?.Invoke(); 
                Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                    try { ZTalk.Utilities.Logger.Log("[NetworkViewModel] RefreshLists called from PeersChanged"); } catch { }
                    RefreshLists();
                }); 
            };
            _uiPulseHandler = () => _uiThrottled?.Invoke();
            AppServices.Events.UiPulse += _uiPulseHandler;
            try { AppServices.Nat.Changed += () => _uiThrottled?.Invoke(); } catch { }
        }
        catch { }
    }

    private int _port;
    public int Port
    {
        get => _port;
        private set
        {
            if (_port != value)
            {
                _port = value;
                OnPropertyChanged();
                (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
                try { AppServices.Events.RaiseNetworkConfigChanged(); } catch { }
            }
        }
    }

    private string _portText = string.Empty;
    public string PortText
    {
        get => _portText;
        set
        {
            if (_portText != value)
            {
                _portText = value;
                if (int.TryParse(value, out var p))
                {
                    if (p < 0) p = 0; if (p > 65535) p = 65535;
                    Port = p;
                }
                OnPropertyChanged();
                (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    private bool _majorNode;
    public bool MajorNode
    {
        get => _majorNode;
        set
        {
            if (_majorNode != value)
            {
                _majorNode = value;
                OnPropertyChanged();
                try { AppServices.Events.RaiseNetworkConfigChanged(); } catch { }
                if (_majorNode)
                {
                    InfoMessage = "If prompted, allow ZTalk through Windows Firewall for inbound connections.";
                }
            }
        }
    }

    // [SECURITY] Geo-blocking settings
    private bool _enableGeoBlocking;
    public bool EnableGeoBlocking
    {
        get => _enableGeoBlocking;
        set
        {
            if (_enableGeoBlocking != value)
            {
                _enableGeoBlocking = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GeoBlockingStatus));
            }
        }
    }

    public string GeoBlockingStatus => SecurityBlocklistService.GetGeoBlockingStatus(_settings.Settings);

    private void ApplyNetworkChangeLiveIfNeeded()
    {
        try { AppServices.Events.RaiseNetworkConfigChanged(); } catch { }
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand CloseApplyCommand { get; }
    public ICommand BlockPeerCommand { get; }
    public ICommand UnblockPeerCommand { get; }
    public ICommand BlockSelectedPeersCommand { get; }
    public ICommand UnblockSelectedPeersCommand { get; }
    public ICommand TrustPeerCommand { get; }
    public ICommand UntrustPeerCommand { get; }
    public ICommand ClearAllBlocksCommand { get; }
    public ICommand RefreshPeersCommand { get; }
    public ICommand MoveAdapterUpCommand { get; }
    public ICommand MoveAdapterDownCommand { get; }
    public ICommand SaveAdaptersCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand CopyAllCommand { get; }
    public ICommand CopySelectedCommand { get; }
    
    // IP Blocking Commands
    public ICommand AddBadActorIpCommand { get; }
    public ICommand RemoveBadActorIpCommand { get; }
    public ICommand AddIpRangeCommand { get; }
    public ICommand RemoveIpRangeCommand { get; }
    public ICommand ImportIpListCommand { get; }
    public ICommand ExportIpListCommand { get; }
    public ICommand ClearAllIpsCommand { get; }
    public ICommand ClearAllRangesCommand { get; }

    private System.Collections.ObjectModel.ObservableCollection<Peer> _discoveredPeers = new();
    public System.Collections.ObjectModel.ObservableCollection<Peer> DiscoveredPeers { get => _discoveredPeers; private set { _discoveredPeers = value; OnPropertyChanged(); } }
    public Peer? SelectedDiscoveredPeer { get; set; }
    
    // Track selected peers for multi-select operations
    private System.Collections.Generic.List<Peer> _selectedPeers = new();
    public System.Collections.Generic.List<Peer> SelectedPeers 
    { 
        get => _selectedPeers; 
        set 
        { 
            _selectedPeers = value; 
            OnPropertyChanged(); 
        } 
    }
    private System.Collections.ObjectModel.ObservableCollection<string> _blockedPeers = new();
    public System.Collections.ObjectModel.ObservableCollection<string> BlockedPeers { get => _blockedPeers; private set { _blockedPeers = value; OnPropertyChanged(); } }
    public string? SelectedBlockedPeer { get; set; }
    
    // IP Blocking Properties
    private System.Collections.ObjectModel.ObservableCollection<IpEntryWithCountry> _badActorIps = new();
    public System.Collections.ObjectModel.ObservableCollection<IpEntryWithCountry> BadActorIps { get => _badActorIps; private set { _badActorIps = value; OnPropertyChanged(); } }
    public IpEntryWithCountry? SelectedBadActorIp { get; set; }
    
    private string _newBadActorIp = string.Empty;
    public string NewBadActorIp { get => _newBadActorIp; set { _newBadActorIp = value; OnPropertyChanged(); (AddBadActorIpCommand as RelayCommand)?.RaiseCanExecuteChanged(); } }
    
    private System.Collections.ObjectModel.ObservableCollection<IpEntryWithCountry> _ipRanges = new();
    public System.Collections.ObjectModel.ObservableCollection<IpEntryWithCountry> IpRanges { get => _ipRanges; private set { _ipRanges = value; OnPropertyChanged(); } }
    public IpEntryWithCountry? SelectedIpRange { get; set; }
    
    private string _newIpRange = string.Empty;
    public string NewIpRange { get => _newIpRange; set { _newIpRange = value; OnPropertyChanged(); (AddIpRangeCommand as RelayCommand)?.RaiseCanExecuteChanged(); } }
    

    
    public string IpBlockingStats
    {
        get
        {
            var (individual, custom, ranges) = AppServices.IpBlocking.GetBlockingStats();
            return $"{individual + custom} IPs, {ranges} ranges";
        }
    }

    private System.Collections.ObjectModel.ObservableCollection<string> _knownMajorNodes = new();
    public System.Collections.ObjectModel.ObservableCollection<string> KnownMajorNodes { get => _knownMajorNodes; private set { _knownMajorNodes = value; OnPropertyChanged(); } }
    public string? SelectedMajorNode { get; set; }
    private string _newMajorNode = string.Empty;
    public string NewMajorNode { get => _newMajorNode; set { _newMajorNode = value; OnPropertyChanged(); (AddMajorNodeCommand as RelayCommand)?.RaiseCanExecuteChanged(); } }
    public ICommand AddMajorNodeCommand { get; }
    public ICommand RemoveMajorNodeCommand { get; }

    private int _intervalIndex;
    public int IntervalIndex { get => _intervalIndex; set { _intervalIndex = value; OnPropertyChanged(); OnIntervalChanged?.Invoke(value); } }
    public event Action<int>? OnIntervalChanged;
    public void OnPortsChanged() { }

    private string _tcpRate = "TCP: 0 B/s";
    public string TcpRate { get => _tcpRate; set { _tcpRate = value; OnPropertyChanged(); } }
    private string _udpRate = "UDP: 0 B/s";
    public string UdpRate { get => _udpRate; set { _udpRate = value; OnPropertyChanged(); } }
    private string _outRate = "Outbound local: 0 B/s";
    public string OutRate { get => _outRate; set { _outRate = value; OnPropertyChanged(); } }

    private Avalonia.Media.IBrush _tcpBrush = Avalonia.Media.Brushes.Gray;
    public Avalonia.Media.IBrush TcpBrush { get => _tcpBrush; set { _tcpBrush = value; OnPropertyChanged(); } }
    private Avalonia.Media.IBrush _udpBrush = Avalonia.Media.Brushes.Gray;
    public Avalonia.Media.IBrush UdpBrush { get => _udpBrush; set { _udpBrush = value; OnPropertyChanged(); } }
    private Avalonia.Media.IBrush _outBrush = Avalonia.Media.Brushes.Gray;
    public Avalonia.Media.IBrush OutBrush { get => _outBrush; set { _outBrush = value; OnPropertyChanged(); } }

    public string TcpPortLabel => AppServices.Network.ListeningPort is int p ? $"TCP: {p}" : "TCP: n/a";
    public string UdpPortLabel => AppServices.Network.UdpBoundPort is int p ? $"UDP: {p}" : "UDP: n/a";
    public string ExternalPortLabel
        => AppServices.Nat.MappedTcpPort is int tp && AppServices.Nat.MappedUdpPort is int up
            ? $"External: {tp} → {up}"
            : "External: n/a";

    private const int MaxLogItems = 500;
    private readonly System.Collections.Generic.LinkedList<string> _log = new();
    public System.Collections.ObjectModel.ObservableCollection<string> LogItems { get; } = new();
    public bool IsLogEmpty => LogItems.Count == 0;
    public void AppendLog(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        if (line.Length > 4096)
        {
            line = string.Concat(line.AsSpan(0, 4096), " …".AsSpan());
        }
        _log.AddLast(line);
        LogItems.Add(line);
        if (_log.Count > MaxLogItems)
        {
            _log.RemoveFirst();
            if (LogItems.Count > 0) LogItems.RemoveAt(0);
        }
        OnPropertyChanged(nameof(IsLogEmpty));
    }
    public void ClearLog()
    {
        _log.Clear();
        LogItems.Clear();
        OnPropertyChanged(nameof(IsLogEmpty));
    }
    private async System.Threading.Tasks.Task CopyAllAsync()
    {
        try
        {
            var text = string.Join(Environment.NewLine, LogItems);
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            if (lifetime?.MainWindow?.Clipboard != null)
                await lifetime.MainWindow.Clipboard.SetTextAsync(text);
        }
        catch { }
    }
    private async System.Threading.Tasks.Task CopySelectedAsync(object? selected)
    {
        try
        {
            if (selected is System.Collections.IEnumerable enumerable)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var item in enumerable)
                {
                    if (item is string s) sb.AppendLine(s);
                }
                var text = sb.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                    if (lifetime?.MainWindow?.Clipboard != null)
                        await lifetime.MainWindow.Clipboard.SetTextAsync(text);
                }
            }
        }
        catch { }
    }

    public event EventHandler? CloseRequested;
    private Action? _uiThrottled;
    private Action? _uiPulseHandler;

    private bool _saveToastVisible;
    public bool SaveToastVisible { get => _saveToastVisible; set { _saveToastVisible = value; OnPropertyChanged(); } }
    private string _saveToastText = "";
    public string SaveToastText { get => _saveToastText; set { _saveToastText = value; OnPropertyChanged(); } }
    private async System.Threading.Tasks.Task SaveAsync(bool showToast, bool close)
    {
        try
        {
            var s = _settings.Settings;
            s.Port = Port;
            s.MajorNode = MajorNode;
            s.EnableGeoBlocking = EnableGeoBlocking;
            _settings.Save(AppServices.Passphrase);
            // Networking lifecycle is handled by app-level service; notify via centralized event
            AppServices.Events.RaiseNetworkConfigChanged();
            if (s.MajorNode)
            {
                InfoMessage = "If prompted, allow ZTalk through Windows Firewall for inbound connections.";
            }
            if (showToast)
            {
                await ShowNetworkSaveToastAsync();
            }
            if (close)
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Network save error: {ex.Message}");
            ErrorMessage = "Failed to save network settings.";
        }
    }

    private async System.Threading.Tasks.Task ShowNetworkSaveToastAsync()
    {
        try
        {
            SaveToastText = "Settings saved";
            SaveToastVisible = true;
            await System.Threading.Tasks.Task.Delay(1500);
            SaveToastVisible = false;
        }
        catch { }
    }

    private void DiscardNetworkChanges()
    {
        try
        {
            var s = _settings.Settings;
            Port = s.Port;
            PortText = s.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            MajorNode = s.MajorNode;
            // Apply reverted values immediately so runtime matches persisted state
            ApplyNetworkChangeLiveIfNeeded();
        }
        catch { }
    }

    public string ErrorMessage { get => _errorMessage; set { _errorMessage = value; OnPropertyChanged(); } }
    public string InfoMessage { get => _infoMessage; set { _infoMessage = value; OnPropertyChanged(); } }

    public string TcpStatus
        => AppServices.Network.IsListening
            ? $"Listening on {AppServices.Network.PreferredBindAddress}:{AppServices.Network.ListeningPort} (UID {AppServices.Identity.UID})"
            : "Idle";
    public string UdpStatus => "Available";
    public string NatStatus => AppServices.Nat.Status;
    public string NatVerification => AppServices.Nat.MappingVerification;
    public string HairpinStatus => AppServices.Nat.HairpinStatus;

    // NAT status indicator (Monitoring tab)
    // - Flashing Yellow: searching for gateway or attempting mapping
    // - Solid Red: mapping failed or unreachable
    // - Solid Green: mapping succeeded and port is reachable
    private IBrush _natIndicatorBrush = Brushes.Gray;
    public IBrush NatIndicatorBrush { get => _natIndicatorBrush; set { _natIndicatorBrush = value; OnPropertyChanged(); } }
    private double _natIndicatorOpacity = 1.0;
    public double NatIndicatorOpacity { get => _natIndicatorOpacity; set { _natIndicatorOpacity = value; OnPropertyChanged(); } }
    private bool _natIndicatorBlink;
    public bool NatIndicatorBlink { get => _natIndicatorBlink; private set { _natIndicatorBlink = value; OnPropertyChanged(); } }

    // Live endpoint summaries for Monitoring tab
    public string ListeningEndpoint => AppServices.Network.IsListening
        ? $"{AppServices.Network.PreferredBindAddress}:{AppServices.Network.ListeningPort}"
        : "n/a";
    public string UdpEndpoint => AppServices.Network.UdpBoundPort is int up
        ? $"{AppServices.Network.UdpBoundAddress}:{up}"
        : "n/a";
    public string OutboundLocalPort => AppServices.Network.LastAutoClientPort?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "n/a";

    // Called by the monitor loop to refresh derived properties
    public void NotifyNetworkStatus()
    {
        OnPropertyChanged(nameof(TcpStatus));
        OnPropertyChanged(nameof(ListeningEndpoint));
        OnPropertyChanged(nameof(UdpEndpoint));
        OnPropertyChanged(nameof(OutboundLocalPort));
        OnPropertyChanged(nameof(TcpPortLabel));
        OnPropertyChanged(nameof(UdpPortLabel));
        OnPropertyChanged(nameof(ExternalPortLabel));
        OnPropertyChanged(nameof(NatStatus));
        OnPropertyChanged(nameof(NatVerification));
        // Compute the NAT indicator visual state from current NAT status and verification
        EvaluateNatIndicatorState();
    }

    // Determine indicator color and blinking behavior from NAT state
    private void EvaluateNatIndicatorState()
    {
        try
        {
            var status = NatStatus ?? string.Empty;
            var ver = NatVerification ?? string.Empty;
            var s = status.ToLowerInvariant();
            var v = ver.ToLowerInvariant();

            // Classify into buckets
            string rawBucket;
            if (s.Contains("discovering") || (s.Contains("gateway discovered") && string.IsNullOrWhiteSpace(v))) rawBucket = "Searching";
            else if (s.Contains("failed") || v.Contains("unreachable") || v.Contains("failed")) rawBucket = "Failed";
            else if (v.Contains("reachable") || v.Contains("ok") || (s.Contains("mapped") && !s.Contains("unmapped"))) rawBucket = "Mapped";
            else if (s.Contains("unmapped") || v.Contains("unmapped") || s.Contains("no gateway")) rawBucket = "Unmapped";
            else rawBucket = "Unknown";

            var now = DateTime.UtcNow;
            if (!string.Equals(_natRawBucket, rawBucket, StringComparison.Ordinal))
            {
                _natRawBucket = rawBucket;
                _natRawSince = now;
            }

            // Hysteresis: require 7s dwell before changing visible bucket
            var dwell = TimeSpan.FromSeconds(7);
            if (!string.Equals(_natVisibleBucket, _natRawBucket, StringComparison.Ordinal))
            {
                if ((now - _natRawSince) >= dwell)
                {
                    _natVisibleBucket = _natRawBucket;
                    _natVisibleSince = now;
                }
            }

            // Map visible bucket to visuals
            switch (_natVisibleBucket)
            {
                case "Searching":
                    NatIndicatorBrush = Brushes.Goldenrod; NatIndicatorBlink = true; NatIndicatorOpacity = 1.0; break;
                case "Failed":
                    NatIndicatorBrush = Brushes.IndianRed; NatIndicatorBlink = false; NatIndicatorOpacity = 1.0; break;
                case "Mapped":
                    NatIndicatorBrush = Brushes.LimeGreen; NatIndicatorBlink = false; NatIndicatorOpacity = 1.0; break;
                case "Unmapped":
                case "Unknown":
                default:
                    NatIndicatorBrush = Brushes.Gray; NatIndicatorBlink = false; NatIndicatorOpacity = 1.0; break;
            }
        }
        catch { }
    }

    // Hysteresis tracking for NAT indicator
    private string _natVisibleBucket = "Unknown";
    private string _natRawBucket = "Unknown";
    private DateTime _natVisibleSince = DateTime.MinValue;
    private DateTime _natRawSince = DateTime.MinValue;


    // Adapters
    // Adapter item now observable so the UI can update live without Save
    public class AdapterItem : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        public string Id { get => _id; set { if (_id != value) { _id = value; OnPropertyChanged(nameof(Id)); } } }

        private string _name = string.Empty;
        public string Name { get => _name; set { if (_name != value) { _name = value; OnPropertyChanged(nameof(Name)); } } }

        private string _address = string.Empty;
        public string Address { get => _address; set { if (_address != value) { _address = value; OnPropertyChanged(nameof(Address)); } } }

        private string _status = string.Empty; // Active/Inactive
        public string Status { get => _status; set { if (_status != value) { _status = value; OnPropertyChanged(nameof(Status)); } } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    private System.Collections.ObjectModel.ObservableCollection<AdapterItem> _adapters = new();
    public System.Collections.ObjectModel.ObservableCollection<AdapterItem> Adapters { get => _adapters; set { _adapters = value; OnPropertyChanged(); } }
    private AdapterItem? _selectedAdapter;
    public AdapterItem? SelectedAdapter
    {
        get => _selectedAdapter;
        set
        {
            if (!object.ReferenceEquals(_selectedAdapter, value))
            {
                _selectedAdapter = value;
                OnPropertyChanged();
                (MoveAdapterUpCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (MoveAdapterDownCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    private void LoadAdapters()
    {
        var list = new System.Collections.Generic.List<AdapterItem>();
        foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            var props = ni.GetIPProperties();
            var ip = "";
            foreach (var ua in props.UnicastAddresses)
            {
                if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                { ip = ua.Address.ToString(); break; }
            }
            var status = ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up ? "Active" : "Inactive";
            list.Add(new AdapterItem { Id = ni.Id, Name = ni.Name, Address = string.IsNullOrEmpty(ip) ? "(no IPv4)" : ip, Status = status });
        }
        // Apply persisted priority order if any
        var order = _settings.Settings.AdapterPriorityIds ?? new System.Collections.Generic.List<string>();
        if (order.Count > 0)
        {
            list.Sort((a, b) => GetIndex(order, a.Id).CompareTo(GetIndex(order, b.Id)));
        }
        Adapters = new System.Collections.ObjectModel.ObservableCollection<AdapterItem>(list);
    }
    private static int GetIndex(System.Collections.Generic.List<string> order, string id)
    {
        var idx = order.IndexOf(id);
        return idx < 0 ? int.MaxValue : idx;
    }
    private void MoveAdapter(int delta)
    {
        if (SelectedAdapter == null) return;
        var idx = Adapters.IndexOf(SelectedAdapter);
        if (idx < 0) return;
        var newIdx = idx + delta;
        if (newIdx < 0 || newIdx >= Adapters.Count) return;
        var item = SelectedAdapter;
        Adapters.Move(idx, newIdx);
        SelectedAdapter = item;
        OnPropertyChanged(nameof(Adapters));
    }
    private void SaveAdapters()
    {
        var ids = new System.Collections.Generic.List<string>();
        foreach (var a in Adapters) ids.Add(a.Id);
        _settings.Settings.AdapterPriorityIds = ids;
        _settings.Save(AppServices.Passphrase);
    }

    // Live refresh of adapter Address/Status without reordering or recreating the list.
    // Called from the window's monitor loop; keeps the Adapters tab up to date in real time.
    public void RefreshAdapterStatusLive()
    {
        try
        {
            var map = new System.Collections.Generic.Dictionary<string, (string Address, string Status)>();
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                var props = ni.GetIPProperties();
                var ip = "";
                foreach (var ua in props.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    { ip = ua.Address.ToString(); break; }
                }
                var status = ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up ? "Active" : "Inactive";
                map[ni.Id] = (string.IsNullOrEmpty(ip) ? "(no IPv4)" : ip, status);
            }

            foreach (var a in Adapters)
            {
                if (map.TryGetValue(a.Id, out var info))
                {
                    if (!string.Equals(a.Address, info.Address, StringComparison.Ordinal)) a.Address = info.Address;
                    if (!string.Equals(a.Status, info.Status, StringComparison.Ordinal)) a.Status = info.Status;
                }
            }
        }
        catch { }
    }

    private void RefreshLists()
    {
        try { ZTalk.Utilities.Logger.Log($"[NetworkViewModel] RefreshLists: Found {_peerManager.Peers.Count} peers"); } catch { }
        var allPeers = _peerManager.Peers.ToList();
        
        // Filter out simulated contacts from discovered peers - they shouldn't appear in the network discovery list
        var peers = allPeers.Where(p => !IsSimulatedContact(p.UID)).ToList();
        try { ZTalk.Utilities.Logger.Log($"[NetworkViewModel] RefreshLists: Filtered to {peers.Count} non-simulated peers (removed {allPeers.Count - peers.Count} simulated contacts)"); } catch { }
        
        var blocked = (_settings.Settings.BlockList ?? new System.Collections.Generic.List<string>()).ToList();
        var now = System.DateTime.UtcNow;
        
        // Set IsBlocked property on each peer and assign country codes from IP address with caching
        foreach (var peer in peers)
        {
            peer.IsBlocked = blocked.Contains(peer.UID);
            
            // Update LastSeenOnline if peer has public key (connected)
            if (peer.PublicKey != null && peer.PublicKey.Length > 0)
            {
                peer.LastSeenOnline = now;
            }
            
            // Derive country code from IP address with 30-minute cache after offline
            // Only show country if peer is connected (has public key) or cache is still valid
            var cacheExpired = peer.CountryCodeCachedAt == null || 
                               (peer.LastSeenOnline != null && (now - peer.LastSeenOnline.Value).TotalMinutes > 30);
            
            if (string.IsNullOrEmpty(peer.CountryCode) || cacheExpired)
            {
                if (peer.PublicKey != null && peer.PublicKey.Length > 0)
                {
                    // Peer is connected, derive country code
                    peer.CountryCode = GetCountryCodeFromIp(peer.Address);
                    peer.CountryCodeCachedAt = now;
                }
                else
                {
                    // Peer not connected yet or cache expired, show placeholder
                    peer.CountryCode = "⚪"; // Empty grey circle placeholder
                    peer.CountryCodeCachedAt = null;
                }
            }
        }
        
        DiscoveredPeers = new System.Collections.ObjectModel.ObservableCollection<Peer>(peers);
        BlockedPeers = new System.Collections.ObjectModel.ObservableCollection<string>(blocked);
        KnownMajorNodes = new System.Collections.ObjectModel.ObservableCollection<string>((_settings.Settings.KnownMajorNodes ?? new System.Collections.Generic.List<string>()).ToList());
        try { ZTalk.Utilities.Logger.Log($"[NetworkViewModel] RefreshLists: Updated UI with {peers.Count} discovered peers"); } catch { }
    }
    
    // Check if a peer UID corresponds to a simulated contact (should not appear in discovered peers)
    private static bool IsSimulatedContact(string uid)
    {
        try
        {
            var contacts = ZTalk.Services.AppServices.Contacts.Contacts;
            var contact = contacts.FirstOrDefault(c => string.Equals(c.UID, uid, StringComparison.OrdinalIgnoreCase));
            return contact?.IsSimulated == true;
        }
        catch { return false; }
    }

    private static string GetCountryCodeFromIp(string ipAddress)
    {
        // Derive country flag emoji from IP address using simple heuristics
        // This is a decorative hint based on IP range patterns, not accurate geolocation
        try
        {
            if (string.IsNullOrWhiteSpace(ipAddress)) return "🌍";
            
            // Extract IP if address contains port (e.g. "192.168.1.100:5000")
            string addressPart = ipAddress;
            if (ipAddress.Contains(':'))
            {
                var parts = ipAddress.Split(':');
                if (parts.Length > 0) addressPart = parts[0];
            }
            
            // Parse IP address
            if (!System.Net.IPAddress.TryParse(addressPart, out var ip)) return "🌍";
            
            var bytes = ip.GetAddressBytes();
            if (bytes.Length != 4) return "🌍"; // Only support IPv4 for now
            
            var firstOctet = bytes[0];
            var secondOctet = bytes[1];
            
            // Private/Local networks - show desktop PC emoji for same-network peers (check first!)
            if (firstOctet == 10) return "\U0001F5A5\uFE0F"; // 10.x.x.x - Desktop PC
            if (firstOctet == 192 && secondOctet == 168) return "\U0001F5A5\uFE0F"; // 192.168.x.x - Desktop PC
            if (firstOctet == 172 && secondOctet >= 16 && secondOctet <= 31) return "\U0001F5A5\uFE0F"; // 172.16-31.x.x - Desktop PC
            if (firstOctet == 127) return "\U0001F4BB"; // Localhost - Laptop
            
            // Simple heuristic based on common IP range patterns (not accurate, just decorative)
            // US: Large portions of early allocations
            if (firstOctet >= 3 && firstOctet <= 38) return "🇺🇸";
            if (firstOctet >= 40 && firstOctet <= 50) return "🇺🇸";
            if (firstOctet >= 63 && firstOctet <= 76) return "��";
            
            // EU regions
            if (firstOctet >= 77 && firstOctet <= 95) return "��"; // UK
            if (firstOctet >= 141 && firstOctet <= 145) return "🇩🇪"; // Germany
            if (firstOctet >= 151 && firstOctet <= 155) return "🇫🇷"; // France
            if (firstOctet >= 185 && firstOctet <= 188) return "🇳🇱"; // Netherlands
            
            // Asia-Pacific
            if (firstOctet >= 202 && firstOctet <= 203) return "��"; // China
            if (firstOctet >= 210 && firstOctet <= 211) return "��"; // Japan
            if (firstOctet >= 119 && firstOctet <= 125) return "��"; // Japan
            if (firstOctet >= 1 && firstOctet <= 2) return "�🇳"; // China
            if (firstOctet >= 58 && firstOctet <= 61) return "🇨🇳"; // China
            if (firstOctet >= 112 && firstOctet <= 115) return "🇰🇷"; // South Korea
            if (firstOctet == 49 || firstOctet == 50) return "��"; // South Korea
            if (firstOctet >= 103) return "🇮�"; // India
            if (firstOctet >= 139 && firstOctet <= 140) return "🇮🇳"; // India
            
            // Americas
            if (firstOctet >= 177 && firstOctet <= 181) return "🇧�"; // Brazil
            if (firstOctet >= 200 && firstOctet <= 201) return "��"; // Brazil
            if (firstOctet >= 142 && firstOctet <= 143) return "🇨�"; // Canada
            if (firstOctet >= 206 && firstOctet <= 209) return "🇨🇦"; // Canada
            
            // Oceania
            if (firstOctet >= 27 && firstOctet <= 29) return "🇦🇺"; // Australia
            if (firstOctet >= 101 && firstOctet <= 103) return "🇦🇺"; // Australia
            
            // Default: derive from hash for consistency per IP
            var hash = Math.Abs(ipAddress.GetHashCode());
            var flags = new[] { "🇺🇸", "🇬🇧", "🇨🇦", "🇩🇪", "🇫🇷", "🇯🇵", "🇦🇺", "🇧🇷", "🇮🇳", "🇨🇳", "🇰🇷", "🇪🇸", "🇮🇹", "🇳🇱", "🇸🇪", "🇨🇭" };
            return flags[hash % flags.Length];
        }
        catch
        {
            return "🌍"; // Unknown/Error
        }
    }
    
    private void BlockSelectedPeers()
    {
        // Block all peers in SelectedPeers list
        if (SelectedPeers == null || SelectedPeers.Count == 0) return;
        
        var uidsToBlock = SelectedPeers.Select(p => p.UID).ToList();
        foreach (var uid in uidsToBlock)
        {
            try
            {
                _peerManager.Block(uid);
            }
            catch { }
        }
        
        SelectedPeers.Clear();
        
        // Ensure UI update happens on UI thread
        Avalonia.Threading.Dispatcher.UIThread.Post(() => RefreshLists());
    }
    
    private void UnblockSelectedPeers()
    {
        // Unblock all peers in SelectedPeers list
        if (SelectedPeers == null || SelectedPeers.Count == 0) return;
        
        var uidsToUnblock = SelectedPeers.Select(p => p.UID).ToList();
        foreach (var uid in uidsToUnblock)
        {
            try
            {
                _peerManager.Unblock(uid);
            }
            catch { }
        }
        
        SelectedPeers.Clear();
        
        // Ensure UI update happens on UI thread
        Avalonia.Threading.Dispatcher.UIThread.Post(() => RefreshLists());
    }

    private static bool TryParseHostPort(string input, out string host, out int port)
    {
        host = string.Empty; port = 0;
        var parts = input.Trim().Split(':');
        if (parts.Length != 2) return false;
        host = parts[0].Trim();
        return int.TryParse(parts[1], out port) && port >= 1 && port <= 65535 && !string.IsNullOrWhiteSpace(host);
    }

    private void AddMajorNode()
    {
        if (!TryParseHostPort(NewMajorNode, out var host, out var port))
        {
            InfoMessage = "Enter a node as host:port (e.g., example.com:26264)";
            return;
        }
        var entry = $"{host}:{port}";
        var list = _settings.Settings.KnownMajorNodes ??= new System.Collections.Generic.List<string>();
        if (!list.Contains(entry))
        {
            list.Add(entry);
            _settings.Save(AppServices.Passphrase);
            KnownMajorNodes.Add(entry);
            NewMajorNode = string.Empty;
            // Optionally restart crawler for immediacy
            if (!ZTalk.Utilities.RuntimeFlags.SafeMode) AppServices.Crawler.Start();
        }
    }

    private void RemoveMajorNode(string node)
    {
        var list = _settings.Settings.KnownMajorNodes ??= new System.Collections.Generic.List<string>();
        if (list.Remove(node))
        {
            _settings.Save(AppServices.Passphrase);
            KnownMajorNodes.Remove(node);
            if (!ZTalk.Utilities.RuntimeFlags.SafeMode) AppServices.Crawler.Start();
        }
    }

    private async void ConfirmUnblock(string uid)
    {
        var ok = await AppServices.Dialogs.ConfirmWarningAsync(
            "Unblock Peer",
            "You are about to unblock a peer previously flagged for misbehavior. Proceed only if you trust this node. Unblocking may expose you to spam or malformed traffic.",
            "Unblock",
            "Cancel");
        if (ok)
        {
            _peerManager.Unblock(uid);
            RefreshLists();
        }
    }

    private async void ConfirmClearAll()
    {
        var ok = await AppServices.Dialogs.ConfirmWarningAsync(
            "Clear All Blocked Peers",
            "This will remove all blocked peers and may expose you to spam or malformed traffic. Continue?",
            "Clear All",
            "Cancel");
        if (ok)
        {
            _peerManager.ClearAllBlocks();
            RefreshLists();
        }
    }

    #region IP Blocking Methods

    private void AddBadActorIp()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(NewBadActorIp)) return;
            
            AppServices.IpBlocking.AddBadActorIp(NewBadActorIp.Trim());
            NewBadActorIp = string.Empty;
            RefreshIpLists();
            ZTalk.Utilities.Logger.Log($"[IP-BLOCK] Added bad actor IP via UI: {NewBadActorIp}");
        }
        catch (Exception ex)
        {
            _ = AppServices.Dialogs.ShowInfoAsync("Add IP Error", ex.Message);
        }
    }

    private void RemoveBadActorIp(string ip)
    {
        try
        {
            AppServices.IpBlocking.RemoveBadActorIp(ip);
            RefreshIpLists();
            ZTalk.Utilities.Logger.Log($"[IP-BLOCK] Removed bad actor IP via UI: {ip}");
        }
        catch (Exception ex)
        {
            _ = AppServices.Dialogs.ShowInfoAsync("Remove IP Error", ex.Message);
        }
    }

    private void AddIpRange()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(NewIpRange)) return;
            
            AppServices.IpBlocking.AddBlockedIpRange(NewIpRange.Trim());
            NewIpRange = string.Empty;
            RefreshIpLists();
            ZTalk.Utilities.Logger.Log($"[IP-BLOCK] Added IP range via UI: {NewIpRange}");
        }
        catch (Exception ex)
        {
            _ = AppServices.Dialogs.ShowInfoAsync("Add IP Range Error", ex.Message);
        }
    }

    private void RemoveIpRange(string range)
    {
        try
        {
            AppServices.IpBlocking.RemoveBlockedIpRange(range);
            RefreshIpLists();
            ZTalk.Utilities.Logger.Log($"[IP-BLOCK] Removed IP range via UI: {range}");
        }
        catch (Exception ex)
        {
            _ = AppServices.Dialogs.ShowInfoAsync("Remove IP Range Error", ex.Message);
        }
    }

    private async Task ImportIpListAsync()
    {
        try
        {
            // Check multiple possible locations for IP block lists
            var appDataPath = ZTalk.Utilities.AppDataPaths.Combine("security", "ip-blocklist.txt");
            var desktopPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ip-blocklist.txt");
            var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "ip-blocklist.txt");
            
            string? foundPath = null;
            if (System.IO.File.Exists(appDataPath)) foundPath = appDataPath;
            else if (System.IO.File.Exists(desktopPath)) foundPath = desktopPath;
            else if (System.IO.File.Exists(downloadsPath)) foundPath = downloadsPath;
            
            if (foundPath != null)
            {
                var imported = await AppServices.IpBlocking.ImportIpListFromFileAsync(foundPath);
                RefreshIpLists();
                await AppServices.Dialogs.ShowInfoAsync("Import Complete", $"Imported {imported} IP addresses and ranges from:\n{foundPath}");
            }
            else
            {
                // Ensure the security directory exists
                var securityDir = ZTalk.Utilities.AppDataPaths.Combine("security");
                Directory.CreateDirectory(securityDir);
                
                await AppServices.Dialogs.ShowInfoAsync("Import IP Block Lists", 
                    "To import IP block lists:\n\n" +
                    "1. Download from security providers:\n" +
                    "   • Spamhaus DROP/EDROP lists\n" +
                    "   • FireHOL comprehensive lists\n" +
                    "   • abuse.ch malware/botnet IPs\n" +
                    "   • Commercial threat intelligence\n\n" +
                    "2. Save the file as 'ip-blocklist.txt' in:\n" +
                    $"   {appDataPath}\n" +
                    "   OR on your Desktop\n" +
                    "   OR in your Downloads folder\n\n" +
                    "3. Click Import again\n\n" +
                    "Format: One IP/CIDR per line, # for comments");
            }
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowInfoAsync("Import Error", ex.Message);
        }
    }

    private async Task ExportIpListAsync()
    {
        try
        {
            // Create security directory if it doesn't exist
            var securityDir = ZTalk.Utilities.AppDataPaths.Combine("security");
            Directory.CreateDirectory(securityDir);
            
            // Export with timestamp
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm", System.Globalization.CultureInfo.InvariantCulture);
            var exportPath = Path.Combine(securityDir, $"ztalk-ip-blocklist-{timestamp}.txt");
            
            await AppServices.IpBlocking.ExportIpListToFileAsync(exportPath);
            await AppServices.Dialogs.ShowInfoAsync("Export Complete", 
                $"IP block list exported to:\n{exportPath}\n\n" +
                "You can share this file or use it as backup.");
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowInfoAsync("Export Error", ex.Message);
        }
    }

    private async void ClearAllIps()
    {
        var ok = await AppServices.Dialogs.ConfirmDestructiveAsync(
            "Clear All Blocked IPs",
            "This will remove ALL blocked individual IP addresses from your block list.\n\n" +
            "This action cannot be undone. Your IP ranges will remain intact.\n\n" +
            "Are you sure you want to continue?",
            "Clear All IPs",
            "Cancel");
            
        if (ok)
        {
            try
            {
                AppServices.IpBlocking.ClearAllBadActorIps();
                RefreshIpLists();
                ZTalk.Utilities.Logger.Log("[IP-BLOCK] Cleared all bad actor IPs via UI");
                await AppServices.Dialogs.ShowInfoAsync("Clear Complete", "All blocked individual IPs have been removed.");
            }
            catch (Exception ex)
            {
                await AppServices.Dialogs.ShowInfoAsync("Clear Error", ex.Message);
            }
        }
    }

    private async void ClearAllRanges()
    {
        var ok = await AppServices.Dialogs.ConfirmDestructiveAsync(
            "Clear All Blocked IP Ranges",
            "This will remove ALL blocked IP ranges (CIDR blocks) from your block list.\n\n" +
            "This includes both custom ranges and imported threat intelligence lists.\n" +
            "This action cannot be undone. Your individual IPs will remain intact.\n\n" +
            "Are you sure you want to continue?",
            "Clear All Ranges",
            "Cancel");
            
        if (ok)
        {
            try
            {
                AppServices.IpBlocking.ClearAllBlockedRanges();
                RefreshIpLists();
                ZTalk.Utilities.Logger.Log("[IP-BLOCK] Cleared all IP ranges via UI");
                await AppServices.Dialogs.ShowInfoAsync("Clear Complete", "All blocked IP ranges have been removed.");
            }
            catch (Exception ex)
            {
                await AppServices.Dialogs.ShowInfoAsync("Clear Error", ex.Message);
            }
        }
    }



    private void RefreshIpLists()
    {
        try
        {
            var settings = _settings.Settings;
            
            BadActorIps.Clear();
            if (settings.BlockedIpAddresses != null)
            {
                foreach (var ip in settings.BlockedIpAddresses)
                {
                    var countryCode = IpCountryDetector.DetectCountryCode(ip);
                    var countryName = IpCountryDetector.GetCountryName(countryCode);
                    BadActorIps.Add(new IpEntryWithCountry { IpOrRange = ip, CountryCode = countryCode, CountryName = countryName });
                }
            }
            if (settings.CustomBadActorIps != null)
            {
                foreach (var ip in settings.CustomBadActorIps)
                {
                    var countryCode = IpCountryDetector.DetectCountryCode(ip);
                    var countryName = IpCountryDetector.GetCountryName(countryCode);
                    BadActorIps.Add(new IpEntryWithCountry { IpOrRange = ip, CountryCode = countryCode, CountryName = countryName });
                }
            }
            
            IpRanges.Clear();
            if (settings.BlockedIpRanges != null)
            {
                foreach (var range in settings.BlockedIpRanges)
                {
                    var countryCode = IpCountryDetector.DetectCountryCode(range);
                    var countryName = IpCountryDetector.GetCountryName(countryCode);
                    IpRanges.Add(new IpEntryWithCountry { IpOrRange = range, CountryCode = countryCode, CountryName = countryName });
                }
            }
            
            OnPropertyChanged(nameof(IpBlockingStats));
        }
        catch (Exception ex)
        {
            ZTalk.Utilities.Logger.Log($"[IP-BLOCK] Error refreshing IP lists: {ex.Message}");
        }
    }



    #endregion

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // Best-effort cleanup (when VM is swapped) to avoid rare leaks if long-lived
    ~NetworkViewModel()
    {
        try { if (_uiPulseHandler != null) AppServices.Events.UiPulse -= _uiPulseHandler; } catch { }
        try { AppServices.Updates.UnregisterUi("NetworkViewModel.UI.throttle"); } catch { }
    }
}
