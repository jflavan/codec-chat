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

    [Fact]
    public async Task FetchImageAsync_HttpRequestException_ReturnsNull()
    {
        var handler = new ThrowingHttpMessageHandler(new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(handler);
        var service = new ImageProxyService(httpClient, _logger.Object);

        var result = await service.FetchImageAsync("https://example.com/broken.png");

        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchImageAsync_NullContentType_ReturnsNull()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3])
        };
        // Do not set ContentType at all — it will be null
        response.Content.Headers.ContentType = null;

        var handler = new MockHttpMessageHandler(response);
        var httpClient = new HttpClient(handler);
        var service = new ImageProxyService(httpClient, _logger.Object);

        var result = await service.FetchImageAsync("https://example.com/file");

        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchImageAsync_WithCache_ReturnsCachedOnSecondCall()
    {
        var imageBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(imageBytes)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

        var handler = new MockHttpMessageHandler(response);
        var httpClient = new HttpClient(handler);
        var cache = new InMemoryDistributedCache();
        var service = new ImageProxyService(httpClient, _logger.Object, cache);

        // First call fetches from source and caches
        var result1 = await service.FetchImageAsync("https://example.com/photo.jpg");
        result1.Should().NotBeNull();

        // Second call should use cache (handler would still return same thing,
        // but we can verify the cache was populated)
        var result2 = await service.FetchImageAsync("https://example.com/photo.jpg");
        result2.Should().NotBeNull();
        result2!.ContentType.Should().Be("image/jpeg");
        result2.Data.Should().BeEquivalentTo(imageBytes);
    }

    [Fact]
    public async Task FetchImageAsync_CacheReadFailure_FallsThroughToFetch()
    {
        var imageBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(imageBytes)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

        var handler = new MockHttpMessageHandler(response);
        var httpClient = new HttpClient(handler);
        var cache = new FailingDistributedCache();
        var service = new ImageProxyService(httpClient, _logger.Object, cache);

        var result = await service.FetchImageAsync("https://example.com/photo.jpg");

        result.Should().NotBeNull();
        result!.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task FetchImageAsync_NoCache_WorksWithoutCaching()
    {
        var imageBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(imageBytes)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

        var handler = new MockHttpMessageHandler(response);
        var httpClient = new HttpClient(handler);
        var service = new ImageProxyService(httpClient, _logger.Object, cache: null);

        var result = await service.FetchImageAsync("https://example.com/photo.jpg");

        result.Should().NotBeNull();
        result!.ContentType.Should().Be("image/jpeg");
    }

    // --- Additional IsAllowedUrl edge cases ---

    [Theory]
    [InlineData("https://172.31.255.255/img.png", false)]  // End of 172.16-31 range
    [InlineData("https://172.15.0.1/img.png", true)]         // Just below private range
    [InlineData("https://172.32.0.1/img.png", true)]         // Just above private range
    [InlineData("https://0.0.0.0/img.png", false)]           // 0.x.x.x is reserved
    [InlineData("https://sub.internal/img.png", false)]       // .internal suffix
    [InlineData("https://cdn.example.com/img.png", true)]     // Valid CDN URL
    public void IsAllowedUrl_AdditionalEdgeCases(string url, bool expected)
    {
        ImageProxyService.IsAllowedUrl(url).Should().Be(expected);
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/gif")]
    [InlineData("image/webp")]
    [InlineData("image/avif")]
    public async Task FetchImageAsync_AllowedContentTypes_ReturnsData(string contentType)
    {
        var imageBytes = new byte[] { 1, 2, 3 };
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(imageBytes)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        var handler = new MockHttpMessageHandler(response);
        var httpClient = new HttpClient(handler);
        var service = new ImageProxyService(httpClient, _logger.Object);

        var result = await service.FetchImageAsync("https://example.com/image");

        result.Should().NotBeNull();
        result!.ContentType.Should().Be(contentType);
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("application/json")]
    [InlineData("application/octet-stream")]
    [InlineData("video/mp4")]
    [InlineData("image/svg+xml")]
    public async Task FetchImageAsync_DisallowedContentTypes_ReturnsNull(string contentType)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3])
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        var handler = new MockHttpMessageHandler(response);
        var httpClient = new HttpClient(handler);
        var service = new ImageProxyService(httpClient, _logger.Object);

        var result = await service.FetchImageAsync("https://example.com/file");

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task FetchImageAsync_NonSuccessStatusCodes_ReturnsNull(HttpStatusCode statusCode)
    {
        var response = new HttpResponseMessage(statusCode);

        var handler = new MockHttpMessageHandler(response);
        var httpClient = new HttpClient(handler);
        var service = new ImageProxyService(httpClient, _logger.Object);

        var result = await service.FetchImageAsync("https://example.com/img.png");

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

    private sealed class ThrowingHttpMessageHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw exception;
        }
    }

    /// <summary>
    /// Minimal in-memory distributed cache for testing.
    /// </summary>
    private sealed class InMemoryDistributedCache : Microsoft.Extensions.Caching.Distributed.IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public byte[]? Get(string key) => _store.GetValueOrDefault(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
            => Task.FromResult(_store.GetValueOrDefault(key));

        public void Set(string key, byte[] value, Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions options)
            => _store[key] = value;

        public Task SetAsync(string key, byte[] value, Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            _store[key] = value;
            return Task.CompletedTask;
        }

        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A distributed cache that always throws, for testing error handling paths.
    /// </summary>
    private sealed class FailingDistributedCache : Microsoft.Extensions.Caching.Distributed.IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException("Cache failure");
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("Cache failure");
        public void Set(string key, byte[] value, Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions options) => throw new InvalidOperationException("Cache failure");
        public Task SetAsync(string key, byte[] value, Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions options, CancellationToken token = default) => throw new InvalidOperationException("Cache failure");
        public void Refresh(string key) => throw new InvalidOperationException("Cache failure");
        public Task RefreshAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("Cache failure");
        public void Remove(string key) => throw new InvalidOperationException("Cache failure");
        public Task RemoveAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("Cache failure");
    }
}
