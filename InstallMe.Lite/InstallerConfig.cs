using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace InstallMe.Lite
{
    public sealed class InstallerConfig
    {
        public string AppDisplayName { get; set; } = "Zer0Talk";
        public string ExecutableName { get; set; } = "Zer0Talk.exe";
        public string DefaultInstallPath { get; set; } = "C:\\Apps\\Zer0Talk";
        public string AppDataFolderName { get; set; } = "Zer0Talk";
        public string ShortcutName { get; set; } = "Zer0Talk";
        public string AppUserModelId { get; set; } = "Zer0Talk.App";
        public string UninstallKeyName { get; set; } = "Zer0Talk_InstallMeLite";
        public string PackageResourceName { get; set; } = "zer0talk_release.zip";
        public string Publisher { get; set; } = "AnotherLaughingMan";
        public string DisplayVersion { get; set; } = "0.0.4.01";
        public List<string> MatchKeywords { get; set; } = new() { "Zer0Talk", "ZTalk", "P2PTalk" };
        public List<string> LegacyCleanupNames { get; set; } = new() { "ZTalk", "P2PTalk" };

        public string ExecutableProcessName => Path.GetFileNameWithoutExtension(ExecutableName);

        public static InstallerConfig Load(string[] args)
        {
            var config = new InstallerConfig();
            var configPath = ResolveConfigPath(args);
            if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            {
                config.Normalize();
                return config;
            }

            try
            {
                var json = File.ReadAllText(configPath);
                var parsed = JsonSerializer.Deserialize<InstallerConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (parsed != null)
                {
                    config = parsed;
                }
            }
            catch
            {
                // Fall back to defaults if config is invalid.
            }

            config.Normalize();
            return config;
        }

        private static string? ResolveConfigPath(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (!args[i].Equals("/config", StringComparison.OrdinalIgnoreCase) &&
                    !args[i].Equals("--config", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    return args[i + 1];
                }
            }

            var exeDir = AppContext.BaseDirectory;
            var candidate = Path.Combine(exeDir, "installer-config.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            return null;
        }

        private void Normalize()
        {
            AppDisplayName = string.IsNullOrWhiteSpace(AppDisplayName) ? "Application" : AppDisplayName.Trim();
            ExecutableName = string.IsNullOrWhiteSpace(ExecutableName) ? "App.exe" : ExecutableName.Trim();
            DefaultInstallPath = string.IsNullOrWhiteSpace(DefaultInstallPath) ? $"C:\\Apps\\{AppDisplayName}" : DefaultInstallPath.Trim();
            AppDataFolderName = string.IsNullOrWhiteSpace(AppDataFolderName) ? AppDisplayName : AppDataFolderName.Trim();
            ShortcutName = string.IsNullOrWhiteSpace(ShortcutName) ? AppDisplayName : ShortcutName.Trim();
            AppUserModelId = string.IsNullOrWhiteSpace(AppUserModelId) ? $"{AppDisplayName}.App" : AppUserModelId.Trim();
            UninstallKeyName = string.IsNullOrWhiteSpace(UninstallKeyName) ? $"{AppDisplayName}_InstallMeLite" : UninstallKeyName.Trim();
            PackageResourceName = string.IsNullOrWhiteSpace(PackageResourceName) ? "release.zip" : PackageResourceName.Trim();
            Publisher = string.IsNullOrWhiteSpace(Publisher) ? "Unknown" : Publisher.Trim();
            DisplayVersion = string.IsNullOrWhiteSpace(DisplayVersion) ? "1.0.0" : DisplayVersion.Trim();

            MatchKeywords = (MatchKeywords ?? new List<string>()).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            LegacyCleanupNames = (LegacyCleanupNames ?? new List<string>()).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (!MatchKeywords.Any())
            {
                MatchKeywords.Add(AppDisplayName);
                MatchKeywords.Add(ShortcutName);
                MatchKeywords.Add(ExecutableProcessName);
            }

            if (!MatchKeywords.Contains(AppDisplayName, StringComparer.OrdinalIgnoreCase))
            {
                MatchKeywords.Add(AppDisplayName);
            }
            if (!MatchKeywords.Contains(ShortcutName, StringComparer.OrdinalIgnoreCase))
            {
                MatchKeywords.Add(ShortcutName);
            }
            if (!MatchKeywords.Contains(ExecutableProcessName, StringComparer.OrdinalIgnoreCase))
            {
                MatchKeywords.Add(ExecutableProcessName);
            }
        }
    }
}
