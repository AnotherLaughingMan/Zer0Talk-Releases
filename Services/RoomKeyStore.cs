using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Zer0Talk.Utilities;

namespace Zer0Talk.Services;

/// <summary>
/// Stores per-room symmetric key material encrypted at rest using DPAPI (Windows).
/// Keys are stored in %APPDATA%\Zer0Talk\room-keys.dpapi as a DPAPI-encrypted JSON blob.
/// On non-Windows platforms a plaintext fallback file is used (callers should warn the user).
/// </summary>
public sealed class RoomKeyStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Zer0Talk", "room-keys.dpapi");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    // In-memory cache — loaded lazily on first access
    private Dictionary<string, string>? _cache; // roomId → base64 key bytes
    private readonly object _lock = new();

    // ═══════════════════════════════════════════════════════════════
    //  Public API
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Get the stored key bytes for a room, or null if not found.</summary>
    public byte[]? GetKey(string roomId)
    {
        EnsureLoaded();
        lock (_lock)
        {
            if (!_cache!.TryGetValue(roomId, out var b64)) return null;
            try { return Convert.FromBase64String(b64); }
            catch { return null; }
        }
    }

    /// <summary>Persist key bytes for a room (overwrites existing).</summary>
    public void SetKey(string roomId, byte[] keyBytes)
    {
        EnsureLoaded();
        lock (_lock)
        {
            _cache![roomId] = Convert.ToBase64String(keyBytes);
        }
        Flush();
    }

    /// <summary>Remove a room's key (e.g. after leaving the room).</summary>
    public void DeleteKey(string roomId)
    {
        EnsureLoaded();
        bool removed;
        lock (_lock) { removed = _cache!.Remove(roomId); }
        if (removed) Flush();
    }

    /// <summary>Remove all keys (e.g. on logout / full wipe).</summary>
    public void Clear()
    {
        lock (_lock) { _cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
        Flush();
    }

    public bool HasKey(string roomId)
    {
        EnsureLoaded();
        lock (_lock) { return _cache!.ContainsKey(roomId); }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Load / save
    // ═══════════════════════════════════════════════════════════════

    private void EnsureLoaded()
    {
        if (_cache != null) return;
        lock (_lock)
        {
            if (_cache != null) return;
            _cache = Load();
        }
    }

    private static Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(StorePath))
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var raw = File.ReadAllBytes(StorePath);
            byte[] json;

            if (OperatingSystem.IsWindows())
            {
                json = Unprotect(raw);
            }
            else
            {
                // Non-Windows: stored as plain JSON (best-effort; data directory should be permission-locked)
                json = raw;
            }

            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts);
            return dict != null
                ? new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Logger.Log($"RoomKeyStore: load failed: {ex.Message}");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Flush()
    {
        try
        {
            Dictionary<string, string> snapshot;
            lock (_lock) { snapshot = new Dictionary<string, string>(_cache!, StringComparer.OrdinalIgnoreCase); }

            var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot, JsonOpts));
            byte[] toWrite;

            if (OperatingSystem.IsWindows())
            {
                toWrite = Protect(json);
            }
            else
            {
                toWrite = json;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllBytes(StorePath, toWrite);
        }
        catch (Exception ex)
        {
            Logger.Log($"RoomKeyStore: flush failed: {ex.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static byte[] Protect(byte[] data) =>
        ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static byte[] Unprotect(byte[] data) =>
        ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
}
