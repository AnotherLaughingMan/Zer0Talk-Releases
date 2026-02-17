using System;
using System.Reflection;

namespace Zer0Talk;

public static class AppInfo
{
    private static readonly Assembly Assembly = typeof(AppInfo).Assembly;
    private static readonly string Informational = Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion
        ?? string.Empty;

    public static string Version => ExtractCoreVersion();
    public const string AppUserModelId = "Zer0Talk.App";
    public const string PrototypeTag = "InDev-Alpha";

    public static string PrototypeBadgeText => $"{PrototypeTag} v{Version}";
    
    // Version comparison utility
    public static bool IsVersionCompatible(string version1, string version2)
    {
        // For now, require exact match. Could be made more sophisticated later.
        return string.Equals(version1, version2, StringComparison.Ordinal);
    }
    
    // Parse version string to components for comparison
    public static (int major, int minor, int patch, int build) ParseVersion(string version)
    {
        try
        {
            var parts = version.Split('.');
            if (parts.Length >= 4)
            {
                return (
                    int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), 
                    int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), 
                    int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture), 
                    int.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture)
                );
            }
        }
        catch { }
        return (0, 0, 0, 0);
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
