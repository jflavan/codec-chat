using System.Security.Cryptography;
using SkiaSharp;

namespace Codec.Api.Services;

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

    public async Task<string?> RehostImageAsync(
        string discordCdnUrl,
        string storageContainer,
        long maxFileSize,
        int? maxDimensionPx,
        CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(discordCdnUrl, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download image from {Url}", discordCdnUrl);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Discord CDN returned {StatusCode} for {Url}", response.StatusCode, discordCdnUrl);
            return null;
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (!SupportedContentTypes.Contains(contentType))
        {
            _logger.LogDebug("Skipping unsupported content type {ContentType} for {Url}", contentType, discordCdnUrl);
            return null;
        }

        var originalBytes = await response.Content.ReadAsByteArrayAsync(ct);

        byte[] finalBytes;
        string finalContentType;
        string extension;

        try
        {
            (finalBytes, finalContentType, extension) = ProcessImage(
                originalBytes, contentType, maxFileSize, maxDimensionPx);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process image from {Url}", discordCdnUrl);
            return null;
        }

        var hashPrefix = Convert.ToHexString(SHA256.HashData(finalBytes))[..16].ToLowerInvariant();
        var blobPath = $"import/{hashPrefix}{extension}";

        using var uploadStream = new MemoryStream(finalBytes);
        var url = await _storage.UploadAsync(storageContainer, blobPath, uploadStream, finalContentType, ct);
        return url;
    }

    private static (byte[] Bytes, string ContentType, string Extension) ProcessImage(
        byte[] imageBytes, string contentType, long maxFileSize, int? maxDimensionPx)
    {
        var isGif = contentType.Equals("image/gif", StringComparison.OrdinalIgnoreCase);

        if (isGif)
        {
            if (imageBytes.Length > maxFileSize)
                throw new InvalidOperationException("GIF exceeds max file size");
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
            return (final!.ToArray(), outputContentType, outputExtension);
        }
        finally
        {
            if (disposeBitmap)
                workingBitmap.Dispose();
        }
    }
}
