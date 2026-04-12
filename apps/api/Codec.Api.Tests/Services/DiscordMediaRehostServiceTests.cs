using Codec.Api.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using SkiaSharp;
using System.Net;

namespace Codec.Api.Tests.Services;

public class DiscordMediaRehostServiceTests
{
    private readonly Mock<IFileStorageService> _storageMock = new();
    private readonly Mock<ILogger<DiscordMediaRehostService>> _loggerMock = new();

    private DiscordMediaRehostService CreateService(HttpResponseMessage response)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
        var httpClient = new HttpClient(handler.Object);
        return new DiscordMediaRehostService(httpClient, _storageMock.Object, _loggerMock.Object);
    }

    private static byte[] CreateTestJpeg(int width = 1, int height = 1)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.SetPixel(0, 0, SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        return data.ToArray();
    }

    private static byte[] CreateTestGif(int width = 100, int height = 100)
    {
        using var bitmap = new SKBitmap(width, height);
        using var image = SKImage.FromBitmap(bitmap);
        // SkiaSharp can't encode GIF, use PNG bytes as stand-in — service checks content-type header
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public async Task RehostImageAsync_Skipped_ForUnsupportedContentType()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[] { 0x00 })
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");

        var service = CreateService(response);
        var result = await service.RehostImageAsync(
            "https://cdn.discordapp.com/attachments/123/file.pdf",
            "images", 10 * 1024 * 1024, 4096, CancellationToken.None);

        Assert.Equal(RehostOutcome.Skipped, result.Outcome);
    }

    [Fact]
    public async Task RehostImageAsync_Failed_OnHttpFailure()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);

        var service = CreateService(response);
        var result = await service.RehostImageAsync(
            "https://cdn.discordapp.com/attachments/123/expired.png",
            "images", 10 * 1024 * 1024, 4096, CancellationToken.None);

        Assert.Equal(RehostOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task RehostImageAsync_Success_UploadsSmallJpeg()
    {
        var jpegBytes = CreateTestJpeg();

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(jpegBytes)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

        _storageMock.Setup(s => s.UploadAsync(
            "images", It.IsAny<string>(), It.IsAny<Stream>(), "image/jpeg", It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://codec.chat/uploads/images/import/abc123.jpg");

        var service = CreateService(response);
        var result = await service.RehostImageAsync(
            "https://cdn.discordapp.com/attachments/123/photo.jpg",
            "images", 10 * 1024 * 1024, 4096, CancellationToken.None);

        Assert.Equal(RehostOutcome.Success, result.Outcome);
        Assert.Equal("https://codec.chat/uploads/images/import/abc123.jpg", result.Url);
        _storageMock.Verify(s => s.UploadAsync(
            "images", It.Is<string>(p => p.StartsWith("import/")), It.IsAny<Stream>(), "image/jpeg", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RehostImageAsync_Skipped_ForGifOverMaxSize()
    {
        var gifBytes = CreateTestGif();

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(gifBytes)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/gif");

        var service = CreateService(response);
        var result = await service.RehostImageAsync(
            "https://cdn.discordapp.com/emojis/123.gif",
            "emojis", 1, null, CancellationToken.None);

        Assert.Equal(RehostOutcome.Skipped, result.Outcome);
    }

    [Fact]
    public async Task RehostImageAsync_Failed_OnNetworkException()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        var httpClient = new HttpClient(handler.Object);
        var service = new DiscordMediaRehostService(httpClient, _storageMock.Object, _loggerMock.Object);

        var result = await service.RehostImageAsync(
            "https://cdn.discordapp.com/attachments/123/photo.jpg",
            "images", 10 * 1024 * 1024, 4096, CancellationToken.None);

        Assert.Equal(RehostOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task RehostImageAsync_Success_UploadsGifWithinSizeLimit()
    {
        var gifBytes = CreateTestGif();

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(gifBytes)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/gif");

        _storageMock.Setup(s => s.UploadAsync(
            "emojis", It.IsAny<string>(), It.IsAny<Stream>(), "image/gif", It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://codec.chat/uploads/emojis/import/abc123.gif");

        var service = CreateService(response);
        var result = await service.RehostImageAsync(
            "https://cdn.discordapp.com/emojis/123.gif",
            "emojis", 10 * 1024 * 1024, null, CancellationToken.None);

        Assert.Equal(RehostOutcome.Success, result.Outcome);
        Assert.Equal("https://codec.chat/uploads/emojis/import/abc123.gif", result.Url);
        _storageMock.Verify(s => s.UploadAsync(
            "emojis", It.Is<string>(p => p.StartsWith("import/") && p.EndsWith(".gif")),
            It.IsAny<Stream>(), "image/gif", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RehostImageAsync_Failed_WhenImageDecodeReturnsNull()
    {
        var invalidBytes = new byte[] { 0xFF, 0xFE, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(invalidBytes)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

        var service = CreateService(response);
        var result = await service.RehostImageAsync(
            "https://cdn.discordapp.com/attachments/123/corrupt.jpg",
            "images", 10 * 1024 * 1024, 4096, CancellationToken.None);

        Assert.Equal(RehostOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task RehostImageAsync_Success_UploadsPngAsPng()
    {
        using var bitmap = new SKBitmap(10, 10);
        bitmap.SetPixel(0, 0, SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var pngBytes = data.ToArray();

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(pngBytes)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");

        _storageMock.Setup(s => s.UploadAsync(
            "images", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://codec.chat/uploads/images/import/abc123.png");

        var service = CreateService(response);
        var result = await service.RehostImageAsync(
            "https://cdn.discordapp.com/attachments/123/icon.png",
            "images", 10 * 1024 * 1024, 4096, CancellationToken.None);

        Assert.Equal(RehostOutcome.Success, result.Outcome);
        _storageMock.Verify(s => s.UploadAsync(
            "images", It.Is<string>(p => p.StartsWith("import/") && p.EndsWith(".png")),
            It.IsAny<Stream>(), "image/png", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RehostImageAsync_Success_UploadsWebP()
    {
        using var bitmap = new SKBitmap(10, 10);
        bitmap.SetPixel(0, 0, SKColors.Green);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Webp, 85);
        var webpBytes = data.ToArray();

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(webpBytes)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/webp");

        _storageMock.Setup(s => s.UploadAsync(
            "images", It.IsAny<string>(), It.IsAny<Stream>(), "image/webp", It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://codec.chat/uploads/images/import/abc123.webp");

        var service = CreateService(response);
        var result = await service.RehostImageAsync(
            "https://cdn.discordapp.com/attachments/123/sticker.webp",
            "images", 10 * 1024 * 1024, 4096, CancellationToken.None);

        Assert.Equal(RehostOutcome.Success, result.Outcome);
        _storageMock.Verify(s => s.UploadAsync(
            "images", It.Is<string>(p => p.StartsWith("import/") && p.EndsWith(".webp")),
            It.IsAny<Stream>(), "image/webp", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RehostImageAsync_Success_ResizesImageWhenExceedsMaxDimension()
    {
        // 200x100 image with maxDimensionPx: 50 → should resize to 50x25
        var jpegBytes = CreateTestJpeg(width: 200, height: 100);

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(jpegBytes)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

        _storageMock.Setup(s => s.UploadAsync(
            "images", It.IsAny<string>(), It.IsAny<Stream>(), "image/jpeg", It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://codec.chat/uploads/images/import/resized.jpg");

        var service = CreateService(response);
        var result = await service.RehostImageAsync(
            "https://cdn.discordapp.com/attachments/123/large.jpg",
            "images", 10 * 1024 * 1024, 50, CancellationToken.None);

        Assert.Equal(RehostOutcome.Success, result.Outcome);
        Assert.Equal("https://codec.chat/uploads/images/import/resized.jpg", result.Url);
        _storageMock.Verify(s => s.UploadAsync(
            "images", It.IsAny<string>(), It.IsAny<Stream>(), "image/jpeg", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RehostImageAsync_Failed_WhenCancellationTokenFires()
    {
        // The service catches all exceptions from GetAsync (including TaskCanceledException)
        // and returns Failed rather than propagating them.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("Cancelled"));

        var httpClient = new HttpClient(handler.Object);
        var service = new DiscordMediaRehostService(httpClient, _storageMock.Object, _loggerMock.Object);

        var result = await service.RehostImageAsync(
            "https://cdn.discordapp.com/attachments/123/photo.jpg",
            "images", 10 * 1024 * 1024, 4096, cts.Token);

        Assert.Equal(RehostOutcome.Failed, result.Outcome);
    }
}
