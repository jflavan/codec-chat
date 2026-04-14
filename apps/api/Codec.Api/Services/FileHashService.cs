using System.Security.Cryptography;

namespace Codec.Api.Services;

/// <summary>
/// Computes content-hash filenames for uploaded files.
/// Shared by AvatarService, ImageUploadService, CustomEmojiService, and FileUploadService.
/// </summary>
public static class FileHashService
{
    /// <summary>
    /// Computes a 16-character lowercase hex SHA-256 hash prefix of the stream contents.
    /// </summary>
    public static async Task<string> ComputeHashAsync(Stream stream)
    {
        var hashBytes = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
    }

    /// <summary>
    /// Computes a 16-character lowercase hex SHA-256 hash prefix of the file contents.
    /// </summary>
    public static async Task<string> ComputeHashAsync(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        return await ComputeHashAsync(stream);
    }
}
