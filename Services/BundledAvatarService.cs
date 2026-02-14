using System;
using System.IO;
using System.Security.Cryptography;

namespace Zer0Talk.Services
{
    public static class BundledAvatarService
    {
        public static byte[]? TryGetRandomAvatarBytes()
        {
            try
            {
                var avatarsDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Avatars");
                if (!Directory.Exists(avatarsDir)) return null;

                var pngFiles = Directory.GetFiles(avatarsDir, "*.png", SearchOption.TopDirectoryOnly);
                if (pngFiles.Length == 0) return null;

                var selected = pngFiles[RandomNumberGenerator.GetInt32(pngFiles.Length)];
                if (string.IsNullOrWhiteSpace(selected) || !File.Exists(selected)) return null;

                var bytes = File.ReadAllBytes(selected);
                return bytes.Length > 0 ? bytes : null;
            }
            catch
            {
                return null;
            }
        }
    }
}