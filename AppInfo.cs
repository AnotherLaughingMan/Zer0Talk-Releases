using System;
using System.Reflection;
using System.Linq;

namespace ZTalk;

public static class AppInfo
{
    // Auto-generated version from assembly metadata  
    public static readonly string Version = 
        Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? "0.0.1.58";
    public const string PrototypeTag = "InDev-Prototype";

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
}
