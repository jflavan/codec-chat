namespace Codec.Api.Services;

/// <summary>
/// Abstracts file storage operations for uploads (avatars, images).
/// Implementations may use local disk or Azure Blob Storage.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Uploads a file and returns the publicly accessible URL.
    /// </summary>
    /// <param name="containerName">Logical container (e.g. "avatars", "images").</param>
    /// <param name="blobPath">Path within the container (e.g. "{userId}/avatar-{hash}.png").</param>
    /// <param name="stream">File content stream.</param>
    /// <param name="contentType">MIME type of the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The public URL of the uploaded file.</returns>
    Task<string> UploadAsync(string containerName, string blobPath, Stream stream, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a file identified by container and path.
    /// Does nothing if the file does not exist.
    /// </summary>
    Task DeleteAsync(string containerName, string blobPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all files in a container whose names start with the given prefix.
    /// Used to clean previous avatar uploads before saving a new one.
    /// </summary>
    Task DeleteByPrefixAsync(string containerName, string prefix, CancellationToken cancellationToken = default);
}
