namespace Codec.Api.Services;

public class CustomEmojiService(IFileStorageService fileStorage) : ICustomEmojiService
{
    private const string ContainerName = "emojis";
    private const long MaxFileSizeBytes = 512 * 1024; // 512 KB

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/gif"
    };

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif"
    };

    public string? Validate(IFormFile file)
    {
        if (file.Length == 0) return "File is empty.";
        if (file.Length > MaxFileSizeBytes) return "File size exceeds 512 KB limit.";
        if (!AllowedContentTypes.Contains(file.ContentType))
            return $"Unsupported file type '{file.ContentType}'. Allowed: PNG, JPEG, WebP, GIF.";
        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            return $"Unsupported file extension '{ext}'.";
        return null;
    }

    public async Task<string> SaveEmojiAsync(Guid serverId, string name, IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        using var stream = file.OpenReadStream();
        var hash = await FileHashService.ComputeHashAsync(stream);
        stream.Position = 0;
        var blobPath = $"server-{serverId}/{name}-{hash}{extension}";
        return await fileStorage.UploadAsync(ContainerName, blobPath, stream, file.ContentType);
    }

    public async Task DeleteEmojiAsync(string imageUrl)
    {
        var containerSegment = $"/{ContainerName}/";
        var idx = imageUrl.IndexOf(containerSegment, StringComparison.Ordinal);
        if (idx >= 0)
        {
            var blobPath = imageUrl[(idx + containerSegment.Length)..];
            await fileStorage.DeleteAsync(ContainerName, blobPath);
        }
    }
}
