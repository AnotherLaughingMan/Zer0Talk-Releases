using System;
using System.IO;

namespace P2PTalk.Utilities
{
    public static class AppDataPaths
    {
        public const string NewRootName = "ZTalk";
        public const string OldRootName = "P2PTalk";
        private static string? _profileSuffix; // optional, set via CLI to isolate roots per instance

        public static void SetProfileSuffix(string? suffix)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(suffix)) { _profileSuffix = null; return; }
                // Sanitize to safe characters: letters, digits, dash, underscore
                var buf = new System.Text.StringBuilder();
                foreach (var ch in suffix.Trim())
                {
                    if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_') buf.Append(ch);
                }
                var s = buf.ToString();
                _profileSuffix = string.IsNullOrWhiteSpace(s) ? null : s;
            }
            catch { _profileSuffix = null; }
        }

        public static string GetAppDataRoot()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        public static string Root
        {
            get
            {
                var baseDir = GetAppDataRoot();
                var name = NewRootName;
                if (!string.IsNullOrWhiteSpace(_profileSuffix)) name = name + "-" + _profileSuffix;
                return Path.Combine(baseDir, name);
            }
        }

        public static string OldRoot
        {
            get
            {
                var baseDir = GetAppDataRoot();
                return Path.Combine(baseDir, OldRootName);
            }
        }

        public static string Combine(params string[] parts)
        {
            if (parts == null || parts.Length == 0) return Root;
            var path = Root;
            foreach (var p in parts) path = Path.Combine(path, p);
            return path;
        }

        public static void MigrateIfNeeded()
        {
            try
            {
                var oldDir = OldRoot;
                var newDir = Root;
                if (Directory.Exists(newDir)) return;
                if (!Directory.Exists(oldDir))
                {
                    // Ensure new root exists
                    Directory.CreateDirectory(newDir);
                    return;
                }

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(newDir)!);
                }
                catch { }

                try
                {
                    Directory.Move(oldDir, newDir);
                }
                catch
                {
                    // Fallback to copy
                    try { CopyDirectoryRecursive(oldDir, newDir); } catch { }
                }

                try
                {
                    var flag = Path.Combine(newDir, "migrated.from.p2ptalk.txt");
                    File.WriteAllText(flag, $"Migrated {DateTime.UtcNow:O} from '{OldRootName}' to '{NewRootName}'.");
                }
                catch { }
            }
            catch { }
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var name = Path.GetFileName(file);
                var dest = Path.Combine(destDir, name);
                try { File.Copy(file, dest, overwrite: false); } catch { }
            }
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var name = Path.GetFileName(dir);
                var dest = Path.Combine(destDir, name);
                try { CopyDirectoryRecursive(dir, dest); } catch { }
            }
        }
    }
}
