using System.Security.Cryptography;

namespace Codec.Api.Services;

/// <summary>
/// Handles general file upload validation and storage.
/// Supports documents, archives, and other common file types.
/// Delegates file I/O to <see cref="IFileStorageService"/> for storage-provider independence.
/// </summary>
public class FileUploadService(IFileStorageService fileStorage) : IFileUploadService
{
    /// <summary>Blob container name for file uploads.</summary>
    private const string ContainerName = "files";

    /// <summary>Maximum upload size in bytes (25 MB).</summary>
    private const long MaxFileSizeBytes = 25 * 1024 * 1024;

    /// <summary>Allowed MIME types for file uploads.</summary>
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Documents
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "text/plain",
        "text/csv",
        "text/markdown",
        "application/rtf",
        // Archives
        "application/zip",
        "application/x-tar",
        "application/gzip",
        "application/x-7z-compressed",
        "application/x-rar-compressed",
        // Code / data
        "application/json",
        "application/xml",
        "text/xml",
        "text/html",
        "text/css",
        "text/javascript",
        "application/javascript",
        // Audio
        "audio/mpeg",
        "audio/ogg",
        "audio/wav",
        "audio/webm",
        // Video
        "video/mp4",
        "video/webm",
        "video/ogg",
    };

    /// <summary>Allowed file extensions.</summary>
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Documents
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".txt", ".csv", ".md", ".rtf",
        // Archives
        ".zip", ".tar", ".gz", ".7z", ".rar",
        // Code / data
        ".json", ".xml", ".html", ".css", ".js", ".ts",
        // Audio
        ".mp3", ".ogg", ".wav", ".webm",
        // Video
        ".mp4",
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
            return $"Unsupported file type '{file.ContentType}'.";
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
        {
            return $"Unsupported file extension '{extension}'.";
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<string> SaveFileAsync(Guid userId, IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var hash = await ComputeHashAsync(file);
        var blobPath = $"{userId}/{hash}{extension}";

        using var stream = file.OpenReadStream();
        return await fileStorage.UploadAsync(ContainerName, blobPath, stream, file.ContentType);
    }

    private static async Task<string> ComputeHashAsync(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        var hashBytes = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
    }
}
