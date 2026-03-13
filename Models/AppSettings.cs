/*
    App settings model: persisted encrypted (settings.p2e) via SettingsService.
    - Holds window states and theme selection.
*/
// TODO[ANCHOR]: AppSettings - Window states and theme
using System;
using Avalonia.Input;

namespace Zer0Talk.Models;

// Removed Auto option: explicit theming only to avoid ambiguity
public enum ThemeOption { Dark, Light, Sandy, Butter }

// Presence/personal status exposed to peers (non-sensitive)
// Offline is a local, derived state (peer unreachable). Not broadcast to others, but may be received as token 'off'.
public enum PresenceStatus { Online, Idle, DoNotDisturb, Invisible, Offline }

public class WindowStateSettings
{
    // Nullable so we only apply values that were actually saved; no defaults.
    public double? Width { get; set; }
    public double? Height { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    // Store Avalonia.Controls.WindowState as int to avoid Avalonia dependency here
    public int? State { get; set; }
    public bool? Topmost { get; set; }
}

public class AppSettings
{
    public const int DefaultLockHotkeyKey = (int)Key.L;
    public const int DefaultLockHotkeyModifiers = (int)KeyModifiers.Control;
    public const int DefaultClearInputHotkeyKey = (int)Key.Q;
    public const int DefaultClearInputHotkeyModifiers = (int)(KeyModifiers.Control | KeyModifiers.Shift);
    public const int DefaultStreamerModeHotkeyKey = (int)Key.P;
    public const int DefaultStreamerModeHotkeyModifiers = (int)(KeyModifiers.Control | KeyModifiers.Shift);

    public string DisplayName { get; set; } = string.Empty;
    public ThemeOption Theme { get; set; } = ThemeOption.Dark;
    // Theme Engine v2: persisted theme identifier (legacy-dark, custom-*, etc.)
    public string? ThemeId { get; set; }
    // Theme Engine: user-selected UI font family (null or empty = default platform font)
    public string? UiFontFamily { get; set; }
    // Theme Engine: global UI scale (1.0 = 100%). Future: allow per-monitor / per-surface scaling.
    public double UiScale { get; set; } = 1.0;
    // Persist local presence; default Online
    public PresenceStatus Status { get; set; } = PresenceStatus.Online;
    public int Port { get; set; } = 26264;
    public bool MajorNode { get; set; }
    public System.Collections.Generic.List<string> BlockList { get; set; } = new(); // UIDs
    public System.Collections.Generic.List<string> BlockedPublicKeyFingerprints { get; set; } = new(); // SHA256 Base64 hashes of blocked public keys
    public System.Collections.Generic.List<string> BlockedIpAddresses { get; set; } = new(); // IP addresses (v4/v6)
    public System.Collections.Generic.List<string> BlockedIpRanges { get; set; } = new(); // CIDR ranges (e.g., 192.168.1.0/24)
    public System.Collections.Generic.HashSet<string> CustomBadActorIps { get; set; } = new(); // User-added bad actor IPs
    // Ordered list of preferred adapter IDs (NetworkInterface.Id)
    public System.Collections.Generic.List<string> AdapterPriorityIds { get; set; } = new();
    // Remembered passphrase preference and protected blob (DPAPI). The blob is also mirrored to a sidecar file for auto-unlock at startup.
    public bool RememberPassphrase { get; set; }
    public string? RememberedPassphraseProtected { get; set; }
    // Lock behavior
    public bool AutoLockEnabled { get; set; }
    public int AutoLockMinutes { get; set; } // 0 = disabled when AutoLockEnabled is false
    public bool LockOnMinimize { get; set; }
    // Lock visuals: blur radius for dimmed main window during lock (0..10)
    public int LockBlurRadius { get; set; } = 6;

    public WindowStateSettings MainWindow { get; set; } = new();
    public WindowStateSettings SettingsWindow { get; set; } = new();
    public WindowStateSettings NetworkWindow { get; set; } = new();
    // New: persist MonitoringWindow size/position/state
    public WindowStateSettings MonitoringWindow { get; set; } = new();
    // Persist LogViewerWindow geometry for consistent placement across sessions
    public WindowStateSettings LogViewerWindow { get; set; } = new();

    // Monitoring window refresh interval (milliseconds). Default 500ms.
    public int MonitoringIntervalMs { get; set; } = 500;

    // Monitoring: persisted diagnostics log font size for accessibility (default small size)
    public double MonitoringLogFontSize { get; set; } = 11;

    // Monitoring: graph render style (0=Line, 1=Bar, 2=Solid Lines)
    public int MonitoringGraphStyleIndex { get; set; } = 0;
    // Monitoring: legend side (0=Left, 1=Right)
    public int MonitoringLegendPositionIndex { get; set; } = 1;

    // Main window layout: persisted widths for resizable columns (in device-independent pixels).
    // Left = navigation + peers; Right = diagnostics. Center remains star-sized.
    public double? MainLeftWidth { get; set; }
    public double? MainRightWidth { get; set; }
    // Notification center sub-view persistence: 0=Invites, 1=Messages, 2=Alerts
    public int LastNotificationCenterView { get; set; } = 0;
    // Composer markdown toolbar row visibility persistence
    public bool ComposerMarkdownToolsVisible { get; set; } = true;

    // Connectivity: optional relay fallback if direct+NAT traversal fail.
    // If enabled, the app will attempt to reach peers via a relay service when direct connection is not possible.
    // Format: "host:port" (e.g., "relay.example.com:443"). Leave null/empty to disable.
    public bool RelayFallbackEnabled { get; set; } = true;
    public string? RelayServer { get; set; }
    public System.Collections.Generic.List<string> SavedRelayServers { get; set; } = new();
    // Optional WAN bootstrap seed nodes used to discover peers when no known endpoints are available.
    // Format: "host:port" (e.g., "seed1.example.net:443").
    // These are not forced by default; they are used opportunistically when needed.
    public System.Collections.Generic.List<string> WanSeedNodes { get; set; } = new();
    // When true, always include seed nodes in WAN bootstrap candidate set.
    // Default false: only use seeds when no explicit relay/directory endpoints are configured.
    public bool ForceSeedBootstrap { get; set; } = false;
    public int RelayPresenceTimeoutSeconds { get; set; } = 45;
    public int RelayDiscoveryTtlMinutes { get; set; } = 3;

    // Performance settings
    // CPU: CCD core affinity selection (0 = Auto, 1 = CCD 0, 2 = CCD 1)
    public int CcdAffinityIndex { get; set; }
    // CPU: Intel Performance-core targeting (prefer P-cores over E-cores)
    public bool IntelPCoreTargeting { get; set; } = true;
    // GPU: user override to disable hardware acceleration (default enabled when false)
    public bool DisableGpuAcceleration { get; set; }
    // Frame throttles (0 = off)
    public int FpsThrottle { get; set; }
    public int RefreshRateThrottle { get; set; }
    // Background UI framerate to maintain when the app is unfocused/minimized
    public int BackgroundFramerateFps { get; set; } = 15;
    // Memory caps (MB) (0 = unlimited)
    public int RamUsageLimitMb { get; set; }
    public int VramUsageLimitMb { get; set; }
    // Enforcement toggles
    public bool EnforceRamLimit { get; set; }
    public bool EnforceVramLimit { get; set; }

    // Privacy: opt-out of screen/window capture (Windows 10 2004+ honored by Snipping Tool & WGC)
    public bool BlockScreenCapture { get; set; } = true;
    // Privacy: show/hide public keys on profiles (default hidden)
    public bool ShowPublicKeys { get; set; }
    // Privacy: streamer mode hides sensitive info (UIDs, IPs, peer names) in the UI
    public bool StreamerMode { get; set; }

    // Security: Geo-blocking settings (pre-emptive defense against known hostile regions)
    public bool EnableGeoBlocking { get; set; } = true; // Default enabled for safety
    public System.Collections.Generic.List<string> BlockedCountryCodes { get; set; } = new(); // ISO 3166-1 alpha-2 codes
    public bool LogGeoBlockEvents { get; set; } = true; // Log when connections are geo-blocked

    // UI: remember last selected Settings menu index (0..8 when debug panel is visible)
    public int LastSettingsMenuIndex { get; set; } = 2;

    // Debug-only log maintenance (applies when debug panel surfaced)
    public bool EnableDebugLogAutoTrim { get; set; } = true;
    public int DebugUiLogMaxLines { get; set; } = 1000;
    public int DebugLogRetentionDays { get; set; } = 1;
    public int DebugLogMaxMegabytes { get; set; } = 16;
    // Debug-only: toggle logging on/off (default off for performance)
    public bool EnableLogging { get; set; }

    // Hotkey configuration (stores Key enum value as int and KeyModifiers as int for serialization)
    public int LockHotkeyKey { get; set; } = DefaultLockHotkeyKey; // Default: Ctrl+L
    public int LockHotkeyModifiers { get; set; } = DefaultLockHotkeyModifiers; // Default: Ctrl modifier only
    public int ClearInputHotkeyKey { get; set; } = DefaultClearInputHotkeyKey; // Default: Ctrl+Shift+Q
    public int ClearInputHotkeyModifiers { get; set; } = DefaultClearInputHotkeyModifiers; // Default: Ctrl+Shift modifiers
    public int StreamerModeHotkeyKey { get; set; } = DefaultStreamerModeHotkeyKey; // Default: Ctrl+F7
    public int StreamerModeHotkeyModifiers { get; set; } = DefaultStreamerModeHotkeyModifiers; // Default: Ctrl modifier only

    // Localization: selected UI language (future expansion)
    public string Language { get; set; } = "English (US)";

    // Accessibility settings
    public bool ReduceMotion { get; set; }
    public bool HighContrastMode { get; set; }
    public int CursorBlinkRate { get; set; } = 530; // milliseconds (Windows default)
    public double CursorWidth { get; set; } = 1.0; // multiplier
    public bool ShowKeyboardFocus { get; set; } = true;
    public bool EnhancedKeyboardNavigation { get; set; } = true;

    // System Tray settings
    public bool MinimizeToTray { get; set; } // When enabled, close button minimizes to tray instead of closing
    public bool RunOnStartup { get; set; } // When enabled, app starts with Windows
    public bool ShowInSystemTray { get; set; } // When enabled, app icon appears in system tray
    public bool StartMinimized { get; set; } // When enabled, app starts hidden in system tray

    // Audio settings (volume ranges from 0.0 to 1.0)
    public double MainVolume { get; set; } = 1.0; // Overall volume control for all sounds
    public double NotificationVolume { get; set; } = 0.8; // Volume for general notifications
    public double ChatVolume { get; set; } = 0.7; // Volume for chat-related sounds (incoming/outgoing messages)

    // Notification behavior settings
    public bool SuppressNotificationsInDnd { get; set; } = true; // When enabled, notification toasts and audio are suppressed in Do Not Disturb mode
    public double NotificationDurationSeconds { get; set; } = 4.5; // Duration in seconds that notification toasts stay visible (0.5 to 30 seconds)
    public bool EnableNotificationBellFlash { get; set; } = true; // When enabled, the notification bell flashes for 10 seconds when new notifications arrive
    public bool EnableSmoothScrolling { get; set; } = true; // When enabled, log/auto-follow views use eased scroll animations
    public bool NotificationQuietHoursEnabled { get; set; } = false;
    public int NotificationQuietHoursStartHour { get; set; } = 22; // local hour (0-23)
    public int NotificationQuietHoursEndHour { get; set; } = 7; // local hour (0-23)
    public bool NotificationQuietHoursAllowPriority { get; set; } = true;
    public bool NotificationQuietHoursAllowMentions { get; set; } = true;

    // Auto-update settings
    public bool AutoUpdateEnabled { get; set; } = true;
    public bool AutoUpdateIncludePrerelease { get; set; } = true;
    public int AutoUpdateIntervalHours { get; set; } = 6;
    public string AutoUpdateOwner { get; set; } = "AnotherLaughingMan";
    public string AutoUpdateRepo { get; set; } = "Zer0Talk-Releases";
    public string AutoUpdateManifestUrl { get; set; } = "https://github.com/AnotherLaughingMan/Zer0Talk-Releases/releases/latest/download/update-manifest.json";
    public string? LastIgnoredUpdateVersion { get; set; }
    public string? LastAutoUpdateCheckUtc { get; set; }

    // Security: Message burning security level (false = Standard 3-pass, true = Enhanced 6-pass)
    public bool UseEnhancedMessageBurn { get; set; } = false; // Default to standard 3-pass for backward compatibility

    // Privacy policy: first-run acceptance tracking
    public bool PrivacyPolicyAccepted { get; set; } = false;
    public bool DoNotShowPrivacyAgain { get; set; } = false;

}
