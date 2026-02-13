namespace Codec.Api.Services;

/// <summary>
/// Handles chat image validation, storage, and URL resolution.
/// </summary>
public interface IImageUploadService
{
    /// <summary>
    /// Validates an uploaded image file for format and size constraints.
    /// Returns <c>null</c> when valid, or an error message when invalid.
    /// </summary>
    string? Validate(IFormFile file);

    /// <summary>
    /// Saves an uploaded image to disk and returns the public URL.
    /// </summary>
    Task<string> SaveImageAsync(Guid userId, IFormFile file);
}
