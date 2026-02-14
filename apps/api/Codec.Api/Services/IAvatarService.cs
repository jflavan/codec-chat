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
    /// Saves a user avatar and returns its public URL.
    /// </summary>
    Task<string> SaveUserAvatarAsync(Guid userId, IFormFile file);

    /// <summary>
    /// Saves a server-specific avatar and returns its public URL.
    /// </summary>
    Task<string> SaveServerAvatarAsync(Guid userId, Guid serverId, IFormFile file);

    /// <summary>
    /// Deletes all avatar files for the given user.
    /// </summary>
    Task DeleteUserAvatarAsync(Guid userId);

    /// <summary>
    /// Deletes the server-specific avatar for the given user and server.
    /// </summary>
    Task DeleteServerAvatarAsync(Guid userId, Guid serverId);

    /// <summary>
    /// Returns the avatar URL stored in <paramref name="storedUrl"/>, or <c>null</c> when empty.
    /// Exists for backward compatibility — the stored value is already a full URL.
    /// </summary>
    string? ResolveUrl(string? storedUrl);
}
