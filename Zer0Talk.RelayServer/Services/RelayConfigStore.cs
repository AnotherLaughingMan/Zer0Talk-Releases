using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Zer0Talk.RelayServer.Services;

public static class RelayConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static RelayConfig Load()
    {
        var path = GetConfigPath();
        if (!File.Exists(path))
        {
            var cfg = new RelayConfig();
            EnsureDefaults(cfg);
            Save(cfg);
            return cfg;
        }

        try
        {
            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<RelayConfig>(json, JsonOptions) ?? new RelayConfig();
            var changed = EnsureDefaults(cfg);
            if (!changed)
            {
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    changed = HasMissingKnownProperties(doc.RootElement);
                }
                catch
                {
                    changed = true;
                }
            }
            if (changed)
            {
                Save(cfg);
            }
            return cfg;
        }
        catch
        {
            var cfg = new RelayConfig();
            EnsureDefaults(cfg);
            Save(cfg);
            return cfg;
        }
    }

    public static void Save(RelayConfig config)
    {
        var path = GetConfigPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json);
    }

    public static string GetConfigPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "Zer0TalkRelay", "relay-config.json");
    }

    public static string GetConfigGuidePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "Zer0TalkRelay", "relay-config-guide.md");
    }

    /// <summary>
    /// Returns the absolute path for the server data directory.
    /// If DataDirectory is a relative path, it is anchored to %APPDATA%\Zer0TalkRelay\.
    /// </summary>
    public static string GetDataDirectoryPath(RelayConfig config)
    {
        var dataDir = config.DataDirectory;
        if (string.IsNullOrWhiteSpace(dataDir)) dataDir = "relay-data";
        if (Path.IsPathRooted(dataDir)) return dataDir;
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "Zer0TalkRelay", dataDir);
    }

    private static bool EnsureDefaults(RelayConfig config)
    {
        var changed = false;
        if (config.Port is < 1 or > 65535)
        {
            config.Port = 443;
            changed = true;
        }

        if (config.DiscoveryPort is < 1 or > 65535)
        {
            config.DiscoveryPort = 38384;
            changed = true;
        }

        if (config.MaxPending <= 0)
        {
            config.MaxPending = 256;
            changed = true;
        }

        if (config.MaxSessions <= 0)
        {
            config.MaxSessions = 512;
            changed = true;
        }

        if (config.PendingTimeoutSeconds < 60)
        {
            config.PendingTimeoutSeconds = 60;
            changed = true;
        }

        if (config.BufferSize <= 0)
        {
            config.BufferSize = 16 * 1024;
            changed = true;
        }

        if (config.MaxConnectionsPerMinute <= 0)
        {
            config.MaxConnectionsPerMinute = 120;
            changed = true;
        }

        if (config.BanSeconds <= 0)
        {
            config.BanSeconds = 120;
            changed = true;
        }

        if (config.OperatorBlockSeconds <= 0)
        {
            config.OperatorBlockSeconds = 1800;
            changed = true;
        }

        if (config.FederationPort is < 1 or > 65535)
        {
            config.FederationPort = 8443;
            changed = true;
        }

        if (!string.Equals(config.FederationTrustMode, "AllowList", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(config.FederationTrustMode, "OpenNetwork", StringComparison.OrdinalIgnoreCase))
        {
            config.FederationTrustMode = "AllowList";
            changed = true;
        }

        if (config.PeerRelays == null)
        {
            config.PeerRelays = new System.Collections.Generic.List<string>();
            changed = true;
        }

        if (config.MaxFederationPeers <= 0)
        {
            config.MaxFederationPeers = 10;
            changed = true;
        }

        if (config.FederationSyncIntervalSeconds <= 0)
        {
            config.FederationSyncIntervalSeconds = 30;
            changed = true;
        }

        if (config.HostingPort is < 1 or > 65535)
        {
            config.HostingPort = 8444;
            changed = true;
        }

        if (config.HostingS2SPort is < 1 or > 65535)
        {
            config.HostingS2SPort = 8445;
            changed = true;
        }

        if (config.PeerHostedServers == null)
        {
            config.PeerHostedServers = new System.Collections.Generic.List<string>();
            changed = true;
        }

        if (config.MaxRegisteredUsers <= 0)
        {
            config.MaxRegisteredUsers = 10_000;
            changed = true;
        }

        if (config.MaxRoomsPerUser <= 0)
        {
            config.MaxRoomsPerUser = 10;
            changed = true;
        }

        if (config.MaxMembersPerRoom <= 0)
        {
            config.MaxMembersPerRoom = 12;
            changed = true;
        }

        if (config.RoomMessageQueueDepth <= 0)
        {
            config.RoomMessageQueueDepth = 200;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(config.DataDirectory))
        {
            config.DataDirectory = "relay-data";
            changed = true;
        }

        if (!IsValidRelayToken(config.RelayAddressToken))
        {
            config.RelayAddressToken = GenerateRelayToken(16);
            changed = true;
        }

        return changed;
    }

    private static bool HasMissingKnownProperties(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return true;

        return
            !root.TryGetProperty("DiscoveryPort", out _) ||
            !root.TryGetProperty("ExposeSensitiveClientData", out _) ||
            !root.TryGetProperty("OperatorBlockSeconds", out _) ||
            !root.TryGetProperty("EnableFederation", out _) ||
            !root.TryGetProperty("FederationPort", out _) ||
            !root.TryGetProperty("FederationTrustMode", out _) ||
            !root.TryGetProperty("PeerRelays", out _) ||
            !root.TryGetProperty("MaxFederationPeers", out _) ||
            !root.TryGetProperty("FederationSyncIntervalSeconds", out _) ||
            !root.TryGetProperty("FederationSharedSecret", out _) ||
            !root.TryGetProperty("EnableHosting", out _) ||
            !root.TryGetProperty("HostingPort", out _) ||
            !root.TryGetProperty("HostingS2SPort", out _) ||
            !root.TryGetProperty("PeerHostedServers", out _) ||
            !root.TryGetProperty("DataDirectory", out _);
    }

    private static bool IsValidRelayToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != 16) return false;
        for (var i = 0; i < token.Length; i++)
        {
            if (char.IsWhiteSpace(token[i]) || token[i] == '|' || token[i] == ':') return false;
        }
        return true;
    }

    private static string GenerateRelayToken(int length)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%^&*_-+=?";
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            var index = RandomNumberGenerator.GetInt32(alphabet.Length);
            chars[i] = alphabet[index];
        }
        return new string(chars);
    }
}
