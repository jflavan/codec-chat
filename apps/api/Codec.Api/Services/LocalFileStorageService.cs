namespace Codec.Api.Services;

/// <summary>
/// Stores files on the local filesystem and serves them via the API's static file middleware.
/// Each container maps to a subdirectory under the configured root path (e.g. <c>uploads/avatars</c>).
/// </summary>
public class LocalFileStorageService : IFileStorageService
{
    private readonly string _rootPath;
    private readonly string _baseUrl;

    /// <param name="rootPath">Absolute filesystem path to the uploads root directory.</param>
    /// <param name="baseUrl">Public base URL prefix (e.g. <c>http://localhost:5050/uploads</c>).</param>
    public LocalFileStorageService(string rootPath, string baseUrl)
    {
        _rootPath = rootPath;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    /// <inheritdoc />
    public async Task<string> UploadAsync(string containerName, string blobPath, Stream stream, string contentType, CancellationToken cancellationToken = default)
    {
        var fullPath = GetSafePath(containerName, blobPath);
        var directory = Path.GetFullPath(Path.GetDirectoryName(fullPath)!);
        var containerPath = Path.GetFullPath(Path.Combine(_rootPath, containerName));

        if (!directory.StartsWith(containerPath + Path.DirectorySeparatorChar) && directory != containerPath)
        {
            throw new InvalidOperationException("Path traversal detected.");
        }

        Directory.CreateDirectory(directory);

        await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
        await stream.CopyToAsync(fileStream, cancellationToken);

        return $"{_baseUrl}/{containerName}/{blobPath.Replace('\\', '/')}";
    }

    /// <inheritdoc />
    public Task DeleteAsync(string containerName, string blobPath, CancellationToken cancellationToken = default)
    {
        var fullPath = GetSafePath(containerName, blobPath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteByPrefixAsync(string containerName, string prefix, CancellationToken cancellationToken = default)
    {
        var containerPath = Path.GetFullPath(Path.Combine(_rootPath, containerName));
        var prefixDir = Path.GetDirectoryName(prefix);
        var prefixFile = Path.GetFileName(prefix);

        var searchDir = string.IsNullOrEmpty(prefixDir)
            ? containerPath
            : Path.GetFullPath(Path.Combine(containerPath, prefixDir));

        // Path traversal guard.
        if (!searchDir.StartsWith(containerPath + Path.DirectorySeparatorChar) && searchDir != containerPath)
        {
            return Task.CompletedTask;
        }

        if (!Directory.Exists(searchDir))
        {
            return Task.CompletedTask;
        }

        foreach (var file in Directory.EnumerateFiles(searchDir, $"{prefixFile}*"))
        {
            File.Delete(file);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves and validates the full filesystem path, preventing directory traversal.
    /// </summary>
    private string GetSafePath(string containerName, string blobPath)
    {
        var containerPath = Path.GetFullPath(Path.Combine(_rootPath, containerName));
        var fullPath = Path.GetFullPath(Path.Combine(containerPath, blobPath));

        if (!fullPath.StartsWith(containerPath + Path.DirectorySeparatorChar))
        {
            throw new InvalidOperationException("Path traversal detected.");
        }

        return fullPath;
    }
}
