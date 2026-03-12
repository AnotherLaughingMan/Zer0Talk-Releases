namespace Zer0Talk.RelayServer.Services;

public sealed class RelayConfig
{
    public int Port { get; set; } = 443;
    public int DiscoveryPort { get; set; } = 38384;
    public bool AutoStart { get; set; } = true;
    public bool DiscoveryEnabled { get; set; } = true;
    public string RelayAddressToken { get; set; } = string.Empty;
    public int MaxPending { get; set; } = 256;
    public int MaxSessions { get; set; } = 512;
    public int PendingTimeoutSeconds { get; set; } = 60;
    public int BufferSize { get; set; } = 16 * 1024;
    public int MaxConnectionsPerMinute { get; set; } = 120;
    public int BanSeconds { get; set; } = 120;
    public bool ExposeSensitiveClientData { get; set; } = false;
    public int OperatorBlockSeconds { get; set; } = 1800;

    // System Tray settings
    public bool ShowInSystemTray { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool RunOnStartup { get; set; }
    public bool EnableSmoothScrolling { get; set; } = true;

    // Federation settings (server-to-server coordination)
    public bool EnableFederation { get; set; } = false;
    public int FederationPort { get; set; } = 8443; // Separate port for relay-to-relay communication
    public string FederationTrustMode { get; set; } = "AllowList"; // "AllowList" or "OpenNetwork"
    public System.Collections.Generic.List<string> PeerRelays { get; set; } = new(); // List of "host:port" for peer relays
    public int MaxFederationPeers { get; set; } = 10;
    public int FederationSyncIntervalSeconds { get; set; } = 30;
    public string FederationSharedSecret { get; set; } = string.Empty; // Optional: for relay authentication

    // Hosted Server settings (server-retained accounts + rooms)
    public bool EnableHosting { get; set; } = false;
    public int HostingPort { get; set; } = 8444;
    public int HostingS2SPort { get; set; } = 8445;                     // Server-to-server room federation port
    public string HostingAddress { get; set; } = string.Empty;          // Publicly reachable address for S2S (e.g. "relay.example.com")
    public System.Collections.Generic.List<string> PeerHostedServers { get; set; } = new(); // "host:8445" per peer
    public int MaxRegisteredUsers { get; set; } = 10_000;
    public int MaxRoomsPerUser { get; set; } = 10;
    public int MaxMembersPerRoom { get; set; } = 12;
    public int RoomMessageQueueDepth { get; set; } = 200;
    public string DataDirectory { get; set; } = "relay-data";
}
