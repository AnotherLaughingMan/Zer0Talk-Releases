using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Zer0Talk.Models;
using Zer0Talk.Utilities;

namespace Zer0Talk.Services;

public partial class LinkPreviewService
{
    private static string BuildDisplayUrl(Uri uri)
    {
        try
        {
            return uri.Host;
        }
        catch
        {
            return uri.ToString();
        }
    }

    private async Task<HttpResponseMessage?> SendWithSafeRedirectsAsync(Uri requestUri, CancellationToken cancellationToken)
    {
        const int maxRedirects = 5;
        var current = requestUri;

        for (var i = 0; i <= maxRedirects; i++)
        {
            if (IsDisallowedPreviewTarget(current))
            {
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (IsRedirectStatusCode(response.StatusCode))
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location == null)
                {
                    return null;
                }

                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }

            return response;
        }

        return null;
    }

    private static bool IsRedirectStatusCode(System.Net.HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code is 301 or 302 or 303 or 307 or 308;
    }

    private static bool IsDisallowedPreviewTarget(Uri uri)
    {
        if (uri.IsLoopback)
        {
            return true;
        }

        var host = uri.Host?.Trim().TrimEnd('.') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(host))
        {
            return true;
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IPAddress.TryParse(host, out var ip))
        {
            return IsPrivateOrLocalAddress(ip);
        }

        try
        {
            var addresses = Dns.GetHostAddresses(host);
            foreach (var addr in addresses)
            {
                if (IsPrivateOrLocalAddress(addr))
                {
                    return true;
                }
            }
        }
        catch (SocketException)
        {
            return false;
        }
        catch
        {
            return true;
        }

        return false;
    }

    private static bool IsPrivateOrLocalAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            if (bytes[0] == 10) return true;
            if (bytes[0] == 127) return true;
            if (bytes[0] == 169 && bytes[1] == 254) return true;
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) return true;
            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
            {
                return true;
            }

            var bytes = ip.GetAddressBytes();
            // fc00::/7 unique local addresses
            if ((bytes[0] & 0xFE) == 0xFC)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBlockedShortUrlHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var trimmed = host.Trim().TrimEnd('.');
        foreach (var blocked in BlockedShortUrlHosts)
        {
            if (trimmed.Equals(blocked, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (trimmed.EndsWith("." + blocked, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static LinkPreview BuildRejectedShortUrlPreview(Uri requestUri)
    {
        var display = BuildDisplayUrl(requestUri);
        return new LinkPreview
        {
            Url = requestUri.ToString(),
            DisplayUrl = display,
            Title = "Short link blocked",
            Description = "This URL uses a known short-link host and was auto-rejected to prevent destination obfuscation.",
            SiteName = "Security"
        };
    }

    private static LinkPreview BuildFallbackPreview(Uri requestUri)
    {
        var display = BuildDisplayUrl(requestUri);
        var path = requestUri.PathAndQuery;
        var description = (string.IsNullOrWhiteSpace(path) || path == "/")
            ? "No preview metadata provided by this site."
            : Truncate(path, 240);

        return new LinkPreview
        {
            Url = requestUri.ToString(),
            DisplayUrl = display,
            Title = display,
            Description = description,
            SiteName = display
        };
    }

    private static LinkPreview ClonePreview(LinkPreview source)
    {
        return new LinkPreview
        {
            Url = source.Url,
            DisplayUrl = source.DisplayUrl,
            Title = source.Title,
            Description = source.Description,
            SiteName = source.SiteName,
            ImageUrl = source.ImageUrl,
            ImageMimeType = source.ImageMimeType,
            ImageBytes = source.ImageBytes != null ? (byte[])source.ImageBytes.Clone() : null,
            FetchedUtc = source.FetchedUtc
        };
    }

    private static void EnsureDefaultHeaders(HttpClient client)
    {
        try
        {
            if (!client.DefaultRequestHeaders.UserAgent.TryParseAdd("Zer0Talk-LinkPreview/1.0"))
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; Zer0Talk-LinkPreview/1.0)");
            }
        }
        catch { }

        var htmlHeader = new MediaTypeWithQualityHeaderValue("text/html");
        if (!client.DefaultRequestHeaders.Accept.Contains(htmlHeader))
        {
            client.DefaultRequestHeaders.Accept.Add(htmlHeader);
        }

        var jsonHeader = new MediaTypeWithQualityHeaderValue("application/json");
        if (!client.DefaultRequestHeaders.Accept.Contains(jsonHeader))
        {
            client.DefaultRequestHeaders.Accept.Add(jsonHeader);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        GC.SuppressFinalize(this);
        if (_disposeClient)
        {
            _httpClient.Dispose();
        }
    }

    private sealed record CachedPreview(LinkPreview Preview, DateTime Timestamp);
    private sealed record ImageDownloadResult(byte[] Bytes, string ContentType);

    private static bool IsYouTubeUri(Uri uri)
    {
        var host = uri.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<LinkPreview?> TryGetYouTubePreviewAsync(Uri requestUri, CancellationToken cancellationToken)
    {
        if (!TryResolveYouTubeWatchUri(requestUri, out var watchUri, out var videoId))
        {
            return null;
        }

        var oEmbedUrl = $"https://www.youtube.com/oembed?format=json&url={Uri.EscapeDataString(watchUri.ToString())}";

        try
        {
            using var response = await _httpClient.GetAsync(new Uri(oEmbedUrl), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? title = null;
            string? author = null;
            string? thumbnailUrl = null;

            if (root.TryGetProperty("title", out var titleElement))
            {
                title = titleElement.GetString();
            }

            if (root.TryGetProperty("author_name", out var authorElement))
            {
                author = authorElement.GetString();
            }

            if (root.TryGetProperty("thumbnail_url", out var thumbElement))
            {
                thumbnailUrl = thumbElement.GetString();
            }

            var preview = new LinkPreview
            {
                Url = watchUri.ToString(),
                DisplayUrl = BuildDisplayUrl(watchUri),
                Title = Truncate(title, 160),
                Description = Truncate(author, 200),
                SiteName = "YouTube"
            };

            if (string.IsNullOrWhiteSpace(preview.Title))
            {
                preview.Title = string.IsNullOrWhiteSpace(videoId) ? "YouTube Video" : $"YouTube Video ({videoId})";
            }

            if (string.IsNullOrWhiteSpace(preview.ImageUrl))
            {
                preview.ImageUrl = ResolveRelativeUrl(watchUri, thumbnailUrl) ?? BuildYouTubeThumbnailUrl(videoId);
            }

            return preview.IsEmpty ? null : preview;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            try { ErrorLogger.LogException(ex, source: "LinkPreview.YouTube"); } catch { }
            return null;
        }
    }

    private static bool TryResolveYouTubeWatchUri(Uri candidate, out Uri watchUri, out string videoId)
    {
        watchUri = null!;
        videoId = string.Empty;

        var host = candidate.Host;
        if (!IsYouTubeUri(candidate))
        {
            return false;
        }

        string? idCandidate = null;
        if (host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            var path = candidate.AbsolutePath.Trim('/');
            if (!string.IsNullOrWhiteSpace(path))
            {
                var slash = path.IndexOf('/');
                idCandidate = slash >= 0 ? path[..slash] : path;
            }
        }
        else
        {
            var path = candidate.AbsolutePath;
            if (path.StartsWith("/watch", StringComparison.OrdinalIgnoreCase))
            {
                idCandidate = GetQueryParameter(candidate, "v");
            }
            else if (path.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase))
            {
                var remainder = path[8..];
                var slashIndex = remainder.IndexOf('/');
                idCandidate = slashIndex >= 0 ? remainder[..slashIndex] : remainder;
            }
            else if (path.StartsWith("/embed/", StringComparison.OrdinalIgnoreCase))
            {
                var remainder = path[7..];
                var slashIndex = remainder.IndexOf('/');
                idCandidate = slashIndex >= 0 ? remainder[..slashIndex] : remainder;
            }
        }

        if (string.IsNullOrWhiteSpace(idCandidate))
        {
            return false;
        }

        videoId = SanitizeYouTubeVideoId(idCandidate);
        if (string.IsNullOrEmpty(videoId))
        {
            return false;
        }

        watchUri = new Uri($"https://www.youtube.com/watch?v={videoId}", UriKind.Absolute);
        return true;
    }

    private static string? GetQueryParameter(Uri uri, string key)
    {
        if (string.IsNullOrEmpty(uri.Query) || string.IsNullOrEmpty(key))
        {
            return null;
        }

        var query = uri.Query.AsSpan(1);
        while (!query.IsEmpty)
        {
            var ampIndex = query.IndexOf('&');
            ReadOnlySpan<char> segment;
            if (ampIndex >= 0)
            {
                segment = query[..ampIndex];
                query = query[(ampIndex + 1)..];
            }
            else
            {
                segment = query;
                query = ReadOnlySpan<char>.Empty;
            }

            if (segment.IsEmpty)
            {
                continue;
            }

            var equalsIndex = segment.IndexOf('=');
            ReadOnlySpan<char> name;
            ReadOnlySpan<char> value;
            if (equalsIndex >= 0)
            {
                name = segment[..equalsIndex];
                value = segment[(equalsIndex + 1)..];
            }
            else
            {
                name = segment;
                value = ReadOnlySpan<char>.Empty;
            }

            if (name.IsEmpty)
            {
                continue;
            }

            var decodedName = Uri.UnescapeDataString(name.ToString());
            if (!decodedName.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return Uri.UnescapeDataString(value.ToString());
        }

        return null;
    }

    private static string SanitizeYouTubeVideoId(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(candidate.Length);
        foreach (var ch in candidate)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')
            {
                builder.Append(ch);
            }
            else if (ch == '%')
            {
                // Attempt to keep percent-encoded sequences intact
                builder.Append(ch);
            }
            else
            {
                break;
            }
        }

        var sanitized = builder.ToString();
        if (sanitized.Contains('%', StringComparison.Ordinal))
        {
            try
            {
                sanitized = Uri.UnescapeDataString(sanitized);
            }
            catch
            {
                return string.Empty;
            }
        }

        var finalBuilder = new StringBuilder(sanitized.Length);
        foreach (var ch in sanitized)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')
            {
                finalBuilder.Append(ch);
            }
        }

        return finalBuilder.ToString();
    }

    private static string? BuildYouTubeThumbnailUrl(string videoId)
    {
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return null;
        }

        return $"https://img.youtube.com/vi/{videoId}/hqdefault.jpg";
    }
}
