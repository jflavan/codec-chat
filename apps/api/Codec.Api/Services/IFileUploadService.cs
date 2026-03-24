namespace Codec.Api.Services;

/// <summary>
/// Handles general file upload validation, storage, and URL resolution.
/// Supports documents, archives, and other non-image file types.
/// </summary>
public interface IFileUploadService
{
    /// <summary>
    /// Validates an uploaded file for format and size constraints.
    /// Returns <c>null</c> when valid, or an error message when invalid.
    /// </summary>
    string? Validate(IFormFile file);

    /// <summary>
    /// Saves an uploaded file and returns the public URL.
    /// </summary>
    Task<string> SaveFileAsync(Guid userId, IFormFile file);
}
