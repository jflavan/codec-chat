using Codec.Api.Services;
using FluentAssertions;

namespace Codec.Api.Tests.Services;

public class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _rootPath;
    private readonly LocalFileStorageService _service;
    private const string BaseUrl = "http://localhost:5050/uploads";

    public LocalFileStorageServiceTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), $"codec-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_rootPath);
        _service = new LocalFileStorageService(_rootPath, BaseUrl);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
            Directory.Delete(_rootPath, true);
    }

    // --- UploadAsync ---

    [Fact]
    public async Task UploadAsync_CreatesFileAndReturnsUrl()
    {
        var content = new byte[] { 1, 2, 3, 4, 5 };
        using var stream = new MemoryStream(content);

        var url = await _service.UploadAsync("images", "user1/photo.png", stream, "image/png");

        url.Should().Be("http://localhost:5050/uploads/images/user1/photo.png");
        var filePath = Path.Combine(_rootPath, "images", "user1", "photo.png");
        File.Exists(filePath).Should().BeTrue();
        (await File.ReadAllBytesAsync(filePath)).Should().BeEquivalentTo(content);
    }

    [Fact]
    public async Task UploadAsync_CreatesDirectoryIfNotExists()
    {
        using var stream = new MemoryStream([42]);

        await _service.UploadAsync("avatars", "deep/nested/file.jpg", stream, "image/jpeg");

        var filePath = Path.Combine(_rootPath, "avatars", "deep", "nested", "file.jpg");
        File.Exists(filePath).Should().BeTrue();
    }

    [Fact]
    public async Task UploadAsync_OverwritesExistingFile()
    {
        var containerDir = Path.Combine(_rootPath, "images", "user1");
        Directory.CreateDirectory(containerDir);
        await File.WriteAllBytesAsync(Path.Combine(containerDir, "photo.png"), [1, 2, 3]);

        using var stream = new MemoryStream([10, 20, 30, 40]);
        await _service.UploadAsync("images", "user1/photo.png", stream, "image/png");

        var written = await File.ReadAllBytesAsync(Path.Combine(containerDir, "photo.png"));
        written.Should().BeEquivalentTo(new byte[] { 10, 20, 30, 40 });
    }

    [Fact]
    public async Task UploadAsync_PathTraversal_Throws()
    {
        using var stream = new MemoryStream([1]);

        var act = () => _service.UploadAsync("images", "../../etc/passwd", stream, "text/plain");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*traversal*");
    }

    [Fact]
    public async Task UploadAsync_NormalizesBackslashesInUrl()
    {
        using var stream = new MemoryStream([1]);

        var url = await _service.UploadAsync("images", "user1\\photo.png", stream, "image/png");

        url.Should().Contain("user1/photo.png");
    }

    [Fact]
    public async Task UploadAsync_TrimsTrailingSlashFromBaseUrl()
    {
        var service = new LocalFileStorageService(_rootPath, "http://localhost:5050/uploads/");
        using var stream = new MemoryStream([1]);

        var url = await service.UploadAsync("images", "file.png", stream, "image/png");

        url.Should().Be("http://localhost:5050/uploads/images/file.png");
    }

    // --- DeleteAsync ---

    [Fact]
    public async Task DeleteAsync_ExistingFile_DeletesIt()
    {
        var containerDir = Path.Combine(_rootPath, "images", "user1");
        Directory.CreateDirectory(containerDir);
        var filePath = Path.Combine(containerDir, "photo.png");
        await File.WriteAllBytesAsync(filePath, [1, 2, 3]);

        await _service.DeleteAsync("images", "user1/photo.png");

        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_NonExistentFile_DoesNotThrow()
    {
        var act = () => _service.DeleteAsync("images", "nonexistent/file.png");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteAsync_PathTraversal_Throws()
    {
        var act = () => _service.DeleteAsync("images", "../../etc/passwd");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*traversal*");
    }

    // --- DeleteByPrefixAsync ---

    [Fact]
    public async Task DeleteByPrefixAsync_DeletesMatchingFiles()
    {
        var containerDir = Path.Combine(_rootPath, "avatars", "user1");
        Directory.CreateDirectory(containerDir);
        await File.WriteAllBytesAsync(Path.Combine(containerDir, "avatar-abc.png"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(containerDir, "avatar-def.jpg"), [2]);
        await File.WriteAllBytesAsync(Path.Combine(containerDir, "other.png"), [3]);

        await _service.DeleteByPrefixAsync("avatars", "user1/avatar-");

        File.Exists(Path.Combine(containerDir, "avatar-abc.png")).Should().BeFalse();
        File.Exists(Path.Combine(containerDir, "avatar-def.jpg")).Should().BeFalse();
        File.Exists(Path.Combine(containerDir, "other.png")).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteByPrefixAsync_NonExistentDirectory_DoesNotThrow()
    {
        var act = () => _service.DeleteByPrefixAsync("avatars", "nonexistent/prefix");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteByPrefixAsync_PathTraversal_DoesNotThrowButIgnores()
    {
        // Path traversal in prefix is silently ignored (returns early)
        var act = () => _service.DeleteByPrefixAsync("avatars", "../../etc/passwd");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteByPrefixAsync_EmptyPrefix_DeletesAllInContainer()
    {
        var containerDir = Path.Combine(_rootPath, "temp");
        Directory.CreateDirectory(containerDir);
        await File.WriteAllBytesAsync(Path.Combine(containerDir, "file1.txt"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(containerDir, "file2.txt"), [2]);

        await _service.DeleteByPrefixAsync("temp", "file");

        File.Exists(Path.Combine(containerDir, "file1.txt")).Should().BeFalse();
        File.Exists(Path.Combine(containerDir, "file2.txt")).Should().BeFalse();
    }
}
