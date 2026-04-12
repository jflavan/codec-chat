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
    public async Task RehostImageAsync_ReturnsEmptyString_ForUnsupportedContentType()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[] { 0x00 })
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");

        var service = CreateService(response);
        // Unsupported content types are intentional skips (empty string), not failures (null)
        var result = await service.RehostImageAsync(
            "https://cdn.discordapp.com/attachments/123/file.pdf",
            "images", 10 * 1024 * 1024, 4096, CancellationToken.None);

        Assert.Equal("", result);
    }

    [Fact]
    public async Task RehostImageAsync_ReturnsNull_OnHttpFailure()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);

        var service = CreateService(response);
        var result = await service.RehostImageAsync(
            "https://cdn.discordapp.com/attachments/123/expired.png",
            "images", 10 * 1024 * 1024, 4096, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task RehostImageAsync_UploadsSmallJpeg_WithoutProcessing()
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

        Assert.Equal("https://codec.chat/uploads/images/import/abc123.jpg", result);
        _storageMock.Verify(s => s.UploadAsync(
            "images", It.Is<string>(p => p.StartsWith("import/")), It.IsAny<Stream>(), "image/jpeg", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RehostImageAsync_ReturnsEmptyString_ForGifOverMaxSize()
    {
        var gifBytes = CreateTestGif();

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(gifBytes)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/gif");

        var service = CreateService(response);
        // Oversized GIFs are intentional skips (empty string), not failures (null)
        var result = await service.RehostImageAsync(
            "https://cdn.discordapp.com/emojis/123.gif",
            "emojis", 1, null, CancellationToken.None);

        Assert.Equal("", result);
    }
}
