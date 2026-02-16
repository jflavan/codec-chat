using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Codec.Api.Models;

namespace Codec.Api.Services;

/// <summary>
/// Result from fetching metadata for a single URL.
/// </summary>
public sealed record LinkPreviewResult(
    string Url,
    string? Title,
    string? Description,
    string? ImageUrl,
    string? SiteName,
    string? CanonicalUrl);

/// <summary>
/// Extracts URLs from message bodies, validates them against SSRF attacks,
/// fetches HTML metadata (Open Graph + fallback meta tags), and produces
/// <see cref="LinkPreviewResult"/> objects for storage.
/// </summary>
public interface ILinkPreviewService
{
    /// <summary>
    /// Extract HTTP(S) URLs from a message body (max <paramref name="maxUrls"/>).
    /// </summary>
    IReadOnlyList<string> ExtractUrls(string body, int maxUrls = 5);

    /// <summary>
    /// Fetch Open Graph / meta-tag metadata for a single URL.
    /// Returns <c>null</c> if the URL is blocked, unreachable, or contains no metadata.
    /// </summary>
    Task<LinkPreviewResult?> FetchMetadataAsync(string url, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class LinkPreviewService : ILinkPreviewService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<LinkPreviewService> _logger;

    private const int MaxResponseBytes = 512 * 1024; // 512 KB
    private const int TimeoutSeconds = 5;

    private static readonly Regex UrlRegex = new(
        @"https?://[^\s<>""')\]},;]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public LinkPreviewService(HttpClient httpClient, ILogger<LinkPreviewService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Strips control characters from a URL to prevent log injection (CRLF, etc.).
    /// </summary>
    private static string SanitizeForLog(string value)
    {
        return string.Create(value.Length, value, static (span, src) =>
        {
            for (var i = 0; i < src.Length; i++)
            {
                span[i] = char.IsControl(src[i]) ? '_' : src[i];
            }
        });
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ExtractUrls(string body, int maxUrls = 5)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        return UrlRegex.Matches(body)
            .Select(m => m.Value.TrimEnd('.', ',', ';', ':', '!', '?'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxUrls)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<LinkPreviewResult?> FetchMetadataAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!IsAllowedUrl(url))
        {
            _logger.LogDebug("Blocked URL for link preview (SSRF check): {Url}", SanitizeForLog(url));
            return null;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "CodecBot/1.0 (+https://codec.chat)");
            request.Headers.Add("Accept", "text/html");

            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType is null || !contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var html = await ReadLimitedAsync(response.Content, MaxResponseBytes, cts.Token);
            return ParseMetadata(html, url);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Timeout fetching link preview for {Url}", SanitizeForLog(url));
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "HTTP error fetching link preview for {Url}", SanitizeForLog(url));
            return null;
        }
    }

    /// <summary>
    /// Validates a URL to prevent SSRF attacks by blocking private IPs,
    /// loopback addresses, and well-known internal hostnames.
    /// </summary>
    internal static bool IsAllowedUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();

        // Block well-known internal hostnames.
        if (host is "localhost" or "metadata.google.internal"
            || host.EndsWith(".local", StringComparison.Ordinal)
            || host.EndsWith(".internal", StringComparison.Ordinal))
        {
            return false;
        }

        // Block IP-based hosts in private ranges.
        if (IPAddress.TryParse(uri.Host, out var ip))
        {
            if (IsPrivateOrReserved(ip))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if an IP address is in a private, loopback, or link-local range.
    /// </summary>
    private static bool IsPrivateOrReserved(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            return bytes[0] switch
            {
                10 => true,                                           // 10.0.0.0/8
                172 => bytes[1] >= 16 && bytes[1] <= 31,             // 172.16.0.0/12
                192 => bytes[1] == 168,                               // 192.168.0.0/16
                169 => bytes[1] == 254,                               // 169.254.0.0/16 link-local
                127 => true,                                          // 127.0.0.0/8
                0 => true,                                            // 0.0.0.0/8
                _ => false
            };
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // ::1 loopback, fc00::/7 unique local, fe80::/10 link-local
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
            {
                return true;
            }

            var bytes = ip.GetAddressBytes();
            // fc00::/7 — first byte is 0xFC or 0xFD
            if (bytes[0] is 0xFC or 0xFD)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads a limited number of bytes from the HTTP response to prevent memory exhaustion.
    /// </summary>
    private static async Task<string> ReadLimitedAsync(
        HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[maxBytes];
        var totalRead = 0;

        while (totalRead < maxBytes)
        {
            var bytesRead = await stream.ReadAsync(
                buffer.AsMemory(totalRead, maxBytes - totalRead), cancellationToken);

            if (bytesRead == 0)
            {
                break;
            }

            totalRead += bytesRead;
        }

        return System.Text.Encoding.UTF8.GetString(buffer, 0, totalRead);
    }

    // Regex patterns for extracting metadata from the HTML <head> section.
    private static readonly Regex HeadRegex = new(
        @"<head[\s>].*?</head>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex OgTitleRegex = new(
        @"<meta\s[^>]*property\s*=\s*[""']og:title[""'][^>]*content\s*=\s*[""']([^""']*)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex OgTitleAltRegex = new(
        @"<meta\s[^>]*content\s*=\s*[""']([^""']*)[""'][^>]*property\s*=\s*[""']og:title[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex OgDescRegex = new(
        @"<meta\s[^>]*property\s*=\s*[""']og:description[""'][^>]*content\s*=\s*[""']([^""']*)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex OgDescAltRegex = new(
        @"<meta\s[^>]*content\s*=\s*[""']([^""']*)[""'][^>]*property\s*=\s*[""']og:description[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex OgImageRegex = new(
        @"<meta\s[^>]*property\s*=\s*[""']og:image[""'][^>]*content\s*=\s*[""']([^""']*)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex OgImageAltRegex = new(
        @"<meta\s[^>]*content\s*=\s*[""']([^""']*)[""'][^>]*property\s*=\s*[""']og:image[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex OgSiteNameRegex = new(
        @"<meta\s[^>]*property\s*=\s*[""']og:site_name[""'][^>]*content\s*=\s*[""']([^""']*)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex OgSiteNameAltRegex = new(
        @"<meta\s[^>]*content\s*=\s*[""']([^""']*)[""'][^>]*property\s*=\s*[""']og:site_name[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex OgUrlRegex = new(
        @"<meta\s[^>]*property\s*=\s*[""']og:url[""'][^>]*content\s*=\s*[""']([^""']*)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex OgUrlAltRegex = new(
        @"<meta\s[^>]*content\s*=\s*[""']([^""']*)[""'][^>]*property\s*=\s*[""']og:url[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TitleTagRegex = new(
        @"<title[^>]*>(.*?)</title>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex MetaDescRegex = new(
        @"<meta\s[^>]*name\s*=\s*[""']description[""'][^>]*content\s*=\s*[""']([^""']*)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MetaDescAltRegex = new(
        @"<meta\s[^>]*content\s*=\s*[""']([^""']*)[""'][^>]*name\s*=\s*[""']description[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parses OG tags and HTML meta fallbacks from the HTML head section.
    /// </summary>
    internal static LinkPreviewResult? ParseMetadata(string html, string originalUrl)
    {
        // Restrict parsing to the <head> section for performance and safety.
        var headMatch = HeadRegex.Match(html);
        var head = headMatch.Success ? headMatch.Value : html;

        // OG tags (primary)
        var ogTitle = MatchFirst(OgTitleRegex, OgTitleAltRegex, head);
        var ogDesc = MatchFirst(OgDescRegex, OgDescAltRegex, head);
        var ogImage = MatchFirst(OgImageRegex, OgImageAltRegex, head);
        var ogSiteName = MatchFirst(OgSiteNameRegex, OgSiteNameAltRegex, head);
        var ogUrl = MatchFirst(OgUrlRegex, OgUrlAltRegex, head);

        // HTML fallbacks
        var fallbackTitle = TitleTagRegex.Match(head) is { Success: true } titleMatch
            ? DecodeHtmlEntities(titleMatch.Groups[1].Value.Trim())
            : null;

        var fallbackDesc = MatchFirst(MetaDescRegex, MetaDescAltRegex, head);

        var title = ogTitle ?? fallbackTitle;
        var description = ogDesc ?? fallbackDesc;

        // If there is no title at all, we cannot render a meaningful preview card.
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        // Validate og:image URL — only allow https:// images.
        string? imageUrl = null;
        if (!string.IsNullOrWhiteSpace(ogImage))
        {
            if (Uri.TryCreate(ogImage, UriKind.Absolute, out var imgUri) &&
                imgUri.Scheme == "https")
            {
                imageUrl = ogImage;
            }
        }

        return new LinkPreviewResult(
            Url: originalUrl,
            Title: Truncate(title, 512),
            Description: Truncate(description, 1024),
            ImageUrl: imageUrl,
            SiteName: Truncate(ogSiteName, 256),
            CanonicalUrl: ogUrl);
    }

    /// <summary>
    /// Tries to match two regex patterns (to handle attribute order variations) and returns
    /// the first successful match's group 1 value, decoded from HTML entities.
    /// </summary>
    private static string? MatchFirst(Regex primary, Regex alt, string input)
    {
        var match = primary.Match(input);
        if (!match.Success)
        {
            match = alt.Match(input);
        }

        return match.Success ? DecodeHtmlEntities(match.Groups[1].Value.Trim()) : null;
    }

    private static string DecodeHtmlEntities(string value)
    {
        return WebUtility.HtmlDecode(value);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (value is null)
        {
            return null;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
