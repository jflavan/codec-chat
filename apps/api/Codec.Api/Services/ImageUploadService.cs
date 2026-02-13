using System.Security.Cryptography;

namespace Codec.Api.Services;

/// <summary>
/// Handles chat image validation, disk storage, and URL resolution.
/// Images are stored under a configurable root directory (default: <c>uploads/images</c>).
/// </summary>
public class ImageUploadService : IImageUploadService
{
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

    private readonly string _rootPath;
    private readonly string _baseUrl;

    /// <param name="rootPath">Absolute path to the image storage directory.</param>
    /// <param name="baseUrl">Public base URL for serving image files (e.g. <c>http://localhost:5050/uploads/images</c>).</param>
    public ImageUploadService(string rootPath, string baseUrl)
    {
        _rootPath = rootPath;
        _baseUrl = baseUrl.TrimEnd('/');
    }

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
        var directory = Path.Combine(_rootPath, userId.ToString());
        Directory.CreateDirectory(directory);

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var hash = await ComputeHashAsync(file);
        var fileName = $"{hash}{extension}";
        var fullPath = Path.Combine(directory, fileName);

        await using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
        await file.CopyToAsync(stream);

        var relativePath = $"{userId}/{fileName}";
        return $"{_baseUrl}/{relativePath}";
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
