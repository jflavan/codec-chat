using System.Security.Cryptography;
using SkiaSharp;

namespace Codec.Api.Services;

public enum RehostOutcome { Success, Skipped, Failed }

public record RehostResult(RehostOutcome Outcome, string? Url = null)
{
    public static RehostResult Success(string url) => new(RehostOutcome.Success, url);
    public static RehostResult Skipped => new(RehostOutcome.Skipped);
    public static RehostResult Failed => new(RehostOutcome.Failed);
}

public class DiscordMediaRehostService
{
    private static readonly HashSet<string> SupportedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/gif"
    };

    private readonly HttpClient _http;
    private readonly IFileStorageService _storage;
    private readonly ILogger<DiscordMediaRehostService> _logger;

    public DiscordMediaRehostService(
        HttpClient http,
        IFileStorageService storage,
        ILogger<DiscordMediaRehostService> logger)
    {
        _http = http;
        _storage = storage;
        _logger = logger;
    }

    public virtual async Task<RehostResult> RehostImageAsync(
        string discordCdnUrl,
        string storageContainer,
        long maxFileSize,
        int? maxDimensionPx,
        CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(discordCdnUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download image from {Url}", discordCdnUrl);
            return RehostResult.Failed;
        }

        byte[] originalBytes;
        string contentType;
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Discord CDN returned {StatusCode} for {Url}", response.StatusCode, discordCdnUrl);
                return RehostResult.Failed;
            }

            contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!SupportedContentTypes.Contains(contentType))
            {
                _logger.LogDebug("Skipping unsupported content type {ContentType} for {Url}", contentType, discordCdnUrl);
                return RehostResult.Skipped;
            }

            // Reject responses that declare a size beyond the limit before downloading
            if (response.Content.Headers.ContentLength is > 0 and var declaredSize && declaredSize > maxFileSize)
            {
                _logger.LogDebug("Skipping oversized download ({Size} bytes) from {Url}", declaredSize, discordCdnUrl);
                return RehostResult.Skipped;
            }

            // Stream the body with a hard size cap to prevent OOM from huge responses
            using var bodyStream = await response.Content.ReadAsStreamAsync(ct);
            using var memoryStream = new MemoryStream();
            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;
            while ((bytesRead = await bodyStream.ReadAsync(buffer, ct)) > 0)
            {
                totalRead += bytesRead;
                if (totalRead > maxFileSize)
                {
                    _logger.LogDebug("Download exceeded max size ({Max} bytes) from {Url}", maxFileSize, discordCdnUrl);
                    return RehostResult.Skipped;
                }
                memoryStream.Write(buffer, 0, bytesRead);
            }
            originalBytes = memoryStream.ToArray();
        }

        byte[] finalBytes;
        string finalContentType;
        string extension;

        try
        {
            var result = ProcessImage(originalBytes, contentType, maxFileSize, maxDimensionPx);
            if (result is null)
            {
                _logger.LogDebug("Skipping oversized GIF from {Url}", discordCdnUrl);
                return RehostResult.Skipped;
            }
            (finalBytes, finalContentType, extension) = result.Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process image from {Url}", discordCdnUrl);
            return RehostResult.Failed;
        }

        var hashPrefix = Convert.ToHexString(SHA256.HashData(finalBytes))[..16].ToLowerInvariant();
        var blobPath = $"import/{hashPrefix}{extension}";

        using var uploadStream = new MemoryStream(finalBytes);
        var url = await _storage.UploadAsync(storageContainer, blobPath, uploadStream, finalContentType, ct);
        return RehostResult.Success(url);
    }

    public virtual async Task<RehostResult> RehostFileAsync(
        string discordCdnUrl,
        string storageContainer,
        long maxFileSize,
        CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(discordCdnUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download file from {Url}", discordCdnUrl);
            return RehostResult.Failed;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Discord CDN returned {StatusCode} for {Url}", response.StatusCode, discordCdnUrl);
                return RehostResult.Failed;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

            if (response.Content.Headers.ContentLength is > 0 and var declaredSize && declaredSize > maxFileSize)
            {
                _logger.LogDebug("Skipping oversized file download ({Size} bytes) from {Url}", declaredSize, discordCdnUrl);
                return RehostResult.Skipped;
            }

            using var bodyStream = await response.Content.ReadAsStreamAsync(ct);
            using var memoryStream = new MemoryStream();
            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;
            while ((bytesRead = await bodyStream.ReadAsync(buffer, ct)) > 0)
            {
                totalRead += bytesRead;
                if (totalRead > maxFileSize)
                {
                    _logger.LogDebug("File download exceeded max size ({Max} bytes) from {Url}", maxFileSize, discordCdnUrl);
                    return RehostResult.Skipped;
                }
                memoryStream.Write(buffer, 0, bytesRead);
            }

            var fileBytes = memoryStream.ToArray();
            var hashPrefix = Convert.ToHexString(SHA256.HashData(fileBytes))[..16].ToLowerInvariant();
            var extension = Path.GetExtension(new Uri(discordCdnUrl).AbsolutePath);
            if (string.IsNullOrEmpty(extension))
                extension = ".bin";
            var blobPath = $"import/{hashPrefix}{extension}";

            using var uploadStream = new MemoryStream(fileBytes);
            var url = await _storage.UploadAsync(storageContainer, blobPath, uploadStream, contentType, ct);
            return RehostResult.Success(url);
        }
    }

    private static (byte[] Bytes, string ContentType, string Extension)? ProcessImage(
        byte[] imageBytes, string contentType, long maxFileSize, int? maxDimensionPx)
    {
        var isGif = contentType.Equals("image/gif", StringComparison.OrdinalIgnoreCase);

        if (isGif)
        {
            if (imageBytes.Length > maxFileSize)
                return null; // Oversized GIF — skip
            return (imageBytes, "image/gif", ".gif");
        }

        using var bitmap = SKBitmap.Decode(imageBytes);
        if (bitmap is null)
            throw new InvalidOperationException("Failed to decode image");

        SKBitmap workingBitmap = bitmap;
        bool disposeBitmap = false;

        if (maxDimensionPx is not null)
        {
            var longest = Math.Max(bitmap.Width, bitmap.Height);
            if (longest > maxDimensionPx.Value)
            {
                var ratio = (float)maxDimensionPx.Value / longest;
                var newWidth = (int)(bitmap.Width * ratio);
                var newHeight = (int)(bitmap.Height * ratio);
                workingBitmap = bitmap.Resize(new SKImageInfo(newWidth, newHeight), SKSamplingOptions.Default);
                disposeBitmap = true;
            }
        }

        try
        {
            using var image = SKImage.FromBitmap(workingBitmap);

            var isPng = contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase);
            var (format, outputContentType, outputExtension) = contentType switch
            {
                "image/jpeg" => (SKEncodedImageFormat.Jpeg, "image/jpeg", ".jpg"),
                "image/webp" => (SKEncodedImageFormat.Webp, "image/webp", ".webp"),
                "image/png" => (SKEncodedImageFormat.Png, "image/png", ".png"),
                _ => (SKEncodedImageFormat.Jpeg, "image/jpeg", ".jpg")
            };

            var quality = isPng ? 100 : 85;
            using var firstPass = image.Encode(format, quality);
            if (firstPass is not null && firstPass.Size <= maxFileSize)
                return (firstPass.ToArray(), outputContentType, outputExtension);

            if (isPng)
            {
                format = SKEncodedImageFormat.Webp;
                outputContentType = "image/webp";
                outputExtension = ".webp";
            }

            for (quality = 75; quality >= 25; quality -= 10)
            {
                using var encoded = image.Encode(format, quality);
                if (encoded is not null && encoded.Size <= maxFileSize)
                    return (encoded.ToArray(), outputContentType, outputExtension);
            }

            using var final = image.Encode(format, 25);
            if (final is null)
                return null;
            return (final.ToArray(), outputContentType, outputExtension);
        }
        finally
        {
            if (disposeBitmap)
                workingBitmap.Dispose();
        }
    }
}
