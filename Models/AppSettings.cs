/*
    App settings model: persisted encrypted (settings.p2e) via SettingsService.
    - Holds window states and theme selection.
*/
// TODO[ANCHOR]: AppSettings - Window states and theme
using System;
using Avalonia.Input;

namespace ZTalk.Models;

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

    public string DisplayName { get; set; } = string.Empty;
    public ThemeOption Theme { get; set; } = ThemeOption.Dark;
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
    public System.Collections.Generic.List<string> KnownMajorNodes { get; set; } = new(); // "host:port"
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

    // Main window layout: persisted widths for resizable columns (in device-independent pixels).
    // Left = navigation + peers; Right = diagnostics. Center remains star-sized.
    public double? MainLeftWidth { get; set; }
    public double? MainRightWidth { get; set; }

    // Connectivity: optional relay fallback if direct+NAT traversal fail.
    // If enabled, the app will attempt to reach peers via a relay service when direct connection is not possible.
    // Format: "host:port" (e.g., "relay.example.com:443"). Leave null/empty to disable.
    public bool RelayFallbackEnabled { get; set; } = true;
    public string? RelayServer { get; set; }

    // Performance settings
    // CPU: CCD core affinity selection (0 = Auto, 1 = CCD 0, 2 = CCD 1)
    public int CcdAffinityIndex { get; set; }
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
    public bool EnableLogging { get; set; } = false;

    // Hotkey configuration (stores Key enum value as int and KeyModifiers as int for serialization)
    public int LockHotkeyKey { get; set; } = DefaultLockHotkeyKey; // Default: Ctrl+L
    public int LockHotkeyModifiers { get; set; } = DefaultLockHotkeyModifiers; // Default: Ctrl modifier only
    public int ClearInputHotkeyKey { get; set; } = DefaultClearInputHotkeyKey; // Default: Ctrl+Shift+Q
    public int ClearInputHotkeyModifiers { get; set; } = DefaultClearInputHotkeyModifiers; // Default: Ctrl+Shift modifiers

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
    public bool MinimizeToTray { get; set; } = false; // When enabled, close button minimizes to tray instead of closing
    public bool RunOnStartup { get; set; } = false; // When enabled, app starts with Windows
    public bool ShowInSystemTray { get; set; } = false; // When enabled, app icon appears in system tray
}
