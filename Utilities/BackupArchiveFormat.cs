using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace Zer0Talk.Utilities
{
    public static class BackupArchiveFormat
    {
        public const string ManifestEntryName = "backup.manifest.json";
        public const string FormatId = "Zer0TalkBackup";
        public const int FormatVersion = 1;

        public static readonly string[] IncludeRoots =
        {
            "messages",
            "outbox",
            "Themes",
            "Logs",
            "security"
        };

        public static readonly string[] IncludeFiles =
        {
            "settings.p2e",
            "contacts.p2e",
            "peers.p2e",
            "user.p2e",
            "window_state.json",
            "unlock.window.json"
        };

        public static string NormalizeEntryPath(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return string.Empty;

            var normalized = fullName.Replace('\\', '/').Trim();
            while (normalized.StartsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(1);
            }

            return normalized;
        }

        public static bool IsAllowedEntry(string relPath)
        {
            if (string.IsNullOrWhiteSpace(relPath)) return false;
            if (relPath.Contains("../", StringComparison.Ordinal) || relPath.Contains("..\\", StringComparison.Ordinal)) return false;

            if (string.Equals(relPath, ManifestEntryName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (IncludeFiles.Any(f => string.Equals(f, relPath, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            foreach (var root in IncludeRoots)
            {
                if (relPath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static void WriteManifest(ZipArchive archive, string appVersion)
        {
            var manifest = new BackupManifest
            {
                Format = FormatId,
                Version = FormatVersion,
                AppVersion = appVersion,
                CreatedUtc = DateTime.UtcNow,
                Includes = IncludeRoots.Concat(IncludeFiles).ToArray()
            };

            var entry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
            using var stream = entry.Open();
            JsonSerializer.Serialize(stream, manifest, new JsonSerializerOptions { WriteIndented = true });
        }

        public static bool TryReadManifest(ZipArchive archive, out BackupManifest? manifest)
        {
            manifest = null;
            var entry = archive.GetEntry(ManifestEntryName);
            if (entry == null) return false;

            using var stream = entry.Open();
            manifest = JsonSerializer.Deserialize<BackupManifest>(stream);
            return manifest != null;
        }

        public static bool IsSupportedManifest(BackupManifest? manifest)
        {
            if (manifest == null) return false;
            if (!string.Equals(manifest.Format, FormatId, StringComparison.Ordinal)) return false;
            return manifest.Version == FormatVersion;
        }

        public sealed class BackupManifest
        {
            public string Format { get; set; } = FormatId;
            public int Version { get; set; } = FormatVersion;
            public string AppVersion { get; set; } = "unknown";
            public DateTime CreatedUtc { get; set; }
            public string[] Includes { get; set; } = Array.Empty<string>();
        }
    }
}