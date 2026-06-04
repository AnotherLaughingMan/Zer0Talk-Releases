using System;
using System.IO;

using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Zer0Talk.Services
{
    public static class AvatarCache
    {
        private static FileSystemWatcher? _watcher;
        private static readonly object _gate = new();
        private static int _version;

        public static event Action<string>? AvatarChanged;
        public static int CurrentVersion { get { lock (_gate) return _version; } }

        public static void Start()
        {
            try
            {
                var dir = GetCacheDir();
                Directory.CreateDirectory(dir);
                _watcher = new FileSystemWatcher(dir)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                    Filter = "*.bin",
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };
                _watcher.Created += OnChanged;
                _watcher.Changed += OnChanged;
                _watcher.Deleted += OnChanged;
                _watcher.Renamed += OnRenamed;
            }
            catch { }
        }

        public static void Stop()
        {
            try
            {
                if (_watcher != null)
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Created -= OnChanged;
                    _watcher.Changed -= OnChanged;
                    _watcher.Deleted -= OnChanged;
                    _watcher.Renamed -= OnRenamed;
                    _watcher.Dispose();
                    _watcher = null;
                }
            }
            catch { }
        }

        private static void OnChanged(object sender, FileSystemEventArgs e)
        {
            var uid = Path.GetFileNameWithoutExtension(e.FullPath);
            Bump(uid);
        }

        private static void OnRenamed(object sender, RenamedEventArgs e)
        {
            var uid = Path.GetFileNameWithoutExtension(e.FullPath);
            Bump(uid);
        }

        private static void Bump(string? uid)
        {
            lock (_gate) { _version++; }
            if (!string.IsNullOrWhiteSpace(uid))
            {
                try { AvatarChanged?.Invoke(uid!); } catch { }
            }
        }

        public static string GetCacheDir()
        {
            return Path.Combine(Zer0Talk.Utilities.AppDataPaths.Root, ".cache");
        }

        public static string GetAvatarPath(string uid)
        {
            var safe = NormalizeUid(uid);
            if (string.IsNullOrWhiteSpace(safe)) return string.Empty;
            return Path.Combine(GetCacheDir(), safe + ".bin");
        }

        private static string NormalizeUid(string? uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return string.Empty;
            var trimmed = uid.Trim();
            if (trimmed.StartsWith("usr-", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 4)
            {
                return trimmed.Substring(4);
            }

            return trimmed;
        }

        public static bool Save(string uid, byte[] bytes)
        {
            try
            {
                var normalizedUid = NormalizeUid(uid);
                if (string.IsNullOrWhiteSpace(normalizedUid)) return false;

                if (bytes == null || bytes.Length == 0)
                {
                    return Delete(normalizedUid);
                }

                var path = GetAvatarPath(normalizedUid);
                if (string.IsNullOrWhiteSpace(path)) return false;

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

                File.WriteAllBytes(path, bytes);
                Bump(normalizedUid);
                return true;
            }
            catch { return false; }
        }

        public static bool Delete(string uid)
        {
            try
            {
                var normalizedUid = NormalizeUid(uid);
                var path = GetAvatarPath(normalizedUid);
                if (string.IsNullOrWhiteSpace(path))
                {
                    Bump(normalizedUid);
                    return false;
                }

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                Bump(normalizedUid);
                return true;
            }
            catch
            {
                try { Bump(NormalizeUid(uid)); } catch { }
                return false;
            }
        }

        public static IImage? TryLoad(string uid)
        {
            try
            {
                var path = GetAvatarPath(NormalizeUid(uid));
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
                using var fs = File.OpenRead(path);
                return new Bitmap(fs);
            }
            catch { return null; }
        }
    }
}

