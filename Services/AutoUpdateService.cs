using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Zer0Talk.Models;
using Zer0Talk.Utilities;

namespace Zer0Talk.Services;

public sealed class AutoUpdateService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private bool _started;
    private bool _disposed;

    private const string BgIntervalKey = "AutoUpdate.PeriodicCheck";
    private const string UserAgent = "Zer0Talk-AutoUpdate/1.0";

    public AutoUpdateService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;

        try
        {
            var settings = AppServices.Settings.Settings;
            var hours = Math.Clamp(settings.AutoUpdateIntervalHours, 1, 24 * 7);
            AppServices.Updates.RegisterBgInterval(BgIntervalKey, (int)TimeSpan.FromHours(hours).TotalMilliseconds, () =>
            {
                _ = CheckForUpdatesAsync(userInitiated: false, CancellationToken.None);
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"AutoUpdate: failed to register interval - {ex.Message}");
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                await CheckForUpdatesAsync(userInitiated: false, CancellationToken.None).ConfigureAwait(false);
            }
            catch { }
        });
    }

    public async Task CheckForUpdatesAsync(bool userInitiated, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        if (!await _checkGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var settings = AppServices.Settings.Settings;
            if (!userInitiated && !settings.AutoUpdateEnabled)
            {
                return;
            }

            var latest = await TryResolveLatestUpdateAsync(settings, cancellationToken).ConfigureAwait(false);
            if (latest == null)
            {
                if (userInitiated)
                {
                    await AppServices.Dialogs.ShowWarningAsync("Update Check", "No update feed was available.", 3500);
                }
                return;
            }

            var currentVersion = NormalizeVersionToken(AppInfo.Version);
            var latestVersion = NormalizeVersionToken(latest.Version);

            if (!IsRemoteVersionNewer(currentVersion, latestVersion))
            {
                if (userInitiated)
                {
                    await AppServices.Dialogs.ShowInfoAsync("Update Check", $"You are up to date (v{AppInfo.Version}).", 3000);
                }
                return;
            }

            if (!userInitiated &&
                !string.IsNullOrWhiteSpace(settings.LastIgnoredUpdateVersion) &&
                string.Equals(settings.LastIgnoredUpdateVersion, latest.Version, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var notes = string.IsNullOrWhiteSpace(latest.ReleaseNotesUrl) ? string.Empty : $"\n\nRelease notes:\n{latest.ReleaseNotesUrl}";
            var promptMessage =
                $"A new version is available.\n\n" +
                $"Current: {AppInfo.Version}\n" +
                $"Latest: {latest.Version}\n\n" +
                $"Zer0Talk will close and launch the installer to continue the update.{notes}";

            var shouldInstall = await AppServices.Dialogs.ConfirmAsync(
                "Update Available",
                promptMessage,
                confirmText: "Install Update",
                cancelText: "Later");

            if (!shouldInstall)
            {
                settings.LastIgnoredUpdateVersion = latest.Version;
                TrySaveSettings();
                return;
            }

            var downloadedInstaller = await DownloadInstallerAsync(latest, cancellationToken).ConfigureAwait(false);
            if (downloadedInstaller == null)
            {
                await AppServices.Dialogs.ShowErrorAsync("Update Failed", "Failed to download update installer.", 4000);
                return;
            }

            if (!string.IsNullOrWhiteSpace(latest.Sha256) && !VerifySha256(downloadedInstaller, latest.Sha256))
            {
                TryDeleteFile(downloadedInstaller);
                await AppServices.Dialogs.ShowErrorAsync("Update Failed", "Installer verification failed (SHA-256 mismatch).", 4500);
                return;
            }

            if (!TryLaunchInstaller(downloadedInstaller))
            {
                await AppServices.Dialogs.ShowErrorAsync("Update Failed", "Could not launch installer.", 4000);
                return;
            }

            settings.LastIgnoredUpdateVersion = null;
            TrySaveSettings();

            await AppServices.Dialogs.ShowInfoAsync("Updating", "Installer launched. Zer0Talk will now close.", 1600);

            try
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                        {
                            desktop.Shutdown();
                        }
                    }
                    catch { }
                });
            }
            catch { }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Log($"AutoUpdate: check failed - {ex.Message}");
            if (userInitiated)
            {
                try { await AppServices.Dialogs.ShowErrorAsync("Update Check", ex.Message, 4500); } catch { }
            }
        }
        finally
        {
            try { _checkGate.Release(); } catch { }
        }
    }

    public void Stop()
    {
        try { AppServices.Updates.UnregisterBg(BgIntervalKey); } catch { }
        _started = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        try { _httpClient.Dispose(); } catch { }
        try { _checkGate.Dispose(); } catch { }
    }

    private async Task<UpdateReleaseInfo?> TryResolveLatestUpdateAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var manifest = await TryGetManifestUpdateAsync(settings, cancellationToken).ConfigureAwait(false);
        if (manifest != null)
        {
            return manifest;
        }

        return await TryGetGitHubReleaseUpdateAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    private async Task<UpdateReleaseInfo?> TryGetManifestUpdateAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.AutoUpdateManifestUrl))
        {
            return null;
        }

        try
        {
            using var response = await _httpClient.GetAsync(settings.AutoUpdateManifestUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = doc.RootElement;

            var version = ReadString(root, "version") ?? string.Empty;
            var installerUrl = ReadString(root, "installerUrl") ?? ReadString(root, "assetUrl") ?? string.Empty;
            var sha = ReadString(root, "sha256") ?? string.Empty;
            var notes = ReadString(root, "releaseNotesUrl") ?? ReadString(root, "notesUrl") ?? string.Empty;
            var prerelease = ReadBool(root, "prerelease") ?? true;

            if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(installerUrl))
            {
                return null;
            }

            if (!settings.AutoUpdateIncludePrerelease && prerelease)
            {
                return null;
            }

            return new UpdateReleaseInfo(version, installerUrl, sha, notes, prerelease);
        }
        catch (Exception ex)
        {
            Logger.Log($"AutoUpdate: manifest check failed - {ex.Message}");
            return null;
        }
    }

    private async Task<UpdateReleaseInfo?> TryGetGitHubReleaseUpdateAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var owner = string.IsNullOrWhiteSpace(settings.AutoUpdateOwner) ? "AnotherLaughingMan" : settings.AutoUpdateOwner.Trim();
        var repo = string.IsNullOrWhiteSpace(settings.AutoUpdateRepo) ? "Zer0Talk-Releases" : settings.AutoUpdateRepo.Trim();

        var latestUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
        var releasesUrl = $"https://api.github.com/repos/{owner}/{repo}/releases";

        var release = await TryFetchReleaseObjectAsync(latestUrl, cancellationToken).ConfigureAwait(false);
        if (release == null)
        {
            var releaseList = await TryFetchReleaseListAsync(releasesUrl, cancellationToken).ConfigureAwait(false);
            release = releaseList?.FirstOrDefault();
        }

        if (release == null)
        {
            return null;
        }

        var prerelease = ReadBool(release.Value, "prerelease") ?? false;
        if (!settings.AutoUpdateIncludePrerelease && prerelease)
        {
            var releaseList = await TryFetchReleaseListAsync(releasesUrl, cancellationToken).ConfigureAwait(false);
            release = releaseList?.FirstOrDefault(r => !(ReadBool(r, "prerelease") ?? false));
            if (release == null)
            {
                return null;
            }
            prerelease = ReadBool(release.Value, "prerelease") ?? false;
        }

        var tagName = ReadString(release.Value, "tag_name") ?? string.Empty;
        var htmlUrl = ReadString(release.Value, "html_url") ?? string.Empty;

        if (!TrySelectInstallerAsset(release.Value, out var installerUrl, out var sha))
        {
            return null;
        }

        var version = ExtractVersionFromTag(tagName);
        if (string.IsNullOrWhiteSpace(version))
        {
            version = tagName;
        }

        return new UpdateReleaseInfo(version, installerUrl, sha, htmlUrl, prerelease);
    }

    private async Task<JsonElement?> TryFetchReleaseObjectAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private async Task<JsonElement[]?> TryFetchReleaseListAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return doc.RootElement
                .EnumerateArray()
                .Select(x => x.Clone())
                .ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static bool TrySelectInstallerAsset(JsonElement release, out string installerUrl, out string sha)
    {
        installerUrl = string.Empty;
        sha = string.Empty;

        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = ReadString(asset, "name") ?? string.Empty;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                name.IndexOf("Installer", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            installerUrl = ReadString(asset, "browser_download_url") ?? string.Empty;
            sha = ReadString(asset, "digest") ?? string.Empty;
            if (sha.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                sha = sha.Substring("sha256:".Length);
            }
            return !string.IsNullOrWhiteSpace(installerUrl);
        }

        return false;
    }

    private async Task<string?> DownloadInstallerAsync(UpdateReleaseInfo release, CancellationToken cancellationToken)
    {
        try
        {
            var fileName = GetSafeFileNameFromUrl(release.InstallerUrl);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = $"Zer0Talk-Installer-{DateTime.UtcNow:yyyyMMddHHmmss}.exe";
            }

            var updateDir = AppDataPaths.Combine(".cache", "updates");
            Directory.CreateDirectory(updateDir);
            var targetPath = Path.Combine(updateDir, fileName);

            using var response = await _httpClient.GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var dest = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 64, useAsync: true);
            await source.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);

            return targetPath;
        }
        catch (Exception ex)
        {
            Logger.Log($"AutoUpdate: download failed - {ex.Message}");
            return null;
        }
    }

    private static bool VerifySha256(string filePath, string expectedHash)
    {
        try
        {
            var normalizedExpected = expectedHash.Trim().ToLowerInvariant();
            using var stream = File.OpenRead(filePath);
            using var sha = SHA256.Create();
            var hashBytes = sha.ComputeHash(stream);
            var actual = Convert.ToHexString(hashBytes).ToLowerInvariant();
            return string.Equals(actual, normalizedExpected, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryLaunchInstaller(string installerPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(installerPath) ?? Environment.CurrentDirectory,
            };
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeVersionToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "0.0.0.0";
        }

        var value = raw.Trim();
        if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase) && value.Length > 1)
        {
            value = value.Substring(1);
        }

        var plus = value.IndexOf('+');
        if (plus >= 0)
        {
            value = value.Substring(0, plus);
        }

        return value;
    }

    private static bool IsRemoteVersionNewer(string current, string remote)
    {
        var currentNumeric = GetNumericVersionPrefix(current);
        var remoteNumeric = GetNumericVersionPrefix(remote);

        if (Version.TryParse(currentNumeric, out var currentVersion) && Version.TryParse(remoteNumeric, out var remoteVersion))
        {
            var cmp = remoteVersion.CompareTo(currentVersion);
            if (cmp > 0) return true;
            if (cmp < 0) return false;
        }

        return string.Compare(remote, current, StringComparison.OrdinalIgnoreCase) > 0;
    }

    private static string GetNumericVersionPrefix(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "0.0.0.0";
        }

        var end = version.Length;
        var dash = version.IndexOf('-');
        if (dash >= 0) end = Math.Min(end, dash);

        var raw = version.Substring(0, end);
        var pieces = raw.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length == 0)
        {
            return "0.0.0.0";
        }

        var nums = pieces
            .Select(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .ToList();

        while (nums.Count < 4)
        {
            nums.Add(0);
        }

        return string.Join('.', nums.Take(4));
    }

    private static string ExtractVersionFromTag(string tag)
    {
        var normalized = NormalizeVersionToken(tag);
        var match = System.Text.RegularExpressions.Regex.Match(normalized, "(?<ver>[0-9]+(?:\\.[0-9]+){1,3}(?:-[A-Za-z0-9.-]+)?)");
        if (match.Success)
        {
            return match.Groups["ver"].Value;
        }
        return normalized;
    }

    private static string? ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static bool? ReadBool(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static string GetSafeFileNameFromUrl(string url)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return string.Empty;
            }

            var fileName = Path.GetFileName(uri.LocalPath);
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }
            return fileName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch { }
    }

    private static void TrySaveSettings()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(AppServices.Passphrase))
            {
                AppServices.Settings.Save(AppServices.Passphrase);
            }
        }
        catch { }
    }

    private sealed record UpdateReleaseInfo(string Version, string InstallerUrl, string Sha256, string ReleaseNotesUrl, bool Prerelease);
}
