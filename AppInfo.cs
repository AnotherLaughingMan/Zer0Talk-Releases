using System;

namespace ZTalk;

public static class AppInfo
{
    public const string Version = "0.0.1.57"; // Increment version for version control update
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
                    int.Parse(parts[0]), 
                    int.Parse(parts[1]), 
                    int.Parse(parts[2]), 
                    int.Parse(parts[3])
                );
            }
        }
        catch { }
        return (0, 0, 0, 0);
    }
}
