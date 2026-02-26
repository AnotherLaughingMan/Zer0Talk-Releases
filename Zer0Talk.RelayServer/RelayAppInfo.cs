using System;
using System.Reflection;

namespace Zer0Talk.RelayServer;

public static class RelayAppInfo
{
    private static readonly Assembly Assembly = typeof(RelayAppInfo).Assembly;
    private static readonly string Informational = Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion
        ?? string.Empty;

    public static string Version => ExtractCoreVersion();
    public const string PrototypeTag = "Alpha";
    public const string AppUserModelId = "Zer0Talk.Relay";

    public static string PrototypeBadgeText => $"{PrototypeTag} v{Version}";

    public static bool IsVersionCompatible(string version1, string version2)
    {
        return string.Equals(version1, version2, StringComparison.Ordinal);
    }

    private static string ExtractCoreVersion()
    {
        var infoVersion = Informational;
        if (!string.IsNullOrWhiteSpace(infoVersion))
        {
            var plusIndex = infoVersion.IndexOf('+');
            if (plusIndex >= 0)
            {
                infoVersion = infoVersion.Substring(0, plusIndex);
            }

            var dashIndex = infoVersion.IndexOf('-');
            if (dashIndex > 0)
            {
                infoVersion = infoVersion.Substring(0, dashIndex);
            }

            if (!string.IsNullOrWhiteSpace(infoVersion))
            {
                return infoVersion;
            }
        }

        return Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
    }
}
