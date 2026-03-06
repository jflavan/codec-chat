using System.Security.Cryptography;

namespace Codec.Api.Services;

public class CustomEmojiService(IFileStorageService fileStorage) : ICustomEmojiService
{
    private const string ContainerName = "emojis";
    private const long MaxFileSizeBytes = 256 * 1024; // 256 KB

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/gif", "image/apng"
    };

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".apng"
    };

    public string? Validate(IFormFile file)
    {
        if (file.Length == 0) return "File is empty.";
        if (file.Length > MaxFileSizeBytes) return "File size exceeds 256 KB limit.";
        if (!AllowedContentTypes.Contains(file.ContentType))
            return $"Unsupported file type '{file.ContentType}'. Allowed: PNG, JPEG, WebP, GIF, APNG.";
        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            return $"Unsupported file extension '{ext}'.";
        return null;
    }

    public async Task<string> SaveEmojiAsync(Guid serverId, string name, IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var hash = await ComputeHashAsync(file);
        var blobPath = $"server-{serverId}/{name}-{hash}{extension}";

        using var stream = file.OpenReadStream();
        return await fileStorage.UploadAsync(ContainerName, blobPath, stream, file.ContentType);
    }

    public async Task DeleteEmojiAsync(string imageUrl)
    {
        await fileStorage.DeleteAsync(ContainerName, imageUrl);
    }

    private static async Task<string> ComputeHashAsync(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        var hashBytes = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
    }
}
