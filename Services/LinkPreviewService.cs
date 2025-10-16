using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using ZTalk.Models;
using ZTalk.Utilities;

namespace ZTalk.Services;

public class LinkPreviewService : IDisposable
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
                AllowAutoRedirect = true,
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
                    return null;
                }

                preview = ParseMetadata(requestUri, html);
                if (preview == null || preview.IsEmpty)
                {
                    return null;
                }
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
        cacheKey = uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped);
        return true;
    }

    private async Task<string?> DownloadHtmlAsync(Uri requestUri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
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

        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
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
            if (!client.DefaultRequestHeaders.UserAgent.TryParseAdd("ZTalk-LinkPreview/1.0"))
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; ZTalk-LinkPreview/1.0)");
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
