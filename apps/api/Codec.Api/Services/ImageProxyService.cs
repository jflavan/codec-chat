using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;

namespace Codec.Api.Services;

public sealed record ProxiedImage(byte[] Data, string ContentType);

public interface IImageProxyService
{
    Task<ProxiedImage?> FetchImageAsync(string url, CancellationToken cancellationToken = default);
}

public sealed class ImageProxyService : IImageProxyService
{
    private readonly HttpClient _httpClient;
    private readonly IDistributedCache? _cache;
    private readonly ILogger<ImageProxyService> _logger;

    private const int MaxImageBytes = 10 * 1024 * 1024; // 10 MB
    private const int TimeoutSeconds = 10;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);
    private const string CacheKeyPrefix = "imgproxy:";

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/gif",
        "image/webp",
        // SVG excluded: can contain embedded JavaScript (stored XSS risk)
        "image/avif",
    };

    public ImageProxyService(HttpClient httpClient, ILogger<ImageProxyService> logger, IDistributedCache? cache = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _cache = cache;
    }

    public async Task<ProxiedImage?> FetchImageAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!IsAllowedUrl(url))
        {
            _logger.LogDebug("Blocked image proxy URL (SSRF check): {Url}", SanitizeForLog(url));
            return null;
        }

        // Check cache first.
        var cacheKey = CacheKeyPrefix + ComputeUrlHash(url);
        if (_cache is not null)
        {
            try
            {
                var cached = await _cache.GetAsync(cacheKey, cancellationToken);
                if (cached is not null)
                {
                    return DeserializeCacheEntry(cached);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Cache read failed for image proxy, falling through to fetch");
            }
        }

        var result = await FetchFromSourceAsync(url, cancellationToken);

        // Cache the result.
        if (result is not null && _cache is not null)
        {
            try
            {
                var entry = SerializeCacheEntry(result);
                var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl };
                await _cache.SetAsync(cacheKey, entry, options, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Cache write failed for image proxy");
            }
        }

        return result;
    }

    private async Task<ProxiedImage?> FetchFromSourceAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "CodecBot/1.0 (+https://codec.chat)");
            request.Headers.Add("Accept", "image/*");

            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Image proxy: upstream returned {StatusCode} for {Url}",
                    (int)response.StatusCode, SanitizeForLog(url));
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType is null || !AllowedContentTypes.Contains(contentType))
            {
                _logger.LogDebug("Image proxy: disallowed content type {ContentType} for {Url}",
                    contentType ?? "(null)", SanitizeForLog(url));
                return null;
            }

            // Check Content-Length header before downloading.
            if (response.Content.Headers.ContentLength > MaxImageBytes)
            {
                _logger.LogDebug("Image proxy: Content-Length {Length} exceeds limit for {Url}",
                    response.Content.Headers.ContentLength, SanitizeForLog(url));
                return null;
            }

            var data = await ReadLimitedAsync(response.Content, MaxImageBytes, cts.Token);
            if (data is null)
            {
                _logger.LogDebug("Image proxy: response body exceeded size limit for {Url}", SanitizeForLog(url));
                return null;
            }

            return new ProxiedImage(data, contentType);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Image proxy: timeout fetching {Url}", SanitizeForLog(url));
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "Image proxy: HTTP error fetching {Url}", SanitizeForLog(url));
            return null;
        }
    }

    private static async Task<byte[]?> ReadLimitedAsync(
        HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        var totalRead = 0;

        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
                break;

            totalRead += bytesRead;
            if (totalRead > maxBytes)
                return null;

            ms.Write(buffer, 0, bytesRead);
        }

        return ms.ToArray();
    }

    internal static bool IsAllowedUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme is not ("http" or "https"))
            return false;

        var host = uri.Host.ToLowerInvariant();

        if (host is "localhost" or "metadata.google.internal"
            || host.EndsWith(".local", StringComparison.Ordinal)
            || host.EndsWith(".internal", StringComparison.Ordinal))
        {
            return false;
        }

        if (IPAddress.TryParse(uri.Host, out var ip))
        {
            if (SsrfValidator.IsPrivateOrReserved(ip))
                return false;
        }

        return true;
    }

    private static string ComputeUrlHash(string url)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return Convert.ToHexStringLower(bytes);
    }

    private static string SanitizeForLog(string value)
    {
        return string.Create(value.Length, value, static (span, src) =>
        {
            for (var i = 0; i < src.Length; i++)
                span[i] = char.IsControl(src[i]) ? '_' : src[i];
        });
    }

    /// <summary>
    /// Serializes a ProxiedImage into a cache-friendly byte array.
    /// Format: [4 bytes content-type length][content-type UTF-8 bytes][image data]
    /// </summary>
    private static byte[] SerializeCacheEntry(ProxiedImage image)
    {
        var ctBytes = Encoding.UTF8.GetBytes(image.ContentType);
        var result = new byte[4 + ctBytes.Length + image.Data.Length];
        BitConverter.TryWriteBytes(result.AsSpan(0, 4), ctBytes.Length);
        ctBytes.CopyTo(result, 4);
        image.Data.CopyTo(result, 4 + ctBytes.Length);
        return result;
    }

    private static ProxiedImage? DeserializeCacheEntry(byte[] cached)
    {
        if (cached.Length < 4)
            return null;

        var ctLength = BitConverter.ToInt32(cached, 0);
        if (ctLength < 0 || 4 + ctLength > cached.Length)
            return null;

        var contentType = Encoding.UTF8.GetString(cached, 4, ctLength);
        var data = new byte[cached.Length - 4 - ctLength];
        Array.Copy(cached, 4 + ctLength, data, 0, data.Length);
        return new ProxiedImage(data, contentType);
    }
}
