using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;

namespace Codec.Api.Tests.Services;

public class CustomEmojiServiceTests
{
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly CustomEmojiService _svc;

    public CustomEmojiServiceTests()
    {
        _svc = new CustomEmojiService(_fileStorage.Object);
    }

    [Fact]
    public void Validate_EmptyFile_ReturnsError()
    {
        var file = CreateFormFile(0, "image/png", "emoji.png");
        _svc.Validate(file).Should().Be("File is empty.");
    }

    [Fact]
    public void Validate_Over512KB_ReturnsError()
    {
        var file = CreateFormFile(513 * 1024, "image/png", "emoji.png");
        _svc.Validate(file).Should().Contain("512 KB");
    }

    [Fact]
    public void Validate_InvalidContentType_ReturnsError()
    {
        var file = CreateFormFile(1024, "video/mp4", "emoji.mp4");
        _svc.Validate(file).Should().Contain("Unsupported file type");
    }

    [Fact]
    public void Validate_InvalidExtension_ReturnsError()
    {
        var file = CreateFormFile(1024, "image/png", "emoji.svg");
        _svc.Validate(file).Should().Contain("Unsupported file extension");
    }

    [Fact]
    public void Validate_ValidFile_ReturnsNull()
    {
        var file = CreateFormFile(1024, "image/png", "emoji.png");
        _svc.Validate(file).Should().BeNull();
    }

    [Fact]
    public async Task SaveEmojiAsync_UploadsCorrectly()
    {
        var serverId = Guid.NewGuid();
        var file = CreateFormFile(100, "image/gif", "pepe.gif");

        _fileStorage.Setup(x => x.UploadAsync("emojis", It.IsAny<string>(), It.IsAny<Stream>(), "image/gif"))
            .ReturnsAsync("https://storage.example.com/emojis/test.gif");

        var result = await _svc.SaveEmojiAsync(serverId, "pepe", file);

        result.Should().Be("https://storage.example.com/emojis/test.gif");
        _fileStorage.Verify(x => x.UploadAsync("emojis",
            It.Is<string>(p => p.StartsWith($"server-{serverId}/pepe-") && p.EndsWith(".gif")),
            It.IsAny<Stream>(), "image/gif"), Times.Once);
    }

    [Fact]
    public async Task DeleteEmojiAsync_ExtractsPathAndDeletes()
    {
        var url = "https://storage.example.com/emojis/server-abc/pepe-hash.png";
        _fileStorage.Setup(x => x.DeleteAsync("emojis", "server-abc/pepe-hash.png")).Returns(Task.CompletedTask);

        await _svc.DeleteEmojiAsync(url);

        _fileStorage.Verify(x => x.DeleteAsync("emojis", "server-abc/pepe-hash.png"), Times.Once);
    }

    [Fact]
    public async Task DeleteEmojiAsync_NoContainerSegment_DoesNothing()
    {
        await _svc.DeleteEmojiAsync("https://example.com/no-match.png");
        _fileStorage.Verify(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

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
