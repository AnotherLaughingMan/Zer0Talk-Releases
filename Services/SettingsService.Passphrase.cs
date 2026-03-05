#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

using Zer0Talk.Containers;
using Zer0Talk.Models;
using Zer0Talk.Utilities;
using Avalonia.Input;

namespace Zer0Talk.Services;

public partial class SettingsService
{
    public bool TryGetRememberedPassphrase(out string? passphrase)
    {
        passphrase = null;
        try
        {
            // Prefer sidecar file so we can auto-unlock before decrypting settings
            var sidecar = GetPassphraseSidecarPath();
            if (OperatingSystem.IsWindows() && File.Exists(sidecar))
            {
                var protectedBytes = File.ReadAllBytes(sidecar);
                try
                {
                    var bytes = UnprotectRememberedPassphrase(protectedBytes);
                    try
                    {
                        passphrase = Encoding.UTF8.GetString(bytes);
                        return true;
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(bytes);
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(protectedBytes);
                }
            }

            // Fallback: read from settings (available only after settings are decrypted)
            var s = Settings;
            if (OperatingSystem.IsWindows() && s.RememberPassphrase && !string.IsNullOrWhiteSpace(s.RememberedPassphraseProtected))
            {
                var protectedBytes = Convert.FromBase64String(s.RememberedPassphraseProtected);
                try
                {
                    var bytes = UnprotectRememberedPassphrase(protectedBytes);
                    try
                    {
                        passphrase = Encoding.UTF8.GetString(bytes);
                        // Migrate legacy setting blob into sidecar-only storage.
                        SetRememberedPassphrase(passphrase);
                        return true;
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(bytes);
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(protectedBytes);
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public void SetRememberedPassphrase(string plaintext)
    {
        byte[]? bytes = null;
        byte[]? protectedBytes = null;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                bytes = Encoding.UTF8.GetBytes(plaintext);
                protectedBytes = ProtectRememberedPassphrase(bytes);
                // Write sidecar for early boot
                var sidecar = GetPassphraseSidecarPath();
                Directory.CreateDirectory(Path.GetDirectoryName(sidecar)!);
                File.WriteAllBytes(sidecar, protectedBytes);
                // Keep only the sidecar copy to reduce duplicate secret material.
                Settings.RememberPassphrase = true;
                Settings.RememberedPassphraseProtected = null;
                Save(AppServices.Passphrase);
                // Persist preference in a plaintext sidecar so Unlock can read it pre-decrypt
                SetRememberPreference(true);
            }
            else
            {
                Settings.RememberPassphrase = false;
                Settings.RememberedPassphraseProtected = null;
                SetRememberPreference(false);
            }
        }
        catch
        {
            Settings.RememberPassphrase = false;
            Settings.RememberedPassphraseProtected = null;
            SetRememberPreference(false);
        }
        finally
        {
            if (bytes != null) CryptographicOperations.ZeroMemory(bytes);
            if (protectedBytes != null) CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    public void ClearRememberedPassphrase()
    {
        Settings.RememberPassphrase = false;
        Settings.RememberedPassphraseProtected = null;
        try { Save(AppServices.Passphrase); } catch { }
        try { var sidecar = GetPassphraseSidecarPath(); if (File.Exists(sidecar)) File.Delete(sidecar); } catch { }
        // Also clear preference sidecar
        try { SetRememberPreference(false); } catch { }
    }

    // Purge the stored passphrase material but keep the user's preference flag intact.
    public void PurgeRememberedPassphraseKeepPreference()
    {
        // Do not change Settings.RememberPassphrase
        Settings.RememberedPassphraseProtected = null;
        try { Save(AppServices.Passphrase); } catch { }
        try { var sidecar = GetPassphraseSidecarPath(); if (File.Exists(sidecar)) File.Delete(sidecar); } catch { }
        // Keep preference sidecar as-is (true if user enabled it)
        try { if (Settings.RememberPassphrase) SetRememberPreference(true); } catch { }
    }

    // Preference sidecar API (now consolidated inside unlock.window.json; keep backward-compat with remember.pref)
    public bool GetRememberPreference()
    {
        try
        {
            // 1) Prefer consolidated unlock.window.json
            var jsonPath = GetUnlockWindowJsonPath();
            if (File.Exists(jsonPath))
            {
                var json = File.ReadAllText(jsonPath);
                var state = System.Text.Json.JsonSerializer.Deserialize<UnlockState?>(json);
                if (state?.RememberPreference is bool b) return b;
            }
            // 2) Legacy remember.pref (plaintext)
            var legacy = GetRememberPrefPath();
            if (File.Exists(legacy))
            {
                var text = File.ReadAllText(legacy).Trim();
                var val = string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) || text == "1";
                // migrate into unlock.window.json to centralize sidecar data
                TryWriteUnlockJsonRemember(val);
                try { File.Delete(legacy); } catch { }
                return val;
            }
            return false;
        }
        catch { return false; }
    }

    public void SetRememberPreference(bool remember)
    {
        try
        {
            // Write to consolidated unlock.window.json; remove legacy file if present
            TryWriteUnlockJsonRemember(remember);
            try { var legacy = GetRememberPrefPath(); if (File.Exists(legacy)) File.Delete(legacy); } catch { }
        }
        catch { }
    }

    private static string GetRememberPrefPath()
    {
        return Zer0Talk.Utilities.AppDataPaths.Combine("remember.pref");
    }

    private static string GetPassphraseSidecarPath()
    {
        return Zer0Talk.Utilities.AppDataPaths.Combine("passphrase.dpapi");
    }

    private static string GetUnlockWindowJsonPath()
    {
        return Zer0Talk.Utilities.AppDataPaths.Combine("unlock.window.json");
    }

    private static byte[] GetPassphraseEntropy()
    {
        var material = $"Zer0Talk|remember-passphrase|v1|{Environment.UserName}|{AppDataPaths.Root}";
        return SHA256.HashData(Encoding.UTF8.GetBytes(material));
    }

    private static byte[] ProtectRememberedPassphrase(byte[] plaintext)
    {
        var entropy = GetPassphraseEntropy();
        try
        {
            return ProtectedData.Protect(plaintext, optionalEntropy: entropy, scope: DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(entropy);
        }
    }

    private static byte[] UnprotectRememberedPassphrase(byte[] protectedBytes)
    {
        var entropy = GetPassphraseEntropy();
        try
        {
            try
            {
                return ProtectedData.Unprotect(protectedBytes, optionalEntropy: entropy, scope: DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException)
            {
                // Backward compatibility for existing blobs written without entropy.
                return ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(entropy);
        }
    }

    private static void TryWriteUnlockJsonRemember(bool remember)
    {
        try
        {
            var path = GetUnlockWindowJsonPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            UnlockState? existing = null;
            try { if (File.Exists(path)) existing = System.Text.Json.JsonSerializer.Deserialize<UnlockState?>(File.ReadAllText(path)); } catch { }
            existing ??= new UnlockState();
            existing.RememberPreference = remember;
            var json = System.Text.Json.JsonSerializer.Serialize(existing);
            System.IO.File.WriteAllText(path, json);
        }
        catch { }
    }

    private sealed class UnlockState
    {
        public double? Width { get; set; }
        public double? Height { get; set; }
        public double? X { get; set; }
        public double? Y { get; set; }
        public bool? RememberPreference { get; set; }
    }

    private static AppSettings CreateDefaultSettings()
    {
        var settings = new AppSettings
        {
            // Explicit defaults; stored only inside the encrypted container
            DisplayName = string.Empty,
            Theme = ThemeOption.Dark,
            Status = PresenceStatus.Online,
            Port = 26264,
            MajorNode = false,
            RememberPassphrase = false,
            RememberedPassphraseProtected = null,
            AutoLockEnabled = false,
            AutoLockMinutes = 0,
            LockOnMinimize = false,
            BlockList = new System.Collections.Generic.List<string>(),
            AdapterPriorityIds = new System.Collections.Generic.List<string>(),
            MainWindow = new WindowStateSettings(),
            SettingsWindow = new WindowStateSettings(),
            NetworkWindow = new WindowStateSettings(),
            MonitoringWindow = new WindowStateSettings(),
            LogViewerWindow = new WindowStateSettings(),
            RelayFallbackEnabled = true,
            RelayServer = null,
            SavedRelayServers = new System.Collections.Generic.List<string>(),
            RelayPresenceTimeoutSeconds = 45,
            RelayDiscoveryTtlMinutes = 3,
            ShowPublicKeys = false,
            EnableDebugLogAutoTrim = true,
            DebugUiLogMaxLines = 1000,
            DebugLogRetentionDays = 1,
            DebugLogMaxMegabytes = 16,
            // System Tray defaults
            ShowInSystemTray = true, // Default to enabled
            MinimizeToTray = false,
            RunOnStartup = false,
            StartMinimized = false,
        };
        NormalizeHotkeySettings(settings);
        return settings;
    }

    private static bool NormalizeHotkeySettings(AppSettings settings)
    {
        var changed = false;

    static bool HasValidModifiers(int modifiers) => (modifiers & ~ValidModifierMask) == 0;

        if (!Enum.IsDefined(typeof(Key), settings.LockHotkeyKey) || settings.LockHotkeyKey == (int)Key.None)
        {
            settings.LockHotkeyKey = AppSettings.DefaultLockHotkeyKey;
            changed = true;
        }

        if (!HasValidModifiers(settings.LockHotkeyModifiers))
        {
            settings.LockHotkeyModifiers = AppSettings.DefaultLockHotkeyModifiers;
            changed = true;
        }

        if (!Enum.IsDefined(typeof(Key), settings.ClearInputHotkeyKey) || settings.ClearInputHotkeyKey == (int)Key.None)
        {
            settings.ClearInputHotkeyKey = AppSettings.DefaultClearInputHotkeyKey;
            changed = true;
        }

        if (!HasValidModifiers(settings.ClearInputHotkeyModifiers))
        {
            settings.ClearInputHotkeyModifiers = AppSettings.DefaultClearInputHotkeyModifiers;
            changed = true;
        }

        // Legacy builds stored ASCII codes for default hotkeys; migrate them back to the intended bindings.
        if (settings.LockHotkeyKey == LegacyLockHotkeyKey && settings.LockHotkeyModifiers == AppSettings.DefaultLockHotkeyModifiers)
        {
            settings.LockHotkeyKey = AppSettings.DefaultLockHotkeyKey;
            changed = true;
        }

        if (settings.ClearInputHotkeyKey == LegacyClearInputHotkeyKey && settings.ClearInputHotkeyModifiers == AppSettings.DefaultClearInputHotkeyModifiers)
        {
            settings.ClearInputHotkeyKey = AppSettings.DefaultClearInputHotkeyKey;
            changed = true;
        }

        return changed;
    }
}
