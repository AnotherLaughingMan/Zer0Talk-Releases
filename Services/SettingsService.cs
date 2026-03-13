/*
    Manages encrypted app settings (settings.p2e).
    - Decrypts only after unlock; on mismatch, ResetToDefaults(passphrase) to recover.
    - Persists window states (size/pos, Topmost) and theme choice.
*/
// TODO[ANCHOR]: SettingsService - Load/Save encrypted settings container
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Zer0Talk.Containers;
using Zer0Talk.Models;
using Zer0Talk.Utilities;
using Avalonia.Input;

namespace Zer0Talk.Services;

public partial class SettingsService
{
    private readonly P2EContainer _container;
    private const string FileName = "settings.p2e";
    private const int LegacyLockHotkeyKey = 68;
    private const int LegacyClearInputHotkeyKey = 82;
    private const int ValidModifierMask = (int)(KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta);
    public AppSettings Settings { get; private set; } = new();
    public string GetSettingsPath() => GetPath();

    public SettingsService(P2EContainer container)
    {
        _container = container;
    }

    public void Load(string passphrase)
    {
        var path = GetPath();
        try
        {
            if (!File.Exists(path))
            {
                // First launch: create encrypted container with explicit defaults
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                Settings = CreateDefaultSettings();
                NormalizeHotkeySettings(Settings);
                Save(passphrase);
                Logger.Log($"Created encrypted settings at: {path}");
                
                // Sync logging state with settings after creation (Debug builds only)
                try
                {
                    Zer0Talk.Utilities.LoggingPaths.SyncWithSettings();
                }
                catch { }

                // Sync audio settings after creation
                try
                {
                    AppServices.SyncAudioSettings();
                }
                catch { }
                
                return;
            }

            // Detect container format for potential upgrade (P2E2 -> P2E3)
            bool needsUpgrade = false;
            using (var fs = File.OpenRead(path))
            {
                Span<byte> header = stackalloc byte[4];
                if (fs.Read(header) != 4) throw new InvalidDataException("Invalid settings header");
                var magic = Encoding.ASCII.GetString(header);
                if (string.Equals(magic, "P2E2", StringComparison.Ordinal))
                {
                    needsUpgrade = true;
                }
            }

            var bytes = _container.LoadFile(path, passphrase);
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json) ?? throw new InvalidDataException("Invalid settings content");
            Settings = loaded;
            var hotkeysNormalized = NormalizeHotkeySettings(Settings);
            if (hotkeysNormalized)
            {
                try { Save(passphrase); } catch { }
            }
            
            // Sync logging state with settings after load (Debug builds only)
            try
            {
                Zer0Talk.Utilities.LoggingPaths.SyncWithSettings();
            }
            catch { }

            // Sync audio settings after load
            try
            {
                AppServices.SyncAudioSettings();
            }
            catch { }

            // Migration: bump legacy default port 5555 to new default 26264 unless explicitly changed by user.
            try
            {
                if (Settings.Port == 5555)
                {
                    Settings.Port = 26264;
                    Save(passphrase);
                    Logger.Log("Settings migration: updated default port from 5555 to 26264.");
                    try { ErrorLogger.LogException(new InvalidOperationException("[Regression] Port auto-migrated from 5555 to 26264"), source: "Settings.Migration"); } catch { }
                }
            }
            catch { }

            if (needsUpgrade)
            {
                // Rewrite with current format (P2E3/XChaCha20)
                Save(passphrase);
                Logger.Log($"Upgraded settings container to P2E3: {path}");
                try { ErrorLogger.LogException(new InvalidOperationException("[Regression] Settings container upgraded to P2E3"), source: "Settings.Upgrade"); } catch { }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Settings load failed: {ex.Message}");
            // Do not proceed with fallback defaults; surface the error
            throw;
        }
    }

    public void Save(string passphrase)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(GetPath())!);
            var json = JsonSerializer.Serialize(Settings, SerializationDefaults.Indented);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            var path = GetPath();
            _container.SaveFile(path, bytes, passphrase);
            Logger.Log($"Settings saved: {path} ({bytes.Length} bytes before encryption)");
        }
        catch (Exception ex)
        {
            Logger.Log($"Settings save failed: {ex.Message}");
            // Swallow to avoid crashing the app on save
        }
    }

    public void ResetToDefaults(string passphrase)
    {
        Settings = CreateDefaultSettings();
        Save(passphrase);
        Logger.Log("Settings reset to defaults and re-encrypted with current passphrase.");
    }

    private static string GetPath()
    {
        return Zer0Talk.Utilities.AppDataPaths.Combine(FileName);
    }

    // (kept single public GetSettingsPath above)

    // Remembered passphrase helpers
}
