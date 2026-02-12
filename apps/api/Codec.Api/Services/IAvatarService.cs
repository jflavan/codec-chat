namespace Codec.Api.Services;

/// <summary>
/// Handles avatar image validation, storage, and URL resolution.
/// </summary>
public interface IAvatarService
{
    /// <summary>
    /// Validates an uploaded avatar file for format and size constraints.
    /// Returns <c>null</c> when valid, or an error message when invalid.
    /// </summary>
    string? Validate(IFormFile file);

    /// <summary>
    /// Saves a user avatar to disk and returns the relative storage path.
    /// </summary>
    Task<string> SaveUserAvatarAsync(Guid userId, IFormFile file);

    /// <summary>
    /// Saves a server-specific avatar to disk and returns the relative storage path.
    /// </summary>
    Task<string> SaveServerAvatarAsync(Guid userId, Guid serverId, IFormFile file);

    /// <summary>
    /// Deletes a previously uploaded avatar from disk.
    /// </summary>
    void DeleteAvatar(string relativePath);

    /// <summary>
    /// Resolves a relative avatar storage path to a publicly accessible URL.
    /// Returns <c>null</c> when <paramref name="relativePath"/> is <c>null</c>.
    /// </summary>
    string? ResolveUrl(string? relativePath);
}
