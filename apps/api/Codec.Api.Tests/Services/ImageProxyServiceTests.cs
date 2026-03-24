using System.Net;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Codec.Api.Tests.Services;

public class ImageProxyServiceTests
{
    private readonly Mock<ILogger<ImageProxyService>> _logger = new();

    // --- IsAllowedUrl (internal static) ---

    [Theory]
    [InlineData("https://example.com/image.png", true)]
    [InlineData("http://example.com/image.jpg", true)]
    [InlineData("ftp://example.com/image.png", false)]
    [InlineData("https://localhost/admin.png", false)]
    [InlineData("https://something.local/img.png", false)]
    [InlineData("https://metadata.google.internal/path", false)]
    [InlineData("https://10.0.0.1/image.png", false)]
    [InlineData("https://192.168.1.1/image.png", false)]
    [InlineData("https://172.16.0.1/image.png", false)]
    [InlineData("https://127.0.0.1/image.png", false)]
    [InlineData("https://169.254.1.1/image.png", false)]
    [InlineData("not-a-url", false)]
    [InlineData("", false)]
    public void IsAllowedUrl_Validates(string url, bool expected)
    {
        ImageProxyService.IsAllowedUrl(url).Should().Be(expected);
    }

    // --- FetchImageAsync ---

    [Fact]
    public async Task FetchImageAsync_BlockedUrl_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var httpClient = new HttpClient(handler);
        var service = new ImageProxyService(httpClient, _logger.Object);

        var result = await service.FetchImageAsync("https://localhost/evil.png");

        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchImageAsync_ValidImage_ReturnsData()
    {
        var imageBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG magic bytes
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(imageBytes)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

        var handler = new MockHttpMessageHandler(response);
        var httpClient = new HttpClient(handler);
        var service = new ImageProxyService(httpClient, _logger.Object);

        var result = await service.FetchImageAsync("https://example.com/photo.jpg");

        result.Should().NotBeNull();
        result!.ContentType.Should().Be("image/jpeg");
        result.Data.Should().BeEquivalentTo(imageBytes);
    }

    [Fact]
    public async Task FetchImageAsync_NonImageContentType_ReturnsNull()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>not an image</html>")
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html");

        var handler = new MockHttpMessageHandler(response);
        var httpClient = new HttpClient(handler);
        var service = new ImageProxyService(httpClient, _logger.Object);

        var result = await service.FetchImageAsync("https://example.com/page.html");

        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchImageAsync_UpstreamError_ReturnsNull()
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var handler = new MockHttpMessageHandler(response);
        var httpClient = new HttpClient(handler);
        var service = new ImageProxyService(httpClient, _logger.Object);

        var result = await service.FetchImageAsync("https://example.com/broken.png");

        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchImageAsync_ContentLengthExceedsLimit_ReturnsNull()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[1])
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        response.Content.Headers.ContentLength = 11 * 1024 * 1024; // 11 MB

        var handler = new MockHttpMessageHandler(response);
        var httpClient = new HttpClient(handler);
        var service = new ImageProxyService(httpClient, _logger.Object);

        var result = await service.FetchImageAsync("https://example.com/huge.png");

        result.Should().BeNull();
    }

    private sealed class MockHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }
    }
}
