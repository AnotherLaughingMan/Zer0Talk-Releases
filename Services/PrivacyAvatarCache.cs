using System;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Zer0Talk.Services;

public static class PrivacyAvatarCache
{
    private static readonly object Gate = new();

    public static string GetCacheDir()
    {
        return Path.Combine(Utilities.AppDataPaths.Root, ".cache", "privacy-avatars");
    }

    public static bool Ensure(string uid)
    {
        try
        {
            var path = GetAvatarPath(uid);
            if (string.IsNullOrWhiteSpace(path)) return false;
            lock (Gate)
            {
                if (File.Exists(path)) return true;
                var bytes = BundledAvatarService.TryGetRandomAvatarBytes();
                if (bytes == null || bytes.Length == 0) return false;
                Directory.CreateDirectory(GetCacheDir());
                File.WriteAllBytes(path, bytes);
                return true;
            }
        }
        catch { return false; }
    }

    public static IImage? TryLoadOrCreate(string uid)
    {
        try
        {
            if (!Ensure(uid)) return null;
            var path = GetAvatarPath(uid);
            using var stream = File.OpenRead(path);
            return new Bitmap(stream);
        }
        catch { return null; }
    }

    private static string GetAvatarPath(string uid)
    {
        var normalized = NormalizeUid(uid);
        return string.IsNullOrWhiteSpace(normalized)
            ? string.Empty
            : Path.Combine(GetCacheDir(), normalized + ".bin");
    }

    private static string NormalizeUid(string? uid)
    {
        if (string.IsNullOrWhiteSpace(uid)) return string.Empty;
        var trimmed = uid.Trim();
        if (trimmed.StartsWith("usr-", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 4)
            trimmed = trimmed.Substring(4);
        return Path.GetFileName(trimmed);
    }

    public static void Clear()
    {
        try
        {
            lock (Gate)
            {
                if (!Directory.Exists(GetCacheDir())) return;
                foreach (var file in Directory.GetFiles(GetCacheDir(), "*.bin", SearchOption.TopDirectoryOnly))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }
}
