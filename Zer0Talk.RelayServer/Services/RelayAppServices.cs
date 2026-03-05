namespace Zer0Talk.RelayServer.Services;

public static class RelayAppServices
{
    public static RelayConfig Config { get; private set; } = new();
    public static RelayHost Host { get; private set; } = new(new RelayConfig());

    public static void Initialize()
    {
        Config = RelayConfigStore.Load();
        Host = new RelayHost(Config);
    }

    public static void Shutdown()
    {
        try { Host.Stop(); } catch { }
    }
}
