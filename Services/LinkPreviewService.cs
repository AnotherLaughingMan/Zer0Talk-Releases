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

public partial class LinkPreviewService : IDisposable
{
    public static readonly TimeSpan PreviewCacheDuration = TimeSpan.FromHours(6);
    private const int MaxHtmlBytes = 256 * 1024;
    private const int MaxImageBytes = 256 * 1024;

    private static readonly Regex MetaTagRegex = new("<meta[^>]+?(?:property|name)=[\"'](?<name>[^\"']+)[\"'][^>]*?content=[\"'](?<content>[^\"']*)[\"'][^>]*?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TitleRegex = new("<title[^>]*>(?<title>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex CharsetMetaRegex = new("<meta[^>]+charset=([\"']?)(?<charset>[a-z0-9_\\-]+)\\1", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UrlRegex = new("https?://[^\\s<>\\)\\]\"']+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.Compiled);
    private static readonly Regex ScriptTagRegex = new("<script[^>]*>[\\s\\S]*?(?:</script>|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StyleTagRegex = new("<style[^>]*>[\\s\\S]*?(?:</style>|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HtmlCommentRegex = new("<!--.*?(?:-->|$)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly HashSet<string> BlockedShortUrlHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "bit.ly",
        "t.co",
        "tinyurl.com",
        "goo.gl",
        "ow.ly",
        "is.gd",
        "buff.ly",
        "cutt.ly",
        "tiny.cc",
        "shorturl.at",
        "rebrand.ly",
        "lnkd.in",
        "rb.gy",
        "soo.gd",
        "s2r.co",
        "adf.ly",
        "bit.do"
    };

    private readonly ConcurrentDictionary<string, CachedPreview> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _httpClient;
    private readonly bool _disposeClient;
    private bool _disposed;

    public LinkPreviewService(HttpClient? httpClient = null)
    {
        if (httpClient is null)
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                // Follow redirects manually so each hop can be SSRF-validated.
                AllowAutoRedirect = false,
            };
            _httpClient = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(6)
            };
            _disposeClient = true;
        }
        else
        {
            _httpClient = httpClient;
            if (_httpClient.Timeout == Timeout.InfiniteTimeSpan)
            {
                _httpClient.Timeout = TimeSpan.FromSeconds(6);
            }
            _disposeClient = false;
        }

        EnsureDefaultHeaders(_httpClient);
    }

    public async Task<LinkPreview?> GetPreviewAsync(string url, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!TryNormalizeUrl(url, out var requestUri, out var cacheKey))
        {
            return null;
        }

        if (IsBlockedShortUrlHost(requestUri.Host))
        {
            return BuildRejectedShortUrlPreview(requestUri);
        }

        if (_cache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.Timestamp <= PreviewCacheDuration)
        {
            return ClonePreview(cached.Preview);
        }

        try
        {
            LinkPreview? preview = null;

            if (IsYouTubeUri(requestUri))
            {
                preview = await TryGetYouTubePreviewAsync(requestUri, cancellationToken).ConfigureAwait(false);
            }

            if (preview == null)
            {
                var html = await DownloadHtmlAsync(requestUri, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(html))
                {
                    return BuildFallbackPreview(requestUri);
                }

                preview = ParseMetadata(requestUri, html);
                preview ??= BuildFallbackPreview(requestUri);
            }

            var previewImageUrl = preview.ImageUrl;
            if (!string.IsNullOrWhiteSpace(previewImageUrl))
            {
                var imageResult = await TryHydratePreviewImageAsync(previewImageUrl, cancellationToken).ConfigureAwait(false);
                if (imageResult != null)
                {
                    preview.ImageBytes = imageResult.Bytes;
                    preview.ImageMimeType = imageResult.ContentType;
                }
            }

            preview.FetchedUtc = DateTime.UtcNow;
            _cache[cacheKey] = new CachedPreview(ClonePreview(preview), DateTime.UtcNow);
            return preview;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            try { ErrorLogger.LogException(ex, source: "LinkPreview.Fetch"); } catch { }
            return null;
        }
    }

    public static bool TryExtractFirstUrl(string? text, out string url)
    {
        url = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = UrlRegex.Match(text);
        if (!match.Success)
        {
            return false;
        }

        url = match.Value.TrimEnd('.', ',', ';');
        return true;
    }

    public static IReadOnlyList<string> ExtractUrls(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var matches = UrlRegex.Matches(text);
        if (matches.Count == 0)
        {
            return Array.Empty<string>();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var urls = new List<string>(matches.Count);
        foreach (Match match in matches)
        {
            if (!match.Success)
            {
                continue;
            }

            var candidate = match.Value.TrimEnd('.', ',', ';');
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (seen.Add(candidate))
            {
                urls.Add(candidate);
            }
        }

        return urls;
    }

    public static bool IsBlockedShortUrl(string? url, out string normalizedUrl, out string blockedHost)
    {
        normalizedUrl = string.Empty;
        blockedHost = string.Empty;

        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!TryNormalizeUrl(url, out var requestUri, out _))
        {
            return false;
        }

        normalizedUrl = requestUri.ToString();
        if (!IsBlockedShortUrlHost(requestUri.Host))
        {
            return false;
        }

        blockedHost = requestUri.Host;
        return true;
    }

    private static bool TryNormalizeUrl(string url, out Uri requestUri, out string cacheKey)
    {
        requestUri = null!;
        cacheKey = string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(uri.Fragment))
        {
            var builder = new UriBuilder(uri) { Fragment = string.Empty };
            uri = builder.Uri;
        }

        requestUri = uri;
        if (IsDisallowedPreviewTarget(requestUri))
        {
            cacheKey = string.Empty;
            return false;
        }
        cacheKey = uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped);
        return true;
    }

    private async Task<string?> DownloadHtmlAsync(Uri requestUri, CancellationToken cancellationToken)
    {
        using var response = await SendWithSafeRedirectsAsync(requestUri, cancellationToken).ConfigureAwait(false);
        if (response == null)
        {
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!mediaType.Contains("html", StringComparison.OrdinalIgnoreCase) && !mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var declaredEncoding = TryGetEncoding(response.Content.Headers.ContentType?.CharSet);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(32 * 1024);
        try
        {
            int read;
            int total = 0;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > MaxHtmlBytes)
                {
                    break;
                }
                ms.Write(buffer, 0, read);
            }

            if (ms.Length == 0)
            {
                return null;
            }

            var bytes = ms.ToArray();
            var encoding = DetectEncoding(bytes, declaredEncoding) ?? declaredEncoding ?? Encoding.UTF8;
            var html = encoding.GetString(bytes);
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            if (declaredEncoding == null)
            {
                var metaMatch = CharsetMetaRegex.Match(html);
                if (metaMatch.Success)
                {
                    var metaCharset = TryGetEncoding(metaMatch.Groups["charset"].Value);
                    if (metaCharset != null && !Equals(metaCharset, encoding))
                    {
                        html = metaCharset.GetString(bytes);
                    }
                }
            }

            return html;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static Encoding? TryGetEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
        {
            return null;
        }

        try
        {
            return Encoding.GetEncoding(charset);
        }
        catch
        {
            return null;
        }
    }

    private static Encoding? DetectEncoding(byte[] bytes, Encoding? declared)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8;
        }
        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode;
            }
            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode;
            }
        }
        return declared;
    }

    private LinkPreview? ParseMetadata(Uri requestUri, string html)
    {
        string? title = null;
        string? description = null;
        string? siteName = null;
        string? imageUrl = null;

        foreach (Match match in MetaTagRegex.Matches(html))
        {
            var name = match.Groups["name"].Value.Trim();
            var content = match.Groups["content"].Value.Trim();
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(content))
            {
                continue;
            }

            switch (name.ToLowerInvariant())
            {
                case "og:title":
                case "twitter:title":
                    title ??= content;
                    break;
                case "og:description":
                case "twitter:description":
                case "description":
                    description ??= content;
                    break;
                case "og:image":
                case "twitter:image":
                    imageUrl ??= content;
                    break;
                case "og:site_name":
                    siteName ??= content;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            var titleMatch = TitleRegex.Match(html);
            if (titleMatch.Success)
            {
                title = CollapseWhitespace(titleMatch.Groups["title"].Value);
            }
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            description = ExtractFallbackDescription(html);
        }

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(description) && string.IsNullOrWhiteSpace(imageUrl))
        {
            return null;
        }

        var preview = new LinkPreview
        {
            Url = requestUri.ToString(),
            DisplayUrl = BuildDisplayUrl(requestUri),
            Title = Truncate(title, 160),
            Description = Truncate(description, 400),
            SiteName = Truncate(siteName, 60)
        };

        if (string.IsNullOrWhiteSpace(preview.SiteName))
        {
            preview.SiteName = preview.DisplayUrl;
        }
        preview.ImageUrl = ResolveRelativeUrl(requestUri, imageUrl);

        return preview.IsEmpty ? null : preview;
    }

    private static string? ResolveRelativeUrl(Uri baseUri, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var absolute))
        {
            if (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps)
            {
                return absolute.ToString();
            }
            return null;
        }

        if (Uri.TryCreate(baseUri, candidate, out var resolved))
        {
            if (resolved.Scheme == Uri.UriSchemeHttp || resolved.Scheme == Uri.UriSchemeHttps)
            {
                return resolved.ToString();
            }
        }
        return null;
    }

    private async Task<ImageDownloadResult?> TryHydratePreviewImageAsync(string imageUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        using var response = await SendWithSafeRedirectsAsync(uri, cancellationToken).ConfigureAwait(false);
        if (response == null)
        {
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!contentType.StartsWith("image", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var length = response.Content.Headers.ContentLength;
        if (length.HasValue && length.Value > MaxImageBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(32 * 1024);
        try
        {
            int read;
            long total = 0;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > MaxImageBytes)
                {
                    return null;
                }
                ms.Write(buffer, 0, read);
            }

            if (ms.Length == 0)
            {
                return null;
            }

            return new ImageDownloadResult(ms.ToArray(), contentType);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string ExtractFallbackDescription(string html)
    {
        var sanitized = HtmlCommentRegex.Replace(html, " ");
        sanitized = ScriptTagRegex.Replace(sanitized, " ");
        sanitized = StyleTagRegex.Replace(sanitized, " ");
        sanitized = HtmlTagRegex.Replace(sanitized, " ");
        var collapsed = CollapseWhitespace(sanitized);
        return Truncate(collapsed, 400) ?? string.Empty;
    }

    private static string CollapseWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }
        return WhitespaceRegex.Replace(value, " ").Trim();
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }
        return value.Length <= max ? value : value[..max];
    }

}
