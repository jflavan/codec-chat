using System.Security.Cryptography;

namespace Codec.Api.Services;

/// <summary>
/// Handles chat image validation and storage.
/// Delegates file I/O to <see cref="IFileStorageService"/> for storage-provider independence.
/// </summary>
public class ImageUploadService(IFileStorageService fileStorage) : IImageUploadService
{
    /// <summary>Blob container name for chat image uploads.</summary>
    private const string ContainerName = "images";

    /// <summary>Maximum upload size in bytes (10 MB).</summary>
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;

    /// <summary>Allowed MIME types for image uploads.</summary>
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif"
    };

    /// <summary>Allowed file extensions.</summary>
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif"
    };

    /// <inheritdoc />
    public string? Validate(IFormFile file)
    {
        if (file.Length == 0)
        {
            return "File is empty.";
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return $"File size exceeds the {MaxFileSizeBytes / (1024 * 1024)} MB limit.";
        }

        if (!AllowedContentTypes.Contains(file.ContentType))
        {
            return $"Unsupported file type '{file.ContentType}'. Allowed types: JPG, JPEG, PNG, WebP, GIF.";
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
        {
            return $"Unsupported file extension '{extension}'. Allowed extensions: .jpg, .jpeg, .png, .webp, .gif.";
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<string> SaveImageAsync(Guid userId, IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var hash = await ComputeHashAsync(file);
        var blobPath = $"{userId}/{hash}{extension}";

        using var stream = file.OpenReadStream();
        return await fileStorage.UploadAsync(ContainerName, blobPath, stream, file.ContentType);
    }

    /// <summary>
    /// Computes a short SHA-256 hash of the file contents for use as a unique filename.
    /// </summary>
    private static async Task<string> ComputeHashAsync(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        var hashBytes = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
    }
}
