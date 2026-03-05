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

public sealed partial class AutoUpdateService
{
    private async Task<DownloadResult> DownloadInstallerAsync(UpdateReleaseInfo release, CancellationToken cancellationToken)
    {
        try
        {
            if (!IsTrustedHttpsUrl(release.InstallerUrl))
            {
                Logger.Log($"AutoUpdate: download blocked for untrusted URL: {release.InstallerUrl}");
                return DownloadResult.Fail("Installer URL is not trusted HTTPS.");
            }

            var fileName = GetSafeFileNameFromUrl(release.InstallerUrl);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = $"Zer0Talk-Installer-{DateTime.UtcNow:yyyyMMddHHmmss}.exe";
            }
            else if (!fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                     !fileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".exe";
            }

            var updateDir = AppDataPaths.Combine(".cache", "updates");
            Directory.CreateDirectory(updateDir);
            var targetPath = Path.Combine(updateDir, fileName);

            Exception? lastException = null;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var response = await _httpClient.GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        if (attempt == 3)
                        {
                            return DownloadResult.Fail($"Download request failed: {(int)response.StatusCode} {response.ReasonPhrase}");
                        }

                        await Task.Delay(TimeSpan.FromMilliseconds(1200), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    await using var dest = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 64, useAsync: true);
                    await source.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);

                    var info = new FileInfo(targetPath);
                    if (!info.Exists || info.Length <= 0)
                    {
                        if (attempt == 3)
                        {
                            TryDeleteFile(targetPath);
                            return DownloadResult.Fail("Downloaded installer was empty.");
                        }

                        TryDeleteFile(targetPath);
                        await Task.Delay(TimeSpan.FromMilliseconds(1200), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (!VerifyInstallerAuthenticode(targetPath))
                    {
                        // Allow unsigned prerelease installers for development channels.
                        if (!release.Prerelease)
                        {
                            TryDeleteFile(targetPath);
                            Logger.Log("AutoUpdate: installer blocked because Authenticode verification failed");
                            return DownloadResult.Fail("Installer signature verification failed.");
                        }

                        Logger.Log("AutoUpdate: prerelease installer has no trusted Authenticode signature; allowing by policy");
                    }

                    return DownloadResult.Ok(targetPath);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (attempt == 3)
                    {
                        break;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(1200), cancellationToken).ConfigureAwait(false);
                }
            }

            return DownloadResult.Fail(lastException?.Message ?? "Unknown download failure.");
        }
        catch (Exception ex)
        {
            Logger.Log($"AutoUpdate: download failed - {ex.Message}");
            return DownloadResult.Fail(ex.Message);
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

        // Same normalized numeric version: do not auto-upgrade/downgrade between suffix variants
        // (for example stable vs alpha/dev of the same base version).
        return false;
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

    private static bool IsTrustedHttpsUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var host = uri.Host.Trim().TrimEnd('.');
        foreach (var trusted in TrustedUpdateHosts)
        {
            if (host.Equals(trusted, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + trusted, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool VerifyInstallerAuthenticode(string installerPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
#pragma warning disable SYSLIB0057
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(installerPath));
#pragma warning restore SYSLIB0057
            if (string.IsNullOrWhiteSpace(cert.Thumbprint))
            {
                return false;
            }

            return cert.Verify();
        }
        catch
        {
            return false;
        }
    }

    private static void PostUpdateNotice(NotificationType type, string body, string? fullBody = null, bool isPersistent = true)
    {
        try
        {
            AppServices.Notifications.PostNotice(type, body, originUid: null, fullBody: fullBody ?? body, isPersistent: isPersistent);
        }
        catch { }
    }

    private static async Task ShowUpdateErrorAsync(string message, bool userInitiated)
    {
        PostUpdateNotice(NotificationType.Error, message);
        if (userInitiated)
        {
            try { await AppServices.Dialogs.ShowErrorAsync("Update Failed", message, 4000); } catch { }
        }
    }

    private static void RequestAppShutdown()
    {
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

    private async Task InstallAndLaunchAsync(UpdateReleaseInfo latest, AppSettings settings, CancellationToken cancellationToken, bool userInitiated)
    {
        var download = await DownloadInstallerAsync(latest, cancellationToken).ConfigureAwait(false);
        if (!download.Success || string.IsNullOrWhiteSpace(download.Path))
        {
            var message = string.IsNullOrWhiteSpace(download.Error)
                ? "Failed to download update installer."
                : $"Failed to download update installer: {download.Error}";
            await ShowUpdateErrorAsync(message, userInitiated).ConfigureAwait(false);
            return;
        }

        var downloadedInstaller = download.Path;

        if (!string.IsNullOrWhiteSpace(latest.Sha256) && !VerifySha256(downloadedInstaller, latest.Sha256))
        {
            TryDeleteFile(downloadedInstaller);
            await ShowUpdateErrorAsync("Installer verification failed (SHA-256 mismatch).", userInitiated).ConfigureAwait(false);
            return;
        }

        if (!TryLaunchInstaller(downloadedInstaller))
        {
            await ShowUpdateErrorAsync("Could not launch installer.", userInitiated).ConfigureAwait(false);
            return;
        }

        settings.LastIgnoredUpdateVersion = null;
        TrySaveSettings();
        PostUpdateNotice(NotificationType.Update, $"Installer launched for v{latest.Version}. Zer0Talk will now close.");

        if (userInitiated)
        {
            try { await AppServices.Dialogs.ShowInfoAsync("Updating", "Installer launched. Zer0Talk will now close.", 1600); } catch { }
        }

        RequestAppShutdown();
    }

    private sealed record UpdateReleaseInfo(string Version, string InstallerUrl, string Sha256, string ReleaseNotesUrl, bool Prerelease);
    private readonly record struct DownloadResult(bool Success, string? Path, string? Error)
    {
        public static DownloadResult Ok(string path) => new(true, path, null);
        public static DownloadResult Fail(string error) => new(false, null, error);
    }
}
