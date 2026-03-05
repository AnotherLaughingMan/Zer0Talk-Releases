using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Zer0Talk.Models;
using Zer0Talk.Utilities;

namespace Zer0Talk.Services;

public sealed partial class AutoUpdateService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private bool _started;
    private bool _disposed;
    private string? _lastNotifiedUpdateVersion;

    private const string BgIntervalKey = "AutoUpdate.PeriodicCheck";
    private const string UserAgent = "Zer0Talk-AutoUpdate/1.0";
    private static readonly string[] InstallerNameHints = { "installer", "setup", "zer0talk" };
    private static readonly string[] TrustedUpdateHosts =
    {
        "github.com",
        "api.github.com",
        "objects.githubusercontent.com",
        "githubusercontent.com",
        "github-releases.githubusercontent.com"
    };

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
        static void LogManualCheck(string stage, string details)
        {
            try { Logger.Log($"AutoUpdate: manual-check {stage} - {details}"); } catch { }
        }

        if (_disposed)
        {
            return;
        }

        if (userInitiated)
        {
            await _checkGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var gateAcquired = await _checkGate.WaitAsync(0, cancellationToken).ConfigureAwait(false);
            if (!gateAcquired)
            {
                try { Logger.Log("AutoUpdate: skipping background check (already running)"); } catch { }
                return;
            }
        }

        AppSettings? settings = null;
        var recordLastCheck = false;

        try
        {
            settings = AppServices.Settings.Settings;
            if (!userInitiated && !settings.AutoUpdateEnabled)
            {
                return;
            }

            recordLastCheck = true;

            if (userInitiated)
            {
                // Give immediate UX feedback before manifest/GitHub network work begins.
                PostUpdateNotice(NotificationType.Update, "Checking for updates...", isPersistent: false);
                LogManualCheck("start", $"current={AppInfo.Version}");
            }

            var latest = await TryResolveLatestUpdateAsync(settings, cancellationToken).ConfigureAwait(false);
            if (latest == null)
            {
                if (userInitiated)
                {
                    LogManualCheck("result", "feed-unavailable");
                    PostUpdateNotice(NotificationType.Warning, "No update feed was available.");
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
                    LogManualCheck("result", $"up-to-date current={AppInfo.Version} latest={latest.Version}");
                    PostUpdateNotice(NotificationType.Update, $"You are already up to date (v{AppInfo.Version}).", isPersistent: false);
                    await AppServices.Dialogs.ShowInfoAsync("Update Check", $"You are already up to date (v{AppInfo.Version}).", 3000);
                }
                return;
            }

            var autoInstallEnabled = settings.AutoUpdateEnabled;

            if (!userInitiated &&
                !autoInstallEnabled &&
                !string.IsNullOrWhiteSpace(settings.LastIgnoredUpdateVersion) &&
                string.Equals(settings.LastIgnoredUpdateVersion, latest.Version, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (userInitiated || !string.Equals(_lastNotifiedUpdateVersion, latest.Version, StringComparison.OrdinalIgnoreCase))
            {
                var availableBody = $"Update available: {latest.Version} (current: {AppInfo.Version}).";
                var availableDetails = string.IsNullOrWhiteSpace(latest.ReleaseNotesUrl)
                    ? availableBody
                    : $"{availableBody}\n\nRelease notes:\n{latest.ReleaseNotesUrl}";
                PostUpdateNotice(NotificationType.Update, availableBody, availableDetails);
                _lastNotifiedUpdateVersion = latest.Version;
            }

            if (userInitiated)
            {
                LogManualCheck("result", $"update-available current={AppInfo.Version} latest={latest.Version} autoInstall={(autoInstallEnabled ? "on" : "off")}");
            }

            if (autoInstallEnabled)
            {
                PostUpdateNotice(NotificationType.Update, $"Auto update is enabled. Downloading v{latest.Version} now...");
                await InstallAndLaunchAsync(latest, settings, cancellationToken, userInitiated: false).ConfigureAwait(false);
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
                PostUpdateNotice(NotificationType.Update, $"Update {latest.Version} postponed.", isPersistent: false);
                return;
            }

            await InstallAndLaunchAsync(latest, settings, cancellationToken, userInitiated: true).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (userInitiated)
            {
                LogManualCheck("result", "canceled");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"AutoUpdate: check failed - {ex.Message}");
            if (userInitiated)
            {
                LogManualCheck("result", $"failed error={ex.Message}");
                try { await AppServices.Dialogs.ShowErrorAsync("Update Check", ex.Message, 4500); } catch { }
            }
        }
        finally
        {
            try
            {
                if (recordLastCheck && settings != null)
                {
                    settings.LastAutoUpdateCheckUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                    TrySaveSettings();
                }
            }
            catch { }

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

        if (!IsTrustedHttpsUrl(settings.AutoUpdateManifestUrl))
        {
            Logger.Log($"AutoUpdate: manifest URL rejected as untrusted: {settings.AutoUpdateManifestUrl}");
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

            if (!IsTrustedHttpsUrl(installerUrl))
            {
                Logger.Log($"AutoUpdate: installer URL rejected as untrusted: {installerUrl}");
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
            var isInstallerExtension = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
            var looksLikeInstaller = InstallerNameHints.Any(h => name.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0);
            if (!isInstallerExtension || !looksLikeInstaller)
            {
                continue;
            }

            installerUrl = ReadString(asset, "browser_download_url") ?? string.Empty;
            sha = ReadString(asset, "digest") ?? string.Empty;
            if (sha.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                sha = sha.Substring("sha256:".Length);
            }
            return !string.IsNullOrWhiteSpace(installerUrl) && IsTrustedHttpsUrl(installerUrl);
        }

        return false;
    }

}
