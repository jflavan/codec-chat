using Codec.Api.Controllers;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Codec.Api.Tests.Controllers;

public class ImageProxyControllerTests
{
    private readonly Mock<IImageProxyService> _proxyService = new();
    private readonly ImageProxyController _controller;

    public ImageProxyControllerTests()
    {
        _controller = new ImageProxyController(_proxyService.Object);
    }

    [Fact]
    public async Task GetImage_NullUrl_ReturnsBadRequest()
    {
        var result = await _controller.GetImage(null!, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetImage_EmptyUrl_ReturnsBadRequest()
    {
        var result = await _controller.GetImage("", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetImage_WhitespaceUrl_ReturnsBadRequest()
    {
        var result = await _controller.GetImage("   ", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetImage_ServiceReturnsNull_ReturnsNotFound()
    {
        _proxyService.Setup(s => s.FetchImageAsync("https://example.com/img.png", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProxiedImage?)null);

        var result = await _controller.GetImage("https://example.com/img.png", CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetImage_ServiceReturnsImage_ReturnsFileResult()
    {
        var imageData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        _proxyService.Setup(s => s.FetchImageAsync("https://example.com/photo.jpg", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProxiedImage(imageData, "image/jpeg"));

        var result = await _controller.GetImage("https://example.com/photo.jpg", CancellationToken.None);

        var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
        fileResult.ContentType.Should().Be("image/jpeg");
        fileResult.FileContents.Should().BeEquivalentTo(imageData);
    }

    [Fact]
    public async Task GetImage_PassesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        _proxyService.Setup(s => s.FetchImageAsync("https://example.com/img.png", cts.Token))
            .ReturnsAsync(new ProxiedImage([1, 2, 3], "image/png"));

        var result = await _controller.GetImage("https://example.com/img.png", cts.Token);

        result.Should().BeOfType<FileContentResult>();
        _proxyService.Verify(s => s.FetchImageAsync("https://example.com/img.png", cts.Token), Times.Once);
    }

    [Fact]
    public async Task GetImage_PngImage_ReturnsCorrectContentType()
    {
        _proxyService.Setup(s => s.FetchImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProxiedImage([1], "image/png"));

        var result = await _controller.GetImage("https://example.com/pic.png", CancellationToken.None);

        var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
        fileResult.ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task GetImage_GifImage_ReturnsCorrectContentType()
    {
        _proxyService.Setup(s => s.FetchImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProxiedImage([1], "image/gif"));

        var result = await _controller.GetImage("https://example.com/anim.gif", CancellationToken.None);

        var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
        fileResult.ContentType.Should().Be("image/gif");
    }

    [Fact]
    public async Task GetImage_WebpImage_ReturnsCorrectContentType()
    {
        _proxyService.Setup(s => s.FetchImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProxiedImage([1], "image/webp"));

        var result = await _controller.GetImage("https://example.com/photo.webp", CancellationToken.None);

        var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
        fileResult.ContentType.Should().Be("image/webp");
    }
}
