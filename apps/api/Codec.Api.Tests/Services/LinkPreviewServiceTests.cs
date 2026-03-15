using Codec.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;

namespace Codec.Api.Tests.Services;

public class LinkPreviewServiceTests
{
    private readonly Mock<ILogger<LinkPreviewService>> _logger = new();

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

    private LinkPreviewService CreateService(HttpMessageHandler? handler = null)
    {
        var httpClient = handler is not null ? new HttpClient(handler) : new HttpClient();
        return new LinkPreviewService(httpClient, _logger.Object);
    }

    private class MockHttpHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response);
    }
}
