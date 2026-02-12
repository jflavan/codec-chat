using System.Security.Cryptography;

namespace Codec.Api.Services;

/// <summary>
/// Handles avatar image validation, disk storage, and URL resolution.
/// Avatars are stored under a configurable root directory (default: <c>uploads/avatars</c>).
/// </summary>
public class AvatarService : IAvatarService
{
    /// <summary>Maximum upload size in bytes (10 MB).</summary>
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;

    /// <summary>Allowed MIME types for avatar uploads.</summary>
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

    /// <param name="rootPath">Absolute path to the avatar storage directory.</param>
    /// <param name="baseUrl">Public base URL for serving avatar files (e.g. <c>/uploads/avatars</c>).</param>
    public AvatarService(string rootPath, string baseUrl)
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
    public async Task<string> SaveUserAvatarAsync(Guid userId, IFormFile file)
    {
        var directory = Path.Combine(_rootPath, userId.ToString());
        return await SaveFileAsync(directory, "avatar", file);
    }

    /// <inheritdoc />
    public async Task<string> SaveServerAvatarAsync(Guid userId, Guid serverId, IFormFile file)
    {
        var directory = Path.Combine(_rootPath, userId.ToString());
        var prefix = $"server-{serverId}";
        return await SaveFileAsync(directory, prefix, file);
    }

    /// <inheritdoc />
    public void DeleteAvatar(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, relativePath));
        if (!fullPath.StartsWith(Path.GetFullPath(_rootPath) + Path.DirectorySeparatorChar))
        {
            return;
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    /// <inheritdoc />
    public string? ResolveUrl(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return null;
        }

        // Ensure the relative path stays within the storage root.
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, relativePath));
        if (!fullPath.StartsWith(Path.GetFullPath(_rootPath) + Path.DirectorySeparatorChar))
        {
            return null;
        }

        return $"{_baseUrl}/{relativePath.Replace('\\', '/')}";
    }

    /// <summary>
    /// Writes the uploaded file to disk with a content-hash filename for cache busting.
    /// Returns the relative path from the avatar storage root.
    /// </summary>
    private static async Task<string> SaveFileAsync(string directory, string prefix, IFormFile file)
    {
        Directory.CreateDirectory(directory);

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var hash = await ComputeHashAsync(file);
        var fileName = $"{prefix}-{hash}{extension}";
        var fullPath = Path.Combine(directory, fileName);

        // Remove any previous avatar with the same prefix in this directory.
        CleanPreviousFiles(directory, prefix);

        await using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
        await file.CopyToAsync(stream);

        // Return path relative to the storage root.
        var directoryName = Path.GetFileName(directory);
        return Path.Combine(directoryName, fileName);
    }

    /// <summary>
    /// Computes a short SHA-256 hash of the file contents for use as a cache-busting token.
    /// </summary>
    private static async Task<string> ComputeHashAsync(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        var hashBytes = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
    }

    /// <summary>
    /// Removes existing files matching the given prefix so only the latest upload remains.
    /// </summary>
    private static void CleanPreviousFiles(string directory, string prefix)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var existing in Directory.EnumerateFiles(directory, $"{prefix}-*"))
        {
            File.Delete(existing);
        }
    }
}
