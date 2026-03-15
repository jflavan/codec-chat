using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;

namespace Codec.Api.Tests.Services;

public class AvatarServiceTests
{
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly AvatarService _svc;

    public AvatarServiceTests()
    {
        _svc = new AvatarService(_fileStorage.Object);
    }

    // --- Validate ---

    [Fact]
    public void Validate_EmptyFile_ReturnsError()
    {
        var file = CreateFormFile(0, "image/png", "avatar.png");
        _svc.Validate(file).Should().Be("File is empty.");
    }

    [Fact]
    public void Validate_OversizedFile_ReturnsError()
    {
        var file = CreateFormFile(11 * 1024 * 1024, "image/png", "avatar.png");
        _svc.Validate(file).Should().Contain("MB limit");
    }

    [Fact]
    public void Validate_InvalidContentType_ReturnsError()
    {
        var file = CreateFormFile(1024, "application/pdf", "avatar.pdf");
        _svc.Validate(file).Should().Contain("Unsupported file type");
    }

    [Fact]
    public void Validate_InvalidExtension_ReturnsError()
    {
        var file = CreateFormFile(1024, "image/png", "avatar.bmp");
        _svc.Validate(file).Should().Contain("Unsupported file extension");
    }

    [Fact]
    public void Validate_NoExtension_ReturnsError()
    {
        var file = CreateFormFile(1024, "image/png", "avatar");
        _svc.Validate(file).Should().Contain("Unsupported file extension");
    }

    [Theory]
    [InlineData("image/jpeg", "test.jpg")]
    [InlineData("image/png", "test.png")]
    [InlineData("image/webp", "test.webp")]
    [InlineData("image/gif", "test.gif")]
    public void Validate_ValidFile_ReturnsNull(string contentType, string fileName)
    {
        var file = CreateFormFile(1024, contentType, fileName);
        _svc.Validate(file).Should().BeNull();
    }

    // --- SaveUserAvatarAsync ---

    [Fact]
    public async Task SaveUserAvatarAsync_UploadsWithCorrectPath()
    {
        var userId = Guid.NewGuid();
        var file = CreateFormFile(100, "image/png", "avatar.png");

        _fileStorage.Setup(x => x.DeleteByPrefixAsync("avatars", It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _fileStorage.Setup(x => x.UploadAsync("avatars", It.IsAny<string>(), It.IsAny<Stream>(), "image/png"))
            .ReturnsAsync("https://storage.example.com/avatars/test");

        var result = await _svc.SaveUserAvatarAsync(userId, file);
        result.Should().Be("https://storage.example.com/avatars/test");

        _fileStorage.Verify(x => x.DeleteByPrefixAsync("avatars", $"{userId}/avatar"), Times.Once);
        _fileStorage.Verify(x => x.UploadAsync("avatars", It.Is<string>(p => p.StartsWith($"{userId}/avatar-")), It.IsAny<Stream>(), "image/png"), Times.Once);
    }

    // --- DeleteUserAvatarAsync ---

    [Fact]
    public async Task DeleteUserAvatarAsync_DeletesByPrefix()
    {
        var userId = Guid.NewGuid();
        _fileStorage.Setup(x => x.DeleteByPrefixAsync("avatars", $"{userId}/avatar")).Returns(Task.CompletedTask);

        await _svc.DeleteUserAvatarAsync(userId);

        _fileStorage.Verify(x => x.DeleteByPrefixAsync("avatars", $"{userId}/avatar"), Times.Once);
    }

    // --- SaveServerAvatarAsync ---

    [Fact]
    public async Task SaveServerAvatarAsync_UploadsWithServerPrefix()
    {
        var userId = Guid.NewGuid();
        var serverId = Guid.NewGuid();
        var file = CreateFormFile(100, "image/jpeg", "avatar.jpg");

        _fileStorage.Setup(x => x.DeleteByPrefixAsync("avatars", It.IsAny<string>())).Returns(Task.CompletedTask);
        _fileStorage.Setup(x => x.UploadAsync("avatars", It.IsAny<string>(), It.IsAny<Stream>(), "image/jpeg"))
            .ReturnsAsync("https://storage.example.com/avatars/server");

        var result = await _svc.SaveServerAvatarAsync(userId, serverId, file);
        result.Should().NotBeNullOrEmpty();

        _fileStorage.Verify(x => x.DeleteByPrefixAsync("avatars", $"{userId}/server-{serverId}"), Times.Once);
    }

    // --- SaveServerIconAsync ---

    [Fact]
    public async Task SaveServerIconAsync_UploadsWithCorrectPrefix()
    {
        var serverId = Guid.NewGuid();
        var file = CreateFormFile(100, "image/png", "icon.png");

        _fileStorage.Setup(x => x.DeleteByPrefixAsync("avatars", It.IsAny<string>())).Returns(Task.CompletedTask);
        _fileStorage.Setup(x => x.UploadAsync("avatars", It.IsAny<string>(), It.IsAny<Stream>(), "image/png"))
            .ReturnsAsync("https://storage.example.com/avatars/icon");

        await _svc.SaveServerIconAsync(serverId, file);

        _fileStorage.Verify(x => x.DeleteByPrefixAsync("avatars", $"server-icons/{serverId}"), Times.Once);
    }

    // --- ResolveUrl ---

    [Fact]
    public void ResolveUrl_NullInput_ReturnsNull()
    {
        _svc.ResolveUrl(null).Should().BeNull();
    }

    [Fact]
    public void ResolveUrl_EmptyInput_ReturnsNull()
    {
        _svc.ResolveUrl("").Should().BeNull();
    }

    [Fact]
    public void ResolveUrl_ValidUrl_ReturnsSame()
    {
        _svc.ResolveUrl("https://example.com/avatar.png").Should().Be("https://example.com/avatar.png");
    }

    // --- Helpers ---

    private static IFormFile CreateFormFile(long length, string contentType, string fileName)
    {
        var mock = new Mock<IFormFile>();
        mock.Setup(f => f.Length).Returns(length);
        mock.Setup(f => f.ContentType).Returns(contentType);
        mock.Setup(f => f.FileName).Returns(fileName);
        mock.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(new byte[Math.Max(length, 0)]));
        return mock.Object;
    }
}
