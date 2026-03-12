using System;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using Zer0Talk.Models;
using Models = Zer0Talk.Models;
using Zer0Talk.Services;
using Zer0Talk.Utilities;

namespace Zer0Talk.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SettingsService _settings;
    private readonly ThemeService _themeService = AppServices.Theme;
    private string _errorMessage = string.Empty;
    private bool _rememberPassphrase;
    // Performance: detected CPU/GPU capabilities (static for session)
    private int _detectedCcdCount = 1; // 1 = single-CCD assumed by default
    private bool _isAmdX3D;
    private bool _isAmdCpu;
    private bool _isIntelCpu;
    public bool IsAmdCpu => _isAmdCpu;
    public bool IsIntelCpu => _isIntelCpu;
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
    private bool _intelPCoreTargeting;
    public bool IntelPCoreTargeting
    {
        get => _intelPCoreTargeting;
        set
        {
            if (_intelPCoreTargeting != value)
            {
                _intelPCoreTargeting = value;
                OnPropertyChanged();
                try { ApplyIntelPCoreTargetingImmediate(value); } catch { }
                try { WritePerformanceLog($"Change IntelPCoreTargeting={value}"); } catch { }
            }
        }
    }
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
    private static readonly System.Collections.Generic.HashSet<string> DirtyTrackedProperties = new(System.StringComparer.Ordinal)
    {
        nameof(DisplayName),
        nameof(ShareAvatar),
        nameof(Bio),
        nameof(AvatarPreview),
        nameof(ThemeIndex),
        nameof(SelectedThemeId),
        nameof(RememberPassphrase),
        nameof(UiFontFamily),
        nameof(Language),
        nameof(DefaultPresenceIndex),
        nameof(AllowAutoUpdates),
        nameof(EnableSmoothScrolling),
        nameof(SuppressNotificationsInDnd),
        nameof(NotificationDurationSeconds),
        nameof(EnableNotificationBellFlash),
        nameof(AutoLockEnabled),
        nameof(AutoLockMinutes),
        nameof(LockOnMinimize),
        nameof(LockBlurRadius),
        nameof(BlockScreenCapture),
        nameof(ShowPublicKeys),
        nameof(StreamerMode),
        nameof(ShowKeyboardFocus),
        nameof(EnhancedKeyboardNavigation),
        nameof(ShowInSystemTray),
        nameof(MinimizeToTray),
        nameof(RunOnStartup),
        nameof(StartMinimized),
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
        nameof(ChatVolume),
        nameof(Port),
        nameof(MajorNode),
        nameof(EnableGeoBlocking),
        nameof(RelayFallbackEnabled),
        nameof(RelayServer),
        nameof(RelayPresenceTimeoutSeconds),
        nameof(RelayDiscoveryTtlMinutes),
        nameof(ForceSeedBootstrap)
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
    private System.Threading.Timer? _autoSaveTimer;
    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set
        {
            if (_hasUnsavedChanges == value) return;
            _hasUnsavedChanges = value;
            OnPropertyChanged();
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
    private int _basePort;
    private bool _baseMajorNode;
    private bool _baseEnableGeoBlocking;
    private bool _baseRelayFallbackEnabled;
    private string _baseRelayServer = string.Empty;
    private int _baseRelayPresenceTimeoutSeconds;
    private int _baseRelayDiscoveryTtlMinutes;
    private string _baseSavedRelayServersSig = string.Empty;
    private bool _baseForceSeedBootstrap;
    private string _baseWanSeedNodesSig = string.Empty;
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
        try { Services.AppServices.Localization.LanguageChanged -= OnLanguageChanged; } catch { }
        try { NetworkVm.PropertyChanged -= OnNetworkVmPropertyChanged; } catch { }
        try { NetworkVm.SavedRelayServers.CollectionChanged -= OnSavedRelayServersChanged; } catch { }
        DismissSaveToast();
        try { _autoSaveTimer?.Dispose(); _autoSaveTimer = null; } catch { }
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
    private bool _baseAllowAutoUpdates;
    private bool _baseEnableSmoothScrolling;
    private bool _baseSuppressNotificationsInDnd;
    private double _baseNotificationDurationSeconds;
    private bool _baseEnableNotificationBellFlash;
    private bool _baseAutoLockEnabled;
    private int _baseAutoLockMinutes;
    private bool _baseLockOnMinimize;
    private int _baseLockBlurRadius;
    private bool _baseBlockScreenCapture;
    private bool _baseShowPublicKeys;
    private bool _baseStreamerMode;
    private Avalonia.Input.Key _baseLockHotkeyKey;
    private Avalonia.Input.KeyModifiers _baseLockHotkeyModifiers;
    // Accessibility properties removed - these are OS-level settings that the app cannot control
    // High Contrast, Reduce Motion, Cursor settings must be configured through Windows Settings
    private bool _baseShowKeyboardFocus;
    private bool _baseEnhancedKeyboardNavigation;
    private bool _baseShowInSystemTray;
    private bool _baseMinimizeToTray;
    private bool _baseRunOnStartup;
    private bool _baseStartMinimized;
    private bool _suppressThemeBinding = true;

    public SettingsViewModel()
    {
        _settings = AppServices.Settings;
        _suppressDirtyCheck = true;
        _suppressThemeBinding = true;

        // Subscribe to theme reload events to refresh dropdown when themes change
        Services.ThemeEngine.ThemesReloaded += OnThemesReloaded;

        // Populate available languages from localization files
        try { PopulateAvailableLanguages(); }
        catch { }

        // Populate theme and presence items
        try { PopulateThemeItems(); }
        catch { }
        try { PopulatePresenceItems(); }
        catch { }

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
            
            // Initialize LocalizationService with saved language
            try
            {
                var langCode = GetLanguageCode(Language);
                Services.AppServices.Localization.LoadLanguage(langCode);
            }
            catch { }
            
            // Subscribe to language changes to refresh localized properties
            try
            {
                Services.AppServices.Localization.LanguageChanged += OnLanguageChanged;
            }
            catch { }
            
            LockBlurRadius = ClampRange(settings.LockBlurRadius, 0, 10);
            ShowKeyboardFocus = settings.ShowKeyboardFocus;
            EnhancedKeyboardNavigation = settings.EnhancedKeyboardNavigation;
            DefaultPresenceIndex = PresenceToIndex(settings.Status);
            AllowAutoUpdates = settings.AutoUpdateEnabled;
            EnableSmoothScrolling = settings.EnableSmoothScrolling;
            UpdateLastAutoUpdateCheckDisplay(settings.LastAutoUpdateCheckUtc);
            SuppressNotificationsInDnd = settings.SuppressNotificationsInDnd;
            NotificationDurationSeconds = Math.Clamp(settings.NotificationDurationSeconds, 0.5, 30.0);
            EnableNotificationBellFlash = settings.EnableNotificationBellFlash;
            AutoLockEnabled = settings.AutoLockEnabled;
            AutoLockMinutes = Math.Max(0, settings.AutoLockMinutes);
            LockOnMinimize = settings.LockOnMinimize;
            BlockScreenCapture = settings.BlockScreenCapture;
            ShowPublicKeys = settings.ShowPublicKeys;
            ShowInSystemTray = settings.ShowInSystemTray;
            MinimizeToTray = settings.MinimizeToTray;
            RunOnStartup = settings.RunOnStartup;
            StartMinimized = settings.StartMinimized;

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

            try
            {
                _streamerModeHotkeyKey = (Avalonia.Input.Key)settings.StreamerModeHotkeyKey;
                _streamerModeHotkeyModifiers = (Avalonia.Input.KeyModifiers)settings.StreamerModeHotkeyModifiers;
            }
            catch
            {
                _streamerModeHotkeyKey = Avalonia.Input.Key.F7;
                _streamerModeHotkeyModifiers = Avalonia.Input.KeyModifiers.Control;
            }

            StreamerMode = settings.StreamerMode;

            CcdAffinityIndex = ClampRange(settings.CcdAffinityIndex, 0, 3);
            _intelPCoreTargeting = settings.IntelPCoreTargeting;
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
            _debugLogMaxMegabytes = ClampRange(settings.DebugLogMaxMegabytes <= 0 ? 16 : settings.DebugLogMaxMegabytes, 1, 512);
            SyncDebugLogMegabytesToSize();
            EnableLogging = settings.EnableLogging;

            var persistedThemeId = NormalizeThemeId(settings.ThemeId);
            if (string.IsNullOrWhiteSpace(persistedThemeId))
            {
                persistedThemeId = ThemeOptionToId(settings.Theme);
            }

            _selectedThemeId = string.IsNullOrWhiteSpace(persistedThemeId) ? "legacy-dark" : persistedThemeId;
            _baseThemeId = _selectedThemeId;

            SetSelectedThemeId(_selectedThemeId, updateIndex: true, triggerChange: false, refreshInspector: false);
            _baseThemeIndex = _themeIndex;
        }
        else
        {
            _selectedThemeId = "legacy-dark";
            _baseThemeId = _selectedThemeId;
            SetSelectedThemeId(_selectedThemeId, updateIndex: true, triggerChange: false, refreshInspector: false);
            _baseThemeIndex = _themeIndex;
            EnableDebugLogAutoTrim = true;
            DebugUiLogMaxLines = 1000;
            DebugLogRetentionDays = 1;
            _debugLogMaxMegabytes = 16;
            SyncDebugLogMegabytesToSize();
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
        AssignRandomBundledAvatarCommand = new RelayCommand(_ => AssignRandomBundledAvatar(), _ => true);
        DeleteAccountCommand = new RelayCommand(_ => DeleteAccount(), _ => !string.IsNullOrWhiteSpace(DeleteConfirmText) && string.Equals(DeleteConfirmText.Trim(), GeneratedDeleteCode, StringComparison.Ordinal));
        ResetLayoutCommand = new RelayCommand(_ => ResetLayout(), _ => true);
        RunLogMaintenanceCommand = new RelayCommand(async _ => await RunLogMaintenanceAsync(), _ => true);
        CheckForUpdatesNowCommand = new RelayCommand(async _ => await CheckForUpdatesNowAsync(), _ => true);
        OpenDocumentationCommand = new RelayCommand(_ => OpenDocumentation(), _ => true);
        OpenPrivacyPolicyCommand = new RelayCommand(_ => OpenPrivacyPolicy(), _ => true);
        ExportDataCommand = new RelayCommand(async _ => await ExportDataAsync(), _ => true);
        ImportDataCommand = new RelayCommand(async _ => await ImportDataAsync(), _ => true);
        ExportMigrationBundleCommand = new RelayCommand(async _ => await ExportMigrationBundleAsync(), _ => true);
        CopyPublicKeyCommand = new RelayCommand(async _ => await CopyPublicKeyAsync(), _ => !string.IsNullOrWhiteSpace(SelfPublicKeyHex));
        ClearErrorLogCommand = new RelayCommand(_ => PurgeAllLogs(), _ => true);
        ExportThemeCommand = new RelayCommand(async _ => await ExportCurrentThemeAsync(), _ => !string.IsNullOrWhiteSpace(CurrentThemeId));
        ImportThemeCommand = new RelayCommand(async _ => await ImportThemeAsync(), _ => true);
        EditColorCommand = new RelayCommand(param => StartEditingColor(param as ThemeColorEntry), param => param is ThemeColorEntry && !IsEditingColor);
        SaveColorEditCommand = new RelayCommand(async _ => await SaveColorEditAsync(), _ => IsEditingColor);
        CancelColorEditCommand = new RelayCommand(_ => CancelColorEdit(), _ => IsEditingColor);
        UndoColorEditCommand = new RelayCommand(_ => UndoColorEdit(), _ => CanUndo && !IsEditingColor);
        RedoColorEditCommand = new RelayCommand(_ => RedoColorEdit(), _ => CanRedo && !IsEditingColor);
        ToggleBatchEditModeCommand = new RelayCommand(_ => ToggleBatchEditMode(), _ => !IsEditingColor);
        SelectAllColorsCommand = new RelayCommand(_ => SelectAllColors(), _ => IsBatchEditMode);
        DeselectAllColorsCommand = new RelayCommand(_ => DeselectAllColors(), _ => IsBatchEditMode && HasSelectedColors);
        CopyColorCommand = new RelayCommand(param => CopyColor(param as ThemeColorEntry), param => param is ThemeColorEntry);
        PasteColorCommand = new RelayCommand(param => PasteColor(param as ThemeColorEntry), param => param is ThemeColorEntry && HasCopiedColor);
        RevertAllEditsCommand = new RelayCommand(async _ => await RevertAllEditsAsync(), _ => CanUndo);
        ApplyThemeLiveCommand = new RelayCommand(async _ => await ApplyThemeLiveAsync(), _ => CanUndo);
        EditGradientCommand = new RelayCommand(param => StartEditingGradient(param as ThemeGradientEntry), param => param is ThemeGradientEntry && !IsEditingGradient && !IsEditingColor);
        SaveGradientEditCommand = new RelayCommand(async _ => await SaveGradientEditAsync(), _ => IsEditingGradient);
        CancelGradientEditCommand = new RelayCommand(_ => CancelGradientEdit(), _ => IsEditingGradient);
        ApplyGradientPresetCommand = new RelayCommand(param => ApplyGradientPreset(param as GradientPreset), param => param is GradientPreset && IsEditingGradient);
        RenameThemeCommand = new RelayCommand(async _ => await RenameThemeAsync(), _ => !IsEditingColor && !IsEditingGradient && !IsEditingMetadata);
        DuplicateThemeCommand = new RelayCommand(async _ => await DuplicateThemeAsync(), _ => !IsEditingColor && !IsEditingGradient && !IsEditingMetadata);
        DeleteThemeCommand = new RelayCommand(async _ => await DeleteThemeAsync(), _ => !IsEditingColor && !IsEditingGradient && !IsEditingMetadata);
        EditMetadataCommand = new RelayCommand(_ => StartEditingMetadata(), _ => !IsEditingColor && !IsEditingGradient && !IsEditingMetadata);
        SaveMetadataCommand = new RelayCommand(async _ => await SaveMetadataAsync(), _ => IsEditingMetadata);
        CancelMetadataEditCommand = new RelayCommand(_ => CancelMetadataEdit(), _ => IsEditingMetadata);
        ExportModifiedThemeCommand = new RelayCommand(async _ => await ExportModifiedThemeAsync(), _ => CanUndo && !IsEditingColor && !IsEditingGradient);
        NewFromBlankTemplateCommand = new RelayCommand(async _ => await NewFromBlankTemplateAsync(), _ => !IsEditingColor && !IsEditingGradient && !IsEditingMetadata);
        SaveAsCommand = new RelayCommand(async _ => await SaveAsAsync(), _ => !IsEditingColor && !IsEditingGradient && !IsEditingMetadata);
        LoadFromLegacyThemeCommand = new RelayCommand(async _ => await LoadFromLegacyThemeAsync(), _ => SelectedLegacyTheme != null && !IsEditingColor && !IsEditingGradient && !IsEditingMetadata);

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
                // Initialize theme inspector with current theme
                RefreshThemeInspector();
            });
        }
        catch
        {
            _suppressThemeBinding = false;
            OnPropertyChanged(nameof(ThemeIndex));
            // Initialize theme inspector with current theme
            try { RefreshThemeInspector(); } catch { }
        }

        try { AttachNetworkDirtyTracking(); } catch { }

        CaptureBaseline();
        _suppressDirtyCheck = false;
        UpdateUnsavedChangesState();
    }

    private void AttachNetworkDirtyTracking()
    {
        var vm = NetworkVm;
        vm.PropertyChanged -= OnNetworkVmPropertyChanged;
        vm.PropertyChanged += OnNetworkVmPropertyChanged;

        TryRewireNetworkCollection(vm.SavedRelayServers, OnSavedRelayServersChanged);
        TryRewireNetworkCollection(vm.WanSeedNodes, OnWanSeedNodesChanged);
    }

    private void TryRewireNetworkCollection(System.Collections.Specialized.INotifyCollectionChanged collection, System.Collections.Specialized.NotifyCollectionChangedEventHandler handler)
    {
        try { collection.CollectionChanged -= handler; } catch { }
        try { collection.CollectionChanged += handler; } catch { }
    }

    private void OnSavedRelayServersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_suppressDirtyCheck) return;
        UpdateUnsavedChangesState();
    }

    private void OnWanSeedNodesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_suppressDirtyCheck) return;
        UpdateUnsavedChangesState();
    }

    private void OnNetworkVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var name = e.PropertyName;
        if (string.IsNullOrWhiteSpace(name)) return;

        if (name is nameof(NetworkViewModel.SavedRelayServers))
        {
            var vm = NetworkVm;
            TryRewireNetworkCollection(vm.SavedRelayServers, OnSavedRelayServersChanged);
            OnPropertyChanged(nameof(SavedRelayServers));
            return;
        }

        if (name is nameof(NetworkViewModel.WanSeedNodes))
        {
            var vm = NetworkVm;
            TryRewireNetworkCollection(vm.WanSeedNodes, OnWanSeedNodesChanged);
            OnPropertyChanged(nameof(WanSeedNodes));
            return;
        }

        if (name is nameof(NetworkViewModel.Port)
            or nameof(NetworkViewModel.MajorNode)
            or nameof(NetworkViewModel.EnableGeoBlocking)
            or nameof(NetworkViewModel.RelayFallbackEnabled)
            or nameof(NetworkViewModel.RelayServer)
            or nameof(NetworkViewModel.RelayPresenceTimeoutSeconds)
            or nameof(NetworkViewModel.RelayDiscoveryTtlMinutes)
            or nameof(NetworkViewModel.ForceSeedBootstrap)
            or nameof(NetworkViewModel.NewRelayServer)
            or nameof(NetworkViewModel.SelectedRelayServer)
            or nameof(NetworkViewModel.NewWanSeedNode)
            or nameof(NetworkViewModel.SelectedWanSeedNode)
            or nameof(NetworkViewModel.IpBlockingStats)
            or nameof(NetworkViewModel.InfoMessage)
            or nameof(NetworkViewModel.ErrorMessage))
        {
            OnPropertyChanged(name);
            if (name == nameof(NetworkViewModel.InfoMessage)) OnPropertyChanged(nameof(NetworkInfoMessage));
            if (name == nameof(NetworkViewModel.ErrorMessage)) OnPropertyChanged(nameof(NetworkErrorMessage));
        }

        if (_suppressDirtyCheck) return;
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

            SetSelectedThemeId(_baseThemeId, updateIndex: true, triggerChange: true);
            RememberPassphrase = _baseRememberPassphrase;
            UiFontFamily = string.IsNullOrWhiteSpace(s.UiFontFamily) ? null : s.UiFontFamily;
            LockBlurRadius = ClampRange(s.LockBlurRadius, 0, 10);
            DefaultPresenceIndex = PresenceToIndex(s.Status);
            AllowAutoUpdates = s.AutoUpdateEnabled;
            EnableSmoothScrolling = s.EnableSmoothScrolling;
            UpdateLastAutoUpdateCheckDisplay(s.LastAutoUpdateCheckUtc);
            SuppressNotificationsInDnd = s.SuppressNotificationsInDnd;
            NotificationDurationSeconds = Math.Clamp(s.NotificationDurationSeconds, 0.5, 30.0);
            AutoLockEnabled = s.AutoLockEnabled;
            AutoLockMinutes = Math.Max(0, s.AutoLockMinutes);
            LockOnMinimize = s.LockOnMinimize;
            ShowPublicKeys = s.ShowPublicKeys;
            BlockScreenCapture = s.BlockScreenCapture;
            StreamerMode = s.StreamerMode;
            ShowInSystemTray = s.ShowInSystemTray;
            MinimizeToTray = s.MinimizeToTray;
            RunOnStartup = s.RunOnStartup;
            StartMinimized = s.StartMinimized;
            EnableDebugLogAutoTrim = s.EnableDebugLogAutoTrim;
            DebugUiLogMaxLines = ClampRange(s.DebugUiLogMaxLines <= 0 ? 1000 : s.DebugUiLogMaxLines, 100, 20000);
            DebugLogRetentionDays = s.DebugLogRetentionDays < 0 ? 0 : (s.DebugLogRetentionDays > 30 ? 30 : s.DebugLogRetentionDays);
            _debugLogMaxMegabytes = ClampRange(s.DebugLogMaxMegabytes <= 0 ? 16 : s.DebugLogMaxMegabytes, 1, 512);
            SyncDebugLogMegabytesToSize();
            EnableLogging = s.EnableLogging;

            CcdAffinityIndex = ClampRange(s.CcdAffinityIndex, 0, 3);
            _intelPCoreTargeting = s.IntelPCoreTargeting;
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

            try { NetworkVm.ResetFromSettings(); } catch { }

            CaptureBaseline();
            try { LogSettingsEvent($"Discarded changes (ThemeId back to {_baseThemeId}, index {_baseThemeIndex})"); }
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
    public string AppName => "Zer0Talk";
    public string AppVersion => Zer0Talk.AppInfo.Version;
#if DEBUG
    public string AppBuildConfiguration => "Debug";
#else
    public string AppBuildConfiguration => "Release";
#endif
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
                try { Zer0Talk.Services.FocusFramerateService.ApplyCurrentPolicy(); } catch { }
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
            _isAmdCpu = ident.Contains("AMD", StringComparison.OrdinalIgnoreCase);
            _isIntelCpu = ident.Contains("Intel", StringComparison.OrdinalIgnoreCase);
            _isAmdX3D = _isAmdCpu && ident.Contains("X3D", StringComparison.OrdinalIgnoreCase);
        }
        catch { _isAmdX3D = false; _isAmdCpu = false; _isIntelCpu = false; }
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
        OnPropertyChanged(nameof(IsAmdCpu));
        OnPropertyChanged(nameof(IsIntelCpu));
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

    private void ApplyIntelPCoreTargetingImmediate(bool enable)
    {
        try
        {
            if (!_isIntelCpu) return;
            if (!OperatingSystem.IsWindows()) return;

            int logicalCores = Math.Max(1, Environment.ProcessorCount);
            ulong allMask = BuildMask(0, logicalCores);

            if (!enable)
            {
                SetProcessAffinity(allMask);
                WritePerformanceLogSafe($"[Intel P-Core] Disabled → mask=0x{allMask:X} (all cores)");
                return;
            }

            // Intel hybrid CPUs (12th gen+): P-cores are typically the lower-numbered logical cores,
            // E-cores are higher-numbered. Heuristic: use first half of cores as P-core estimate.
            // For a 24-thread CPU (8P+8E = 16P-threads + 8E-threads), prefer the first 16.
            int pCoreCount = DetectIntelPCoreCount(logicalCores);
            if (pCoreCount <= 0 || pCoreCount >= logicalCores)
            {
                // Non-hybrid or detection failed — use all cores
                SetProcessAffinity(allMask);
                WritePerformanceLogSafe($"[Intel P-Core] Non-hybrid or detection failed (pCores={pCoreCount}) → mask=0x{allMask:X}");
                return;
            }

            // P-cores occupy the lower logical processor indices on Intel hybrid architectures
            ulong pCoreMask = BuildMask(0, pCoreCount);
            if (pCoreMask == 0) pCoreMask = allMask;
            SetProcessAffinity(pCoreMask);
            WritePerformanceLogSafe($"[Intel P-Core] Targeting {pCoreCount} P-core threads → mask=0x{pCoreMask:X}");
        }
        catch (Exception ex)
        {
            try { WritePerformanceLogSafe($"[Intel P-Core] Error: {ex.Message}"); } catch { }
        }
    }

    private static int DetectIntelPCoreCount(int logicalCores)
    {
        // On Windows, use GetLogicalProcessorInformationEx to detect core efficiencies
        // Fallback heuristic: common Intel hybrid layouts
        if (OperatingSystem.IsWindows())
        {
            try
            {
                // Use the efficiency class from Win32 API (Win10 21H2+)
                var pCores = DetectPCoreCountViaEfficiencyClass();
                if (pCores > 0) return pCores;
            }
            catch { }
        }

        // Heuristic fallback: Intel hybrid CPUs typically have more logical P-core threads
        // Common layouts: 8P+8E=24T, 8P+4E=20T, 6P+8E=20T, 6P+4E=16T, 4P+8E=16T
        // P-cores have HyperThreading (2 threads each), E-cores have 1 thread each
        // For a rough heuristic: assume ~2/3 of threads are P-core threads
        return logicalCores * 2 / 3;
    }

    private static int DetectPCoreCountViaEfficiencyClass()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return 0;
            uint bufferSize = 0;
            GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref bufferSize);
            if (bufferSize == 0) return 0;

            var buffer = new byte[bufferSize];
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                if (!GetLogicalProcessorInformationEx(RelationProcessorCore, handle.AddrOfPinnedObject(), ref bufferSize))
                    return 0;

                int pCoreThreads = 0;
                byte maxEfficiency = 0;

                // Record layout (SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX):
                // offset 0: int Relationship (4 bytes)
                // offset 4: uint Size (4 bytes)
                // For RelationProcessorCore:
                // offset 8: byte Flags
                // offset 9: byte EfficiencyClass
                // offset 10: byte[6] Reserved
                // offset 16: ushort GroupCount
                // offset 18: byte[2] padding
                // offset 20: GROUP_AFFINITY[GroupCount] - each 12 bytes (ulong Mask + ushort Group + ushort[3] Reserved)
                const int OFFSET_SIZE = 4;
                const int OFFSET_EFFICIENCY_CLASS = 9;
                const int OFFSET_GROUP_COUNT = 16;
                const int OFFSET_GROUP_AFFINITY = 20;

                // First pass: find the maximum efficiency class (P-cores have higher efficiency class)
                uint offset = 0;
                while (offset + 8 <= bufferSize)
                {
                    var recordSize = BitConverter.ToUInt32(buffer, (int)offset + OFFSET_SIZE);
                    if (recordSize == 0 || offset + recordSize > bufferSize) break;
                    var relationship = BitConverter.ToInt32(buffer, (int)offset);
                    if (relationship == RelationProcessorCore && offset + OFFSET_EFFICIENCY_CLASS < bufferSize)
                    {
                        var eff = buffer[offset + OFFSET_EFFICIENCY_CLASS];
                        if (eff > maxEfficiency) maxEfficiency = eff;
                    }
                    offset += recordSize;
                }

                if (maxEfficiency == 0) return 0; // Non-hybrid, all same class

                // Second pass: count threads on cores with max efficiency (P-cores)
                offset = 0;
                while (offset + 8 <= bufferSize)
                {
                    var recordSize = BitConverter.ToUInt32(buffer, (int)offset + OFFSET_SIZE);
                    if (recordSize == 0 || offset + recordSize > bufferSize) break;
                    var relationship = BitConverter.ToInt32(buffer, (int)offset);
                    if (relationship == RelationProcessorCore && offset + OFFSET_EFFICIENCY_CLASS < bufferSize)
                    {
                        var eff = buffer[offset + OFFSET_EFFICIENCY_CLASS];
                        if (eff == maxEfficiency && offset + OFFSET_GROUP_COUNT + 2 <= bufferSize)
                        {
                            var groupCount = BitConverter.ToUInt16(buffer, (int)offset + OFFSET_GROUP_COUNT);
                            for (int g = 0; g < groupCount; g++)
                            {
                                var maskOffset = (int)offset + OFFSET_GROUP_AFFINITY + g * 12;
                                if (maskOffset + 8 <= bufferSize)
                                {
                                    var mask = BitConverter.ToUInt64(buffer, maskOffset);
                                    pCoreThreads += CountBits(mask);
                                }
                            }
                        }
                    }
                    offset += recordSize;
                }
                return pCoreThreads;
            }
            finally
            {
                handle.Free();
            }
        }
        catch { return 0; }
    }

    private const int RelationProcessorCore = 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformationEx(
        int RelationshipType,
        IntPtr Buffer,
        ref uint ReturnedLength);

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

    private readonly System.Collections.Generic.List<string> _themeIdOrder = new();
    private string _selectedThemeId = "legacy-dark";
    private int _themeIndex;
    private string _baseThemeId = "legacy-dark";

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
                UpdateSelectedThemeFromIndex();
            }
        }
    }

    public string SelectedThemeId
    {
        get => _selectedThemeId;
        set => SetSelectedThemeId(value, updateIndex: true, triggerChange: true);
    }

    private static string NormalizeThemeId(string? themeId)
        => string.IsNullOrWhiteSpace(themeId) ? string.Empty : themeId.Trim();

    private string DetermineFallbackThemeId()
    {
        if (_themeIdOrder.Count > 0)
        {
            return _themeIdOrder[0];
        }

        if (!string.IsNullOrWhiteSpace(_baseThemeId))
        {
            return _baseThemeId;
        }

        return "legacy-dark";
    }

    private void UpdateThemeIndexForSelected(bool notifyChange)
    {
        if (_themeIdOrder.Count == 0)
        {
            if (notifyChange)
            {
                OnPropertyChanged(nameof(ThemeIndex));
            }
            return;
        }

        var index = _themeIdOrder.FindIndex(id => string.Equals(id, _selectedThemeId, StringComparison.Ordinal));
        if (index < 0)
        {
            index = 0;
            var fallbackId = _themeIdOrder[0];
            if (!string.Equals(_selectedThemeId, fallbackId, StringComparison.Ordinal))
            {
                _selectedThemeId = fallbackId;
                OnPropertyChanged(nameof(SelectedThemeId));
            }
        }

        if (_themeIndex != index)
        {
            var prevSuppress = _suppressThemeBinding;
            _suppressThemeBinding = true;
            _themeIndex = index;
            if (notifyChange)
            {
                OnPropertyChanged(nameof(ThemeIndex));
            }
            _suppressThemeBinding = prevSuppress;
        }
        else if (notifyChange)
        {
            OnPropertyChanged(nameof(ThemeIndex));
        }
    }

    private void SetSelectedThemeId(string? themeId, bool updateIndex, bool triggerChange, bool refreshInspector = true, bool applyTheme = true)
    {
        var normalized = NormalizeThemeId(themeId);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = DetermineFallbackThemeId();
        }

        var changed = !string.Equals(_selectedThemeId, normalized, StringComparison.Ordinal);
        _selectedThemeId = normalized;

        if (triggerChange && changed)
        {
            OnPropertyChanged(nameof(SelectedThemeId));
        }

        if (updateIndex)
        {
            UpdateThemeIndexForSelected(notifyChange: triggerChange || changed);
        }

        if (changed)
        {
            try { LogSettingsEvent($"SelectedThemeId changed to {_selectedThemeId}"); } catch { }
            try { Utilities.Logger.Info($"SelectedThemeId changed to {_selectedThemeId} (applyTheme={applyTheme}, suppressBinding={_suppressThemeBinding})", source: "SettingsVM", categoryOverride: "theme"); } catch { }
            
            // Apply the theme when it changes
            if (applyTheme && !_suppressThemeBinding)
            {
                try 
                { 
                    Utilities.Logger.Info($"Attempting to apply theme: {_selectedThemeId}", source: "SettingsVM", categoryOverride: "theme");
                    var engine = Services.AppServices.ThemeEngine;
                    var result = engine.SetThemeById(_selectedThemeId);
                    
                    if (!result)
                    {
                        LogSettingsEvent($"Failed to apply theme: {_selectedThemeId}");
                        Utilities.Logger.Warning($"SetThemeById returned false for: {_selectedThemeId}", source: "SettingsVM", categoryOverride: "theme");
                    }
                    else
                    {
                        LogSettingsEvent($"Successfully applied theme: {_selectedThemeId}");
                        Utilities.Logger.Info($"Successfully applied theme: {_selectedThemeId}", source: "SettingsVM", categoryOverride: "theme");
                    }
                } 
                catch (Exception ex) 
                { 
                    LogSettingsEvent($"Error applying theme: {ex.Message}"); 
                    Utilities.Logger.Error($"Error applying theme: {ex.Message}\nStack: {ex.StackTrace}", source: "SettingsVM", categoryOverride: "theme");
                }
            }
            else
            {
                if (!applyTheme)
                    Utilities.Logger.Info($"Skipping theme application (applyTheme=false)", source: "SettingsVM", categoryOverride: "theme");
                if (_suppressThemeBinding)
                    Utilities.Logger.Info($"Skipping theme application (suppressThemeBinding=true)", source: "SettingsVM", categoryOverride: "theme");
            }
        }

        if (!_suppressThemeBinding && refreshInspector && changed)
        {
            try { RefreshThemeInspector(); } catch { }
        }
    }

    private void UpdateSelectedThemeFromIndex()
    {
        if (_themeIndex < 0 || _themeIndex >= _themeIdOrder.Count)
        {
            return;
        }

        var mappedId = _themeIdOrder[_themeIndex];
        var changed = !string.Equals(_selectedThemeId, mappedId, StringComparison.Ordinal);
        _selectedThemeId = mappedId;

        if (changed)
        {
            OnPropertyChanged(nameof(SelectedThemeId));
            try { LogSettingsEvent($"SelectedThemeId changed via ThemeIndex → {_selectedThemeId}"); } catch { }
            
            // Apply the theme when user selects it from dropdown
            if (!_suppressThemeBinding)
            {
                try
                {
                    Utilities.Logger.Info($"Applying theme from dropdown selection: {_selectedThemeId}", source: "SettingsVM", categoryOverride: "theme");
                    var engine = Services.AppServices.ThemeEngine;
                    var result = engine.SetThemeById(_selectedThemeId);
                    
                    if (!result)
                    {
                        LogSettingsEvent($"Failed to apply theme from dropdown: {_selectedThemeId}");
                        Utilities.Logger.Warning($"SetThemeById returned false for dropdown selection: {_selectedThemeId}", source: "SettingsVM", categoryOverride: "theme");
                    }
                    else
                    {
                        LogSettingsEvent($"Successfully applied theme from dropdown: {_selectedThemeId}");
                        Utilities.Logger.Info($"Successfully applied theme from dropdown: {_selectedThemeId}", source: "SettingsVM", categoryOverride: "theme");
                    }
                }
                catch (Exception ex)
                {
                    LogSettingsEvent($"Error applying theme from dropdown: {ex.Message}");
                    Utilities.Logger.Error($"Error applying theme from dropdown: {ex.Message}\nStack: {ex.StackTrace}", source: "SettingsVM", categoryOverride: "theme");
                }
            }
        }

        if (!_suppressThemeBinding)
        {
            try { RefreshThemeInspector(); } catch { }
        }
    }

    private System.Collections.ObjectModel.ObservableCollection<string> _availableLanguages = new();
    public System.Collections.ObjectModel.ObservableCollection<string> AvailableLanguages
    {
        get => _availableLanguages;
        set { _availableLanguages = value; OnPropertyChanged(); }
    }

    private System.Collections.ObjectModel.ObservableCollection<string> _themeItems = new();
    public System.Collections.ObjectModel.ObservableCollection<string> ThemeItems
    {
        get => _themeItems;
        set { _themeItems = value; OnPropertyChanged(); }
    }

    private System.Collections.ObjectModel.ObservableCollection<string> _presenceItems = new();
    public System.Collections.ObjectModel.ObservableCollection<string> PresenceItems
    {
        get => _presenceItems;
        set { _presenceItems = value; OnPropertyChanged(); }
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
                
                // Update LocalizationService when language changes
                try
                {
                    var langCode = GetLanguageCode(_language);
                    Services.AppServices.Localization.LoadLanguage(langCode);
                }
                catch { }
            }
        }
    }
    
    // Localized UI strings
    public string LocalizedSettingsTitle => Services.AppServices.Localization.GetString("Settings.Title", "Settings");
    public string LocalizedAppearance => Services.AppServices.Localization.GetString("Settings.Appearance", "Appearance");
    public string LocalizedGeneral => Services.AppServices.Localization.GetString("Settings.General", "General");
    public string LocalizedHotkeys => Services.AppServices.Localization.GetString("Settings.Hotkeys", "Hotkeys");
    public string LocalizedProfile => Services.AppServices.Localization.GetString("Settings.Profile", "Profile");
    public string LocalizedNetwork => Services.AppServices.Localization.GetString("Settings.Network", "Network");
    public string LocalizedPerformance => Services.AppServices.Localization.GetString("Settings.Performance", "Performance");
    public string LocalizedAccessibility => Services.AppServices.Localization.GetString("Settings.Accessibility", "Accessibility");
    public string LocalizedAbout => Services.AppServices.Localization.GetString("Settings.About", "About");
    public string LocalizedDangerZone => Services.AppServices.Localization.GetString("Settings.DangerZone", "Danger Zone");
    public string LocalizedLogOut => Services.AppServices.Localization.GetString("Settings.LogOut", "Log Out");
    public string LocalizedLanguage => Services.AppServices.Localization.GetString("Settings.Language", "Language");
    public string LocalizedLanguageHelp => Services.AppServices.Localization.GetString("Settings.LanguageHelp", "Change the application language");
    public string LocalizedTheme => Services.AppServices.Localization.GetString("Settings.Theme", "Theme");
    public string LocalizedThemeHelp => Services.AppServices.Localization.GetString("Settings.ThemeHelp", "Select the overall application theme");
    public string LocalizedFont => Services.AppServices.Localization.GetString("Settings.Font", "Font");
    public string LocalizedFontHelp => Services.AppServices.Localization.GetString("Settings.FontHelp", "Override the UI font family (leave blank for default)");
    public string LocalizedPrivacy => Services.AppServices.Localization.GetString("Settings.Privacy", "Privacy");
    
    // General panel strings
    public string LocalizedDefaultPresence => Services.AppServices.Localization.GetString("Settings.DefaultPresence", "Default Presence");
    public string LocalizedDefaultPresenceHelp => Services.AppServices.Localization.GetString("Settings.DefaultPresenceHelp", "Sets your default status when the app is running");
    public string LocalizedStatus => Services.AppServices.Localization.GetString("Settings.Status", "Status");
    public string LocalizedOnline => Services.AppServices.Localization.GetString("Settings.Online", "Online");
    public string LocalizedAway => Services.AppServices.Localization.GetString("Settings.Away", "Away");
    public string LocalizedDoNotDisturb => Services.AppServices.Localization.GetString("Settings.DoNotDisturb", "Do Not Disturb");
    public string LocalizedOffline => Services.AppServices.Localization.GetString("Settings.Offline", "Offline");
    public string LocalizedSuppressNotifications => Services.AppServices.Localization.GetString("Settings.SuppressNotifications", "Suppress notifications and audio while in Do Not Disturb");
    public string LocalizedNotificationDuration => Services.AppServices.Localization.GetString("Settings.NotificationDuration", "Notification Duration");
    public string LocalizedDuration => Services.AppServices.Localization.GetString("Settings.Duration", "Duration:");
    public string LocalizedPrecise => Services.AppServices.Localization.GetString("Settings.Precise", "Precise:");
    public string LocalizedNotificationDurationHelp => Services.AppServices.Localization.GetString("Settings.NotificationDurationHelp", "Controls how long notification toasts stay visible. Slider: 0.5-20s, Input: 0.5-30s.");
    public string LocalizedCurrentSetting => Services.AppServices.Localization.GetString("Settings.CurrentSetting", "Current setting:");
    public string LocalizedSeconds => Services.AppServices.Localization.GetString("Settings.Seconds", "seconds");
    public string LocalizedOfflineAutomatic => Services.AppServices.Localization.GetString("Settings.OfflineAutomatic", "Offline is automatic only and cannot be selected.");
    public string LocalizedAllowAutoUpdates => Services.AppServices.Localization.GetString("Settings.AllowAutoUpdates", "Allow Auto Updates");
    public string LocalizedAllowAutoUpdatesHelp => Services.AppServices.Localization.GetString("Settings.AllowAutoUpdatesHelp", "Automatically check for and prompt to install new versions.");
    public string LocalizedEnableSmoothScrolling => Services.AppServices.Localization.GetString("Settings.EnableSmoothScrolling", "Enable smooth scrolling");
    public string LocalizedEnableSmoothScrollingHelp => Services.AppServices.Localization.GetString("Settings.EnableSmoothScrollingHelp", "Use animated scrolling in log viewers and live feeds.");
    public string LocalizedCheckForUpdatesNow => Services.AppServices.Localization.GetString("Settings.CheckForUpdatesNow", "Check for Updates Now");
    public string LocalizedCheckForUpdatesNowHelp => Services.AppServices.Localization.GetString("Settings.CheckForUpdatesNowHelp", "Runs an immediate update check even when auto updates are disabled.");
    public string LocalizedLastUpdateCheck => Services.AppServices.Localization.GetString("Settings.LastUpdateCheck", "Last checked:");
    public string LocalizedNever => Services.AppServices.Localization.GetString("Common.Never", "Never");
    public string LocalizedEnableNotificationBellFlash => Services.AppServices.Localization.GetString("Settings.EnableNotificationBellFlash", "Enable notification bell flash");
    public string LocalizedNotificationBellFlashHelp => Services.AppServices.Localization.GetString("Settings.NotificationBellFlashHelp", "Flash the bell icon for 10 seconds when notifications arrive");
    public string LocalizedAutoLock => Services.AppServices.Localization.GetString("Settings.AutoLock", "Auto-Lock");
    public string LocalizedEnableAutoLock => Services.AppServices.Localization.GetString("Settings.EnableAutoLock", "Enable Auto-Lock");
    public string LocalizedAutoLockHelp => Services.AppServices.Localization.GetString("Settings.AutoLockHelp", "Automatically lock the app after inactivity");
    public string LocalizedMinutes => Services.AppServices.Localization.GetString("Settings.Minutes", "Minutes");
    public string LocalizedAutoLockMinutesHelp => Services.AppServices.Localization.GetString("Settings.AutoLockMinutesHelp", "Number of idle minutes before locking");
    public string LocalizedLockOnMinimize => Services.AppServices.Localization.GetString("Settings.LockOnMinimize", "Lock on minimize");
    public string LocalizedLockOnMinimizeHelp => Services.AppServices.Localization.GetString("Settings.LockOnMinimizeHelp", "Lock immediately when the window is minimized");
    public string LocalizedLockBlurRadius => Services.AppServices.Localization.GetString("Settings.LockBlurRadius", "Lock Blur Radius");
    public string LocalizedLockBlurHelp => Services.AppServices.Localization.GetString("Settings.LockBlurHelp", "Controls blur strength while locked. 0 = no blur, 10 = maximum.");
    public string LocalizedBlockScreenCapture => Services.AppServices.Localization.GetString("Settings.BlockScreenCapture", "Block screen capture");
    public string LocalizedScreenCaptureWarning => Services.AppServices.Localization.GetString("Settings.ScreenCaptureWarning", "Windows-only; older/legacy capture tools may not honor this.");
    public string LocalizedKeyVisibility => Services.AppServices.Localization.GetString("Settings.KeyVisibility", "Key Visibility");
    public string LocalizedShowPublicKeys => Services.AppServices.Localization.GetString("Settings.ShowPublicKeys", "Show public keys on profiles");
    public string LocalizedShowPublicKeysHelp => Services.AppServices.Localization.GetString("Settings.ShowPublicKeysHelp", "Display public keys in profile views");
    public string LocalizedStreamerModeLabel => Services.AppServices.Localization.GetString("Settings.StreamerMode", "Streamer Mode");
    public string LocalizedStreamerModeHelp => Services.AppServices.Localization.GetString("Settings.StreamerModeHelp", "Hides sensitive information (UIDs, IPs, peer names) in the UI for safe streaming");
    public string LocalizedAudio => Services.AppServices.Localization.GetString("Settings.Audio", "Audio");
    public string LocalizedAudioHelp => Services.AppServices.Localization.GetString("Settings.AudioHelp", "Configure volume levels for different types of sounds. Main Volume acts as a master control, while individual channels allow fine-tuning of specific sound categories.");
    public string LocalizedMainVolume => Services.AppServices.Localization.GetString("Settings.MainVolume", "Main Volume");
    public string LocalizedNotifications => Services.AppServices.Localization.GetString("Settings.Notifications", "Notifications");
    public string LocalizedChatSounds => Services.AppServices.Localization.GetString("Settings.ChatSounds", "Chat Sounds");
    public string LocalizedSystemTray => Services.AppServices.Localization.GetString("Settings.SystemTray", "System Tray");
    public string LocalizedSystemTrayHelp => Services.AppServices.Localization.GetString("Settings.SystemTrayHelp", "Control how Zer0Talk behaves with the Windows system tray.");
    public string LocalizedShowSystemTrayIcon => Services.AppServices.Localization.GetString("Settings.ShowSystemTrayIcon", "Show icon in system tray");
    public string LocalizedShowSystemTrayIconHelp => Services.AppServices.Localization.GetString("Settings.ShowSystemTrayIconHelp", "Display Zer0Talk icon in the Windows system tray for quick access");
    public string LocalizedMinimizeToTray => Services.AppServices.Localization.GetString("Settings.MinimizeToTray", "Minimize to tray on close");
    public string LocalizedMinimizeToTrayHelp => Services.AppServices.Localization.GetString("Settings.MinimizeToTrayHelp", "Close button will minimize to system tray instead of exiting the application");
    public string LocalizedStartMinimized => Services.AppServices.Localization.GetString("Settings.StartMinimized", "Start minimized to tray");
    public string LocalizedStartMinimizedHelp => Services.AppServices.Localization.GetString("Settings.StartMinimizedHelp", "Launch Zer0Talk in the system tray instead of showing the main window");
    public string LocalizedRunOnStartup => Services.AppServices.Localization.GetString("Settings.RunOnStartup", "Run on Windows startup");
    public string LocalizedRunOnStartupHelp => Services.AppServices.Localization.GetString("Settings.RunOnStartupHelp", "Automatically start Zer0Talk when Windows starts (minimized to tray)");
    public string LocalizedFamily => Services.AppServices.Localization.GetString("Settings.Family", "Family:");
    
    // Profile panel strings
    public string LocalizedIdentity => Services.AppServices.Localization.GetString("Settings.Identity", "Identity");
    public string LocalizedUsername => Services.AppServices.Localization.GetString("Settings.Username", "Username");
    public string LocalizedUID => Services.AppServices.Localization.GetString("Settings.UID", "UID");
    public string LocalizedDisplayName => Services.AppServices.Localization.GetString("Settings.DisplayName", "Display Name");
    public string LocalizedPrevName => Services.AppServices.Localization.GetString("Settings.PrevName", "Prev. Name");
    public string LocalizedPublicKey => Services.AppServices.Localization.GetString("Settings.PublicKey", "Public Key");
    public string LocalizedAvatar => Services.AppServices.Localization.GetString("Settings.Avatar", "Avatar");
    public string LocalizedChoose => Services.AppServices.Localization.GetString("Settings.Choose", "Choose...");
    public string LocalizedClear => Services.AppServices.Localization.GetString("Settings.Clear", "Clear");
    public string LocalizedRandomAvatar => Services.AppServices.Localization.GetString("Settings.RandomAvatar", "Random Avatar");
    public string LocalizedRandomAvatarTooltip => Services.AppServices.Localization.GetString("Settings.RandomAvatarTooltip", "Assign a random bundled avatar");
    public string LocalizedShareAvatar => Services.AppServices.Localization.GetString("Settings.ShareAvatar", "Share Avatar with peers");
    public string LocalizedBio => Services.AppServices.Localization.GetString("Settings.Bio", "Bio");
    public string LocalizedBioHelp => Services.AppServices.Localization.GetString("Settings.BioHelp", "Add a short profile bio shown to your contacts");
    
    // Hotkeys panel strings
    public string LocalizedCustomizeKeyboardShortcuts => Services.AppServices.Localization.GetString("Settings.CustomizeKeyboardShortcuts", "Customize keyboard shortcuts");
    public string LocalizedHotkeyInstructions => Services.AppServices.Localization.GetString("Settings.HotkeyInstructions", "Click a hotkey field and press your desired key combination. Conflicting assignments will be rejected.");
    public string LocalizedLockApplication => Services.AppServices.Localization.GetString("Settings.LockApplication", "Lock Application");
    public string LocalizedLockApplicationHelp => Services.AppServices.Localization.GetString("Settings.LockApplicationHelp", "Quickly lock the app to protect your privacy");
    public string LocalizedHotkey => Services.AppServices.Localization.GetString("Settings.Hotkey", "Hotkey");
    public string LocalizedPressKeys => Services.AppServices.Localization.GetString("Settings.PressKeys", "Press keys...");
    public string LocalizedResetToDefault => Services.AppServices.Localization.GetString("Settings.ResetToDefault", "Reset to Default");
    public string LocalizedClearMessageInput => Services.AppServices.Localization.GetString("Settings.ClearMessageInput", "Clear Message Input");
    public string LocalizedClearMessageInputHelp => Services.AppServices.Localization.GetString("Settings.ClearMessageInputHelp", "Instantly clear all text in the message input box");
    public string LocalizedStreamerModeHotkey => Services.AppServices.Localization.GetString("Settings.StreamerModeHotkey", "Toggle Streamer Mode");
    public string LocalizedStreamerModeHotkeyHelp => Services.AppServices.Localization.GetString("Settings.StreamerModeHotkeyHelp", "Quickly toggle streamer mode to hide sensitive information");
    public string LocalizedHotkeyTips => Services.AppServices.Localization.GetString("Settings.HotkeyTips", "💡 Tips");
    public string LocalizedHotkeyTipsText => Services.AppServices.Localization.GetString("Settings.HotkeyTipsText", "Avoid system hotkeys like Alt+F4, Ctrl+C, Win+L, etc. Common safe combinations use Ctrl, Ctrl+Shift, or Ctrl+Alt with function keys or letters.");
    
    // Appearance panel extended strings
    public string LocalizedDark => Services.AppServices.Localization.GetString("Settings.Dark", "Dark");
    public string LocalizedLight => Services.AppServices.Localization.GetString("Settings.Light", "Light");
    public string LocalizedSandy => Services.AppServices.Localization.GetString("Settings.Sandy", "Sandy");
    public string LocalizedButter => Services.AppServices.Localization.GetString("Settings.Butter", "Butter");
    public string LocalizedEnterFontFamilyName => Services.AppServices.Localization.GetString("Settings.EnterFontFamilyName", "Enter font family name");
    public string LocalizedOSScalingMessage => Services.AppServices.Localization.GetString("Settings.OSScalingMessage", "For UI scaling, use your operating system's display scaling settings.");
    public string LocalizedAdditionalLanguagesMessage => Services.AppServices.Localization.GetString("Settings.AdditionalLanguagesMessage", "Additional languages will be added in future updates.");
    public string LocalizedEnterDisplayName => Services.AppServices.Localization.GetString("Settings.EnterDisplayName", "Enter your display name");
    
    // About panel strings
    public string LocalizedApplicationInformation => Services.AppServices.Localization.GetString("Settings.ApplicationInformation", "Application Information");
    public string LocalizedVersion => Services.AppServices.Localization.GetString("Settings.Version", "Version:");
    public string LocalizedBuild => Services.AppServices.Localization.GetString("Settings.Build", "Build:");
    public string LocalizedDeveloper => Services.AppServices.Localization.GetString("Settings.Developer", "Developer:");
    public string LocalizedRepository => Services.AppServices.Localization.GetString("Settings.Repository", "Repository:");
    public string LocalizedLicense => Services.AppServices.Localization.GetString("Settings.License", "License:");
    public string LocalizedCopyright => Services.AppServices.Localization.GetString("Settings.Copyright", "Copyright:");
    
    // Performance panel strings
    public string LocalizedCPU => Services.AppServices.Localization.GetString("Settings.CPU", "CPU");
    public string LocalizedGPU => Services.AppServices.Localization.GetString("Settings.GPU", "GPU");
    public string LocalizedFramerate => Services.AppServices.Localization.GetString("Settings.Framerate", "Framerate");
    public string LocalizedCCDOffinity => Services.AppServices.Localization.GetString("Settings.CCDOffinity", "CCD Affinity:");
    public string LocalizedIntelPCoreTargeting => Services.AppServices.Localization.GetString("Settings.IntelPCoreTargeting", "Prefer Performance Cores");
    public string LocalizedIntelPCoreTargetingHelp => Services.AppServices.Localization.GetString("Settings.IntelPCoreTargetingHelp", "Target Performance cores over Efficiency cores for lower latency on Intel hybrid CPUs");
    public string LocalizedNotRecommended => Services.AppServices.Localization.GetString("Settings.NotRecommended", "Not Recommended");
    public string LocalizedEnforceRAMLimit => Services.AppServices.Localization.GetString("Settings.EnforceRAMLimit", "Enforce RAM Limit");
    public string LocalizedEnforceRAMLimitHelp => Services.AppServices.Localization.GetString("Settings.EnforceRAMLimitHelp", "Limit memory usage for the app");
    public string LocalizedRAMLimit => Services.AppServices.Localization.GetString("Settings.RAMLimit", "RAM Limit:");
    public string LocalizedRamLimitHelp => Services.AppServices.Localization.GetString("Settings.RamLimitHelp", "Maximum RAM usage in MB (0 = unlimited)");
    public string LocalizedMBUnlimited => Services.AppServices.Localization.GetString("Settings.MBUnlimited", "MB (0 = unlimited)");
    public string LocalizedEnableGPUAcceleration => Services.AppServices.Localization.GetString("Settings.EnableGPUAcceleration", "Enable GPU Acceleration");
    public string LocalizedEnableGpuAccelerationHelp => Services.AppServices.Localization.GetString("Settings.EnableGpuAccelerationHelp", "Use hardware acceleration for rendering when available");
    public string LocalizedEnforceVRAMLimit => Services.AppServices.Localization.GetString("Settings.EnforceVRAMLimit", "Enforce VRAM Limit");
    public string LocalizedEnforceVramLimitHelp => Services.AppServices.Localization.GetString("Settings.EnforceVramLimitHelp", "Limit GPU memory usage for the app");
    public string LocalizedVRAMLimit => Services.AppServices.Localization.GetString("Settings.VRAMLimit", "VRAM Limit:");
    public string LocalizedVramLimitHelp => Services.AppServices.Localization.GetString("Settings.VramLimitHelp", "Maximum VRAM usage in MB (0 = unlimited)");
    public string LocalizedFPSThrottle => Services.AppServices.Localization.GetString("Settings.FPSThrottle", "FPS Throttle:");
    public string LocalizedFpsThrottleHelp => Services.AppServices.Localization.GetString("Settings.FpsThrottleHelp", "Limit UI rendering frames per second (0 = unlimited)");
    public string LocalizedFPSUnlimited => Services.AppServices.Localization.GetString("Settings.FPSUnlimited", "fps (0 = unlimited)");
    public string LocalizedRefreshRateThrottle => Services.AppServices.Localization.GetString("Settings.RefreshRateThrottle", "Refresh Rate Throttle:");
    public string LocalizedRefreshRateThrottleHelp => Services.AppServices.Localization.GetString("Settings.RefreshRateThrottleHelp", "Limit display refresh rate used by the app (0 = unlimited)");
    public string LocalizedHzUnlimited => Services.AppServices.Localization.GetString("Settings.HzUnlimited", "hz (0 = unlimited)");
    public string LocalizedBackgroundFramerate => Services.AppServices.Localization.GetString("Settings.BackgroundFramerate", "Background Framerate:");
    public string LocalizedBackgroundFramerateHelp => Services.AppServices.Localization.GetString("Settings.BackgroundFramerateHelp", "Limit UI framerate when the app is unfocused");
    public string LocalizedFPS => Services.AppServices.Localization.GetString("Settings.FPS", "fps");
    
    // Accessibility panel strings
    public string LocalizedAccessibilitySettings => Services.AppServices.Localization.GetString("Settings.AccessibilitySettings", "Accessibility Settings");
    public string LocalizedAccessibilityOSMessage => Services.AppServices.Localization.GetString("Settings.AccessibilityOSMessage", "Most accessibility features are controlled by your operating system:");
    public string LocalizedKeyboardNavigation => Services.AppServices.Localization.GetString("Settings.KeyboardNavigation", "Keyboard Navigation");
    public string LocalizedShowKeyboardFocusIndicators => Services.AppServices.Localization.GetString("Settings.ShowKeyboardFocusIndicators", "Show keyboard focus indicators");
    public string LocalizedShowKeyboardFocusHelp => Services.AppServices.Localization.GetString("Settings.ShowKeyboardFocusHelp", "Highlight the currently focused control");
    public string LocalizedEnhancedKeyboardNavigation => Services.AppServices.Localization.GetString("Settings.EnhancedKeyboardNavigation", "Enhanced keyboard navigation");
    public string LocalizedEnhancedKeyboardNavigationHelp => Services.AppServices.Localization.GetString("Settings.EnhancedKeyboardNavigationHelp", "Enable additional keyboard navigation behaviors");
    public string LocalizedNavigationKeys => Services.AppServices.Localization.GetString("Settings.NavigationKeys", "Navigation Keys:");
    public string LocalizedNavigationHelp => Services.AppServices.Localization.GetString("Settings.NavigationHelp", "All windows and panels support full keyboard navigation. Focus indicators will highlight the active control.");
    public string LocalizedFontRendering => Services.AppServices.Localization.GetString("Settings.FontRendering", "Font Rendering");
    public string LocalizedOSFontSmoothing => Services.AppServices.Localization.GetString("Settings.OSFontSmoothing", "OS Font Smoothing:");
    public string LocalizedFontSmoothingMessage => Services.AppServices.Localization.GetString("Settings.FontSmoothingMessage", "Font smoothing is controlled by your operating system. Change it in your OS display settings.");
    
    // Debug/Network panel strings
    public string LocalizedDebugTools => Services.AppServices.Localization.GetString("Settings.DebugTools", "Debug Tools");
    public string LocalizedLogging => Services.AppServices.Localization.GetString("Settings.Logging", "Logging");
    public string LocalizedEnableLogging => Services.AppServices.Localization.GetString("Settings.EnableLogging", "Enable logging");
    public string LocalizedPerformanceWarning => Services.AppServices.Localization.GetString("Settings.PerformanceWarning", "Performance Warning");
    public string LocalizedPerformanceWarningText => Services.AppServices.Localization.GetString("Settings.PerformanceWarningText", "Logging causes significant performance degradation. Only enable when actively tracking down problems or debugging issues. Disable immediately after troubleshooting is complete.");
    public string LocalizedLogMaintenance => Services.AppServices.Localization.GetString("Settings.LogMaintenance", "Log Maintenance");
    public string LocalizedAutoTrimUILog => Services.AppServices.Localization.GetString("Settings.AutoTrimUILog", "Auto-trim UI log");
    public string LocalizedAutoTrimUILogHelp => Services.AppServices.Localization.GetString("Settings.AutoTrimUILogHelp", "Automatically trim the UI log to the limits below");
    public string LocalizedMaxEntries => Services.AppServices.Localization.GetString("Settings.MaxEntries", "Max entries to keep");
    public string LocalizedDebugUiLogMaxLinesHelp => Services.AppServices.Localization.GetString("Settings.DebugUiLogMaxLinesHelp", "Maximum number of UI log entries to keep");
    public string LocalizedRetentionDays => Services.AppServices.Localization.GetString("Settings.RetentionDays", "Retention window (days)");
    public string LocalizedDebugLogRetentionDaysHelp => Services.AppServices.Localization.GetString("Settings.DebugLogRetentionDaysHelp", "How many days to retain debug logs");
    public string LocalizedSizeCap => Services.AppServices.Localization.GetString("Settings.SizeCap", "Size cap");
    public string LocalizedDebugLogSizeHelp => Services.AppServices.Localization.GetString("Settings.DebugLogSizeHelp", "Maximum total size of debug logs before trimming");
    public string LocalizedLastMaintenance => Services.AppServices.Localization.GetString("Settings.LastMaintenance", "Last maintenance");
    public string LocalizedRunMaintenanceNow => Services.AppServices.Localization.GetString("Settings.RunMaintenanceNow", "Run Maintenance Now");
    public string LocalizedClearErrorLog => Services.AppServices.Localization.GetString("Settings.PurgeAllLogs", "Purge All Logs");
    public string LocalizedClearErrorLogTooltip => Services.AppServices.Localization.GetString("Settings.PurgeAllLogsTooltip", "Delete all log files in logs directory (ephemeral debug data)");
    public string LocalizedConnectionSettings => Services.AppServices.Localization.GetString("Settings.ConnectionSettings", "Connection Settings");
    public string LocalizedRelaySettings => Services.AppServices.Localization.GetString("Settings.RelaySettings", "Relay Settings");
    
    // Network panel strings
    public string LocalizedPort => Services.AppServices.Localization.GetString("Settings.Port", "Port");
    public string LocalizedPortLabel => Services.AppServices.Localization.GetString("Settings.PortLabel", "Port:");
    public string LocalizedPortDescription => Services.AppServices.Localization.GetString("Settings.PortDescription", "The port number Zer0Talk uses for P2P connections. Default is 26264. Changing this requires restarting the application.");
    public string LocalizedDedicatedPeerNode => Services.AppServices.Localization.GetString("Settings.DedicatedPeerNode", "Dedicated Peer Node");
    public string LocalizedEnableDedicatedPeerNode => Services.AppServices.Localization.GetString("Settings.EnableDedicatedPeerNode", "Enable as dedicated peer node (requires port forwarding)");
    public string LocalizedDedicatedPeerNodeHelp => Services.AppServices.Localization.GetString("Settings.DedicatedPeerNodeHelp", "Make this device act as a dedicated peer node");
    public string LocalizedDedicatedPeerNodeInfo => Services.AppServices.Localization.GetString("Settings.DedicatedPeerNodeInfo", "Dedicated Peer Node:");
    public string LocalizedEnablesUPnP => Services.AppServices.Localization.GetString("Settings.EnablesUPnP", "• Enables UPnP port mapping for WAN reachability (requires router support)");
    public string LocalizedMakesNodeAccessible => Services.AppServices.Localization.GetString("Settings.MakesNodeAccessible", "• Makes your node accessible from outside your local network");
    public string LocalizedImprovesConnectivity => Services.AppServices.Localization.GetString("Settings.ImprovesConnectivity", "• Improves direct P2P connectivity for all peers");
    public string LocalizedDoesNotRelay => Services.AppServices.Localization.GetString("Settings.DoesNotRelay", "• Does NOT relay encrypted message traffic - only helps with NAT traversal");
    public string LocalizedRequiresPortForwarding => Services.AppServices.Localization.GetString("Settings.RequiresPortForwarding", "• Requires manual port forwarding if UPnP fails (forward port 26264 TCP to this machine)");
    public string LocalizedRelayDescription1 => Services.AppServices.Localization.GetString("Settings.RelayDescription1", "Relay servers provide last-resort connectivity when direct P2P and NAT traversal fail.");
    public string LocalizedRelayDescription2 => Services.AppServices.Localization.GetString("Settings.RelayDescription2", "All messages remain end-to-end encrypted - the relay only forwards encrypted traffic.");
    public string LocalizedRelayFallback => Services.AppServices.Localization.GetString("Settings.RelayFallback", "Relay Fallback");
    public string LocalizedEnableRelayFallback => Services.AppServices.Localization.GetString("Settings.EnableRelayFallback", "Enable relay fallback for blocked connections");
    public string LocalizedRelayFallbackHelp => Services.AppServices.Localization.GetString("Settings.RelayFallbackHelp", "Use a relay server when direct P2P connections fail");
    public string LocalizedRelayServer => Services.AppServices.Localization.GetString("Settings.RelayServer", "Relay Server");
    public string LocalizedRelayServerHelp => Services.AppServices.Localization.GetString("Settings.RelayServerHelp", "Relay endpoint (host:port, [IPv6]:port, or 16-character LAN token). Leave blank to disable.");
    public string LocalizedRelayPresenceTimeout => Services.AppServices.Localization.GetString("Settings.RelayPresenceTimeout", "Presence Timeout (seconds)");
    public string LocalizedRelayDiscoveryTtl => Services.AppServices.Localization.GetString("Settings.RelayDiscoveryTtl", "Discovery TTL (minutes)");
    public string LocalizedSavedRelays => Services.AppServices.Localization.GetString("Settings.SavedRelays", "Saved Relay Endpoints");
    public string LocalizedSavedRelaysHelp => Services.AppServices.Localization.GetString("Settings.SavedRelaysHelp", "Add relay endpoints (host:port or [IPv6]:port) or 16-character LAN relay tokens for quick reuse");
    public string LocalizedRequirements => Services.AppServices.Localization.GetString("Settings.Requirements", "Requirements:");
    public string LocalizedRequiresDedicatedRelay => Services.AppServices.Localization.GetString("Settings.RequiresDedicatedRelay", "• Requires a dedicated relay server with public IP or DNS name");
    public string LocalizedServerMustBeConfigured => Services.AppServices.Localization.GetString("Settings.ServerMustBeConfigured", "• Server must be configured to accept Zer0Talk relay protocol connections");
    public string LocalizedRelayFormat => Services.AppServices.Localization.GetString("Settings.RelayFormat", "• Format: hostname:port, [IPv6]:port, or 16-character LAN relay token");
    public string LocalizedLeaveBlankToDisable => Services.AppServices.Localization.GetString("Settings.LeaveBlankToDisable", "• Leave blank to disable relay fallback and rely on direct/NAT traversal only");
    public string LocalizedKnownDedicatedPeerNodes => Services.AppServices.Localization.GetString("Settings.KnownDedicatedPeerNodes", "Known Dedicated Peer Nodes");
    public string LocalizedKnownDedicatedPeerNodesHelp => Services.AppServices.Localization.GetString("Settings.KnownDedicatedPeerNodesHelp", "Add a host:port peer node to prefer for discovery");
    public string LocalizedAdd => Services.AppServices.Localization.GetString("Settings.Add", "Add");
    public string LocalizedRemove => Services.AppServices.Localization.GetString("Settings.Remove", "Remove");
    public string LocalizedAddBadActorIpHelp => Services.AppServices.Localization.GetString("Settings.AddBadActorIpHelp", "Add a single IP address to block");
    public string LocalizedAddIpRangeHelp => Services.AppServices.Localization.GetString("Settings.AddIpRangeHelp", "Add a CIDR IP range to block");
    public string LocalizedDiscoveredPeers => Services.AppServices.Localization.GetString("Settings.DiscoveredPeers", "Discovered Peers");
    public string LocalizedLocationsEstimated => Services.AppServices.Localization.GetString("Settings.LocationsEstimated", "(locations are estimated and not even remotely accurate)");
    public string LocalizedBlockSelected => Services.AppServices.Localization.GetString("Settings.BlockSelected", "Block Selected");
    public string LocalizedUnblockSelected => Services.AppServices.Localization.GetString("Settings.UnblockSelected", "Unblock Selected");
    public string LocalizedNode => Services.AppServices.Localization.GetString("Settings.Node", "Node");
    public string LocalizedBlock => Services.AppServices.Localization.GetString("Settings.Block", "Block");
    
    // Performance panel ComboBox items
    public string LocalizedAuto => Services.AppServices.Localization.GetString("Settings.Auto", "Auto");
    public string LocalizedCCD0 => Services.AppServices.Localization.GetString("Settings.CCD0", "CCD 0");
    public string LocalizedCCD1 => Services.AppServices.Localization.GetString("Settings.CCD1", "CCD 1");
    public string LocalizedBothCCDs => Services.AppServices.Localization.GetString("Settings.BothCCDs", "Both CCDs");
    
    // Accessibility panel strings
    public string LocalizedAccessibilityTitle => Services.AppServices.Localization.GetString("Settings.AccessibilityTitle", "Accessibility");
    public string LocalizedDisplayScaling => Services.AppServices.Localization.GetString("Settings.DisplayScaling", "• Display Scaling:");
    public string LocalizedDisplayScalingPath => Services.AppServices.Localization.GetString("Settings.DisplayScalingPath", "Windows Display Settings > Scale and layout");
    public string LocalizedHighContrast => Services.AppServices.Localization.GetString("Settings.HighContrast", "• High Contrast:");
    public string LocalizedHighContrastPath => Services.AppServices.Localization.GetString("Settings.HighContrastPath", "Windows Settings > Accessibility > Contrast themes");
    public string LocalizedAnimations => Services.AppServices.Localization.GetString("Settings.Animations", "• Animations:");
    public string LocalizedAnimationsPath => Services.AppServices.Localization.GetString("Settings.AnimationsPath", "Windows Settings > Accessibility > Visual effects");
    public string LocalizedCursorSize => Services.AppServices.Localization.GetString("Settings.CursorSize", "• Cursor Size:");
    public string LocalizedCursorSizePath => Services.AppServices.Localization.GetString("Settings.CursorSizePath", "Windows Settings > Accessibility > Mouse pointer and touch");
    public string LocalizedTextCursor => Services.AppServices.Localization.GetString("Settings.TextCursor", "• Text Cursor:");
    public string LocalizedTextCursorPath => Services.AppServices.Localization.GetString("Settings.TextCursorPath", "Windows Settings > Accessibility > Text cursor");
    public string LocalizedTab => Services.AppServices.Localization.GetString("Settings.Tab", "• Tab");
    public string LocalizedTabDescription => Services.AppServices.Localization.GetString("Settings.TabDescription", "– Move forward between controls");
    public string LocalizedShiftTab => Services.AppServices.Localization.GetString("Settings.ShiftTab", "• Shift+Tab");
    public string LocalizedShiftTabDescription => Services.AppServices.Localization.GetString("Settings.ShiftTabDescription", "– Move backward between controls");
    public string LocalizedSpaceEnter => Services.AppServices.Localization.GetString("Settings.SpaceEnter", "• Space/Enter");
    public string LocalizedSpaceEnterDescription => Services.AppServices.Localization.GetString("Settings.SpaceEnterDescription", "– Activate buttons and toggles");
    public string LocalizedArrowKeys => Services.AppServices.Localization.GetString("Settings.ArrowKeys", "• Arrow Keys");
    public string LocalizedArrowKeysDescription => Services.AppServices.Localization.GetString("Settings.ArrowKeysDescription", "– Navigate within lists, menus, and dropdowns");
    public string LocalizedEsc => Services.AppServices.Localization.GetString("Settings.Esc", "• Esc");
    public string LocalizedEscDescription => Services.AppServices.Localization.GetString("Settings.EscDescription", "– Close dialogs and overlays");
    public string LocalizedCtrlL => Services.AppServices.Localization.GetString("Settings.CtrlL", "• Ctrl+L");
    public string LocalizedCtrlLDescription => Services.AppServices.Localization.GetString("Settings.CtrlLDescription", "– Lock application (customizable in Hotkeys)");
    
    // About panel strings
    public string LocalizedAboutName => Services.AppServices.Localization.GetString("Settings.AboutName", "Name:");
    public string LocalizedAboutPlatform => Services.AppServices.Localization.GetString("Settings.AboutPlatform", "Platform:");
    public string LocalizedAboutAuthor => Services.AppServices.Localization.GetString("Settings.AboutAuthor", "Author:");
    public string LocalizedAboutFrameworkInfo => Services.AppServices.Localization.GetString("Settings.AboutFrameworkInfo", "Framework Information");
    public string LocalizedAboutAvaloniaUI => Services.AppServices.Localization.GetString("Settings.AboutAvaloniaUI", "Avalonia UI:");
    public string LocalizedAboutDotNetRuntime => Services.AppServices.Localization.GetString("Settings.AboutDotNetRuntime", ".NET Runtime:");
    public string LocalizedAboutLinksResources => Services.AppServices.Localization.GetString("Settings.AboutLinksResources", "Links & Resources");
    public string LocalizedAboutDocumentation => Services.AppServices.Localization.GetString("Settings.AboutDocumentation", "Documentation");
    public string LocalizedAboutPrivacyPolicy => Services.AppServices.Localization.GetString("Settings.AboutPrivacyPolicy", "Privacy Policy");
    public string LocalizedAboutAcknowledgments => Services.AppServices.Localization.GetString("Settings.AboutAcknowledgments", "Acknowledgments");
    public string LocalizedAboutBuiltWithAvalonia => Services.AppServices.Localization.GetString("Settings.AboutBuiltWithAvalonia", "Built with Avalonia UI - A cross-platform XAML-based UI framework for .NET");
    public string LocalizedAboutSpecialThanks => Services.AppServices.Localization.GetString("Settings.AboutSpecialThanks", "Special thanks to the open source community for their contributions and support.");
    
    // Message Burn Security strings
    public string LocalizedMessageBurnSecurity => Services.AppServices.Localization.GetString("Settings.MessageBurnSecurity", "Message Burn Security");
    public string LocalizedMessageBurnSecurityDesc => Services.AppServices.Localization.GetString("Settings.MessageBurnSecurityDesc", "Choose the security level for burning conversations. Standard is faster, Enhanced provides maximum security.");
    public string LocalizedMessageBurnStandard => Services.AppServices.Localization.GetString("Settings.MessageBurnStandard", "Standard (3-pass)");
    public string LocalizedMessageBurnStandardDesc => Services.AppServices.Localization.GetString("Settings.MessageBurnStandardDesc", "Overwrites data with random bits, lorem text, alternating patterns, and zeros.");
    public string LocalizedMessageBurnStandardTooltip => Services.AppServices.Localization.GetString("Settings.MessageBurnStandardTooltip", "Balanced security with good performance");
    public string LocalizedMessageBurnEnhanced => Services.AppServices.Localization.GetString("Settings.MessageBurnEnhanced", "Enhanced (6-pass)");
    public string LocalizedMessageBurnEnhancedDesc => Services.AppServices.Localization.GetString("Settings.MessageBurnEnhancedDesc", "Overwrites with random data, 0xFF, 0x00, random, 0xAA, and 0x55 patterns for maximum security.");
    public string LocalizedMessageBurnEnhancedTooltip => Services.AppServices.Localization.GetString("Settings.MessageBurnEnhancedTooltip", "Maximum security, slower but more thorough");
    
    // Danger Zone strings
    public string LocalizedDangerZoneTitle => Services.AppServices.Localization.GetString("Settings.DangerZoneTitle", "⚠️ Danger Zone");
    public string LocalizedDangerZoneWarning => Services.AppServices.Localization.GetString("Settings.DangerZoneWarning", "These actions are irreversible and will permanently delete your data.");
    public string LocalizedDeleteAccount => Services.AppServices.Localization.GetString("Settings.DeleteAccount", "Delete Account");
    public string LocalizedWhatHappens => Services.AppServices.Localization.GetString("Settings.WhatHappens", "What happens when you delete your account:");
    public string LocalizedMessagesDeleted => Services.AppServices.Localization.GetString("Settings.MessagesDeleted", "• All your messages will be permanently deleted");
    public string LocalizedContactListRemoved => Services.AppServices.Localization.GetString("Settings.ContactListRemoved", "• Your contact list will be removed");
    public string LocalizedProfileErased => Services.AppServices.Localization.GetString("Settings.ProfileErased", "• Your profile information will be erased");
    public string LocalizedRemovedFromConversations => Services.AppServices.Localization.GetString("Settings.RemovedFromConversations", "• You will be removed from all conversations");
    public string LocalizedCannotUndo => Services.AppServices.Localization.GetString("Settings.CannotUndo", "• This action cannot be undone");
    public string LocalizedDeleteAccountWarning => Services.AppServices.Localization.GetString("Settings.DeleteAccountWarning", "Once you delete your account, there is no going back. This will permanently delete your account and wipe all local data from this device.");
    public string LocalizedTypeConfirmationCode => Services.AppServices.Localization.GetString("Settings.TypeConfirmationCode", "Please type the following code to confirm:");
    public string LocalizedEnterConfirmationCode => Services.AppServices.Localization.GetString("Settings.EnterConfirmationCode", "Enter confirmation code...");
    public string LocalizedDeleteAccountButton => Services.AppServices.Localization.GetString("Settings.DeleteAccountButton", "Delete Account");
    public string LocalizedPurgeAllMessages => Services.AppServices.Localization.GetString("Settings.PurgeAllMessages", "Purge All Messages");
    public string LocalizedWhatHappensPurge => Services.AppServices.Localization.GetString("Settings.WhatHappensPurge", "What happens when you purge all messages:");
    public string LocalizedConversationsDeleted => Services.AppServices.Localization.GetString("Settings.ConversationsDeleted", "• Every conversation will be permanently deleted");
    public string LocalizedOutboxRemoved => Services.AppServices.Localization.GetString("Settings.OutboxRemoved", "• All pending outbox messages will be removed");
    public string LocalizedArchivesOverwritten => Services.AppServices.Localization.GetString("Settings.ArchivesOverwritten", "• Encrypted archives will be securely overwritten");
    public string LocalizedRecoveryImpossible => Services.AppServices.Localization.GetString("Settings.RecoveryImpossible", "• Recovery will be impossible");
    public string LocalizedPurgeWarning => Services.AppServices.Localization.GetString("Settings.PurgeWarning", "Securely delete every conversation and pending message from this device. This process overwrites the encrypted archives before removal making recovery impossible.");
    public string LocalizedPurgeUseCase => Services.AppServices.Localization.GetString("Settings.PurgeUseCase", "Use this if you're decommissioning the device or need a clean slate.");
    public string LocalizedTypePurgeConfirm => Services.AppServices.Localization.GetString("Settings.TypePurgeConfirm", "Type 'PURGE-ALL-DATA' to confirm:");
    public string LocalizedTypePurgeAllData => Services.AppServices.Localization.GetString("Settings.TypePurgeAllData", "Type: PURGE-ALL-DATA");
    public string LocalizedPurgeAllMessagesButton => Services.AppServices.Localization.GetString("Settings.PurgeAllMessagesButton", "Purge All Messages");
    public string LocalizedExportData => Services.AppServices.Localization.GetString("Settings.ExportData", "Export Data");
    public string LocalizedExportDataDescription => Services.AppServices.Localization.GetString("Settings.ExportDataDescription", "Create an encrypted backup of your local data. This includes messages, contacts, and settings.");
    public string LocalizedExportDataButton => Services.AppServices.Localization.GetString("Settings.ExportDataButton", "Export Encrypted Backup");
    public string LocalizedImportData => Services.AppServices.Localization.GetString("Settings.ImportData", "Restore Backup");
    public string LocalizedImportDataDescription => Services.AppServices.Localization.GetString("Settings.ImportDataDescription", "Restore from an encrypted backup created on this profile. Existing local data in included folders will be replaced.");
    public string LocalizedImportDataButton => Services.AppServices.Localization.GetString("Settings.ImportDataButton", "Import Backup");
    public string LocalizedExportMigrationBundle => Services.AppServices.Localization.GetString("Settings.ExportMigrationBundle", "Create Migration Bundle");
    public string LocalizedExportMigrationBundleDescription => Services.AppServices.Localization.GetString("Settings.ExportMigrationBundleDescription", "Create a one-time encrypted transfer bundle for moving to another device. A transfer code will be shown after export.");
    public string LocalizedExportMigrationBundleButton => Services.AppServices.Localization.GetString("Settings.ExportMigrationBundleButton", "Export Migration Bundle");
    public string LocalizedImportMigrationBundleButton => Services.AppServices.Localization.GetString("Settings.ImportMigrationBundleButton", "Import Backup / Migration Bundle");
    
    // Log Out panel strings
    public string LocalizedSignOut => Services.AppServices.Localization.GetString("Settings.SignOut", "Sign Out");
    public string LocalizedSignOutDescription => Services.AppServices.Localization.GetString("Settings.SignOutDescription", "Signing out will disconnect you from the application and require you to enter your passphrase again to access your account.");
    public string LocalizedDataRemains => Services.AppServices.Localization.GetString("Settings.DataRemains", "Your data will remain securely stored locally and will be available when you sign back in.");
    public string LocalizedCurrentSession => Services.AppServices.Localization.GetString("Settings.CurrentSession", "Current Session");
    public string LocalizedSessionStatus => Services.AppServices.Localization.GetString("Settings.SessionStatus", "Status:");
    public string LocalizedActive => Services.AppServices.Localization.GetString("Settings.Active", "Active");
    public string LocalizedLastActivity => Services.AppServices.Localization.GetString("Settings.LastActivity", "Last Activity:");
    public string LocalizedJustNow => Services.AppServices.Localization.GetString("Settings.JustNow", "Just now");
    public string LocalizedReadyToSignOut => Services.AppServices.Localization.GetString("Settings.ReadyToSignOut", "Ready to sign out?");
    public string LocalizedSignOutPrompt => Services.AppServices.Localization.GetString("Settings.SignOutPrompt", "Click the button below to sign out of your account. You'll need your passphrase to sign back in.");
    public string LocalizedSignOutButton => Services.AppServices.Localization.GetString("Settings.SignOutButton", "Sign Out");
    public string LocalizedSecurityReminder => Services.AppServices.Localization.GetString("Settings.SecurityReminder", "🔒 Security Reminder");
    public string LocalizedSecurityReminderText => Services.AppServices.Localization.GetString("Settings.SecurityReminderText", "Always sign out when using shared or public computers to protect your privacy and security.");



    
    // Populate available languages from localization files
    private void PopulateAvailableLanguages()
    {
        try
        {
            AvailableLanguages.Clear();
            var codes = Services.AppServices.Localization.GetAvailableLanguages();
            
            // Convert codes to display names and sort alphabetically
            var displayNames = new System.Collections.Generic.List<string>();
            foreach (var code in codes)
            {
                var displayName = GetLanguageDisplayName(code);
                displayNames.Add(displayName);
            }
            displayNames.Sort();
            
            // Add sorted languages to observable collection
            foreach (var displayName in displayNames)
            {
                AvailableLanguages.Add(displayName);
            }
            
            // Ensure at least English is available
            if (AvailableLanguages.Count == 0)
            {
                AvailableLanguages.Add("English (US)");
            }
        }
        catch
        {
            // Fallback to English if something fails
            AvailableLanguages.Clear();
            AvailableLanguages.Add("English (US)");
        }
    }
    
    // Populate theme items with current localization
    private void PopulateThemeItems()
    {
        try
        {
            var engine = AppServices.ThemeEngine;
            var registered = engine.GetRegisteredThemes();

                        Zer0Talk.Utilities.Logger.Log($"[PopulateThemeItems] Found {registered.Count} registered themes", 
                Utilities.LogLevel.Info, categoryOverride: "theme");            var themeEntries = registered.Values
                .Where(t => t != null && t.ThemeType != ThemeType.BuiltInTemplate)
                .Select(t => new
                {
                    ThemeId = t.Id,
                    Display = BuildThemeDisplayName(t),
                    Order = t.ThemeType switch
                    {
                        ThemeType.BuiltInLegacy => 0,
                        ThemeType.Custom => 1,
                        ThemeType.Imported => 2,
                        _ => 3
                    }
                })
                .OrderBy(t => t.Order)
                .ThenBy(t => t.Display, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            Zer0Talk.Utilities.Logger.Log($"[PopulateThemeItems] After filtering: {themeEntries.Count} theme entries", 
                Utilities.LogLevel.Info, categoryOverride: "theme");
            
            foreach (var entry in themeEntries)
            {
                Zer0Talk.Utilities.Logger.Log($"[PopulateThemeItems] - {entry.Display} (ID: {entry.ThemeId}, Order: {entry.Order})", 
                    Utilities.LogLevel.Info, categoryOverride: "theme");
            }

            if (themeEntries.Count == 0)
            {
                throw new InvalidOperationException("No registered themes");
            }

            var previousId = _selectedThemeId;

            ThemeItems.Clear();
            _themeIdOrder.Clear();

            foreach (var entry in themeEntries)
            {
                ThemeItems.Add(entry.Display);
                _themeIdOrder.Add(entry.ThemeId);
            }

            if (!string.IsNullOrWhiteSpace(previousId))
            {
                SetSelectedThemeId(previousId, updateIndex: true, triggerChange: false, refreshInspector: false);
            }
            else if (_themeIdOrder.Count > 0)
            {
                SetSelectedThemeId(_themeIdOrder[0], updateIndex: true, triggerChange: false, refreshInspector: false);
            }

            OnPropertyChanged(nameof(ThemeIndex));

            if (!_suppressThemeBinding)
            {
                RefreshThemeInspector();
            }
        }
        catch
        {
            ThemeItems.Clear();
            _themeIdOrder.Clear();

            ThemeItems.Add(LocalizedDark);
            _themeIdOrder.Add("legacy-dark");
            ThemeItems.Add(LocalizedLight);
            _themeIdOrder.Add("legacy-light");
            ThemeItems.Add(LocalizedSandy);
            _themeIdOrder.Add("legacy-sandy");
            ThemeItems.Add(LocalizedButter);
            _themeIdOrder.Add("legacy-butter");

            SetSelectedThemeId(_selectedThemeId, updateIndex: true, triggerChange: false, refreshInspector: false);
            OnPropertyChanged(nameof(ThemeIndex));
        }
    }

    private void OnThemesReloaded(object? sender, EventArgs e)
    {
        try
        {
            // Theme list changed (e.g., new custom theme saved), refresh the dropdown
            // Ensure we update on the UI thread
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    PopulateThemeItems();
                }
                catch (Exception ex)
                {
                    Zer0Talk.Utilities.Logger.Log($"[Settings] Error refreshing theme list: {ex.Message}", 
                        Utilities.LogLevel.Warning, categoryOverride: "theme");
                }
            });
        }
        catch (Exception ex)
        {
            Zer0Talk.Utilities.Logger.Log($"[Settings] Error dispatching theme refresh: {ex.Message}", 
                Utilities.LogLevel.Warning, categoryOverride: "theme");
        }
    }

    private string BuildThemeDisplayName(ThemeDefinition theme)
    {
        if (theme.ThemeType == ThemeType.BuiltInLegacy && theme.LegacyThemeOption.HasValue)
        {
            return theme.LegacyThemeOption.Value switch
            {
                ThemeOption.Dark => LocalizedDark,
                ThemeOption.Light => LocalizedLight,
                ThemeOption.Sandy => LocalizedSandy,
                ThemeOption.Butter => LocalizedButter,
                _ => theme.DisplayName
            };
        }

        var baseName = string.IsNullOrWhiteSpace(theme.DisplayName) ? theme.Id : theme.DisplayName;

        return theme.ThemeType switch
        {
            ThemeType.Custom => $"{baseName} (Custom)",
            ThemeType.Imported => $"{baseName} (Imported)",
            _ => baseName
        };
    }

    private static string ThemeOptionToId(ThemeOption option)
        => option switch
        {
            ThemeOption.Light => "legacy-light",
            ThemeOption.Sandy => "legacy-sandy",
            ThemeOption.Butter => "legacy-butter",
            _ => "legacy-dark"
        };

    private static ThemeOption ThemeIdToThemeOption(string? themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId))
        {
            return ThemeOption.Dark;
        }

        return themeId.Trim().ToLowerInvariant() switch
        {
            "legacy-light" => ThemeOption.Light,
            "legacy-sandy" => ThemeOption.Sandy,
            "legacy-butter" => ThemeOption.Butter,
            _ => ThemeOption.Dark
        };
    }

    private string GetResolvedSelectedThemeId()
    {
        var normalized = NormalizeThemeId(_selectedThemeId);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        if (_themeIdOrder.Count > 0)
        {
            return _themeIdOrder[0];
        }

        return "legacy-dark";
    }

    private bool TryGetThemeDefinition(string themeId, out ThemeDefinition? themeDef)
    {
        themeDef = null;
        try
        {
            var registered = AppServices.ThemeEngine.GetRegisteredThemes();
            if (registered.TryGetValue(themeId, out var def))
            {
                themeDef = def;
                return true;
            }
        }
        catch { }

        return false;
    }
    
    // Populate presence items with current localization
    private void PopulatePresenceItems()
    {
        try
        {
            var currentSelection = _defaultPresenceIndex;
            
            PresenceItems.Clear();
            PresenceItems.Add(LocalizedOnline);
            PresenceItems.Add(LocalizedAway);
            PresenceItems.Add(LocalizedDoNotDisturb);
            PresenceItems.Add(LocalizedOffline);
            
            // Restore selection
            if (currentSelection >= 0 && currentSelection < PresenceItems.Count)
            {
                _defaultPresenceIndex = currentSelection;
                OnPropertyChanged(nameof(DefaultPresenceIndex));
            }
        }
        catch
        {
            // Fallback to English
            PresenceItems.Clear();
            PresenceItems.Add("Online");
            PresenceItems.Add("Away");
            PresenceItems.Add("Do Not Disturb");
            PresenceItems.Add("Offline");
        }
    }
    
    // Refresh theme and presence selections after language change
    private void RefreshDropdownSelections()
    {
        try
        {
            // Save current selections
            var currentThemeId = _selectedThemeId;
            var currentPresence = _defaultPresenceIndex;

            var previousBindingState = _suppressThemeBinding;
            _suppressThemeBinding = true;

            // Set to -1 to force UI refresh
            _themeIndex = -1;
            OnPropertyChanged(nameof(ThemeIndex));

            _defaultPresenceIndex = -1;
            OnPropertyChanged(nameof(DefaultPresenceIndex));

            // Use dispatcher to restore after UI processes the change
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    _suppressThemeBinding = true;
                    SetSelectedThemeId(currentThemeId, updateIndex: true, triggerChange: false, refreshInspector: false);
                    OnPropertyChanged(nameof(ThemeIndex));
                    _defaultPresenceIndex = currentPresence >= 0 && currentPresence < PresenceItems.Count ? currentPresence : 0;
                    OnPropertyChanged(nameof(DefaultPresenceIndex));
                }
                catch { }
                finally
                {
                    _suppressThemeBinding = previousBindingState;
                }
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
        catch { }
    }
    
    // Helper to convert language code to display name
    private static string GetLanguageDisplayName(string code)
    {
        return code switch
        {
            "en" => "English (US)",
            "es" => "Spanish",
            "fr" => "French",
            "de" => "German",
            "ja" => "Japanese",
            "zh-CN" => "Chinese (Simplified)",
            "zh-TW" => "Chinese (Traditional)",
            "pt" => "Portuguese",
            "ru" => "Russian",
            "it" => "Italian",
            _ => code // Fallback to code itself
        };
    }
    
    // Helper to convert display name to language code
    private static string GetLanguageCode(string displayName)
    {
        // Map display names to ISO 639-1 codes
        return displayName switch
        {
            "English (US)" => "en",
            "Spanish" => "es",
            "French" => "fr",
            "German" => "de",
            "Japanese" => "ja",
            "Chinese (Simplified)" => "zh-CN",
            "Chinese (Traditional)" => "zh-TW",
            "Portuguese" => "pt",
            "Russian" => "ru",
            "Italian" => "it",
            _ => "en" // Default to English
        };
    }
    
    private void OnLanguageChanged()
    {
        // Refresh all localized properties when language changes
        // Suppress dirty checking since we're only updating display strings, not actual values
        var prevSuppress = _suppressDirtyCheck;
        _suppressDirtyCheck = true;
        try
        {
            // Repopulate theme and presence items with new translations
            PopulateThemeItems();
            PopulatePresenceItems();
            
            // Refresh dropdown selections to show new translations
            RefreshDropdownSelections();
            
            // Main menu items
            OnPropertyChanged(nameof(LocalizedSettingsTitle));
            OnPropertyChanged(nameof(LocalizedAppearance));
            OnPropertyChanged(nameof(LocalizedGeneral));
            OnPropertyChanged(nameof(LocalizedHotkeys));
            OnPropertyChanged(nameof(LocalizedProfile));
            OnPropertyChanged(nameof(LocalizedNetwork));
            OnPropertyChanged(nameof(LocalizedPerformance));
            OnPropertyChanged(nameof(LocalizedAccessibility));
            OnPropertyChanged(nameof(LocalizedAbout));
            OnPropertyChanged(nameof(LocalizedDangerZone));
            OnPropertyChanged(nameof(LocalizedLogOut));
            OnPropertyChanged(nameof(LocalizedLanguage));
            OnPropertyChanged(nameof(LocalizedLanguageHelp));
            OnPropertyChanged(nameof(LocalizedTheme));
            OnPropertyChanged(nameof(LocalizedThemeHelp));
            OnPropertyChanged(nameof(LocalizedFont));
            OnPropertyChanged(nameof(LocalizedFontHelp));
            OnPropertyChanged(nameof(LocalizedPrivacy));
            
            // General panel
            OnPropertyChanged(nameof(LocalizedDefaultPresence));
            OnPropertyChanged(nameof(LocalizedDefaultPresenceHelp));
            OnPropertyChanged(nameof(LocalizedStatus));
            OnPropertyChanged(nameof(LocalizedOnline));
            OnPropertyChanged(nameof(LocalizedAway));
            OnPropertyChanged(nameof(LocalizedDoNotDisturb));
            OnPropertyChanged(nameof(LocalizedOffline));
            OnPropertyChanged(nameof(LocalizedSuppressNotifications));
            OnPropertyChanged(nameof(LocalizedNotificationDuration));
            OnPropertyChanged(nameof(LocalizedDuration));
            OnPropertyChanged(nameof(LocalizedPrecise));
            OnPropertyChanged(nameof(LocalizedNotificationDurationHelp));
            OnPropertyChanged(nameof(LocalizedCurrentSetting));
            OnPropertyChanged(nameof(LocalizedSeconds));
            OnPropertyChanged(nameof(LocalizedOfflineAutomatic));
            OnPropertyChanged(nameof(LocalizedAllowAutoUpdates));
            OnPropertyChanged(nameof(LocalizedAllowAutoUpdatesHelp));
            OnPropertyChanged(nameof(LocalizedEnableSmoothScrolling));
            OnPropertyChanged(nameof(LocalizedEnableSmoothScrollingHelp));
            OnPropertyChanged(nameof(LocalizedCheckForUpdatesNow));
            OnPropertyChanged(nameof(LocalizedCheckForUpdatesNowHelp));
            OnPropertyChanged(nameof(LocalizedLastUpdateCheck));
            OnPropertyChanged(nameof(LocalizedNever));
            UpdateLastAutoUpdateCheckDisplay(_lastAutoUpdateCheckUtcRaw);
            OnPropertyChanged(nameof(LocalizedEnableNotificationBellFlash));
            OnPropertyChanged(nameof(LocalizedNotificationBellFlashHelp));
            OnPropertyChanged(nameof(LocalizedAutoLock));
            OnPropertyChanged(nameof(LocalizedEnableAutoLock));
            OnPropertyChanged(nameof(LocalizedAutoLockHelp));
            OnPropertyChanged(nameof(LocalizedMinutes));
            OnPropertyChanged(nameof(LocalizedAutoLockMinutesHelp));
            OnPropertyChanged(nameof(LocalizedLockOnMinimize));
            OnPropertyChanged(nameof(LocalizedLockOnMinimizeHelp));
            OnPropertyChanged(nameof(LocalizedLockBlurRadius));
            OnPropertyChanged(nameof(LocalizedLockBlurHelp));
            OnPropertyChanged(nameof(LocalizedBlockScreenCapture));
            OnPropertyChanged(nameof(LocalizedScreenCaptureWarning));
            OnPropertyChanged(nameof(LocalizedKeyVisibility));
            OnPropertyChanged(nameof(LocalizedShowPublicKeys));
            OnPropertyChanged(nameof(LocalizedShowPublicKeysHelp));
            OnPropertyChanged(nameof(LocalizedStreamerModeLabel));
            OnPropertyChanged(nameof(LocalizedStreamerModeHelp));
            OnPropertyChanged(nameof(LocalizedAudio));
            OnPropertyChanged(nameof(LocalizedAudioHelp));
            OnPropertyChanged(nameof(LocalizedMainVolume));
            OnPropertyChanged(nameof(LocalizedNotifications));
            OnPropertyChanged(nameof(LocalizedChatSounds));
            OnPropertyChanged(nameof(LocalizedSystemTray));
            OnPropertyChanged(nameof(LocalizedSystemTrayHelp));
            OnPropertyChanged(nameof(LocalizedShowSystemTrayIcon));
            OnPropertyChanged(nameof(LocalizedShowSystemTrayIconHelp));
            OnPropertyChanged(nameof(LocalizedMinimizeToTray));
            OnPropertyChanged(nameof(LocalizedMinimizeToTrayHelp));
            OnPropertyChanged(nameof(LocalizedStartMinimized));
            OnPropertyChanged(nameof(LocalizedStartMinimizedHelp));
            OnPropertyChanged(nameof(LocalizedRunOnStartup));
            OnPropertyChanged(nameof(LocalizedRunOnStartupHelp));
            OnPropertyChanged(nameof(LocalizedFamily));
            
            // Profile panel
            OnPropertyChanged(nameof(LocalizedIdentity));
            OnPropertyChanged(nameof(LocalizedUsername));
            OnPropertyChanged(nameof(LocalizedUID));
            OnPropertyChanged(nameof(LocalizedDisplayName));
            OnPropertyChanged(nameof(LocalizedPrevName));
            OnPropertyChanged(nameof(LocalizedPublicKey));
            OnPropertyChanged(nameof(LocalizedAvatar));
            OnPropertyChanged(nameof(LocalizedChoose));
            OnPropertyChanged(nameof(LocalizedClear));
            OnPropertyChanged(nameof(LocalizedRandomAvatar));
            OnPropertyChanged(nameof(LocalizedRandomAvatarTooltip));
            OnPropertyChanged(nameof(LocalizedShareAvatar));
            OnPropertyChanged(nameof(LocalizedBio));
            OnPropertyChanged(nameof(LocalizedBioHelp));
            
            // Hotkeys panel
            OnPropertyChanged(nameof(LocalizedCustomizeKeyboardShortcuts));
            OnPropertyChanged(nameof(LocalizedHotkeyInstructions));
            OnPropertyChanged(nameof(LocalizedLockApplication));
            OnPropertyChanged(nameof(LocalizedLockApplicationHelp));
            OnPropertyChanged(nameof(LocalizedHotkey));
            OnPropertyChanged(nameof(LocalizedPressKeys));
            OnPropertyChanged(nameof(LocalizedResetToDefault));
            OnPropertyChanged(nameof(LocalizedClearMessageInput));
            OnPropertyChanged(nameof(LocalizedClearMessageInputHelp));
            OnPropertyChanged(nameof(LocalizedStreamerModeHotkey));
            OnPropertyChanged(nameof(LocalizedStreamerModeHotkeyHelp));
            OnPropertyChanged(nameof(LocalizedHotkeyTips));
            OnPropertyChanged(nameof(LocalizedHotkeyTipsText));
            
            // Appearance panel extended
            OnPropertyChanged(nameof(LocalizedDark));
            OnPropertyChanged(nameof(LocalizedLight));
            OnPropertyChanged(nameof(LocalizedSandy));
            OnPropertyChanged(nameof(LocalizedButter));
            OnPropertyChanged(nameof(LocalizedEnterFontFamilyName));
            OnPropertyChanged(nameof(LocalizedOSScalingMessage));
            OnPropertyChanged(nameof(LocalizedAdditionalLanguagesMessage));
            OnPropertyChanged(nameof(LocalizedEnterDisplayName));
            
            // About panel
            OnPropertyChanged(nameof(LocalizedApplicationInformation));
            OnPropertyChanged(nameof(LocalizedVersion));
            OnPropertyChanged(nameof(LocalizedBuild));
            OnPropertyChanged(nameof(LocalizedDeveloper));
            OnPropertyChanged(nameof(LocalizedRepository));
            OnPropertyChanged(nameof(LocalizedLicense));
            OnPropertyChanged(nameof(LocalizedCopyright));
            
            // Performance panel
            OnPropertyChanged(nameof(LocalizedCPU));
            OnPropertyChanged(nameof(LocalizedGPU));
            OnPropertyChanged(nameof(LocalizedFramerate));
            OnPropertyChanged(nameof(LocalizedCCDOffinity));
            OnPropertyChanged(nameof(LocalizedNotRecommended));
            OnPropertyChanged(nameof(LocalizedEnforceRAMLimit));
            OnPropertyChanged(nameof(LocalizedEnforceRAMLimitHelp));
            OnPropertyChanged(nameof(LocalizedRAMLimit));
            OnPropertyChanged(nameof(LocalizedRamLimitHelp));
            OnPropertyChanged(nameof(LocalizedMBUnlimited));
            OnPropertyChanged(nameof(LocalizedEnableGPUAcceleration));
            OnPropertyChanged(nameof(LocalizedEnableGpuAccelerationHelp));
            OnPropertyChanged(nameof(LocalizedEnforceVRAMLimit));
            OnPropertyChanged(nameof(LocalizedEnforceVramLimitHelp));
            OnPropertyChanged(nameof(LocalizedVRAMLimit));
            OnPropertyChanged(nameof(LocalizedVramLimitHelp));
            OnPropertyChanged(nameof(LocalizedFPSThrottle));
            OnPropertyChanged(nameof(LocalizedFpsThrottleHelp));
            OnPropertyChanged(nameof(LocalizedFPSUnlimited));
            OnPropertyChanged(nameof(LocalizedRefreshRateThrottle));
            OnPropertyChanged(nameof(LocalizedRefreshRateThrottleHelp));
            OnPropertyChanged(nameof(LocalizedHzUnlimited));
            OnPropertyChanged(nameof(LocalizedBackgroundFramerate));
            OnPropertyChanged(nameof(LocalizedBackgroundFramerateHelp));
            OnPropertyChanged(nameof(LocalizedFPS));
            
            // Accessibility panel
            OnPropertyChanged(nameof(LocalizedAccessibilitySettings));
            OnPropertyChanged(nameof(LocalizedAccessibilityOSMessage));
            OnPropertyChanged(nameof(LocalizedKeyboardNavigation));
            OnPropertyChanged(nameof(LocalizedShowKeyboardFocusIndicators));
            OnPropertyChanged(nameof(LocalizedShowKeyboardFocusHelp));
            OnPropertyChanged(nameof(LocalizedEnhancedKeyboardNavigation));
            OnPropertyChanged(nameof(LocalizedEnhancedKeyboardNavigationHelp));
            OnPropertyChanged(nameof(LocalizedNavigationKeys));
            OnPropertyChanged(nameof(LocalizedNavigationHelp));
            OnPropertyChanged(nameof(LocalizedFontRendering));
            OnPropertyChanged(nameof(LocalizedOSFontSmoothing));
            OnPropertyChanged(nameof(LocalizedFontSmoothingMessage));
            
            // Debug/Network panels
            OnPropertyChanged(nameof(LocalizedDebugTools));
            OnPropertyChanged(nameof(LocalizedLogging));
            OnPropertyChanged(nameof(LocalizedEnableLogging));
            OnPropertyChanged(nameof(LocalizedPerformanceWarning));
            OnPropertyChanged(nameof(LocalizedPerformanceWarningText));
            OnPropertyChanged(nameof(LocalizedLogMaintenance));
            OnPropertyChanged(nameof(LocalizedAutoTrimUILog));
            OnPropertyChanged(nameof(LocalizedAutoTrimUILogHelp));
            OnPropertyChanged(nameof(LocalizedMaxEntries));
            OnPropertyChanged(nameof(LocalizedDebugUiLogMaxLinesHelp));
            OnPropertyChanged(nameof(LocalizedRetentionDays));
            OnPropertyChanged(nameof(LocalizedDebugLogRetentionDaysHelp));
            OnPropertyChanged(nameof(LocalizedSizeCap));
            OnPropertyChanged(nameof(LocalizedDebugLogSizeHelp));
            OnPropertyChanged(nameof(LocalizedLastMaintenance));
            OnPropertyChanged(nameof(LocalizedRunMaintenanceNow));
            OnPropertyChanged(nameof(LocalizedClearErrorLog));
            OnPropertyChanged(nameof(LocalizedClearErrorLogTooltip));
            OnPropertyChanged(nameof(LocalizedConnectionSettings));
            OnPropertyChanged(nameof(LocalizedRelaySettings));
            OnPropertyChanged(nameof(LocalizedDedicatedPeerNodeHelp));
            OnPropertyChanged(nameof(LocalizedRelayFallbackHelp));
            OnPropertyChanged(nameof(LocalizedRelayServerHelp));
            OnPropertyChanged(nameof(LocalizedRelayPresenceTimeout));
            OnPropertyChanged(nameof(LocalizedRelayDiscoveryTtl));
            OnPropertyChanged(nameof(LocalizedSavedRelays));
            OnPropertyChanged(nameof(LocalizedSavedRelaysHelp));
            OnPropertyChanged(nameof(LocalizedKnownDedicatedPeerNodesHelp));
            OnPropertyChanged(nameof(LocalizedAddBadActorIpHelp));
            OnPropertyChanged(nameof(LocalizedAddIpRangeHelp));
            
            // Network panel extended
            OnPropertyChanged(nameof(LocalizedPort));
            OnPropertyChanged(nameof(LocalizedPortLabel));
            OnPropertyChanged(nameof(LocalizedPortDescription));
            OnPropertyChanged(nameof(LocalizedDedicatedPeerNode));
            OnPropertyChanged(nameof(LocalizedEnableDedicatedPeerNode));
            OnPropertyChanged(nameof(LocalizedDedicatedPeerNodeInfo));
            OnPropertyChanged(nameof(LocalizedEnablesUPnP));
            OnPropertyChanged(nameof(LocalizedMakesNodeAccessible));
            OnPropertyChanged(nameof(LocalizedImprovesConnectivity));
            OnPropertyChanged(nameof(LocalizedDoesNotRelay));
            OnPropertyChanged(nameof(LocalizedRequiresPortForwarding));
            OnPropertyChanged(nameof(LocalizedRelayDescription1));
            OnPropertyChanged(nameof(LocalizedRelayDescription2));
            OnPropertyChanged(nameof(LocalizedRelayFallback));
            OnPropertyChanged(nameof(LocalizedEnableRelayFallback));
            OnPropertyChanged(nameof(LocalizedRelayServer));
            OnPropertyChanged(nameof(LocalizedRequirements));
            OnPropertyChanged(nameof(LocalizedRequiresDedicatedRelay));
            OnPropertyChanged(nameof(LocalizedServerMustBeConfigured));
            OnPropertyChanged(nameof(LocalizedRelayFormat));
            OnPropertyChanged(nameof(LocalizedLeaveBlankToDisable));
            OnPropertyChanged(nameof(LocalizedKnownDedicatedPeerNodes));
            OnPropertyChanged(nameof(LocalizedAdd));
            OnPropertyChanged(nameof(LocalizedRemove));
            OnPropertyChanged(nameof(LocalizedDiscoveredPeers));
            OnPropertyChanged(nameof(LocalizedLocationsEstimated));
            OnPropertyChanged(nameof(LocalizedBlockSelected));
            OnPropertyChanged(nameof(LocalizedUnblockSelected));
            OnPropertyChanged(nameof(LocalizedNode));
            OnPropertyChanged(nameof(LocalizedBlock));
            
            // Performance panel ComboBox items
            OnPropertyChanged(nameof(LocalizedAuto));
            OnPropertyChanged(nameof(LocalizedCCD0));
            OnPropertyChanged(nameof(LocalizedCCD1));
            OnPropertyChanged(nameof(LocalizedBothCCDs));
            
            // Accessibility panel extended
            OnPropertyChanged(nameof(LocalizedAccessibilityTitle));
            OnPropertyChanged(nameof(LocalizedDisplayScaling));
            OnPropertyChanged(nameof(LocalizedDisplayScalingPath));
            OnPropertyChanged(nameof(LocalizedHighContrast));
            OnPropertyChanged(nameof(LocalizedHighContrastPath));
            OnPropertyChanged(nameof(LocalizedAnimations));
            OnPropertyChanged(nameof(LocalizedAnimationsPath));
            OnPropertyChanged(nameof(LocalizedCursorSize));
            OnPropertyChanged(nameof(LocalizedCursorSizePath));
            OnPropertyChanged(nameof(LocalizedTextCursor));
            OnPropertyChanged(nameof(LocalizedTextCursorPath));
            OnPropertyChanged(nameof(LocalizedTab));
            OnPropertyChanged(nameof(LocalizedTabDescription));
            OnPropertyChanged(nameof(LocalizedShiftTab));
            OnPropertyChanged(nameof(LocalizedShiftTabDescription));
            OnPropertyChanged(nameof(LocalizedSpaceEnter));
            OnPropertyChanged(nameof(LocalizedSpaceEnterDescription));
            OnPropertyChanged(nameof(LocalizedArrowKeys));
            OnPropertyChanged(nameof(LocalizedArrowKeysDescription));
            OnPropertyChanged(nameof(LocalizedEsc));
            OnPropertyChanged(nameof(LocalizedEscDescription));
            OnPropertyChanged(nameof(LocalizedCtrlL));
            OnPropertyChanged(nameof(LocalizedCtrlLDescription));
            
            // About panel extended
            OnPropertyChanged(nameof(LocalizedAboutName));
            OnPropertyChanged(nameof(LocalizedAboutPlatform));
            OnPropertyChanged(nameof(LocalizedAboutAuthor));
            OnPropertyChanged(nameof(LocalizedAboutFrameworkInfo));
            OnPropertyChanged(nameof(LocalizedAboutAvaloniaUI));
            OnPropertyChanged(nameof(LocalizedAboutDotNetRuntime));
            OnPropertyChanged(nameof(LocalizedAboutLinksResources));
            OnPropertyChanged(nameof(LocalizedAboutDocumentation));
            OnPropertyChanged(nameof(LocalizedAboutPrivacyPolicy));
            OnPropertyChanged(nameof(LocalizedAboutAcknowledgments));
            OnPropertyChanged(nameof(LocalizedAboutBuiltWithAvalonia));
            OnPropertyChanged(nameof(LocalizedAboutSpecialThanks));
            
            // Danger Zone panel
            OnPropertyChanged(nameof(LocalizedDangerZoneTitle));
            OnPropertyChanged(nameof(LocalizedDangerZoneWarning));
            OnPropertyChanged(nameof(LocalizedDeleteAccount));
            OnPropertyChanged(nameof(LocalizedWhatHappens));
            OnPropertyChanged(nameof(LocalizedMessagesDeleted));
            OnPropertyChanged(nameof(LocalizedContactListRemoved));
            OnPropertyChanged(nameof(LocalizedProfileErased));
            OnPropertyChanged(nameof(LocalizedRemovedFromConversations));
            OnPropertyChanged(nameof(LocalizedCannotUndo));
            OnPropertyChanged(nameof(LocalizedDeleteAccountWarning));
            OnPropertyChanged(nameof(LocalizedTypeConfirmationCode));
            OnPropertyChanged(nameof(LocalizedEnterConfirmationCode));
            OnPropertyChanged(nameof(LocalizedDeleteAccountButton));
            OnPropertyChanged(nameof(LocalizedPurgeAllMessages));
            OnPropertyChanged(nameof(LocalizedWhatHappensPurge));
            OnPropertyChanged(nameof(LocalizedConversationsDeleted));
            OnPropertyChanged(nameof(LocalizedOutboxRemoved));
            OnPropertyChanged(nameof(LocalizedArchivesOverwritten));
            OnPropertyChanged(nameof(LocalizedRecoveryImpossible));
            OnPropertyChanged(nameof(LocalizedPurgeWarning));
            OnPropertyChanged(nameof(LocalizedPurgeUseCase));
            OnPropertyChanged(nameof(LocalizedTypePurgeConfirm));
            OnPropertyChanged(nameof(LocalizedTypePurgeAllData));
            OnPropertyChanged(nameof(LocalizedPurgeAllMessagesButton));
            OnPropertyChanged(nameof(LocalizedExportData));
            OnPropertyChanged(nameof(LocalizedExportDataDescription));
            OnPropertyChanged(nameof(LocalizedExportDataButton));
            OnPropertyChanged(nameof(LocalizedImportData));
            OnPropertyChanged(nameof(LocalizedImportDataDescription));
            OnPropertyChanged(nameof(LocalizedImportDataButton));
            
            // Log Out panel
            OnPropertyChanged(nameof(LocalizedSignOut));
            OnPropertyChanged(nameof(LocalizedSignOutDescription));
            OnPropertyChanged(nameof(LocalizedDataRemains));
            OnPropertyChanged(nameof(LocalizedCurrentSession));
            OnPropertyChanged(nameof(LocalizedSessionStatus));
            OnPropertyChanged(nameof(LocalizedActive));
            OnPropertyChanged(nameof(LocalizedLastActivity));
            OnPropertyChanged(nameof(LocalizedJustNow));
            OnPropertyChanged(nameof(LocalizedReadyToSignOut));
            OnPropertyChanged(nameof(LocalizedSignOutPrompt));
            OnPropertyChanged(nameof(LocalizedSignOutButton));
            OnPropertyChanged(nameof(LocalizedSecurityReminder));
            OnPropertyChanged(nameof(LocalizedSecurityReminderText));
            
            // Refresh ComboBox selections to update displayed text
            OnPropertyChanged(nameof(ThemeIndex));
            OnPropertyChanged(nameof(DefaultPresenceIndex));
        }
        catch { }
        finally
        {
            _suppressDirtyCheck = prevSuppress;
        }
    }

    // Sync ThemeIndex from persisted settings without marking Unsaved.
    // Useful on overlay open when settings may have been loaded in the background.
    public void SyncThemeFromPersisted()
    {
        try
        {
            var persisted = _settings.Settings;
            var themeId = NormalizeThemeId(persisted.ThemeId);
            if (string.IsNullOrWhiteSpace(themeId))
            {
                themeId = ThemeOptionToId(persisted.Theme);
            }

            var prevSuppress = _suppressThemeBinding;
            _suppressThemeBinding = true;
            SetSelectedThemeId(themeId, updateIndex: true, triggerChange: false, refreshInspector: false);
            OnPropertyChanged(nameof(ThemeIndex));
            _baseThemeId = _selectedThemeId;
            _baseThemeIndex = _themeIndex; // align baseline with persisted
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

    private bool _allowAutoUpdates;
    public bool AllowAutoUpdates { get => _allowAutoUpdates; set { if (_allowAutoUpdates != value) { _allowAutoUpdates = value; OnPropertyChanged(); } } }

    private bool _enableSmoothScrolling = true;
    public bool EnableSmoothScrolling { get => _enableSmoothScrolling; set { if (_enableSmoothScrolling != value) { _enableSmoothScrolling = value; OnPropertyChanged(); } } }

    private string? _lastAutoUpdateCheckUtcRaw;
    private string _lastAutoUpdateCheckDisplay = "Never";
    public string LastAutoUpdateCheckDisplay
    {
        get => _lastAutoUpdateCheckDisplay;
        private set
        {
            if (!string.Equals(_lastAutoUpdateCheckDisplay, value, StringComparison.Ordinal))
            {
                _lastAutoUpdateCheckDisplay = value;
                OnPropertyChanged();
            }
        }
    }
    
    private bool _suppressNotificationsInDnd;
    public bool SuppressNotificationsInDnd { get => _suppressNotificationsInDnd; set { if (_suppressNotificationsInDnd != value) { _suppressNotificationsInDnd = value; OnPropertyChanged(); } } }
    
    private double _notificationDurationSeconds;
    public double NotificationDurationSeconds { get => _notificationDurationSeconds; set { var v = Math.Clamp(value, 0.5, 30.0); if (Math.Abs(_notificationDurationSeconds - v) > 0.01) { _notificationDurationSeconds = v; OnPropertyChanged(); } } }
    
    private bool _enableNotificationBellFlash;
    public bool EnableNotificationBellFlash { get => _enableNotificationBellFlash; set { if (_enableNotificationBellFlash != value) { _enableNotificationBellFlash = value; OnPropertyChanged(); } } }

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
    public ICommand AssignRandomBundledAvatarCommand { get; }
    public ICommand DeleteAccountCommand { get; }
    public ICommand PurgeAllMessagesCommand { get; }
    public ICommand ResetLayoutCommand { get; }
    public ICommand RunLogMaintenanceCommand { get; }
    public ICommand CheckForUpdatesNowCommand { get; }
    public ICommand OpenDocumentationCommand { get; }
    public ICommand OpenPrivacyPolicyCommand { get; }
    public ICommand ExportDataCommand { get; }
    public ICommand ImportDataCommand { get; }
    public ICommand ExportMigrationBundleCommand { get; }
    public ICommand RetryNatVerificationCommand => new RelayCommand(async _ => { try { await AppServices.Nat.RetryVerificationAsync(); } catch { } });
    public ICommand CopyPublicKeyCommand { get; }
    public ICommand ExportThemeCommand { get; }  // Phase 3 Step 2
    public ICommand ImportThemeCommand { get; }  // Phase 3 Step 3
    public ICommand EditColorCommand { get; }     // Phase 3 Step 4
    public ICommand SaveColorEditCommand { get; } // Phase 3 Step 4
    public ICommand CancelColorEditCommand { get; } // Phase 3 Step 4
    public ICommand UndoColorEditCommand { get; } // Phase 3 Step 4
    public ICommand RedoColorEditCommand { get; } // Phase 3 Step 4
    public ICommand ToggleBatchEditModeCommand { get; } // Phase 3 Step 5
    public ICommand SelectAllColorsCommand { get; } // Phase 3 Step 5
    public ICommand DeselectAllColorsCommand { get; } // Phase 3 Step 5
    public ICommand CopyColorCommand { get; } // Phase 3 Step 5
    public ICommand PasteColorCommand { get; } // Phase 3 Step 5
    public ICommand RevertAllEditsCommand { get; } // Phase 3 Step 5
    public ICommand ApplyThemeLiveCommand { get; } // Phase 3 Step 5
    public ICommand EditGradientCommand { get; } // Phase 3 Step 6
    public ICommand SaveGradientEditCommand { get; } // Phase 3 Step 6
    public ICommand CancelGradientEditCommand { get; } // Phase 3 Step 6
    public ICommand ApplyGradientPresetCommand { get; } // Phase 3 Step 6
    public ICommand RenameThemeCommand { get; } // Phase 3 Step 7
    public ICommand DuplicateThemeCommand { get; } // Phase 3 Step 7
    public ICommand DeleteThemeCommand { get; } // Phase 3 Step 7
    public ICommand EditMetadataCommand { get; } // Phase 3 Step 7
    public ICommand SaveMetadataCommand { get; } // Phase 3 Step 7
    public ICommand CancelMetadataEditCommand { get; } // Phase 3 Step 7
    public ICommand ExportModifiedThemeCommand { get; } // Phase 3 Step 7
    public ICommand NewFromBlankTemplateCommand { get; } // Blank Template Feature
    public ICommand SaveAsCommand { get; } // Save read-only themes as new custom themes
    public ICommand LoadFromLegacyThemeCommand { get; } // Load legacy theme as editable template

    private bool _copyToastVisible;
    public bool CopyToastVisible { get => _copyToastVisible; set { _copyToastVisible = value; OnPropertyChanged(); } }
    private string _copyToastText = string.Empty;
    public string CopyToastText { get => _copyToastText; set { _copyToastText = value; OnPropertyChanged(); } }

    public event EventHandler? CloseRequested;

    public string ErrorMessage { get => _errorMessage; set { _errorMessage = value; OnPropertyChanged(); } }

    private void UpdateLastAutoUpdateCheckDisplay(string? utcRaw)
    {
        _lastAutoUpdateCheckUtcRaw = utcRaw;

        if (string.IsNullOrWhiteSpace(utcRaw))
        {
            LastAutoUpdateCheckDisplay = LocalizedNever;
            return;
        }

        if (DateTime.TryParse(utcRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedUtc))
        {
            LastAutoUpdateCheckDisplay = parsedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            return;
        }

        LastAutoUpdateCheckDisplay = utcRaw;
    }

    private async Task CheckForUpdatesNowAsync()
    {
        try
        {
            await AppServices.AutoUpdate.CheckForUpdatesAsync(userInitiated: true, System.Threading.CancellationToken.None);
            UpdateLastAutoUpdateCheckDisplay(_settings.Settings.LastAutoUpdateCheckUtc);
        }
        catch (Exception ex)
        {
            try { Logger.Log($"Settings: CheckForUpdatesNow failed - {ex.Message}"); } catch { }
            try { AppServices.Notifications.PostNotice(Models.NotificationType.Warning, "Update check failed. See logs for details.", isPersistent: true); } catch { }
        }
    }

    private static void OpenFileInShell(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            };
            _ = System.Diagnostics.Process.Start(psi);
        }
        catch { }
    }

    private void OpenDocumentation()
    {
        try
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "docs", "user-guide.md"),
                Path.Combine(AppContext.BaseDirectory, "README.md")
            };
            var file = candidates.FirstOrDefault(File.Exists);
            if (string.IsNullOrWhiteSpace(file))
            {
                _ = ShowSaveToastAsync("Documentation file not found", 2500);
                return;
            }

            OpenFileInShell(file);
            _ = ShowSaveToastAsync("Opened documentation", 1800);
        }
        catch
        {
            _ = ShowSaveToastAsync("Unable to open documentation", 2500);
        }
    }

    private async void OpenPrivacyPolicy()
    {
        try
        {
            var window = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
            if (window == null) return;

            var settings = _settings.Settings;
            var dlg = new Views.PrivacyPolicyDialog(alreadyAccepted: settings.PrivacyPolicyAccepted, doNotShowChecked: settings.DoNotShowPrivacyAgain, mandatory: false);
            await dlg.ShowDialog(window);
            settings.PrivacyPolicyAccepted = settings.PrivacyPolicyAccepted || dlg.Accepted;
            settings.DoNotShowPrivacyAgain = dlg.DoNotShowAgain;
            try { _settings.Save(AppServices.Passphrase); } catch { }
        }
        catch
        {
            _ = ShowSaveToastAsync("Unable to open privacy information", 2500);
        }
    }

    private static readonly string[] BackupIncludeRoots = BackupArchiveFormat.IncludeRoots;
    private static readonly string[] BackupIncludeFiles = BackupArchiveFormat.IncludeFiles;

    private async Task ExportDataAsync()
    {
        byte[]? zipBytes = null;
        byte[]? encryptedBytes = null;

        try
        {
            var window = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
            if (window == null)
            {
                await ShowSaveToastAsync("Unable to access file system", 2500);
                return;
            }

            var file = await window.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Export Encrypted Zer0Talk Backup",
                SuggestedFileName = $"zer0talk-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.ztbackup",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Zer0Talk Encrypted Backup")
                    {
                        Patterns = new[] { "*.ztbackup" }
                    },
                    new Avalonia.Platform.Storage.FilePickerFileType("ZIP Archive (legacy)")
                    {
                        Patterns = new[] { "*.zip" }
                    }
                }
            });

            if (file == null) return;
            var outputPath = file.Path.LocalPath;
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                await ShowSaveToastAsync("Invalid export destination", 2500);
                return;
            }

            var root = AppDataPaths.Root;
            if (!Directory.Exists(root))
            {
                await ShowSaveToastAsync("No app data found to export", 2500);
                return;
            }

            await Task.Run(() =>
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);

                zipBytes = CreateBackupZipBytes(root);
                if (outputPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    File.WriteAllBytes(outputPath, zipBytes);
                    return;
                }

                var encryption = new EncryptionService();
                encryptedBytes = encryption.Encrypt(zipBytes, AppServices.Passphrase);
                File.WriteAllBytes(outputPath, encryptedBytes);
            });

            await ShowSaveToastAsync($"Exported data: {Path.GetFileName(outputPath)}", 2500);
        }
        catch
        {
            await ShowSaveToastAsync("Data export failed", 3000);
        }
        finally
        {
            if (zipBytes != null) CryptographicOperations.ZeroMemory(zipBytes);
            if (encryptedBytes != null) CryptographicOperations.ZeroMemory(encryptedBytes);
        }
    }

    private async Task ExportMigrationBundleAsync()
    {
        byte[]? zipBytes = null;
        byte[]? encryptedBytes = null;

        try
        {
            var window = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
            if (window == null)
            {
                await ShowSaveToastAsync("Unable to access file system", 2500);
                return;
            }

            var file = await window.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Export Zer0Talk Migration Bundle",
                SuggestedFileName = $"zer0talk-migration-{DateTime.UtcNow:yyyyMMdd-HHmmss}.ztmigrate",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Zer0Talk Migration Bundle")
                    {
                        Patterns = new[] { "*.ztmigrate" }
                    }
                }
            });

            if (file == null) return;
            var outputPath = file.Path.LocalPath;
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                await ShowSaveToastAsync("Invalid export destination", 2500);
                return;
            }

            var root = AppDataPaths.Root;
            if (!Directory.Exists(root))
            {
                await ShowSaveToastAsync("No app data found to export", 2500);
                return;
            }

            var transferCode = GenerateTransferCode();

            await Task.Run(() =>
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
                zipBytes = CreateBackupZipBytes(root);
                var encryption = new EncryptionService();
                encryptedBytes = encryption.Encrypt(zipBytes, transferCode);
                File.WriteAllBytes(outputPath, encryptedBytes);
            });

            await AppServices.Dialogs.ShowInfoAsync(
                "Migration Bundle Created",
                $"Bundle: {Path.GetFileName(outputPath)}\n\nTransfer code (required to import):\n{transferCode}\n\nKeep this code private. The receiving device will need it to restore this bundle.",
                9000);
        }
        catch
        {
            await ShowSaveToastAsync("Migration bundle export failed", 3500);
        }
        finally
        {
            if (zipBytes != null) CryptographicOperations.ZeroMemory(zipBytes);
            if (encryptedBytes != null) CryptographicOperations.ZeroMemory(encryptedBytes);
        }
    }

    private static string GenerateTransferCode()
    {
        Span<byte> bytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(bytes);
        return string.Create(14, bytes.ToArray(), static (span, data) =>
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var chars = new char[12];
            for (var i = 0; i < 12; i++)
            {
                chars[i] = alphabet[data[i % data.Length] % alphabet.Length];
            }

            span[0] = chars[0];
            span[1] = chars[1];
            span[2] = chars[2];
            span[3] = chars[3];
            span[4] = '-';
            span[5] = chars[4];
            span[6] = chars[5];
            span[7] = chars[6];
            span[8] = chars[7];
            span[9] = '-';
            span[10] = chars[8];
            span[11] = chars[9];
            span[12] = chars[10];
            span[13] = chars[11];
        });
    }

    private async Task ImportDataAsync()
    {
        byte[]? containerBytes = null;
        byte[]? zipBytes = null;

        try
        {
            var window = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
            if (window == null)
            {
                await ShowSaveToastAsync("Unable to access file system", 2500);
                return;
            }

            var files = await window.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Import Zer0Talk Backup",
                AllowMultiple = false,
                FileTypeFilter = new System.Collections.Generic.List<Avalonia.Platform.Storage.FilePickerFileType>
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Zer0Talk Encrypted Backup") { Patterns = new[] { "*.ztbackup" } },
                    new Avalonia.Platform.Storage.FilePickerFileType("ZIP Archive (legacy)") { Patterns = new[] { "*.zip" } },
                    new Avalonia.Platform.Storage.FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
                }
            });

            if (files == null || files.Count == 0) return;

            var selectedPath = files[0].Path.LocalPath;
            if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
            {
                await ShowSaveToastAsync("Invalid backup file", 2500);
                return;
            }

            var confirmed = await AppServices.Dialogs.ConfirmWarningAsync(
                "Restore backup",
                "This will overwrite local data in backup-managed folders (messages, outbox, themes, logs, security) and key data files. Continue?",
                "Restore",
                "Cancel");

            if (!confirmed) return;

            containerBytes = await Task.Run(() => File.ReadAllBytes(selectedPath));
            if (selectedPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                zipBytes = containerBytes.ToArray();
            }
            else
            {
                var encryption = new EncryptionService();
                try
                {
                    zipBytes = await Task.Run(() => encryption.Decrypt(containerBytes, AppServices.Passphrase));
                }
                catch (CryptographicException)
                {
                    var transferCode = await AppServices.Dialogs.PromptAsync(
                        "Transfer Code Required",
                        "");
                    if (string.IsNullOrWhiteSpace(transferCode)) throw;
                    zipBytes = await Task.Run(() => encryption.Decrypt(containerBytes, transferCode.Trim()));
                }
            }

            await Task.Run(() => RestoreBackupZipBytes(AppDataPaths.Root, zipBytes));

            await ShowSaveToastAsync("Backup restored. Restart Zer0Talk to fully reload data.", 4500);
        }
        catch (CryptographicException)
        {
            await ShowSaveToastAsync("Backup decrypt failed (wrong passphrase/transfer code or corrupt file)", 3500);
        }
        catch
        {
            await ShowSaveToastAsync("Backup restore failed", 3500);
        }
        finally
        {
            if (containerBytes != null) CryptographicOperations.ZeroMemory(containerBytes);
            if (zipBytes != null) CryptographicOperations.ZeroMemory(zipBytes);
        }
    }

    private static byte[] CreateBackupZipBytes(string root)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var fileName in BackupIncludeFiles)
            {
                var full = Path.Combine(root, fileName);
                if (File.Exists(full))
                {
                    archive.CreateEntryFromFile(full, fileName, CompressionLevel.Optimal);
                }
            }

            foreach (var dirName in BackupIncludeRoots)
            {
                var fullDir = Path.Combine(root, dirName);
                if (!Directory.Exists(fullDir)) continue;

                foreach (var fullPath in Directory.GetFiles(fullDir, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
                    archive.CreateEntryFromFile(fullPath, rel, CompressionLevel.Optimal);
                }
            }

            BackupArchiveFormat.WriteManifest(archive, AppInfo.Version);
        }

        return stream.ToArray();
    }

    private static void RestoreBackupZipBytes(string root, byte[] zipBytes)
    {
        var stagingRoot = Path.Combine(Path.GetTempPath(), $"zer0talk-restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRoot);

        try
        {
            using var input = new MemoryStream(zipBytes, writable: false);
            using var archive = new ZipArchive(input, ZipArchiveMode.Read);

            if (BackupArchiveFormat.TryReadManifest(archive, out var manifest) && !BackupArchiveFormat.IsSupportedManifest(manifest))
            {
                throw new InvalidDataException("Unsupported backup format version.");
            }

            foreach (var entry in archive.Entries)
            {
                if (entry.Length == 0 && entry.FullName.EndsWith("/", StringComparison.Ordinal))
                {
                    continue;
                }

                var normalized = BackupArchiveFormat.NormalizeEntryPath(entry.FullName);
                if (string.IsNullOrWhiteSpace(normalized) || !BackupArchiveFormat.IsAllowedEntry(normalized))
                {
                    continue;
                }

                var stagedPath = Path.GetFullPath(Path.Combine(stagingRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
                if (!stagedPath.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var stagedDir = Path.GetDirectoryName(stagedPath);
                if (!string.IsNullOrWhiteSpace(stagedDir))
                {
                    Directory.CreateDirectory(stagedDir);
                }

                using var inStream = entry.Open();
                using var outStream = File.Create(stagedPath);
                inStream.CopyTo(outStream);
            }

            foreach (var fileName in BackupIncludeFiles)
            {
                var stagedFile = Path.Combine(stagingRoot, fileName);
                if (!File.Exists(stagedFile)) continue;

                var targetFile = Path.Combine(root, fileName);
                var targetDir = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrWhiteSpace(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                File.Copy(stagedFile, targetFile, overwrite: true);
            }

            foreach (var dirName in BackupIncludeRoots)
            {
                var stagedDir = Path.Combine(stagingRoot, dirName);
                if (!Directory.Exists(stagedDir)) continue;

                var targetDir = Path.Combine(root, dirName);
                if (Directory.Exists(targetDir))
                {
                    Directory.Delete(targetDir, recursive: true);
                }

                CopyDirectoryRecursive(stagedDir, targetDir);
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(stagingRoot))
                {
                    Directory.Delete(stagingRoot, recursive: true);
                }
            }
            catch { }
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            var destFile = Path.Combine(destDir, fileName);
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var name = Path.GetFileName(dir);
            var nextDest = Path.Combine(destDir, name);
            CopyDirectoryRecursive(dir, nextDest);
        }
    }

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
            var selectedThemeId = GetResolvedSelectedThemeId();

            s.ThemeId = selectedThemeId;
            var fallbackTheme = ThemeIdToThemeOption(selectedThemeId);
            s.Theme = fallbackTheme;
            // Performance settings
            s.CcdAffinityIndex = ClampRange(CcdAffinityIndex, 0, 3);
            s.IntelPCoreTargeting = IntelPCoreTargeting;
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
            s.AutoUpdateEnabled = AllowAutoUpdates;
            s.EnableSmoothScrolling = EnableSmoothScrolling;
            s.SuppressNotificationsInDnd = SuppressNotificationsInDnd;
            s.NotificationDurationSeconds = Math.Clamp(NotificationDurationSeconds, 0.5, 30.0);
            s.EnableNotificationBellFlash = EnableNotificationBellFlash;
            s.AutoLockEnabled = AutoLockEnabled;
            s.AutoLockMinutes = Math.Max(0, AutoLockMinutes);
            s.LockOnMinimize = LockOnMinimize;
            s.LockBlurRadius = ClampRange(LockBlurRadius, 0, 10);
            s.ShowPublicKeys = ShowPublicKeys;
            s.StreamerMode = StreamerMode;
            // Persist system tray settings
            s.ShowInSystemTray = ShowInSystemTray;
            s.MinimizeToTray = MinimizeToTray;
            s.RunOnStartup = RunOnStartup;
            s.StartMinimized = StartMinimized;
            s.Port = Port;
            s.MajorNode = MajorNode;
            s.EnableGeoBlocking = EnableGeoBlocking;
            s.RelayFallbackEnabled = RelayFallbackEnabled;
            s.RelayServer = string.IsNullOrWhiteSpace(RelayServer) ? null : RelayServer.Trim();
            s.RelayPresenceTimeoutSeconds = RelayPresenceTimeoutSeconds;
            s.RelayDiscoveryTtlMinutes = RelayDiscoveryTtlMinutes;
            s.SavedRelayServers = SavedRelayServers.ToList();
            s.ForceSeedBootstrap = ForceSeedBootstrap;
            s.WanSeedNodes = WanSeedNodes.ToList();
            // Persist accessibility settings
            s.ShowKeyboardFocus = ShowKeyboardFocus;
            s.EnhancedKeyboardNavigation = EnhancedKeyboardNavigation;
            // Persist hotkey settings
            s.LockHotkeyKey = (int)_lockHotkeyKey;
            s.LockHotkeyModifiers = (int)_lockHotkeyModifiers;
            s.ClearInputHotkeyKey = (int)_clearInputHotkeyKey;
            s.ClearInputHotkeyModifiers = (int)_clearInputHotkeyModifiers;
            s.StreamerModeHotkeyKey = (int)_streamerModeHotkeyKey;
            s.StreamerModeHotkeyModifiers = (int)_streamerModeHotkeyModifiers;
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
                    if (!string.IsNullOrWhiteSpace(pass))
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
            try { LogSettingsEvent($"Saved settings (ThemeId={s.ThemeId}, ThemeOption={s.Theme})"); } catch { }
            try { WritePerformanceLog($"Saved perf: CcdAffinityIndex={s.CcdAffinityIndex}, DisableGPU={s.DisableGpuAcceleration}, FPS={s.FpsThrottle}, Refresh={s.RefreshRateThrottle}, RAMmb={s.RamUsageLimitMb}, VRAMmb={s.VramUsageLimitMb}"); } catch { }
            try { ApplyGpuModeImmediate(s.DisableGpuAcceleration); } catch { }
            try { ApplyFpsThrottleImmediate(s.FpsThrottle); } catch { }
            try { ApplyRefreshRateThrottleImmediate(s.RefreshRateThrottle); } catch { }
            try { Zer0Talk.Services.FocusFramerateService.ApplyCurrentPolicy(); } catch { }
            try { ApplyCcdAffinityImmediate(s.CcdAffinityIndex); } catch { }
            try { if (_isIntelCpu) ApplyIntelPCoreTargetingImmediate(s.IntelPCoreTargeting); } catch { }
            // Apply theme + theme engine live
            try
            {
                var engine = AppServices.ThemeEngine;
                var appliedViaEngine = engine.SetThemeById(selectedThemeId);
                if (!appliedViaEngine)
                {
                    engine.SetTheme(fallbackTheme);
                }
            }
            catch { }

            try { _themeService.SetTheme(fallbackTheme); } catch { }
            try { _themeService.ApplyThemeEngine(s.UiFontFamily, 1.0); } catch { }
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
                        Zer0Talk.Services.ScreenCaptureProtection.SetExcludeFromCapture(w, BlockScreenCapture);
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
                HotkeyManager.Instance.UpdateKeyBinding("app.streamerMode", _streamerModeHotkeyKey, _streamerModeHotkeyModifiers);
            }
            catch { }

            // Apply streamer mode to MainWindowViewModel
            try
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life
                    && life.MainWindow?.DataContext is MainWindowViewModel mwvm)
                {
                    mwvm.IsStreamerMode = StreamerMode;
                }
            }
            catch { }

            // Refresh baseline (including theme now)
            CaptureBaseline();
            // Apply deferred passphrase clear (must run after settings are persisted)
            if (string.Equals(passphraseAction, "cleared", StringComparison.Ordinal))
            {
                try { AppServices.Passphrase = string.Empty; } catch { }
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
    private static bool UiLoggingEnabled => Zer0Talk.Utilities.LoggingPaths.Enabled;
    private static void WriteUiLog(string line)
    {
        try
        {
            if (!UiLoggingEnabled) return;
            System.IO.File.AppendAllText(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} {line}{Environment.NewLine}");
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
            Utilities.LoggingPaths.TryWrite(Utilities.LoggingPaths.Settings, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}");
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

    private void AssignRandomBundledAvatar()
    {
        try
        {
            var avatar = BundledAvatarService.TryGetRandomAvatarBytes();
            if (avatar == null || avatar.Length == 0)
            {
                _ = ShowToastAsync("No bundled avatars were found.");
                return;
            }

            _avatarBytes = avatar;
            RefreshAvatarPreview();
            _ = ShowToastAsync("Random avatar selected.");
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
        if (HasUnsavedChanges)
            ScheduleAutoSave();
    }

    private void ScheduleAutoSave()
    {
        // Debounce: reset the timer on every change; fires 700 ms after the last change.
        _autoSaveTimer?.Dispose();
        _autoSaveTimer = new System.Threading.Timer(_ =>
        {
            _autoSaveTimer?.Dispose();
            _autoSaveTimer = null;
            if (!HasUnsavedChanges) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                try { await SaveAsync(showToast: false, close: false).ConfigureAwait(false); } catch { }
            });
        }, null, 700, System.Threading.Timeout.Infinite);
    }

    private bool ComputeHasUnsavedChanges()
    {
        try
        {
            if (!string.Equals(_baseDisplayName, DisplayName ?? string.Empty, StringComparison.Ordinal)) return true;
            if (!string.Equals(_baseThemeId, _selectedThemeId, StringComparison.Ordinal)) return true;
            if (_baseShareAvatar != _shareAvatar) return true;
            if (!string.Equals(_baseBio, Bio ?? string.Empty, StringComparison.Ordinal)) return true;
            if (!string.Equals(_baseAvatarSig, GetAvatarSignature(_avatarBytes), StringComparison.Ordinal)) return true;
            if (_baseRememberPassphrase != _rememberPassphrase) return true;
            if (!string.Equals(_baseUiFontFamily ?? string.Empty, UiFontFamily ?? string.Empty, StringComparison.Ordinal)) return true;
            if (!string.Equals(_baseLanguage ?? "English (US)", Language ?? "English (US)", StringComparison.Ordinal)) return true;
            if (_baseDefaultPresenceIndex != _defaultPresenceIndex) return true;
            if (_baseAllowAutoUpdates != _allowAutoUpdates) return true;
            if (_baseEnableSmoothScrolling != _enableSmoothScrolling) return true;
            if (_baseSuppressNotificationsInDnd != _suppressNotificationsInDnd) return true;
            if (Math.Abs(_baseNotificationDurationSeconds - _notificationDurationSeconds) > 0.01) return true;
            if (_baseAutoLockEnabled != _autoLockEnabled) return true;
            if (_baseAutoLockMinutes != _autoLockMinutes) return true;
            if (_baseLockOnMinimize != _lockOnMinimize) return true;
            if (_baseLockBlurRadius != _lockBlurRadius) return true;
            if (_baseBlockScreenCapture != _blockScreenCapture) return true;
            if (_baseShowPublicKeys != _showPublicKeys) return true;
            if (_baseStreamerMode != _streamerMode) return true;
            if (_baseLockHotkeyKey != _lockHotkeyKey || _baseLockHotkeyModifiers != _lockHotkeyModifiers) return true;
            if (_baseShowKeyboardFocus != _showKeyboardFocus) return true;
            if (_baseEnhancedKeyboardNavigation != _enhancedKeyboardNavigation) return true;
            if (_baseShowInSystemTray != _showInSystemTray) return true;
            if (_baseMinimizeToTray != _minimizeToTray) return true;
            if (_baseRunOnStartup != _runOnStartup) return true;
            if (_baseStartMinimized != _startMinimized) return true;
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
            if (_basePort != Port) return true;
            if (_baseMajorNode != MajorNode) return true;
            if (_baseEnableGeoBlocking != EnableGeoBlocking) return true;
            if (_baseRelayFallbackEnabled != RelayFallbackEnabled) return true;
            if (!string.Equals(_baseRelayServer, RelayServer ?? string.Empty, StringComparison.Ordinal)) return true;
            if (_baseRelayPresenceTimeoutSeconds != RelayPresenceTimeoutSeconds) return true;
            if (_baseRelayDiscoveryTtlMinutes != RelayDiscoveryTtlMinutes) return true;
            if (!string.Equals(_baseSavedRelayServersSig, BuildSequenceSignature(SavedRelayServers), StringComparison.Ordinal)) return true;
            if (_baseForceSeedBootstrap != ForceSeedBootstrap) return true;
            if (!string.Equals(_baseWanSeedNodesSig, BuildSequenceSignature(WanSeedNodes), StringComparison.Ordinal)) return true;
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static string BuildSequenceSignature(System.Collections.Generic.IEnumerable<string> values)
    {
        try
        {
            return string.Join("\n", values.Select(v => (v ?? string.Empty).Trim()));
        }
        catch
        {
            return string.Empty;
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
            _baseThemeId = _selectedThemeId;
            _baseShareAvatar = ShareAvatar;
            _baseBio = Bio ?? string.Empty;
            _baseAvatarSig = GetAvatarSignature(_avatarBytes);
            _baseRememberPassphrase = _rememberPassphrase;
        _baseUiFontFamily = UiFontFamily;
            _baseLanguage = Language ?? "English (US)";
            _baseDefaultPresenceIndex = _defaultPresenceIndex;
            _baseAllowAutoUpdates = _allowAutoUpdates;
            _baseEnableSmoothScrolling = _enableSmoothScrolling;
            _baseSuppressNotificationsInDnd = _suppressNotificationsInDnd;
            _baseNotificationDurationSeconds = _notificationDurationSeconds;
            _baseEnableNotificationBellFlash = _enableNotificationBellFlash;
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
            _baseStreamerMode = _streamerMode;
            _baseLockHotkeyKey = _lockHotkeyKey;
            _baseLockHotkeyModifiers = _lockHotkeyModifiers;
            _baseShowKeyboardFocus = _showKeyboardFocus;
            _baseEnhancedKeyboardNavigation = _enhancedKeyboardNavigation;
            _baseShowInSystemTray = _showInSystemTray;
            _baseMinimizeToTray = _minimizeToTray;
            _baseRunOnStartup = _runOnStartup;
            _baseStartMinimized = _startMinimized;
            _baseEnableDebugLogAutoTrim = _enableDebugLogAutoTrim;
            _baseDebugUiLogMaxLines = _debugUiLogMaxLines;
            _baseDebugLogRetentionDays = _debugLogRetentionDays;
            _baseDebugLogMaxMegabytes = _debugLogMaxMegabytes;
            _baseEnableLogging = _enableLogging;
            _basePort = Port;
            _baseMajorNode = MajorNode;
            _baseEnableGeoBlocking = EnableGeoBlocking;
            _baseRelayFallbackEnabled = RelayFallbackEnabled;
            _baseRelayServer = RelayServer ?? string.Empty;
            _baseRelayPresenceTimeoutSeconds = RelayPresenceTimeoutSeconds;
            _baseRelayDiscoveryTtlMinutes = RelayDiscoveryTtlMinutes;
            _baseSavedRelayServersSig = BuildSequenceSignature(SavedRelayServers);
            _baseForceSeedBootstrap = ForceSeedBootstrap;
            _baseWanSeedNodesSig = BuildSequenceSignature(WanSeedNodes);
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
    private bool _streamerMode;
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
    public bool StreamerMode
    {
        get => _streamerMode;
        set { if (_streamerMode != value) { _streamerMode = value; OnPropertyChanged(); } }
    }

    // System Tray settings
    private bool _showInSystemTray;
    private bool _minimizeToTray;
    private bool _runOnStartup;
    private bool _startMinimized;

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
    public bool StartMinimized
    {
        get => _startMinimized;
        set
        {
            if (_startMinimized == value) return;
            _startMinimized = value;
            if (_startMinimized && !_showInSystemTray)
            {
                _showInSystemTray = true;
                OnPropertyChanged(nameof(ShowInSystemTray));
            }
            OnPropertyChanged();
        }
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

    private Avalonia.Input.Key _streamerModeHotkeyKey = Avalonia.Input.Key.F7;
    private Avalonia.Input.KeyModifiers _streamerModeHotkeyModifiers = Avalonia.Input.KeyModifiers.Control;
    private bool _isCapturingStreamerModeHotkey;

    public string StreamerModeHotkeyDisplay => HotkeyManager.FormatKeyBinding(_streamerModeHotkeyKey, _streamerModeHotkeyModifiers);
    public bool IsCapturingStreamerModeHotkey
    {
        get => _isCapturingStreamerModeHotkey;
        set
        {
            if (_isCapturingStreamerModeHotkey != value)
            {
                _isCapturingStreamerModeHotkey = value;
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

    public void StartCapturingStreamerModeHotkey()
    {
        IsCapturingStreamerModeHotkey = true;
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
            {
                var mainWindow = life.MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.KeyDown -= OnCaptureStreamerModeKeyDown;
                    mainWindow.KeyDown += OnCaptureStreamerModeKeyDown;
                }
            }
        }
        catch { IsCapturingStreamerModeHotkey = false; }
    }

    private void OnCaptureStreamerModeKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        try
        {
            if (!IsCapturingStreamerModeHotkey) return;

            if (e.Key == Avalonia.Input.Key.LeftCtrl || e.Key == Avalonia.Input.Key.RightCtrl ||
                e.Key == Avalonia.Input.Key.LeftAlt || e.Key == Avalonia.Input.Key.RightAlt ||
                e.Key == Avalonia.Input.Key.LeftShift || e.Key == Avalonia.Input.Key.RightShift ||
                e.Key == Avalonia.Input.Key.LWin || e.Key == Avalonia.Input.Key.RWin)
            {
                return;
            }

            if (IsReservedHotkey(e.Key, e.KeyModifiers))
            {
                _ = ShowSaveToastAsync("This hotkey is reserved by the system or conflicts with common text editor shortcuts.", 2500);
                IsCapturingStreamerModeHotkey = false;
                return;
            }

            bool isSettingToDefault = (e.Key == Avalonia.Input.Key.F7 && e.KeyModifiers == Avalonia.Input.KeyModifiers.Control);

            if (!isSettingToDefault && HotkeyManager.Instance.HasConflict(e.Key, e.KeyModifiers, "app.streamerMode"))
            {
                _ = ShowSaveToastAsync("Hotkey conflict! This combination is already in use.", 2000);
                IsCapturingStreamerModeHotkey = false;
                return;
            }

            _streamerModeHotkeyKey = e.Key;
            _streamerModeHotkeyModifiers = e.KeyModifiers;
            OnPropertyChanged(nameof(StreamerModeHotkeyDisplay));
            IsCapturingStreamerModeHotkey = false;

            if (sender is Window w)
            {
                w.KeyDown -= OnCaptureStreamerModeKeyDown;
            }

            e.Handled = true;
        }
        catch
        {
            IsCapturingStreamerModeHotkey = false;
        }
    }

    public ICommand ResetStreamerModeHotkeyCommand => new RelayCommand(_ =>
    {
        _streamerModeHotkeyKey = Avalonia.Input.Key.F7;
        _streamerModeHotkeyModifiers = Avalonia.Input.KeyModifiers.Control;
        OnPropertyChanged(nameof(StreamerModeHotkeyDisplay));
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
                    Zer0Talk.Utilities.LoggingPaths.SetEnabled(value);
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
                SyncDebugLogMegabytesToSize();
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
    private static bool AccessibilityLoggingEnabled => Zer0Talk.Utilities.LoggingPaths.Enabled;
    private static void WriteAccessibilityLog(string line)
    {
        try
        {
            if (!AccessibilityLoggingEnabled) return;
            System.IO.File.AppendAllText(Zer0Talk.Utilities.LoggingPaths.Debug, $"[ACCESS] {DateTime.Now:O} {line}{Environment.NewLine}");
        }
        catch { }
    }

    private bool _wipeLocalSettings;
    public bool WipeLocalSettings { get => _wipeLocalSettings; set { _wipeLocalSettings = value; OnPropertyChanged(); } }
    
    // Message burn security setting
    public bool UseEnhancedMessageBurn
    {
        get => AppServices.Settings.Settings.UseEnhancedMessageBurn;
        set
        {
            if (AppServices.Settings.Settings.UseEnhancedMessageBurn != value)
            {
                AppServices.Settings.Settings.UseEnhancedMessageBurn = value;
                try { AppServices.Settings.Save(AppServices.Passphrase); } catch { }
                OnPropertyChanged();
            }
        }
    }
    
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
            
            long totalWiped = 0;
            
            // Securely wipe account files with maximum security
            totalWiped += TrySecureWipe(AppServices.Accounts.GetPath());
            totalWiped += TrySecureWipe(GetContactsPath());
            totalWiped += TrySecureWipe(GetMessagesPath());
            totalWiped += TrySecureWipe(GetPeersPath());
            
            // Wipe messages directory
            totalWiped += TrySecureWipeDirectory(Utilities.AppDataPaths.Combine("messages"));
            
            // Wipe outbox directory
            totalWiped += TrySecureWipeDirectory(Utilities.AppDataPaths.Combine("outbox"));
            
            AppServices.Settings.ClearRememberedPassphrase();
            
            if (WipeLocalSettings)
            {
                totalWiped += TrySecureWipe(AppServices.Settings.GetSettingsPath());
                totalWiped += TrySecureWipeDirectory(GetThemesFolder());
            }
            
            Logger.Log($"Account deletion completed (local only). Securely wiped {totalWiped:N0} bytes.");
            (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        catch (System.Exception ex)
        {
            Logger.Log($"Account deletion error: {ex.Message}");
        }
    }
    
    private static long TrySecureWipe(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return 0;
            if (System.IO.File.Exists(path))
            {
                return Utilities.SecureFileWiper.SecureWipeFileMaximum(path);
            }
        }
        catch { }
        return 0;
    }
    
    private static long TrySecureWipeDirectory(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return 0;
            if (System.IO.Directory.Exists(path))
            {
                return Utilities.SecureFileWiper.SecureWipeDirectoryMaximum(path);
            }
        }
        catch { }
        return 0;
    }
    private static string GetContactsPath()
    {
    return Zer0Talk.Utilities.AppDataPaths.Combine("contacts.p2e");
    }
    private static string GetMessagesPath()
    {
    return Zer0Talk.Utilities.AppDataPaths.Combine("messages.p2e");
    }
    private static string GetPeersPath()
    {
    return Zer0Talk.Utilities.AppDataPaths.Combine("peers.p2e");
    }
    private static string GetThemesFolder()
    {
    return Zer0Talk.Utilities.AppDataPaths.Combine("Themes");
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

    private void PurgeAllLogs()
    {
        try
        {
            var logsDir = Utilities.LoggingPaths.LogsDirectory;
            if (!System.IO.Directory.Exists(logsDir))
            {
                _ = ShowSaveToastAsync("No logs directory found");
                return;
            }

            var files = System.IO.Directory.GetFiles(logsDir, "*", System.IO.SearchOption.TopDirectoryOnly)
                .Where(file =>
                {
                    var ext = System.IO.Path.GetExtension(file);
                    return string.Equals(ext, ".log", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase);
                })
                .ToArray();
            if (files.Length == 0)
            {
                _ = ShowSaveToastAsync("No log files to purge");
                return;
            }

            long bytesWiped = 0;
            int deletedCount = 0;
            
            foreach (var file in files)
            {
                try
                {
                    bytesWiped += Utilities.SecureFileWiper.SecureWipeFile(file);
                    deletedCount++;
                }
                catch { /* Skip files that can't be deleted */ }
            }
            
            _ = ShowSaveToastAsync($"Purged {deletedCount} log file(s) ({bytesWiped:N0} bytes wiped)");
        }
        catch (Exception ex)
        {
            _ = ShowSaveToastAsync($"Failed to purge logs: {ex.Message}");
        }
    }

    private void Logout()
    {
        try
        {
            // Do not clear passphrase; simply lock the app.
            try { Logger.Log("User logout requested (lock only; passphrase retained)"); } catch { }
            try { CloseRequested?.Invoke(this, EventArgs.Empty); } catch { }
            try { new Zer0Talk.Services.LockService().Lock(); } catch { }
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
    public bool RelayFallbackEnabled { get => NetworkVm.RelayFallbackEnabled; set => NetworkVm.RelayFallbackEnabled = value; }
    public string RelayServer { get => NetworkVm.RelayServer; set => NetworkVm.RelayServer = value; }
    public int RelayPresenceTimeoutSeconds { get => NetworkVm.RelayPresenceTimeoutSeconds; set => NetworkVm.RelayPresenceTimeoutSeconds = value; }
    public int RelayDiscoveryTtlMinutes { get => NetworkVm.RelayDiscoveryTtlMinutes; set => NetworkVm.RelayDiscoveryTtlMinutes = value; }
    public bool ForceSeedBootstrap { get => NetworkVm.ForceSeedBootstrap; set => NetworkVm.ForceSeedBootstrap = value; }
    public string NewRelayServer { get => NetworkVm.NewRelayServer; set => NetworkVm.NewRelayServer = value; }
    public string NewWanSeedNode { get => NetworkVm.NewWanSeedNode; set => NetworkVm.NewWanSeedNode = value; }
    public string NetworkInfoMessage => NetworkVm.InfoMessage;
    public string NetworkErrorMessage => NetworkVm.ErrorMessage;
    public string NewBlockedPeer { get; set; } = string.Empty; // TODO: Implement in NetworkViewModel

    // Network collections exposed from NetworkViewModel
    public System.Collections.ObjectModel.ObservableCollection<string> SavedRelayServers => NetworkVm.SavedRelayServers;
    public System.Collections.ObjectModel.ObservableCollection<string> WanSeedNodes => NetworkVm.WanSeedNodes;
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
    public string? SelectedRelayServer
    {
        get => NetworkVm.SelectedRelayServer;
        set => NetworkVm.SelectedRelayServer = value;
    }
    public string? SelectedWanSeedNode
    {
        get => NetworkVm.SelectedWanSeedNode;
        set => NetworkVm.SelectedWanSeedNode = value;
    }
    public NetworkViewModel.AdapterItem? SelectedNetworkAdapter 
    { 
        get => NetworkVm.SelectedAdapter; 
        set => NetworkVm.SelectedAdapter = value; 
    }

    // Network commands exposed from NetworkViewModel
    public ICommand AddRelayServerCommand => NetworkVm.AddRelayServerCommand;
    public ICommand RemoveRelayServerCommand => NetworkVm.RemoveRelayServerCommand;
    public ICommand UseRelayServerCommand => NetworkVm.UseRelayServerCommand;
    public ICommand UseDefaultRelayCommand => NetworkVm.UseDefaultRelayCommand;
    public System.Collections.ObjectModel.ObservableCollection<DefaultRelayEntry> DefaultRelays => NetworkVm.DefaultRelays;
    public ICommand AddWanSeedNodeCommand => NetworkVm.AddWanSeedNodeCommand;
    public ICommand RemoveWanSeedNodeCommand => NetworkVm.RemoveWanSeedNodeCommand;
    public ICommand UseWanSeedNodeCommand => NetworkVm.UseWanSeedNodeCommand;
    public ICommand BlockPeerCommand => NetworkVm.BlockPeerCommand;
    public ICommand UnblockPeerCommand => NetworkVm.UnblockPeerCommand;
    public ICommand BlockSelectedPeersCommand => NetworkVm.BlockSelectedPeersCommand;
    public ICommand UnblockSelectedPeersCommand => NetworkVm.UnblockSelectedPeersCommand;
    public ICommand ClearAllBlocksCommand => NetworkVm.ClearAllBlocksCommand;
    public ICommand RefreshPeersCommand => NetworkVm.RefreshPeersCommand;
    public ICommand RunFirewallTroubleshooterCommand => NetworkVm.RunFirewallTroubleshooterCommand;
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
    private int _debugLogSizeValue = 16;
    public int DebugLogSizeValue
    {
        get => _debugLogSizeValue;
        set
        {
            if (_debugLogSizeValue != value)
            {
                _debugLogSizeValue = value;
                OnPropertyChanged();
                SyncDebugLogSizeToMegabytes();
            }
        }
    }
    
    public int DebugLogSizeMaxValue => DebugLogSizeUnit == "KB" ? 524288 : 512;
    
    private string _debugLogSizeUnit = "MB";
    public string DebugLogSizeUnit
    {
        get => _debugLogSizeUnit;
        set
        {
            if (_debugLogSizeUnit != value)
            {
                _debugLogSizeUnit = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DebugLogSizeMaxValue));
                SyncDebugLogSizeToMegabytes();
            }
        }
    }
    
    private void SyncDebugLogSizeToMegabytes()
    {
        try
        {
            var megabytes = DebugLogSizeUnit == "KB" ? Math.Max(1, _debugLogSizeValue / 1024) : _debugLogSizeValue;
            DebugLogMaxMegabytes = megabytes;
        }
        catch { }
    }
    
    private void SyncDebugLogMegabytesToSize()
    {
        try
        {
            var mb = _debugLogMaxMegabytes;
            if (mb >= 1)
            {
                // Use MB for values >= 1 MB
                _debugLogSizeUnit = "MB";
                _debugLogSizeValue = mb;
            }
            else
            {
                // Use KB for sub-MB values (though minimum is 1 MB typically)
                _debugLogSizeUnit = "KB";
                _debugLogSizeValue = mb * 1024;
            }
            OnPropertyChanged(nameof(DebugLogSizeValue));
            OnPropertyChanged(nameof(DebugLogSizeUnit));
            OnPropertyChanged(nameof(DebugLogSizeMaxValue));
        }
        catch { }
    }
    
    public ICommand ClearErrorLogCommand { get; }

    #region Theme Inspector (Phase 3 - Read-Only)
    
    // Observable collection of color overrides from current theme
    private System.Collections.ObjectModel.ObservableCollection<ThemeColorEntry> _themeColors = new();
    public System.Collections.ObjectModel.ObservableCollection<ThemeColorEntry> ThemeColors
    {
        get => _themeColors;
        set { _themeColors = value; OnPropertyChanged(); }
    }

    // Observable collection of gradients from current theme
    private System.Collections.ObjectModel.ObservableCollection<ThemeGradientEntry> _themeGradients = new();
    public System.Collections.ObjectModel.ObservableCollection<ThemeGradientEntry> ThemeGradients
    {
        get => _themeGradients;
        set { _themeGradients = value; OnPropertyChanged(); }
    }

    // Theme metadata properties
    private string _currentThemeId = string.Empty;
    public string CurrentThemeId
    {
        get => _currentThemeId;
        set { _currentThemeId = value; OnPropertyChanged(); }
    }

    private string _currentThemeDisplayName = string.Empty;
    public string CurrentThemeDisplayName
    {
        get => _currentThemeDisplayName;
        set { _currentThemeDisplayName = value; OnPropertyChanged(); }
    }

    private string _currentThemeDescription = string.Empty;
    public string CurrentThemeDescription
    {
        get => _currentThemeDescription;
        set { _currentThemeDescription = value; OnPropertyChanged(); }
    }

    private string _currentThemeVersion = string.Empty;
    public string CurrentThemeVersion
    {
        get => _currentThemeVersion;
        set { _currentThemeVersion = value; OnPropertyChanged(); }
    }

    private string _currentThemeAuthor = string.Empty;
    public string CurrentThemeAuthor
    {
        get => _currentThemeAuthor;
        set { _currentThemeAuthor = value; OnPropertyChanged(); }
    }

    private bool _currentThemeAllowsCustomization;
    public bool CurrentThemeAllowsCustomization
    {
        get => _currentThemeAllowsCustomization;
        set { _currentThemeAllowsCustomization = value; OnPropertyChanged(); }
    }

    private bool _currentThemeIsReadOnly;
    public bool CurrentThemeIsReadOnly
    {
        get => _currentThemeIsReadOnly;
        set { _currentThemeIsReadOnly = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentThemeIsEditable)); }
    }

    public bool CurrentThemeIsEditable => !_currentThemeIsReadOnly;

    // Phase 3 Step 4: Color editing with undo/redo
    private readonly System.Collections.Generic.Stack<ColorEditAction> _undoStack = new();
    private readonly System.Collections.Generic.Stack<ColorEditAction> _redoStack = new();
    private ThemeColorEntry? _currentlyEditingColor = null;
    
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public bool IsEditingColor => _currentlyEditingColor != null;

    // Phase 3 Step 5: Batch editing and advanced features
    private bool _isBatchEditMode = false;
    public bool IsBatchEditMode
    {
        get => _isBatchEditMode;
        set
        {
            if (_isBatchEditMode != value)
            {
                _isBatchEditMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotBatchEditMode));
            }
        }
    }
    public bool IsNotBatchEditMode => !_isBatchEditMode;

    private readonly System.Collections.ObjectModel.ObservableCollection<string> _recentColors = new();
    public System.Collections.ObjectModel.ObservableCollection<string> RecentColors => _recentColors;

    private string? _copiedColor = null;
    public bool HasCopiedColor => !string.IsNullOrEmpty(_copiedColor);

    public int SelectedColorCount => ThemeColors.Count(c => c.IsSelected);
    public bool HasSelectedColors => SelectedColorCount > 0;

    // Phase 3 Step 6: Gradient editing
    private ThemeGradientEntry? _currentlyEditingGradient = null;
    public bool IsEditingGradient => _currentlyEditingGradient != null;

    private readonly System.Collections.Generic.List<GradientPreset> _gradientPresets = new()
    {
        new GradientPreset { Name = "Sunset", StartColor = "#FF6B6B", EndColor = "#FFD93D", Angle = 135 },
        new GradientPreset { Name = "Ocean", StartColor = "#4FACFE", EndColor = "#00F2FE", Angle = 180 },
        new GradientPreset { Name = "Forest", StartColor = "#38EF7D", EndColor = "#11998E", Angle = 90 },
        new GradientPreset { Name = "Purple Haze", StartColor = "#A18CD1", EndColor = "#FBC2EB", Angle = 45 },
        new GradientPreset { Name = "Fire", StartColor = "#FF0844", EndColor = "#FFBC0D", Angle = 0 },
        new GradientPreset { Name = "Ice", StartColor = "#E0EAFC", EndColor = "#CFDEF3", Angle = 270 }
    };

    public System.Collections.Generic.IReadOnlyList<GradientPreset> GradientPresets => _gradientPresets;

    // Load From Legacy Theme feature
    private readonly System.Collections.Generic.List<LegacyThemeOption> _legacyThemes = new()
    {
        new LegacyThemeOption { DisplayName = "Dark", ThemeId = "legacy-dark", Description = "Classic dark theme" },
        new LegacyThemeOption { DisplayName = "Light", ThemeId = "legacy-light", Description = "Classic light theme" },
        new LegacyThemeOption { DisplayName = "Sandy", ThemeId = "legacy-sandy", Description = "Warm sandy theme" },
        new LegacyThemeOption { DisplayName = "Butter", ThemeId = "legacy-butter", Description = "Soft butter theme" }
    };

    public System.Collections.Generic.IReadOnlyList<LegacyThemeOption> LegacyThemes => _legacyThemes;

    private LegacyThemeOption? _selectedLegacyTheme = null;
    public LegacyThemeOption? SelectedLegacyTheme
    {
        get => _selectedLegacyTheme;
        set
        {
            if (!Equals(_selectedLegacyTheme, value))
            {
                _selectedLegacyTheme = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanLoadLegacyTheme));
                // Keep SelectedLegacyThemeId in sync for SelectedValue binding
                try { SelectedLegacyThemeId = _selectedLegacyTheme?.ThemeId ?? string.Empty; } catch { }
            }
        }
    }

    private string _selectedLegacyThemeId = string.Empty;
    public string SelectedLegacyThemeId
    {
        get => _selectedLegacyThemeId;
        set
        {
            if (!string.Equals(_selectedLegacyThemeId, value, StringComparison.Ordinal))
            {
                _selectedLegacyThemeId = value ?? string.Empty;
                OnPropertyChanged();
                // Update SelectedLegacyTheme reference to match the id (if possible)
                try
                {
                    var match = _legacyThemes.FirstOrDefault(t => string.Equals(t.ThemeId, _selectedLegacyThemeId, StringComparison.Ordinal));
                    if (!Equals(_selectedLegacyTheme, match))
                    {
                        _selectedLegacyTheme = match;
                        OnPropertyChanged(nameof(SelectedLegacyTheme));
                        OnPropertyChanged(nameof(CanLoadLegacyTheme));
                    }
                }
                catch { }
            }
        }
    }

    // Helper property used by the Load button to enable/disable reliably
    public bool CanLoadLegacyTheme => SelectedLegacyTheme != null && !IsEditingColor && !IsEditingGradient && !IsEditingMetadata;

    // Phase 3 Step 7: Theme management and metadata editing
    private bool _isEditingMetadata = false;
    public bool IsEditingMetadata
    {
        get => _isEditingMetadata;
        set
        {
            if (_isEditingMetadata != value)
            {
                _isEditingMetadata = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanLoadLegacyTheme));
            }
        }
    }

    // Editable metadata properties
    private string _editableThemeName = string.Empty;
    public string EditableThemeName
    {
        get => _editableThemeName;
        set { _editableThemeName = value; OnPropertyChanged(); }
    }

    private string _editableThemeDescription = string.Empty;
    public string EditableThemeDescription
    {
        get => _editableThemeDescription;
        set { _editableThemeDescription = value; OnPropertyChanged(); }
    }

    private string _editableThemeAuthor = string.Empty;
    public string EditableThemeAuthor
    {
        get => _editableThemeAuthor;
        set { _editableThemeAuthor = value; OnPropertyChanged(); }
    }

    private string _editableThemeVersion = string.Empty;
    public string EditableThemeVersion
    {
        get => _editableThemeVersion;
        set { _editableThemeVersion = value; OnPropertyChanged(); }
    }

    // Helper method to load current theme data into inspector
    private void RefreshThemeInspector()
    {
        try
        {
            var themeId = GetResolvedSelectedThemeId();

            if (TryGetThemeDefinition(themeId, out var themeDef) && themeDef != null)
            {
                CurrentThemeId = themeDef.Id;
                CurrentThemeDisplayName = themeDef.DisplayName;
                CurrentThemeDescription = themeDef.Description ?? "No description available";
                CurrentThemeVersion = themeDef.Version;
                CurrentThemeAuthor = themeDef.Author ?? "Unknown";
                CurrentThemeAllowsCustomization = themeDef.AllowsCustomization;
                CurrentThemeIsReadOnly = themeDef.IsReadOnly;

                // Populate color overrides
                ThemeColors.Clear();
                foreach (var kvp in themeDef.ColorOverrides)
                {
                    ThemeColors.Add(new ThemeColorEntry
                    {
                        ResourceKey = kvp.Key,
                        ColorValue = kvp.Value,
                        IsEditable = false // Read-only for Phase 3 Step 1
                    });
                }

                // Populate gradients
                ThemeGradients.Clear();
                foreach (var kvp in themeDef.Gradients)
                {
                    ThemeGradients.Add(new ThemeGradientEntry
                    {
                        ResourceKey = kvp.Key,
                        GradientDefinition = kvp.Value,
                        IsEditable = false // Read-only for Phase 3 Step 1
                    });
                }
            }
            else
            {
                // Fallback if theme not found
                CurrentThemeId = themeId;
                CurrentThemeDisplayName = string.IsNullOrWhiteSpace(themeId) ? "Unknown Theme" : themeId;
                CurrentThemeDescription = "Theme definition not found";
                CurrentThemeVersion = "1.0.0";
                CurrentThemeAuthor = "Unknown";
                CurrentThemeAllowsCustomization = false;
                CurrentThemeIsReadOnly = false;
                ThemeColors.Clear();
                ThemeGradients.Clear();
            }
        }
        catch (Exception ex)
        {
            Zer0Talk.Utilities.Logger.Log($"[Theme Inspector] Error refreshing theme data: {ex.Message}", Utilities.LogLevel.Error);
            CurrentThemeId = "error";
            CurrentThemeDisplayName = "Error Loading Theme";
            CurrentThemeDescription = ex.Message;
            ThemeColors.Clear();
            ThemeGradients.Clear();
        }
    }

    // Phase 3 Step 2: Export current theme to .zttheme file
    private async System.Threading.Tasks.Task ExportCurrentThemeAsync()
    {
        try
        {
            var engine = AppServices.ThemeEngine;
            var registered = engine.GetRegisteredThemes();

            var themeId = GetResolvedSelectedThemeId();

            if (!registered.TryGetValue(themeId, out var themeDef))
            {
                await ShowSaveToastAsync("❌ Theme not found", 3000);
                return;
            }

            // Get main window for file dialog
            var window = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (window == null)
            {
                await ShowSaveToastAsync("❌ Cannot open file dialog", 3000);
                return;
            }

            // Use StorageProvider API for file picker
            var file = await window.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Export Theme",
                SuggestedFileName = $"{themeDef.DisplayName}.zttheme",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Zer0Talk Theme Files")
                    {
                        Patterns = new[] { "*.zttheme" }
                    },
                    new Avalonia.Platform.Storage.FilePickerFileType("All Files")
                    {
                        Patterns = new[] { "*" }
                    }
                }
            });

            if (file == null)
            {
                // User cancelled
                return;
            }

            // Update ModifiedAt timestamp before export
            themeDef.ModifiedAt = DateTime.UtcNow;

            // Get file path from storage file
            var filePath = file.Path.LocalPath;

            // Save theme to file
            themeDef.SaveToFile(filePath);

            await ShowSaveToastAsync($"✅ Theme exported to {System.IO.Path.GetFileName(filePath)}", 3000);
        }
        catch (Exception ex)
        {
            Zer0Talk.Utilities.Logger.Log($"[Theme Export] Error exporting theme: {ex.Message}", Utilities.LogLevel.Error);
            await ShowSaveToastAsync($"❌ Export failed: {ex.Message}", 4000);
        }
    }

    // Phase 3 Step 3: Import theme from .zttheme file with validation
    private async System.Threading.Tasks.Task ImportThemeAsync()
    {
        try
        {
            // Get main window for file dialog
            var window = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (window == null)
            {
                await ShowSaveToastAsync("❌ Cannot open file dialog", 3000);
                return;
            }

            // Use StorageProvider API for file picker
            var files = await window.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Import Theme",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Zer0Talk Theme Files")
                    {
                        Patterns = new[] { "*.zttheme" }
                    },
                    new Avalonia.Platform.Storage.FilePickerFileType("All Files")
                    {
                        Patterns = new[] { "*" }
                    }
                }
            });

            if (files == null || files.Count == 0)
            {
                // User cancelled
                return;
            }

            var filePath = files[0].Path.LocalPath;
            var fileName = System.IO.Path.GetFileName(filePath);

            // Load and validate theme file
            var themeDef = Models.ThemeDefinition.LoadFromFile(filePath, out var warnings);

            // Display warnings if any
            if (warnings.Count > 0)
            {
                var warningMsg = string.Join("\n", warnings.Take(5));
                if (warnings.Count > 5)
                {
                    warningMsg += $"\n... and {warnings.Count - 5} more warnings";
                }

                Zer0Talk.Utilities.Logger.Log($"[Theme Import] Theme '{themeDef.DisplayName}' imported with {warnings.Count} warning(s)", Utilities.LogLevel.Warning, categoryOverride: "theme");
                await ShowSaveToastAsync($"⚠️ Theme imported with warnings:\n{warningMsg}", 6000);
            }

            // Preview theme in inspector (without registering yet)
            CurrentThemeId = themeDef.Id;
            CurrentThemeDisplayName = themeDef.DisplayName;
            CurrentThemeDescription = themeDef.Description ?? "(No description)";
            CurrentThemeVersion = themeDef.Version;
            CurrentThemeAuthor = themeDef.Author ?? "(Unknown)";

            // Populate colors
            ThemeColors.Clear();
            if (themeDef.ColorOverrides != null)
            {
                foreach (var kvp in themeDef.ColorOverrides.OrderBy(x => x.Key))
                {
                    ThemeColors.Add(new ThemeColorEntry
                    {
                        ResourceKey = kvp.Key,
                        ColorValue = kvp.Value
                    });
                }
            }

            // Populate gradients
            ThemeGradients.Clear();
            if (themeDef.Gradients != null)
            {
                foreach (var kvp in themeDef.Gradients.OrderBy(x => x.Key))
                {
                    ThemeGradients.Add(new ThemeGradientEntry
                    {
                        ResourceKey = kvp.Key,
                        GradientDefinition = kvp.Value
                    });
                }
            }

            // Ask for confirmation before registering
            var confirmMsg = $"Preview theme '{themeDef.DisplayName}' loaded.\n\n" +
                            $"Do you want to register this theme?\n" +
                            $"(You can apply it from the theme dropdown after registration)";

            // For Step 3, we just show preview - full registration UI will come in later steps
            // For now, show success toast
            await ShowSaveToastAsync($"✅ Theme '{themeDef.DisplayName}' imported and previewed\n" +
                                    $"Theme registration UI will be added in Step 4+", 5000);

            Zer0Talk.Utilities.Logger.Log($"[Theme Import] Successfully imported theme '{themeDef.DisplayName}' from {fileName}", Utilities.LogLevel.Info, categoryOverride: "theme");
        }
        catch (InvalidOperationException ex)
        {
            // Validation errors
            Zer0Talk.Utilities.Logger.Log($"[Theme Import] Validation error: {ex.Message}", Utilities.LogLevel.Error, categoryOverride: "theme");
            await ShowSaveToastAsync($"❌ Invalid theme file:\n{ex.Message}", 5000);
        }
        catch (Exception ex)
        {
            Zer0Talk.Utilities.Logger.Log($"[Theme Import] Error importing theme: {ex.Message}", Utilities.LogLevel.Error, categoryOverride: "theme");
            await ShowSaveToastAsync($"❌ Import failed: {ex.Message}", 4000);
        }
    }

    // Phase 3 Step 4: Color editing methods
    private void StartEditingColor(ThemeColorEntry? entry)
    {
        if (entry == null || _currentlyEditingColor != null) return;

        _currentlyEditingColor = entry;
        entry.IsEditing = true;
        entry.OriginalValue = entry.ColorValue; // Store original for cancel
        
        OnPropertyChanged(nameof(IsEditingColor));
        OnPropertyChanged(nameof(CanLoadLegacyTheme));
        Zer0Talk.Utilities.Logger.Log($"[Theme Edit] Started editing color '{entry.ResourceKey}' (current: {entry.ColorValue})", Utilities.LogLevel.Info, categoryOverride: "theme");
    }

    private async System.Threading.Tasks.Task SaveColorEditAsync()
    {
        if (_currentlyEditingColor == null) return;

        var entry = _currentlyEditingColor;
        var oldValue = entry.OriginalValue ?? entry.ColorValue;
        var newValue = entry.ColorValue;

        // Validate color format
        if (!Models.ThemeDefinition.IsValidColorPublic(newValue))
        {
            await ShowSaveToastAsync($"❌ Invalid color format: {newValue}\nExpected: #RGB, #ARGB, #RRGGBB, or #AARRGGBB", 4000);
            return;
        }

        // Only save if changed
        if (oldValue != newValue)
        {
            // Add to undo stack
            _undoStack.Push(new ColorEditAction
            {
                ResourceKey = entry.ResourceKey,
                OldValue = oldValue,
                NewValue = newValue
            });
            _redoStack.Clear(); // Clear redo stack on new action

            // Add to recent colors (Step 5)
            AddToRecentColors(newValue);

            Zer0Talk.Utilities.Logger.Log($"[Theme Edit] Saved color edit '{entry.ResourceKey}': {oldValue} → {newValue}", Utilities.LogLevel.Info, categoryOverride: "theme");
            await ShowSaveToastAsync($"✅ Color updated: {entry.ResourceKey}", 2000);
            
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
        }

        entry.IsEditing = false;
        _currentlyEditingColor = null;
        OnPropertyChanged(nameof(IsEditingColor));
        OnPropertyChanged(nameof(CanLoadLegacyTheme));
    }

    private void CancelColorEdit()
    {
        if (_currentlyEditingColor == null) return;

        var entry = _currentlyEditingColor;
        entry.ColorValue = entry.OriginalValue ?? entry.ColorValue; // Restore original
        entry.IsEditing = false;
        
        _currentlyEditingColor = null;
        OnPropertyChanged(nameof(IsEditingColor));
        OnPropertyChanged(nameof(CanLoadLegacyTheme));
        
        Zer0Talk.Utilities.Logger.Log($"[Theme Edit] Cancelled editing color '{entry.ResourceKey}'", Utilities.LogLevel.Info, categoryOverride: "theme");
    }

    private void UndoColorEdit()
    {
        if (_undoStack.Count == 0) return;

        var action = _undoStack.Pop();
        _redoStack.Push(action);

        // Find and update the color entry
        var entry = ThemeColors.FirstOrDefault(c => c.ResourceKey == action.ResourceKey);
        if (entry != null)
        {
            entry.ColorValue = action.OldValue;
            Zer0Talk.Utilities.Logger.Log($"[Theme Edit] Undo: {action.ResourceKey} restored to {action.OldValue}", Utilities.LogLevel.Info, categoryOverride: "theme");
        }

        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void RedoColorEdit()
    {
        if (_redoStack.Count == 0) return;

        var action = _redoStack.Pop();
        _undoStack.Push(action);

        // Find and update the color entry
        var entry = ThemeColors.FirstOrDefault(c => c.ResourceKey == action.ResourceKey);
        if (entry != null)
        {
            entry.ColorValue = action.NewValue;
            Zer0Talk.Utilities.Logger.Log($"[Theme Edit] Redo: {action.ResourceKey} changed to {action.NewValue}", Utilities.LogLevel.Info, categoryOverride: "theme");
        }

        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    // Phase 3 Step 5: Batch editing and advanced palette management
    private void ToggleBatchEditMode()
    {
        IsBatchEditMode = !IsBatchEditMode;
        
        if (!IsBatchEditMode)
        {
            // Exiting batch mode - deselect all
            DeselectAllColors();
        }
        
        Zer0Talk.Utilities.Logger.Log($"[Theme Edit] Batch edit mode: {(IsBatchEditMode ? "ON" : "OFF")}", 
                                   Utilities.LogLevel.Info, categoryOverride: "theme");
    }

    private void SelectAllColors()
    {
        foreach (var color in ThemeColors)
        {
            color.IsSelected = true;
        }
        OnPropertyChanged(nameof(SelectedColorCount));
        OnPropertyChanged(nameof(HasSelectedColors));
    }

    private void DeselectAllColors()
    {
        foreach (var color in ThemeColors)
        {
            color.IsSelected = false;
        }
        OnPropertyChanged(nameof(SelectedColorCount));
        OnPropertyChanged(nameof(HasSelectedColors));
    }

    private void CopyColor(ThemeColorEntry? entry)
    {
        if (entry == null) return;

        _copiedColor = entry.ColorValue;
        OnPropertyChanged(nameof(HasCopiedColor));
        
        Zer0Talk.Utilities.Logger.Log($"[Theme Edit] Copied color '{entry.ResourceKey}': {entry.ColorValue}", 
                                   Utilities.LogLevel.Info, categoryOverride: "theme");
    }

    private void PasteColor(ThemeColorEntry? entry)
    {
        if (entry == null || string.IsNullOrEmpty(_copiedColor)) return;

        var oldValue = entry.ColorValue;
        var newValue = _copiedColor;

        if (oldValue != newValue)
        {
            entry.ColorValue = newValue;
            
            // Add to undo stack
            _undoStack.Push(new ColorEditAction
            {
                ResourceKey = entry.ResourceKey,
                OldValue = oldValue,
                NewValue = newValue
            });
            _redoStack.Clear();

            // Add to recent colors
            AddToRecentColors(newValue);

            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            
            Zer0Talk.Utilities.Logger.Log($"[Theme Edit] Pasted color to '{entry.ResourceKey}': {oldValue} → {newValue}", 
                                       Utilities.LogLevel.Info, categoryOverride: "theme");
        }
    }

    private async System.Threading.Tasks.Task RevertAllEditsAsync()
    {
        if (_undoStack.Count == 0) return;

        var count = _undoStack.Count;
        
        // Undo all changes
        while (_undoStack.Count > 0)
        {
            UndoColorEdit();
        }

        await ShowSaveToastAsync($"✅ Reverted {count} color edit(s)", 2000);
        Zer0Talk.Utilities.Logger.Log($"[Theme Edit] Reverted all edits ({count} changes)", 
                                   Utilities.LogLevel.Info, categoryOverride: "theme");
    }

    private async System.Threading.Tasks.Task ApplyThemeLiveAsync()
    {
        try
        {
            // Get current theme engine
            var engine = AppServices.ThemeEngine;
            var registered = engine.GetRegisteredThemes();

            var themeId = GetResolvedSelectedThemeId();

            if (!registered.TryGetValue(themeId, out var themeDef))
            {
                await ShowSaveToastAsync("❌ Current theme not found", 3000);
                return;
            }

            // Apply all edits from undo stack to theme definition
            var editedTheme = new Models.ThemeDefinition
            {
                Id = themeDef.Id + "-preview",
                DisplayName = themeDef.DisplayName + " (Preview)",
                Description = "Live preview of edited theme",
                Version = themeDef.Version,
                Author = themeDef.Author,
                ColorOverrides = new System.Collections.Generic.Dictionary<string, string>(themeDef.ColorOverrides ?? new()),
                Gradients = new System.Collections.Generic.Dictionary<string, Models.GradientDefinition>(themeDef.Gradients ?? new())
            };

            // Apply current color values from UI
            foreach (var colorEntry in ThemeColors)
            {
                if (editedTheme.ColorOverrides != null)
                {
                    editedTheme.ColorOverrides[colorEntry.ResourceKey] = colorEntry.ColorValue;
                }
            }

            // Register preview theme (but don't switch to it - ThemeEngine uses ThemeOption enum)
            engine.RegisterTheme(editedTheme);
            // Note: Live theme switching requires ThemeEngine refactor to support dynamic theme IDs
            // For now, preview theme is registered but user must manually select from dropdown

            await ShowSaveToastAsync($"🎨 Preview theme registered: {editedTheme.DisplayName}\nSelect from theme dropdown to apply", 4000);
            Zer0Talk.Utilities.Logger.Log($"[Theme Edit] Applied live preview theme: {editedTheme.Id}", 
                                       Utilities.LogLevel.Info, categoryOverride: "theme");
        }
        catch (Exception ex)
        {
            Zer0Talk.Utilities.Logger.Log($"[Theme Edit] Error applying live preview: {ex.Message}", 
                                       Utilities.LogLevel.Error, categoryOverride: "theme");
            await ShowSaveToastAsync($"❌ Preview failed: {ex.Message}", 4000);
        }
    }

    private void AddToRecentColors(string color)
    {
        if (string.IsNullOrWhiteSpace(color)) return;

        // Remove if already exists (move to top)
        _recentColors.Remove(color);
        
        // Add to beginning
        _recentColors.Insert(0, color);
        
        // Keep only last 10
        while (_recentColors.Count > 10)
        {
            _recentColors.RemoveAt(_recentColors.Count - 1);
        }
    }

    // Phase 3 Step 6: Gradient editing methods
    private void StartEditingGradient(ThemeGradientEntry? entry)
    {
        if (entry == null || _currentlyEditingGradient != null || entry.GradientDefinition == null) return;

        _currentlyEditingGradient = entry;
        entry.IsEditing = true;
        
        // Store original values for cancel
        entry.OriginalStartColor = entry.GradientDefinition.StartColor;
        entry.OriginalEndColor = entry.GradientDefinition.EndColor;
        entry.OriginalAngle = entry.GradientDefinition.Angle;
        
        OnPropertyChanged(nameof(IsEditingGradient));
        OnPropertyChanged(nameof(CanLoadLegacyTheme));
        Zer0Talk.Utilities.Logger.Log($"[Theme Edit] Started editing gradient '{entry.ResourceKey}' (angle: {entry.GradientDefinition.Angle}°)", 
                                   Utilities.LogLevel.Info, categoryOverride: "theme");
    }

    private async System.Threading.Tasks.Task SaveGradientEditAsync()
    {
        if (_currentlyEditingGradient == null || _currentlyEditingGradient.GradientDefinition == null) return;

        var entry = _currentlyEditingGradient;
        var gradient = entry.GradientDefinition;

        // Validate colors
        if (!Models.ThemeDefinition.IsValidColorPublic(gradient.StartColor))
        {
            await ShowSaveToastAsync($"❌ Invalid start color format: {gradient.StartColor}", 4000);
            return;
        }

        if (!Models.ThemeDefinition.IsValidColorPublic(gradient.EndColor))
        {
            await ShowSaveToastAsync($"❌ Invalid end color format: {gradient.EndColor}", 4000);
            return;
        }

        // Validate angle range
        if (gradient.Angle < 0 || gradient.Angle > 360)
        {
            await ShowSaveToastAsync($"❌ Angle must be between 0 and 360 degrees", 3000);
            return;
        }

        // Check if changed
        var changed = entry.OriginalStartColor != gradient.StartColor ||
                      entry.OriginalEndColor != gradient.EndColor ||
                      entry.OriginalAngle != gradient.Angle;

        if (changed)
        {
            // For gradients, we just log the change (undo for gradients could be added in future)
            Zer0Talk.Utilities.Logger.Log($"[Theme Edit] Saved gradient edit '{entry.ResourceKey}': " +
                                       $"{entry.OriginalStartColor}→{entry.OriginalEndColor} ({entry.OriginalAngle}°) to " +
                                       $"{gradient.StartColor}→{gradient.EndColor} ({gradient.Angle}°)", 
                                       Utilities.LogLevel.Info, categoryOverride: "theme");
            await ShowSaveToastAsync($"✅ Gradient updated: {entry.ResourceKey}", 2000);
        }

        entry.IsEditing = false;
        _currentlyEditingGradient = null;
        OnPropertyChanged(nameof(IsEditingGradient));
        OnPropertyChanged(nameof(CanLoadLegacyTheme));
    }

    private void CancelGradientEdit()
    {
        if (_currentlyEditingGradient == null || _currentlyEditingGradient.GradientDefinition == null) return;

        var entry = _currentlyEditingGradient;
        var gradient = entry.GradientDefinition;
        
        // Restore original values
        gradient.StartColor = entry.OriginalStartColor ?? gradient.StartColor;
        gradient.EndColor = entry.OriginalEndColor ?? gradient.EndColor;
        gradient.Angle = entry.OriginalAngle;
        
        entry.IsEditing = false;
        _currentlyEditingGradient = null;
        OnPropertyChanged(nameof(IsEditingGradient));
        OnPropertyChanged(nameof(CanLoadLegacyTheme));
        
        Zer0Talk.Utilities.Logger.Log($"[Theme Edit] Cancelled editing gradient '{entry.ResourceKey}'", 
                                   Utilities.LogLevel.Info, categoryOverride: "theme");
    }

    private void ApplyGradientPreset(GradientPreset? preset)
    {
        if (preset == null || _currentlyEditingGradient?.GradientDefinition == null) return;

        var gradient = _currentlyEditingGradient.GradientDefinition;
        gradient.StartColor = preset.StartColor;
        gradient.EndColor = preset.EndColor;
        gradient.Angle = preset.Angle;
        
        Zer0Talk.Utilities.Logger.Log($"[Theme Edit] Applied gradient preset '{preset.Name}' to '{_currentlyEditingGradient.ResourceKey}'", 
                                   Utilities.LogLevel.Info, categoryOverride: "theme");
    }

    #endregion

    #region Theme Management Operations (Phase 3 Step 7)

    // Rename theme operation
    private async Task RenameThemeAsync()
    {
        try
        {
            // For now, log that this feature is being implemented
            await ShowSaveToastAsync("Rename theme feature coming soon", 2000);
            Zer0Talk.Utilities.Logger.Log("[Theme Management] Rename theme requested", Utilities.LogLevel.Info, categoryOverride: "theme");
        }
        catch (Exception ex)
        {
            await ShowSaveToastAsync($"Error: {ex.Message}", 3000);
            Zer0Talk.Utilities.Logger.Log($"[Theme Management] Error renaming theme: {ex.Message}", Utilities.LogLevel.Error, categoryOverride: "theme");
        }
    }

    // Duplicate current theme
    private async Task DuplicateThemeAsync()
    {
        try
        {
            await ShowSaveToastAsync("Duplicate theme feature coming soon", 2000);
            Zer0Talk.Utilities.Logger.Log("[Theme Management] Duplicate theme requested", Utilities.LogLevel.Info, categoryOverride: "theme");
        }
        catch (Exception ex)
        {
            await ShowSaveToastAsync($"Error: {ex.Message}", 3000);
            Zer0Talk.Utilities.Logger.Log($"[Theme Management] Error duplicating theme: {ex.Message}", Utilities.LogLevel.Error, categoryOverride: "theme");
        }
    }

    // Delete current theme
    private async Task DeleteThemeAsync()
    {
        try
        {
            await ShowSaveToastAsync("Delete theme feature coming soon", 2000);
            Zer0Talk.Utilities.Logger.Log("[Theme Management] Delete theme requested", Utilities.LogLevel.Info, categoryOverride: "theme");
        }
        catch (Exception ex)
        {
            await ShowSaveToastAsync($"Error: {ex.Message}", 3000);
            Zer0Talk.Utilities.Logger.Log($"[Theme Management] Error deleting theme: {ex.Message}", Utilities.LogLevel.Error, categoryOverride: "theme");
        }
    }

    // Load blank template into editor for customization
    private async Task NewFromBlankTemplateAsync()
    {
        try
        {
            // Get the blank template
            var blank = Models.ThemeDefinition.CreateBlankTemplate();
            
            Zer0Talk.Utilities.Logger.Log("[Blank Template] Loading blank template into editor", 
                                       Utilities.LogLevel.Info, categoryOverride: "theme");

            // Populate inspector with blank template data
            CurrentThemeId = blank.Id;
            CurrentThemeDisplayName = blank.DisplayName;
            CurrentThemeDescription = blank.Description ?? "No description available";
            CurrentThemeVersion = blank.Version;
            CurrentThemeAuthor = blank.Author ?? "Unknown";
            CurrentThemeAllowsCustomization = blank.AllowsCustomization;
            CurrentThemeIsReadOnly = blank.IsReadOnly;

            // Clear undo/redo stacks (starting fresh)
            _undoStack.Clear();
            _redoStack.Clear();
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));

            // Populate color overrides
            ThemeColors.Clear();
            if (blank.ColorOverrides != null)
            {
                foreach (var kvp in blank.ColorOverrides.OrderBy(x => x.Key))
                {
                    ThemeColors.Add(new ThemeColorEntry
                    {
                        ResourceKey = kvp.Key,
                        ColorValue = kvp.Value,
                        IsEditing = false
                    });
                }
            }

            // Populate gradients (blank has no gradients by default)
            ThemeGradients.Clear();
            if (blank.Gradients != null)
            {
                foreach (var kvp in blank.Gradients.OrderBy(x => x.Key))
                {
                    ThemeGradients.Add(new ThemeGradientEntry
                    {
                        ResourceKey = kvp.Key,
                        GradientDefinition = kvp.Value,
                        IsEditing = false
                    });
                }
            }

            // Exit batch mode if active
            if (IsBatchEditMode)
            {
                IsBatchEditMode = false;
            }

            // Clear selections
            foreach (var color in ThemeColors)
            {
                color.IsSelected = false;
            }

            await ShowSaveToastAsync("📄 Blank template loaded - Start customizing!", 3000);
            Zer0Talk.Utilities.Logger.Log($"[Blank Template] Loaded {ThemeColors.Count} colors, {ThemeGradients.Count} gradients", 
                                       Utilities.LogLevel.Info, categoryOverride: "theme");
        }
        catch (Exception ex)
        {
            await ShowSaveToastAsync($"Error loading blank template: {ex.Message}", 3000);
            Zer0Talk.Utilities.Logger.Log($"[Blank Template] Error loading template: {ex.Message}", 
                                       Utilities.LogLevel.Error, categoryOverride: "theme");
        }
    }

    // Load From Legacy Theme: Load selected legacy theme as editable template
    private async Task LoadFromLegacyThemeAsync()
    {
        try
        {
            if (SelectedLegacyTheme == null)
            {
                await ShowSaveToastAsync("❌ No legacy theme selected", 2000);
                return;
            }

            // Get the theme engine and registered themes
            var engine = AppServices.ThemeEngine;
            var registered = engine.GetRegisteredThemes();

            if (!registered.TryGetValue(SelectedLegacyTheme.ThemeId, out var themeDef))
            {
                await ShowSaveToastAsync($"❌ Theme '{SelectedLegacyTheme.DisplayName}' not found", 3000);
                return;
            }

            Zer0Talk.Utilities.Logger.Log($"[Load From Legacy] Loading theme '{themeDef.DisplayName}' (ID: {themeDef.Id}) as editable template", 
                                       Utilities.LogLevel.Info, categoryOverride: "theme");

            // Load theme metadata
            CurrentThemeId = themeDef.Id;
            CurrentThemeDisplayName = themeDef.DisplayName;
            CurrentThemeDescription = themeDef.Description ?? "No description available";
            CurrentThemeVersion = themeDef.Version;
            CurrentThemeAuthor = themeDef.Author ?? "Unknown";
            CurrentThemeAllowsCustomization = themeDef.AllowsCustomization;
            CurrentThemeIsReadOnly = themeDef.IsReadOnly;

            // Clear undo/redo stacks (starting fresh)
            _undoStack.Clear();
            _redoStack.Clear();
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));

            // Populate color overrides as EDITABLE
            ThemeColors.Clear();
            if (themeDef.ColorOverrides != null)
            {
                foreach (var kvp in themeDef.ColorOverrides.OrderBy(x => x.Key))
                {
                    ThemeColors.Add(new ThemeColorEntry
                    {
                        ResourceKey = kvp.Key,
                        ColorValue = kvp.Value,
                        IsEditing = false,
                        IsEditable = true  // KEY: Make editable
                    });
                }
            }

            // Populate gradients as EDITABLE
            ThemeGradients.Clear();
            if (themeDef.Gradients != null)
            {
                foreach (var kvp in themeDef.Gradients.OrderBy(x => x.Key))
                {
                    ThemeGradients.Add(new ThemeGradientEntry
                    {
                        ResourceKey = kvp.Key,
                        GradientDefinition = kvp.Value,
                        IsEditing = false,
                        IsEditable = true  // KEY: Make editable
                    });
                }
            }

            // Exit batch mode if active
            if (IsBatchEditMode)
            {
                IsBatchEditMode = false;
            }

            // Clear selections
            foreach (var color in ThemeColors)
            {
                color.IsSelected = false;
            }

            // Reset recent colors
            _recentColors.Clear();
            _copiedColor = null;
            OnPropertyChanged(nameof(HasCopiedColor));

            await ShowSaveToastAsync($"📂 Loaded '{SelectedLegacyTheme.DisplayName}' theme - Ready to customize!", 3000);
            Zer0Talk.Utilities.Logger.Log($"[Load From Legacy] Loaded {ThemeColors.Count} colors, {ThemeGradients.Count} gradients as editable", 
                                       Utilities.LogLevel.Info, categoryOverride: "theme");

            // Clear selection after loading
            SelectedLegacyTheme = null;
        }
        catch (Exception ex)
        {
            await ShowSaveToastAsync($"Error loading theme: {ex.Message}", 3000);
            Zer0Talk.Utilities.Logger.Log($"[Load From Legacy] Error: {ex.Message}", 
                                       Utilities.LogLevel.Error, categoryOverride: "theme");
        }
    }

    // Start editing theme metadata
    private void StartEditingMetadata()
    {
        EditableThemeName = CurrentThemeDisplayName;
        EditableThemeDescription = CurrentThemeDescription;
        EditableThemeAuthor = CurrentThemeAuthor;
        EditableThemeVersion = CurrentThemeVersion;
        IsEditingMetadata = true;
        
        Zer0Talk.Utilities.Logger.Log($"[Theme Metadata] Started editing metadata for theme: {CurrentThemeId}", 
                                   Utilities.LogLevel.Info, categoryOverride: "theme");
    }

    // Save metadata changes
    private async Task SaveMetadataAsync()
    {
        try
        {
            // Validate inputs
            if (string.IsNullOrWhiteSpace(EditableThemeName))
            {
                await ShowSaveToastAsync("Theme name cannot be empty", 3000);
                return;
            }

            // Check if anything changed
            bool hasChanges = EditableThemeName != CurrentThemeDisplayName ||
                            EditableThemeDescription != CurrentThemeDescription ||
                            EditableThemeAuthor != CurrentThemeAuthor ||
                            EditableThemeVersion != CurrentThemeVersion;

            if (!hasChanges)
            {
                await ShowSaveToastAsync("No changes to save", 2000);
                IsEditingMetadata = false;
                return;
            }

            // Apply changes
            CurrentThemeDisplayName = EditableThemeName;
            CurrentThemeDescription = EditableThemeDescription;
            CurrentThemeAuthor = EditableThemeAuthor;
            CurrentThemeVersion = EditableThemeVersion;

            IsEditingMetadata = false;

            await ShowSaveToastAsync("Metadata updated successfully", 2000);
            Zer0Talk.Utilities.Logger.Log($"[Theme Metadata] Updated metadata for theme: {CurrentThemeId}", 
                                       Utilities.LogLevel.Info, categoryOverride: "theme");
        }
        catch (Exception ex)
        {
            await ShowSaveToastAsync($"Error saving metadata: {ex.Message}", 3000);
            Zer0Talk.Utilities.Logger.Log($"[Theme Metadata] Error saving metadata: {ex.Message}", 
                                       Utilities.LogLevel.Error, categoryOverride: "theme");
        }
    }

    // Cancel metadata editing
    private void CancelMetadataEdit()
    {
        IsEditingMetadata = false;
        Zer0Talk.Utilities.Logger.Log("[Theme Metadata] Cancelled metadata editing", 
                                   Utilities.LogLevel.Info, categoryOverride: "theme");
    }

    // Export modified theme (includes all edits)
    private async Task ExportModifiedThemeAsync()
    {
        try
        {
            // Create theme definition with current state
            var theme = new Models.ThemeDefinition
            {
                Id = $"custom-{DateTime.UtcNow:yyyyMMddHHmmss}",
                DisplayName = CurrentThemeDisplayName,
                Description = CurrentThemeDescription,
                Version = CurrentThemeVersion,
                Author = CurrentThemeAuthor,
                BaseVariant = "Dark",
                AllowsCustomization = true,
                ModifiedAt = DateTime.UtcNow
            };

            // Add color overrides from current edits
            foreach (var color in ThemeColors.Where(c => !string.IsNullOrEmpty(c.ColorValue)))
            {
                theme.ColorOverrides[color.ResourceKey] = color.ColorValue;
            }

            // Add gradients
            foreach (var gradient in ThemeGradients.Where(g => g.GradientDefinition != null))
            {
                theme.Gradients[gradient.ResourceKey] = gradient.GradientDefinition!;
            }

            // Get main window for file dialog
            var window = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (window == null)
            {
                await ShowSaveToastAsync("Unable to access file system", 3000);
                return;
            }

            // Show save file dialog
            var file = await window.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Export Modified Theme",
                SuggestedFileName = $"{theme.DisplayName.Replace(" ", "_")}.zttheme",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Zer0Talk Theme Files")
                    {
                        Patterns = new[] { "*.zttheme" }
                    }
                }
            });

            if (file != null)
            {
                var filePath = file.Path.LocalPath;
                theme.SaveToFile(filePath);
                await ShowSaveToastAsync($"Theme exported to: {System.IO.Path.GetFileName(filePath)}", 3000);
                Zer0Talk.Utilities.Logger.Log($"[Theme Export] Exported modified theme to: {filePath}", 
                                           Utilities.LogLevel.Info, categoryOverride: "theme");
            }
        }
        catch (Exception ex)
        {
            await ShowSaveToastAsync($"Error exporting theme: {ex.Message}", 3000);
            Zer0Talk.Utilities.Logger.Log($"[Theme Export] Error exporting modified theme: {ex.Message}", 
                                       Utilities.LogLevel.Error, categoryOverride: "theme");
        }
    }

    // Save As: Export current theme state as a new custom theme
    // This is the primary save mechanism for built-in/read-only themes
    private async Task SaveAsAsync()
    {
        try
        {
            Zer0Talk.Utilities.Logger.Log("[Theme SaveAs] Starting Save As operation", 
                                       Utilities.LogLevel.Info, categoryOverride: "theme");

            // Get current theme from engine to check if it's read-only
            var engine = AppServices.ThemeEngine;
            var registered = engine.GetRegisteredThemes();
            
            Models.ThemeDefinition? currentTheme = null;
            if (!string.IsNullOrEmpty(CurrentThemeId) && registered.TryGetValue(CurrentThemeId, out var existing))
            {
                currentTheme = existing;
            }

            // Determine if we should warn about built-in themes
            bool isBuiltIn = currentTheme?.IsBuiltIn() ?? false;
            if (isBuiltIn)
            {
                Zer0Talk.Utilities.Logger.Log($"[Theme SaveAs] Saving built-in theme '{CurrentThemeDisplayName}' as new custom theme", 
                                           Utilities.LogLevel.Info, categoryOverride: "theme");
            }

            // Create theme definition with current editor state
            // Always generate a new unique ID for saved themes to avoid conflicts
            var theme = new Models.ThemeDefinition
            {
                Id = $"custom-{Guid.NewGuid():N}",
                DisplayName = string.IsNullOrWhiteSpace(CurrentThemeDisplayName) ? "Custom Theme" : CurrentThemeDisplayName,
                Description = CurrentThemeDescription ?? "Custom theme created from Theme Builder",
                Version = CurrentThemeVersion,
                Author = CurrentThemeAuthor ?? Environment.UserName,
                BaseVariant = "Dark",
                AllowsCustomization = true,
                IsReadOnly = false,
                ThemeType = Models.ThemeType.Custom,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow,
                Tags = new System.Collections.Generic.List<string> { "custom", "user-created" }
            };

            // Add color overrides from current editor
            foreach (var color in ThemeColors.Where(c => !string.IsNullOrEmpty(c.ColorValue)))
            {
                theme.ColorOverrides[color.ResourceKey] = color.ColorValue;
            }

            // Add gradients from current editor
            foreach (var gradient in ThemeGradients.Where(g => g.GradientDefinition != null))
            {
                theme.Gradients[gradient.ResourceKey] = gradient.GradientDefinition!;
            }

            Zer0Talk.Utilities.Logger.Log($"[Theme SaveAs] Created theme definition with {theme.ColorOverrides.Count} colors, {theme.Gradients.Count} gradients", 
                                       Utilities.LogLevel.Info, categoryOverride: "theme");

            // Get main window for file dialog
            var window = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (window == null)
            {
                await ShowSaveToastAsync("❌ Unable to access file system", 3000);
                return;
            }

            // Show save file dialog
            var suggestedName = theme.DisplayName.Replace(" ", "_");
            // Remove invalid filename characters
            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            {
                suggestedName = suggestedName.Replace(c, '_');
            }

            var file = await window.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Save Theme As",
                SuggestedFileName = $"{suggestedName}.zttheme",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Zer0Talk Theme Files")
                    {
                        Patterns = new[] { "*.zttheme" }
                    },
                    new Avalonia.Platform.Storage.FilePickerFileType("All Files")
                    {
                        Patterns = new[] { "*" }
                    }
                }
            });

            if (file == null)
            {
                // User cancelled
                Zer0Talk.Utilities.Logger.Log("[Theme SaveAs] User cancelled file dialog", 
                                           Utilities.LogLevel.Info, categoryOverride: "theme");
                return;
            }

            var filePath = file.Path.LocalPath;
            
            // Save theme to file
            theme.SaveToFile(filePath);

            await ShowSaveToastAsync($"💾 Theme saved as: {System.IO.Path.GetFileName(filePath)}", 3000);
            Zer0Talk.Utilities.Logger.Log($"[Theme SaveAs] Successfully saved theme to: {filePath}", 
                                       Utilities.LogLevel.Info, categoryOverride: "theme");

            // Optional: Ask if user wants to import this new theme
            // For now, just log success
        }
        catch (Exception ex)
        {
            await ShowSaveToastAsync($"❌ Save failed: {ex.Message}", 4000);
            Zer0Talk.Utilities.Logger.Log($"[Theme SaveAs] Error saving theme: {ex.Message}", 
                                       Utilities.LogLevel.Error, categoryOverride: "theme");
        }
    }

    // Helper class for undo/redo tracking
    private class ColorEditAction
    {
        public string ResourceKey { get; set; } = string.Empty;
        public string OldValue { get; set; } = string.Empty;
        public string NewValue { get; set; } = string.Empty;
    }

    // Helper classes for data binding
    public class ThemeColorEntry : INotifyPropertyChanged
    {
        private string _colorValue = string.Empty;
        private bool _isEditing = false;
        private bool _isSelected = false;
        
        public string ResourceKey { get; set; } = string.Empty;
        
        public string ColorValue
        {
            get => _colorValue;
            set
            {
                if (_colorValue != value)
                {
                    _colorValue = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ColorValue)));
                }
            }
        }
        
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing != value)
                {
                    _isEditing = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditing)));
                }
            }
        }
        
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }
        
        public string? OriginalValue { get; set; } // For cancel operation
        public bool IsEditable { get; set; } = true; // Phase 3 Step 4: Enable editing

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class ThemeGradientEntry : INotifyPropertyChanged
    {
        private Models.GradientDefinition? _gradientDefinition;
        private bool _isEditing = false;
        
        public string ResourceKey { get; set; } = string.Empty;
        
        public Models.GradientDefinition? GradientDefinition
        {
            get => _gradientDefinition;
            set
            {
                if (_gradientDefinition != value)
                {
                    _gradientDefinition = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GradientDefinition)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GradientPreview)));
                }
            }
        }
        
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing != value)
                {
                    _isEditing = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditing)));
                }
            }
        }
        
        public bool IsEditable { get; set; } = true;
        
        // Original values for cancel operation
        public string? OriginalStartColor { get; set; }
        public string? OriginalEndColor { get; set; }
        public double OriginalAngle { get; set; }
        
        public string GradientPreview => GradientDefinition != null 
            ? $"{GradientDefinition.StartColor} → {GradientDefinition.EndColor} ({GradientDefinition.Angle}°)"
            : "No gradient data";

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    // Helper class for gradient presets
    public class GradientPreset
    {
        public string Name { get; set; } = string.Empty;
        public string StartColor { get; set; } = "#000000";
        public string EndColor { get; set; } = "#FFFFFF";
        public double Angle { get; set; } = 0.0;
    }

    // Helper class for legacy theme options in Load From dropdown
    public class LegacyThemeOption
    {
        public string DisplayName { get; set; } = string.Empty;
        public string ThemeId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    #endregion
}

internal sealed class LockServiceSingleton
{
    private LockServiceSingleton() { Service = new Zer0Talk.Services.LockService(); }
    public static LockServiceSingleton Instance { get; } = new LockServiceSingleton();
    public Zer0Talk.Services.LockService Service { get; }
    public void LockNow() => Service.Lock();
}

public record DefaultRelayEntry(string Name, string Address);

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
        RelayFallbackEnabled = s.RelayFallbackEnabled;
        RelayServer = s.RelayServer?.Trim() ?? string.Empty;
        RelayPresenceTimeoutSeconds = Math.Clamp(s.RelayPresenceTimeoutSeconds <= 0 ? 45 : s.RelayPresenceTimeoutSeconds, 10, 300);
        RelayDiscoveryTtlMinutes = Math.Clamp(s.RelayDiscoveryTtlMinutes <= 0 ? 3 : s.RelayDiscoveryTtlMinutes, 1, 60);
        ForceSeedBootstrap = s.ForceSeedBootstrap;
        EnableGeoBlocking = s.EnableGeoBlocking;
    RetryNatVerificationCommand = new RelayCommand(async _ => { try { await AppServices.Nat.RetryVerificationAsync(); } catch { } });
        SaveCommand = new RelayCommand(async _ => await SaveAsync(showToast: true, close: false), _ => Port >= 1 && Port <= 65535);
        CloseApplyCommand = new RelayCommand(async _ => await SaveAsync(showToast: false, close: true), _ => Port >= 1 && Port <= 65535);
        CancelCommand = new RelayCommand(_ => { DiscardNetworkChanges(); CloseRequested?.Invoke(this, EventArgs.Empty); });
        BlockPeerCommand = new RelayCommand(p => { if (p is string uid) { _peerManager.Block(uid); RefreshLists(); } });
        UnblockPeerCommand = new RelayCommand(p => { if (p is string uid) ConfirmUnblock(uid); });
        BlockSelectedPeersCommand = new RelayCommand(_ => BlockSelectedPeers());
        UnblockSelectedPeersCommand = new RelayCommand(_ => UnblockSelectedPeers());
        RemoveSelectedTestPeersCommand = new RelayCommand(_ => RemoveSelectedTestPeers());
        RemoveSelectedPeersCommand = new RelayCommand(_ => RemoveSelectedPeers());
        TrustPeerCommand = new RelayCommand(p => { if (p is string uid) { _peerManager.SetTrusted(uid, true); RefreshLists(); } });
        UntrustPeerCommand = new RelayCommand(p => { if (p is string uid) { _peerManager.SetTrusted(uid, false); RefreshLists(); } });
        ClearAllBlocksCommand = new RelayCommand(_ => ConfirmClearAll());
        RefreshPeersCommand = new RelayCommand(_ => { 
            try { Zer0Talk.Utilities.Logger.Log("[NetworkViewModel] Manual refresh peers triggered"); } catch { }
            try { AppServices.Discovery.Restart(); } catch { }
            RefreshLists(); 
        });
        RunFirewallTroubleshooterCommand = new RelayCommand(async _ => await RunFirewallTroubleshooterAsync(), _ => true);
        AddRelayServerCommand = new RelayCommand(_ => AddRelayServer(), _ => IsValidRelayEntry(NewRelayServer));
        RemoveRelayServerCommand = new RelayCommand(entry => { if (entry is string relay) RemoveRelayServer(relay); });
        UseRelayServerCommand = new RelayCommand(entry => { if (entry is string relay) RelayServer = relay; });
        UseDefaultRelayCommand = new RelayCommand(entry => { if (entry is DefaultRelayEntry r) RelayServer = r.Address; });
        AddWanSeedNodeCommand = new RelayCommand(_ => AddWanSeedNode(), _ => IsValidRelayEntry(NewWanSeedNode));
        RemoveWanSeedNodeCommand = new RelayCommand(entry => { if (entry is string seed) RemoveWanSeedNode(seed); });
        UseWanSeedNodeCommand = new RelayCommand(entry => { if (entry is string seed) RelayServer = seed; });
        
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
        RefreshRelayServers();
        RefreshWanSeedNodes();
        LoadDefaultRelays();

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
                try { Zer0Talk.Utilities.Logger.Log("[NetworkViewModel] PeersChanged event received"); } catch { }
                _uiThrottled?.Invoke(); 
                Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                    try { Zer0Talk.Utilities.Logger.Log("[NetworkViewModel] RefreshLists called from PeersChanged"); } catch { }
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
                    InfoMessage = "If prompted, allow Zer0Talk through Windows Firewall for inbound connections.";
                }
            }
        }
    }

    private bool _relayFallbackEnabled;
    public bool RelayFallbackEnabled
    {
        get => _relayFallbackEnabled;
        set
        {
            if (_relayFallbackEnabled != value)
            {
                _relayFallbackEnabled = value;
                OnPropertyChanged();
                try { AppServices.Events.RaiseNetworkConfigChanged(); } catch { }
            }
        }
    }

    private string _relayServer = string.Empty;
    public string RelayServer
    {
        get => _relayServer;
        set
        {
            var next = value?.Trim() ?? string.Empty;
            if (!string.Equals(_relayServer, next, StringComparison.Ordinal))
            {
                _relayServer = next;
                OnPropertyChanged();
                try { AppServices.Events.RaiseNetworkConfigChanged(); } catch { }
            }
        }
    }

    private int _relayPresenceTimeoutSeconds = 45;
    public int RelayPresenceTimeoutSeconds
    {
        get => _relayPresenceTimeoutSeconds;
        set
        {
            var next = Math.Clamp(value, 10, 300);
            if (_relayPresenceTimeoutSeconds != next)
            {
                _relayPresenceTimeoutSeconds = next;
                OnPropertyChanged();
                try { AppServices.Events.RaiseNetworkConfigChanged(); } catch { }
            }
        }
    }

    private int _relayDiscoveryTtlMinutes = 3;
    public int RelayDiscoveryTtlMinutes
    {
        get => _relayDiscoveryTtlMinutes;
        set
        {
            var next = Math.Clamp(value, 1, 60);
            if (_relayDiscoveryTtlMinutes != next)
            {
                _relayDiscoveryTtlMinutes = next;
                OnPropertyChanged();
                try { AppServices.Events.RaiseNetworkConfigChanged(); } catch { }
            }
        }
    }

    private System.Collections.ObjectModel.ObservableCollection<string> _savedRelayServers = new();
    public System.Collections.ObjectModel.ObservableCollection<string> SavedRelayServers
    {
        get => _savedRelayServers;
        private set
        {
            _savedRelayServers = value;
            OnPropertyChanged();
        }
    }

    public System.Collections.ObjectModel.ObservableCollection<DefaultRelayEntry> DefaultRelays { get; } = new();

    private string _newRelayServer = string.Empty;
    public string NewRelayServer
    {
        get => _newRelayServer;
        set
        {
            if (!string.Equals(_newRelayServer, value, StringComparison.Ordinal))
            {
                _newRelayServer = value ?? string.Empty;
                OnPropertyChanged();
                (AddRelayServerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    private string? _selectedRelayServer;
    public string? SelectedRelayServer
    {
        get => _selectedRelayServer;
        set
        {
            if (!string.Equals(_selectedRelayServer, value, StringComparison.Ordinal))
            {
                _selectedRelayServer = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _forceSeedBootstrap;
    public bool ForceSeedBootstrap
    {
        get => _forceSeedBootstrap;
        set
        {
            if (_forceSeedBootstrap != value)
            {
                _forceSeedBootstrap = value;
                OnPropertyChanged();
                try { AppServices.Events.RaiseNetworkConfigChanged(); } catch { }
            }
        }
    }

    private System.Collections.ObjectModel.ObservableCollection<string> _wanSeedNodes = new();
    public System.Collections.ObjectModel.ObservableCollection<string> WanSeedNodes
    {
        get => _wanSeedNodes;
        private set
        {
            _wanSeedNodes = value;
            OnPropertyChanged();
        }
    }

    private string _newWanSeedNode = string.Empty;
    public string NewWanSeedNode
    {
        get => _newWanSeedNode;
        set
        {
            if (!string.Equals(_newWanSeedNode, value, StringComparison.Ordinal))
            {
                _newWanSeedNode = value ?? string.Empty;
                OnPropertyChanged();
                (AddWanSeedNodeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    private string? _selectedWanSeedNode;
    public string? SelectedWanSeedNode
    {
        get => _selectedWanSeedNode;
        set
        {
            if (!string.Equals(_selectedWanSeedNode, value, StringComparison.Ordinal))
            {
                _selectedWanSeedNode = value;
                OnPropertyChanged();
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
    public ICommand RemoveSelectedTestPeersCommand { get; }
    public ICommand RemoveSelectedPeersCommand { get; }
    public ICommand TrustPeerCommand { get; }
    public ICommand UntrustPeerCommand { get; }
    public ICommand ClearAllBlocksCommand { get; }
    public ICommand RefreshPeersCommand { get; }
    public ICommand RunFirewallTroubleshooterCommand { get; }
    public ICommand AddRelayServerCommand { get; }
    public ICommand RemoveRelayServerCommand { get; }
    public ICommand UseRelayServerCommand { get; }
    public ICommand UseDefaultRelayCommand { get; }
    public ICommand AddWanSeedNodeCommand { get; }
    public ICommand RemoveWanSeedNodeCommand { get; }
    public ICommand UseWanSeedNodeCommand { get; }
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

    private int _intervalIndex;
    public int IntervalIndex { get => _intervalIndex; set { _intervalIndex = value; OnPropertyChanged(); OnIntervalChanged?.Invoke(value); } }
    public event Action<int>? OnIntervalChanged;
    public void OnPortsChanged() { }

    private bool _autoRefreshPeers;
    public bool AutoRefreshPeers
    {
        get => _autoRefreshPeers;
        set
        {
            if (_autoRefreshPeers != value)
            {
                _autoRefreshPeers = value;
                OnPropertyChanged();
            }
        }
    }

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

    private async System.Threading.Tasks.Task SaveAsync(bool showToast, bool close)
    {
        try
        {
            var s = _settings.Settings;
            var relayServer = RelayServer?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(relayServer) && !IsValidRelayEntry(relayServer))
            {
                ErrorMessage = "Invalid relay server. Use host:port, [IPv6]:port, or a 16-character relay token.";
                return;
            }

            s.Port = Port;
            s.MajorNode = MajorNode;
            s.RelayFallbackEnabled = RelayFallbackEnabled;
            s.RelayServer = string.IsNullOrWhiteSpace(relayServer) ? null : relayServer;
            s.RelayPresenceTimeoutSeconds = RelayPresenceTimeoutSeconds;
            s.RelayDiscoveryTtlMinutes = RelayDiscoveryTtlMinutes;
            s.SavedRelayServers = SavedRelayServers
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(IsValidRelayEntry)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            s.ForceSeedBootstrap = ForceSeedBootstrap;
            s.WanSeedNodes = WanSeedNodes
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(IsValidRelayEntry)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            s.EnableGeoBlocking = EnableGeoBlocking;
            _settings.Save(AppServices.Passphrase);
            // Networking lifecycle is handled by app-level service; notify via centralized event
            AppServices.Events.RaiseNetworkConfigChanged();
            ErrorMessage = string.Empty;
            if (s.MajorNode)
            {
                InfoMessage = "If prompted, allow Zer0Talk through Windows Firewall for inbound connections.";
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

    private async System.Threading.Tasks.Task RunFirewallTroubleshooterAsync()
    {
        try
        {
            var settingsPort = 26264;
            try { settingsPort = _settings.Settings.Port; } catch { }

            var result = await WindowsFirewallRuleManager.RefreshRulesAsync(settingsPort, force: true);
            switch (result)
            {
                case FirewallRuleRefreshResult.SkippedNonWindows:
                    InfoMessage = "Firewall troubleshooter is available on Windows only.";
                    return;
                case FirewallRuleRefreshResult.MissingExecutablePath:
                    ErrorMessage = "Unable to determine executable path for firewall repair.";
                    return;
                case FirewallRuleRefreshResult.Canceled:
                    InfoMessage = "Firewall troubleshooter canceled.";
                    return;
                case FirewallRuleRefreshResult.Success:
                case FirewallRuleRefreshResult.UpToDate:
                    ErrorMessage = string.Empty;
                    InfoMessage = "Firewall rules refreshed. Networking restarted.";
                    // Force-restart the network listener so the new firewall rules take effect
                    // immediately without requiring a full app restart.
                    try { AppServices.Network.ForceRestart(); } catch { }
                    return;
                default:
                    ErrorMessage = "Firewall troubleshooter failed. Try running Zer0Talk as Administrator.";
                    return;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Firewall troubleshooter failed: {ex.Message}");
            ErrorMessage = "Firewall troubleshooter failed. Check permissions and try again.";
        }
    }

    private void DiscardNetworkChanges()
    {
        try
        {
            var s = _settings.Settings;
            Port = s.Port;
            PortText = s.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            MajorNode = s.MajorNode;
            RelayFallbackEnabled = s.RelayFallbackEnabled;
            RelayServer = s.RelayServer?.Trim() ?? string.Empty;
            RelayPresenceTimeoutSeconds = Math.Clamp(s.RelayPresenceTimeoutSeconds <= 0 ? 45 : s.RelayPresenceTimeoutSeconds, 10, 300);
            RelayDiscoveryTtlMinutes = Math.Clamp(s.RelayDiscoveryTtlMinutes <= 0 ? 3 : s.RelayDiscoveryTtlMinutes, 1, 60);
            ForceSeedBootstrap = s.ForceSeedBootstrap;
            RefreshRelayServers();
            RefreshWanSeedNodes();
            // Apply reverted values immediately so runtime matches persisted state
            ApplyNetworkChangeLiveIfNeeded();
        }
        catch { }
    }

    public void ResetFromSettings() => DiscardNetworkChanges();

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
        try { Zer0Talk.Utilities.Logger.Log($"[NetworkViewModel] RefreshLists: Found {_peerManager.Peers.Count} peers"); } catch { }
        var allPeers = _peerManager.Peers.ToList();
        var now = System.DateTime.UtcNow;

        var contacts = Zer0Talk.Services.AppServices.Contacts.Contacts.ToList();
        var simulatedContactUids = new System.Collections.Generic.HashSet<string>(
            contacts.Where(c => c.IsSimulated).Select(c => NormalizeUid(c.UID)),
            StringComparer.OrdinalIgnoreCase);
        var realContactUids = new System.Collections.Generic.HashSet<string>(
            contacts.Where(c => !c.IsSimulated).Select(c => NormalizeUid(c.UID)),
            StringComparer.OrdinalIgnoreCase);

        // Hide test/simulated/no-UID entries and stale non-contact peers.
        var blocked = (_settings.Settings.BlockList ?? new System.Collections.Generic.List<string>())
            .Select(NormalizeUid)
            .Where(uid => !string.IsNullOrWhiteSpace(uid))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var blockedSet = new System.Collections.Generic.HashSet<string>(blocked, StringComparer.OrdinalIgnoreCase);

        var peers = allPeers.Where(p => ShouldDisplayDiscoveredPeer(p, now, realContactUids, simulatedContactUids)).ToList();
        peers = peers
            .OrderByDescending(p => blockedSet.Contains(NormalizeUid(p.UID)))
            .ThenByDescending(IsPeerOnline)
            .ThenBy(p => NormalizeUid(p.UID), StringComparer.OrdinalIgnoreCase)
            .ToList();
        try { Zer0Talk.Utilities.Logger.Log($"[NetworkViewModel] RefreshLists: Filtered to {peers.Count} visible peers (from {allPeers.Count})"); } catch { }
        
        // Set IsBlocked property on each peer and assign country codes from IP address with caching
        foreach (var peer in peers)
        {
            var normalizedUid = NormalizeUid(peer.UID);
            var isRealContact = realContactUids.Contains(normalizedUid);
            peer.IsBlocked = blockedSet.Contains(NormalizeUid(peer.UID));
            peer.IsLan = IsLanAddress(peer.Address);
            peer.ModeLabel = Zer0Talk.Services.AppServices.Network.GetConnectionMode(normalizedUid) switch
            {
                Models.ConnectionMode.Direct => "Direct",
                Models.ConnectionMode.Relay => "Relay",
                _ => "—"
            };
            var (bytesIn, bytesOut) = Zer0Talk.Services.AppServices.Network.GetSessionBytes(normalizedUid);
            peer.BytesIn = bytesIn;
            peer.BytesOut = bytesOut;
            
            // Update LastSeenOnline if peer appears online.
            if (IsPeerOnline(peer))
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
                else if (isRealContact && !string.IsNullOrWhiteSpace(peer.CountryCode) && peer.CountryCode != "⚪")
                {
                    // Preserve last known contact region hint while offline/unblocked.
                }
                else
                {
                    // Peer not connected yet or cache expired, show placeholder
                    peer.CountryCode = "⚪"; // Empty grey circle placeholder
                    peer.CountryCodeCachedAt = null;
                }
            }
        }
        
        SyncDiscoveredPeers(peers);
        BlockedPeers = new System.Collections.ObjectModel.ObservableCollection<string>(blocked);
        try { Zer0Talk.Utilities.Logger.Log($"[NetworkViewModel] RefreshLists: Updated UI with {peers.Count} discovered peers"); } catch { }
    }

    public void RefreshPeersRealtime()
    {
        try { Avalonia.Threading.Dispatcher.UIThread.Post(() => RefreshLists()); } catch { }
    }

    public void RefreshListsDirect()
    {
        try { RefreshLists(); } catch { }
    }
    
    // Check if a peer UID corresponds to a simulated contact (should not appear in discovered peers)
    private static bool IsSimulatedContact(string uid)
    {
        try
        {
            var normalized = NormalizeUid(uid);
            var contacts = Zer0Talk.Services.AppServices.Contacts.Contacts;
            var contact = contacts.FirstOrDefault(c => string.Equals(NormalizeUid(c.UID), normalized, StringComparison.OrdinalIgnoreCase));
            return contact?.IsSimulated == true;
        }
        catch { return false; }
    }

    private static string NormalizeUid(string? uid)
    {
        var value = (uid ?? string.Empty).Trim();
        if (value.StartsWith("usr-", StringComparison.OrdinalIgnoreCase) && value.Length > 4)
        {
            return value.Substring(4);
        }
        return value;
    }

    private static bool IsUidLikelyTest(string uid)
    {
        if (string.IsNullOrWhiteSpace(uid)) return true;
        var normalized = NormalizeUid(uid);
        return normalized.StartsWith("test", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("sim", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("debug", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("dummy", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPeerOnline(Peer peer)
    {
        var status = peer.Status?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (string.Equals(status, "Offline", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        return peer.PublicKey != null && peer.PublicKey.Length > 0;
    }

    private static bool ShouldDisplayDiscoveredPeer(
        Peer peer,
        System.DateTime now,
        System.Collections.Generic.HashSet<string> realContactUids,
        System.Collections.Generic.HashSet<string> simulatedContactUids)
    {
        if (peer == null) return false;

        var uid = NormalizeUid(peer.UID);
        if (string.IsNullOrWhiteSpace(uid)) return false;
        if (simulatedContactUids.Contains(uid)) return false;
        if (IsUidLikelyTest(uid)) return false;

        if (realContactUids.Contains(uid)) return true;

        var status = peer.Status?.Trim() ?? string.Empty;
        if (string.Equals(status, "Offline", StringComparison.OrdinalIgnoreCase)) return false;

        if (IsPeerOnline(peer)) return true;

        if (peer.LastSeenOnline.HasValue)
        {
            return (now - peer.LastSeenOnline.Value).TotalSeconds <= 8;
        }

        return false;
    }

    private void SyncDiscoveredPeers(System.Collections.Generic.IReadOnlyList<Peer> targetPeers)
    {
        var targetByUid = new System.Collections.Generic.Dictionary<string, Peer>(StringComparer.OrdinalIgnoreCase);
        foreach (var peer in targetPeers)
        {
            var uid = NormalizeUid(peer.UID);
            if (string.IsNullOrWhiteSpace(uid)) continue;
            targetByUid[uid] = peer;
        }

        for (var i = DiscoveredPeers.Count - 1; i >= 0; i--)
        {
            var existing = DiscoveredPeers[i];
            var uid = NormalizeUid(existing.UID);
            if (!targetByUid.ContainsKey(uid))
            {
                DiscoveredPeers.RemoveAt(i);
            }
        }

        for (var i = 0; i < targetPeers.Count; i++)
        {
            var target = targetPeers[i];
            if (i < DiscoveredPeers.Count)
            {
                var currentUid = NormalizeUid(DiscoveredPeers[i].UID);
                var targetUid = NormalizeUid(target.UID);
                if (!string.Equals(currentUid, targetUid, StringComparison.OrdinalIgnoreCase))
                {
                    var existingIndex = IndexOfPeerByUid(targetUid);
                    if (existingIndex >= 0)
                    {
                        DiscoveredPeers.Move(existingIndex, i);
                    }
                    else
                    {
                        DiscoveredPeers.Insert(i, target);
                    }
                }
            }
            else
            {
                DiscoveredPeers.Add(target);
            }
        }

        while (DiscoveredPeers.Count > targetPeers.Count)
        {
            DiscoveredPeers.RemoveAt(DiscoveredPeers.Count - 1);
        }
    }

    private int IndexOfPeerByUid(string uid)
    {
        for (var i = 0; i < DiscoveredPeers.Count; i++)
        {
            if (string.Equals(NormalizeUid(DiscoveredPeers[i].UID), uid, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    private static bool IsLanAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        var host = address.Contains(':') ? address.Split(':')[0] : address;
        if (!System.Net.IPAddress.TryParse(host, out var ip)) return false;
        if (System.Net.IPAddress.IsLoopback(ip)) return true;
        var b = ip.GetAddressBytes();
        if (b.Length != 4) return false;
        if (b[0] == 10) return true;
        if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
        if (b[0] == 192 && b[1] == 168) return true;
        if (b[0] == 169 && b[1] == 254) return true; // link-local
        return false;
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
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try { AppServices.Discovery.Restart(); } catch { }
            RefreshLists();
        });
    }

    private void RemoveSelectedTestPeers()
    {
        if (SelectedPeers == null || SelectedPeers.Count == 0) return;

        var selected = SelectedPeers.ToList();
        var removedAny = false;

        foreach (var peer in selected)
        {
            if (peer == null) continue;

            var uid = NormalizeUid(peer.UID);
            var removable = IsSimulatedContact(uid) || IsUidLikelyTest(uid) || string.IsNullOrWhiteSpace(uid);
            if (!removable) continue;

            Peer? target = null;
            if (!string.IsNullOrWhiteSpace(uid))
            {
                target = _peerManager.Peers.FirstOrDefault(p => string.Equals(NormalizeUid(p.UID), uid, StringComparison.OrdinalIgnoreCase));
            }

            target ??= _peerManager.Peers.FirstOrDefault(p => object.ReferenceEquals(p, peer));

            if (target != null)
            {
                _peerManager.Peers.Remove(target);
                removedAny = true;
            }
        }

        SelectedPeers.Clear();

        if (removedAny)
        {
            try { Zer0Talk.Services.AppServices.PeersStore.Save(_peerManager.Peers, Zer0Talk.Services.AppServices.Passphrase); } catch { }
            try { _peerManager.IncludeContacts(); } catch { }
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() => RefreshLists());
    }

    private void RemoveSelectedPeers()
    {
        if (SelectedPeers == null || SelectedPeers.Count == 0) return;

        var selected = SelectedPeers.ToList();
        var removedAny = false;

        foreach (var peer in selected)
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
            try { Zer0Talk.Services.AppServices.PeersStore.Save(_peerManager.Peers, Zer0Talk.Services.AppServices.Passphrase); } catch { }
            try { _peerManager.IncludeContacts(); } catch { }
        }

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

    private void RefreshRelayServers()
    {
        try
        {
            SavedRelayServers = new System.Collections.ObjectModel.ObservableCollection<string>(
                (_settings.Settings.SavedRelayServers ?? new System.Collections.Generic.List<string>())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList());
        }
        catch
        {
            SavedRelayServers = new System.Collections.ObjectModel.ObservableCollection<string>();
        }
    }

    private void LoadDefaultRelays()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("Zer0Talk.Assets.Data.default-relays.json");
            if (stream == null) return;
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var entries = System.Text.Json.JsonSerializer.Deserialize<DefaultRelayEntry[]>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (entries == null) return;
            foreach (var e in entries)
            {
                if (!string.IsNullOrWhiteSpace(e.Name) && !string.IsNullOrWhiteSpace(e.Address))
                    DefaultRelays.Add(e);
            }
        }
        catch { }
    }

    private void RefreshWanSeedNodes()
    {
        try
        {
            WanSeedNodes = new System.Collections.ObjectModel.ObservableCollection<string>(
                (_settings.Settings.WanSeedNodes ?? new System.Collections.Generic.List<string>())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList());
        }
        catch
        {
            WanSeedNodes = new System.Collections.ObjectModel.ObservableCollection<string>();
        }
    }

    private static bool IsValidRelayEntry(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        var value = input.Trim();
        if (value.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '|' }) >= 0) return false;
        if (IsValidRelayToken(value)) return true;
        return TryParseRelayEndpoint(value, out _);
    }

    private static bool IsValidRelayToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 16) return false;
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsWhiteSpace(value[i]) || value[i] == '|' || value[i] == ':') return false;
        }
        return true;
    }

    private static bool TryParseRelayEndpoint(string input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var value = input.Trim();
        string host;
        int port = 443;

        if (value.StartsWith("[", StringComparison.Ordinal))
        {
            var end = value.IndexOf(']');
            if (end <= 1) return false;
            host = value.Substring(1, end - 1);
            if (end + 1 < value.Length)
            {
                if (value[end + 1] != ':') return false;
                if (!int.TryParse(value.Substring(end + 2), out port)) return false;
            }
        }
        else
        {
            var firstColon = value.IndexOf(':');
            var lastColon = value.LastIndexOf(':');
            if (firstColon >= 0 && firstColon != lastColon)
            {
                return false;
            }

            if (lastColon > 0 && lastColon < value.Length - 1)
            {
                host = value.Substring(0, lastColon);
                if (!int.TryParse(value.Substring(lastColon + 1), out port)) return false;
            }
            else
            {
                host = value;
            }
        }

        if (string.IsNullOrWhiteSpace(host)) return false;
        if (port < 1 || port > 65535) return false;

        if (!System.Net.IPAddress.TryParse(host, out _))
        {
            var hostType = Uri.CheckHostName(host);
            if (hostType != UriHostNameType.Dns) return false;
        }

        normalized = host.Contains(':', StringComparison.Ordinal)
            ? $"[{host}]:{port}"
            : $"{host}:{port}";
        return true;
    }

    private void AddRelayServer()
    {
        var entry = NewRelayServer?.Trim() ?? string.Empty;
        if (!IsValidRelayEntry(entry))
        {
            InfoMessage = "Relay entry must be host:port, [IPv6]:port, or a 16-character relay token.";
            return;
        }

        if (!IsValidRelayToken(entry) && TryParseRelayEndpoint(entry, out var normalized))
        {
            entry = normalized;
        }

        var list = _settings.Settings.SavedRelayServers ??= new System.Collections.Generic.List<string>();
        if (list.Any(existing => string.Equals(existing, entry, StringComparison.OrdinalIgnoreCase)))
        {
            NewRelayServer = string.Empty;
            return;
        }

        list.Add(entry);
        SavedRelayServers.Add(entry);
        NewRelayServer = string.Empty;
    }

    private void RemoveRelayServer(string relay)
    {
        var entry = relay?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(entry)) return;

        var list = _settings.Settings.SavedRelayServers ??= new System.Collections.Generic.List<string>();
        list.RemoveAll(existing => string.Equals(existing, entry, StringComparison.OrdinalIgnoreCase));

        var existingVm = SavedRelayServers.FirstOrDefault(existing => string.Equals(existing, entry, StringComparison.OrdinalIgnoreCase));
        if (existingVm != null)
        {
            SavedRelayServers.Remove(existingVm);
        }

        if (string.Equals(SelectedRelayServer, entry, StringComparison.OrdinalIgnoreCase))
        {
            SelectedRelayServer = null;
        }
    }

    private void AddWanSeedNode()
    {
        var entry = NewWanSeedNode?.Trim() ?? string.Empty;
        if (!IsValidRelayEntry(entry))
        {
            InfoMessage = "Seed node must be host:port, [IPv6]:port, or a 16-character token.";
            return;
        }

        if (!IsValidRelayToken(entry) && TryParseRelayEndpoint(entry, out var normalized))
        {
            entry = normalized;
        }

        var list = _settings.Settings.WanSeedNodes ??= new System.Collections.Generic.List<string>();
        if (list.Any(existing => string.Equals(existing, entry, StringComparison.OrdinalIgnoreCase)))
        {
            NewWanSeedNode = string.Empty;
            return;
        }

        list.Add(entry);
        WanSeedNodes.Add(entry);
        NewWanSeedNode = string.Empty;
    }

    private void RemoveWanSeedNode(string seed)
    {
        var entry = seed?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(entry)) return;

        var list = _settings.Settings.WanSeedNodes ??= new System.Collections.Generic.List<string>();
        list.RemoveAll(existing => string.Equals(existing, entry, StringComparison.OrdinalIgnoreCase));

        var existingVm = WanSeedNodes.FirstOrDefault(existing => string.Equals(existing, entry, StringComparison.OrdinalIgnoreCase));
        if (existingVm != null)
        {
            WanSeedNodes.Remove(existingVm);
        }

        if (string.Equals(SelectedWanSeedNode, entry, StringComparison.OrdinalIgnoreCase))
        {
            SelectedWanSeedNode = null;
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
            try { AppServices.Discovery.Restart(); } catch { }
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
            Zer0Talk.Utilities.Logger.Log($"[IP-BLOCK] Added bad actor IP via UI: {NewBadActorIp}");
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
            Zer0Talk.Utilities.Logger.Log($"[IP-BLOCK] Removed bad actor IP via UI: {ip}");
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
            Zer0Talk.Utilities.Logger.Log($"[IP-BLOCK] Added IP range via UI: {NewIpRange}");
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
            Zer0Talk.Utilities.Logger.Log($"[IP-BLOCK] Removed IP range via UI: {range}");
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
            var appDataPath = Zer0Talk.Utilities.AppDataPaths.Combine("security", "ip-blocklist.txt");
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
                var securityDir = Zer0Talk.Utilities.AppDataPaths.Combine("security");
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
            var securityDir = Zer0Talk.Utilities.AppDataPaths.Combine("security");
            Directory.CreateDirectory(securityDir);
            
            // Export with timestamp
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm", System.Globalization.CultureInfo.InvariantCulture);
            var exportPath = Path.Combine(securityDir, $"zer0talk-ip-blocklist-{timestamp}.txt");
            
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
                Zer0Talk.Utilities.Logger.Log("[IP-BLOCK] Cleared all bad actor IPs via UI");
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
                Zer0Talk.Utilities.Logger.Log("[IP-BLOCK] Cleared all IP ranges via UI");
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
            Zer0Talk.Utilities.Logger.Log($"[IP-BLOCK] Error refreshing IP lists: {ex.Message}");
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
