using System.Net;
using System.Text;
using System.Text.Json;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;

namespace Codec.Api.Tests.Services;

public class LinkPreviewServiceTests
{
    private readonly Mock<ILogger<LinkPreviewService>> _logger = new();
    private readonly Mock<IDistributedCache> _cache = new();

    // --- ExtractUrls ---

    [Fact]
    public void ExtractUrls_EmptyBody_ReturnsEmpty()
    {
        var svc = CreateService();
        svc.ExtractUrls("").Should().BeEmpty();
    }

    [Fact]
    public void ExtractUrls_NullBody_ReturnsEmpty()
    {
        var svc = CreateService();
        svc.ExtractUrls(null!).Should().BeEmpty();
    }

    [Fact]
    public void ExtractUrls_SingleUrl_ReturnsIt()
    {
        var svc = CreateService();
        var urls = svc.ExtractUrls("Check this out: https://example.com/page");
        urls.Should().ContainSingle().Which.Should().Be("https://example.com/page");
    }

    [Fact]
    public void ExtractUrls_MultipleUrls_ReturnsAll()
    {
        var svc = CreateService();
        var urls = svc.ExtractUrls("https://a.com and http://b.com");
        urls.Should().HaveCount(2);
    }

    [Fact]
    public void ExtractUrls_RespectsMaxLimit()
    {
        var svc = CreateService();
        var urls = svc.ExtractUrls("https://a.com https://b.com https://c.com", maxUrls: 2);
        urls.Should().HaveCount(2);
    }

    [Fact]
    public void ExtractUrls_TrimsTrailingPunctuation()
    {
        var svc = CreateService();
        var urls = svc.ExtractUrls("Visit https://example.com.");
        urls.Should().ContainSingle().Which.Should().Be("https://example.com");
    }

    [Fact]
    public void ExtractUrls_DeduplicatesUrls()
    {
        var svc = CreateService();
        var urls = svc.ExtractUrls("https://example.com and https://example.com again");
        urls.Should().ContainSingle();
    }

    // --- IsAllowedUrl (internal static) ---

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("https://localhost/admin", false)]
    [InlineData("https://something.local", false)]
    [InlineData("https://metadata.google.internal", false)]
    [InlineData("https://something.internal", false)]
    [InlineData("http://127.0.0.1/secret", false)]
    [InlineData("http://10.0.0.1/internal", false)]
    [InlineData("http://192.168.1.1/admin", false)]
    [InlineData("http://172.16.0.1/data", false)]
    [InlineData("http://169.254.1.1/metadata", false)]
    [InlineData("not-a-url", false)]
    public void IsAllowedUrl_Validates(string url, bool expected)
    {
        LinkPreviewService.IsAllowedUrl(url).Should().Be(expected);
    }

    // --- ParseMetadata (internal static) ---

    [Fact]
    public void ParseMetadata_WithOgTags_ReturnsPreview()
    {
        var html = """
            <html><head>
                <meta property="og:title" content="Test Title" />
                <meta property="og:description" content="Test Desc" />
                <meta property="og:image" content="https://img.example.com/pic.jpg" />
                <meta property="og:site_name" content="Example" />
                <meta property="og:url" content="https://example.com/page" />
            </head></html>
            """;

        var result = LinkPreviewService.ParseMetadata(html, "https://example.com/page");
        result.Should().NotBeNull();
        result!.Title.Should().Be("Test Title");
        result.Description.Should().Be("Test Desc");
        result.ImageUrl.Should().Be("https://img.example.com/pic.jpg");
        result.SiteName.Should().Be("Example");
        result.CanonicalUrl.Should().Be("https://example.com/page");
    }

    [Fact]
    public void ParseMetadata_FallsBackToTitleTag()
    {
        var html = "<html><head><title>Fallback Title</title></head></html>";
        var result = LinkPreviewService.ParseMetadata(html, "https://example.com");
        result.Should().NotBeNull();
        result!.Title.Should().Be("Fallback Title");
    }

    [Fact]
    public void ParseMetadata_NoTitle_ReturnsNull()
    {
        var html = "<html><head><meta name='description' content='No title here'></head></html>";
        var result = LinkPreviewService.ParseMetadata(html, "https://example.com");
        result.Should().BeNull();
    }

    [Fact]
    public void ParseMetadata_HttpImageUrl_Rejected()
    {
        var html = """
            <html><head>
                <meta property="og:title" content="Test" />
                <meta property="og:image" content="http://img.example.com/pic.jpg" />
            </head></html>
            """;
        var result = LinkPreviewService.ParseMetadata(html, "https://example.com");
        result.Should().NotBeNull();
        result!.ImageUrl.Should().BeNull();
    }

    [Fact]
    public void ParseMetadata_AltAttributeOrder_StillWorks()
    {
        var html = """
            <html><head>
                <meta content="Alt Order Title" property="og:title" />
            </head></html>
            """;
        var result = LinkPreviewService.ParseMetadata(html, "https://example.com");
        result.Should().NotBeNull();
        result!.Title.Should().Be("Alt Order Title");
    }

    [Fact]
    public void ParseMetadata_DecodesHtmlEntities()
    {
        var html = """
            <html><head>
                <meta property="og:title" content="Tom &amp; Jerry" />
            </head></html>
            """;
        var result = LinkPreviewService.ParseMetadata(html, "https://example.com");
        result!.Title.Should().Be("Tom & Jerry");
    }

    // --- FetchMetadataAsync ---

    [Fact]
    public async Task FetchMetadataAsync_BlockedUrl_ReturnsNull()
    {
        var svc = CreateService();
        var result = await svc.FetchMetadataAsync("http://localhost/admin");
        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchMetadataAsync_NonHtmlResponse_ReturnsNull()
    {
        var handler = new MockHttpHandler(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        });
        var svc = CreateService(handler);
        var result = await svc.FetchMetadataAsync("https://api.example.com/data");
        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchMetadataAsync_ValidHtml_ReturnsPreview()
    {
        var html = """
            <html><head>
                <meta property="og:title" content="Remote Page" />
            </head></html>
            """;
        var handler = new MockHttpHandler(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html")
        });
        var svc = CreateService(handler);
        var result = await svc.FetchMetadataAsync("https://example.com/page");
        result.Should().NotBeNull();
        result!.Title.Should().Be("Remote Page");
    }

    [Fact]
    public async Task FetchMetadataAsync_NonSuccessStatus_ReturnsNull()
    {
        var handler = new MockHttpHandler(new HttpResponseMessage(HttpStatusCode.NotFound));
        var svc = CreateService(handler);
        var result = await svc.FetchMetadataAsync("https://example.com/missing");
        result.Should().BeNull();
    }

    // --- Caching ---

    [Fact]
    public async Task FetchMetadataAsync_CacheHit_ReturnsCachedResultWithoutHttpFetch()
    {
        var cached = new LinkPreviewResult("https://example.com", "Cached Title", "Desc", null, null, null);
        var json = JsonSerializer.Serialize(cached, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        _cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(json));

        var handler = new MockHttpHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var svc = CreateService(handler);

        var result = await svc.FetchMetadataAsync("https://example.com");
        result.Should().NotBeNull();
        result!.Title.Should().Be("Cached Title");
        handler.CallCount.Should().Be(0, "HTTP fetch should be skipped on cache hit");
    }

    [Fact]
    public async Task FetchMetadataAsync_CacheHitFailedSentinel_ReturnsNullWithoutHttpFetch()
    {
        _cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes("__failed__"));

        var handler = new MockHttpHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html><head><title>Should Not Reach</title></head></html>",
                Encoding.UTF8, "text/html")
        });
        var svc = CreateService(handler);

        var result = await svc.FetchMetadataAsync("https://example.com/broken");
        result.Should().BeNull();
        handler.CallCount.Should().Be(0, "HTTP fetch should be skipped for failed sentinel");
    }

    [Fact]
    public async Task FetchMetadataAsync_CacheMiss_FetchesAndWritesToCache()
    {
        // Cache returns null (miss).
        _cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var html = "<html><head><meta property=\"og:title\" content=\"Fresh\" /></head></html>";
        var handler = new MockHttpHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html")
        });
        var svc = CreateService(handler);

        var result = await svc.FetchMetadataAsync("https://example.com/new");
        result.Should().NotBeNull();
        result!.Title.Should().Be("Fresh");
        handler.CallCount.Should().Be(1);

        _cache.Verify(c => c.SetAsync(
            It.IsAny<string>(),
            It.Is<byte[]>(b => !Encoding.UTF8.GetString(b).Contains("__failed__")),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FetchMetadataAsync_CacheMiss_FailedFetch_WritesFailedSentinel()
    {
        _cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var handler = new MockHttpHandler(new HttpResponseMessage(HttpStatusCode.NotFound));
        var svc = CreateService(handler);

        var result = await svc.FetchMetadataAsync("https://example.com/gone");
        result.Should().BeNull();

        _cache.Verify(c => c.SetAsync(
            It.IsAny<string>(),
            It.Is<byte[]>(b => Encoding.UTF8.GetString(b) == "__failed__"),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FetchMetadataAsync_CacheReadThrows_StillFetches()
    {
        _cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis down"));

        var html = "<html><head><title>Fallback</title></head></html>";
        var handler = new MockHttpHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html")
        });
        var svc = CreateService(handler);

        var result = await svc.FetchMetadataAsync("https://example.com/resilient");
        result.Should().NotBeNull();
        result!.Title.Should().Be("Fallback");
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task FetchMetadataAsync_CacheWriteThrows_StillReturnsResult()
    {
        _cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        _cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis down"));

        var html = "<html><head><title>Still Works</title></head></html>";
        var handler = new MockHttpHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html")
        });
        var svc = CreateService(handler);

        var result = await svc.FetchMetadataAsync("https://example.com/write-fail");
        result.Should().NotBeNull();
        result!.Title.Should().Be("Still Works");
    }

    [Fact]
    public async Task FetchMetadataAsync_NoCacheConfigured_StillFetches()
    {
        var html = "<html><head><title>No Cache</title></head></html>";
        var handler = new MockHttpHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html")
        });
        var httpClient = new HttpClient(handler);
        var svc = new LinkPreviewService(httpClient, _logger.Object); // no cache parameter

        var result = await svc.FetchMetadataAsync("https://example.com/no-cache");
        result.Should().NotBeNull();
        result!.Title.Should().Be("No Cache");
    }

    private LinkPreviewService CreateService(HttpMessageHandler? handler = null)
    {
        var httpClient = handler is not null ? new HttpClient(handler) : new HttpClient();
        return new LinkPreviewService(httpClient, _logger.Object, _cache.Object);
    }

    private class MockHttpHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(response);
        }
    }
}
