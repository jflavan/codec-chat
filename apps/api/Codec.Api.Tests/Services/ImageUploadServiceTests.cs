using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;

namespace Codec.Api.Tests.Services;

public class ImageUploadServiceTests
{
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly ImageUploadService _svc;

    public ImageUploadServiceTests()
    {
        _svc = new ImageUploadService(_fileStorage.Object);
    }

    [Fact]
    public void Validate_EmptyFile_ReturnsError()
    {
        var file = CreateFormFile(0, "image/png", "img.png");
        _svc.Validate(file).Should().Be("File is empty.");
    }

    [Fact]
    public void Validate_OversizedFile_ReturnsError()
    {
        var file = CreateFormFile(11 * 1024 * 1024, "image/png", "img.png");
        _svc.Validate(file).Should().Contain("MB limit");
    }

    [Fact]
    public void Validate_InvalidContentType_ReturnsError()
    {
        var file = CreateFormFile(1024, "application/pdf", "doc.pdf");
        _svc.Validate(file).Should().Contain("Unsupported file type");
    }

    [Fact]
    public void Validate_InvalidExtension_ReturnsError()
    {
        var file = CreateFormFile(1024, "image/png", "img.bmp");
        _svc.Validate(file).Should().Contain("Unsupported file extension");
    }

    [Theory]
    [InlineData("image/jpeg", "img.jpg")]
    [InlineData("image/jpeg", "img.jpeg")]
    [InlineData("image/png", "img.png")]
    [InlineData("image/webp", "img.webp")]
    [InlineData("image/gif", "img.gif")]
    public void Validate_ValidFile_ReturnsNull(string contentType, string fileName)
    {
        var file = CreateFormFile(1024, contentType, fileName);
        _svc.Validate(file).Should().BeNull();
    }

    [Fact]
    public async Task SaveImageAsync_UploadsWithHashFilename()
    {
        var userId = Guid.NewGuid();
        var file = CreateFormFile(100, "image/png", "photo.png");

        _fileStorage.Setup(x => x.UploadAsync("images", It.IsAny<string>(), It.IsAny<Stream>(), "image/png"))
            .ReturnsAsync("https://storage.example.com/images/test.png");

        var result = await _svc.SaveImageAsync(userId, file);

        result.Should().Be("https://storage.example.com/images/test.png");
        _fileStorage.Verify(x => x.UploadAsync("images",
            It.Is<string>(p => p.StartsWith($"{userId}/") && p.EndsWith(".png")),
            It.IsAny<Stream>(), "image/png"), Times.Once);
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
