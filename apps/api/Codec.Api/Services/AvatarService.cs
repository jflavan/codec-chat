namespace Codec.Api.Services;

/// <summary>
/// Handles avatar image validation, storage, and URL resolution.
/// Delegates file I/O to <see cref="IFileStorageService"/> for storage-provider independence.
/// </summary>
public class AvatarService(IFileStorageService fileStorage) : IAvatarService
{
    /// <summary>Blob container name for avatar uploads.</summary>
    private const string ContainerName = "avatars";

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
        var prefix = $"{userId}/avatar";
        return await SaveFileAsync(prefix, file);
    }

    /// <inheritdoc />
    public async Task<string> SaveServerAvatarAsync(Guid userId, Guid serverId, IFormFile file)
    {
        var prefix = $"{userId}/server-{serverId}";
        return await SaveFileAsync(prefix, file);
    }

    /// <inheritdoc />
    public async Task DeleteUserAvatarAsync(Guid userId)
    {
        await fileStorage.DeleteByPrefixAsync(ContainerName, $"{userId}/avatar");
    }

    /// <inheritdoc />
    public async Task DeleteServerAvatarAsync(Guid userId, Guid serverId)
    {
        await fileStorage.DeleteByPrefixAsync(ContainerName, $"{userId}/server-{serverId}");
    }

    /// <inheritdoc />
    public async Task<string> SaveServerIconAsync(Guid serverId, IFormFile file)
    {
        var prefix = $"server-icons/{serverId}";
        return await SaveFileAsync(prefix, file);
    }

    /// <inheritdoc />
    public async Task DeleteServerIconAsync(Guid serverId)
    {
        await fileStorage.DeleteByPrefixAsync(ContainerName, $"server-icons/{serverId}");
    }

    /// <inheritdoc />
    public string? ResolveUrl(string? storedUrl) => string.IsNullOrEmpty(storedUrl) ? null : storedUrl;

    /// <summary>
    /// Uploads the file via <see cref="IFileStorageService"/> with a content-hash filename for cache busting.
    /// Cleans previous files with the same prefix before uploading.
    /// Returns the public URL of the uploaded file.
    /// </summary>
    private async Task<string> SaveFileAsync(string prefix, IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        using var stream = file.OpenReadStream();
        var hash = await FileHashService.ComputeHashAsync(stream);
        stream.Position = 0;
        var blobPath = $"{prefix}-{hash}{extension}";

        // Remove any previous avatar with the same prefix.
        await fileStorage.DeleteByPrefixAsync(ContainerName, prefix);

        return await fileStorage.UploadAsync(ContainerName, blobPath, stream, file.ContentType);
    }
}
