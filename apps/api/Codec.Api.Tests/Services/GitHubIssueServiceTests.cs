using System.Net;
using System.Text;
using System.Text.Json;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Codec.Api.Tests.Services;

public class GitHubIssueServiceTests
{
    private const string TestOwner = "test-owner";
    private const string TestRepo = "test-repo";

    private static IConfiguration CreateConfig(string? owner = TestOwner, string? repo = TestRepo)
    {
        var dict = new Dictionary<string, string?>();
        if (owner is not null) dict["GitHub:RepoOwner"] = owner;
        if (repo is not null) dict["GitHub:RepoName"] = repo;

        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
    }

    private static GitHubIssueService CreateService(HttpResponseMessage response, IConfiguration? config = null)
    {
        var handler = new FakeHttpMessageHandler(response);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        return new GitHubIssueService(httpClient, config ?? CreateConfig());
    }

    private static HttpResponseMessage CreateJsonResponse(object body, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var json = JsonSerializer.Serialize(body);
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    // --- CreateIssueAsync: Happy Path ---

    [Fact]
    public async Task CreateIssueAsync_ValidRequest_ReturnsHtmlUrl()
    {
        var expectedUrl = "https://github.com/test-owner/test-repo/issues/42";
        var response = CreateJsonResponse(new { html_url = expectedUrl });

        var service = CreateService(response);
        var result = await service.CreateIssueAsync("Bug report", "Something broke");

        result.Should().Be(expectedUrl);
    }

    [Fact]
    public async Task CreateIssueAsync_SendsCorrectRequestPath()
    {
        var response = CreateJsonResponse(new { html_url = "https://github.com/test-owner/test-repo/issues/1" });
        var handler = new CapturingHttpMessageHandler(response);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        var service = new GitHubIssueService(httpClient, CreateConfig());

        await service.CreateIssueAsync("Title", "Body");

        handler.CapturedRequest.Should().NotBeNull();
        handler.CapturedRequest!.Method.Should().Be(HttpMethod.Post);
        handler.CapturedRequest.RequestUri!.PathAndQuery.Should().Be("/repos/test-owner/test-repo/issues");
    }

    [Fact]
    public async Task CreateIssueAsync_SendsCorrectPayload()
    {
        var response = CreateJsonResponse(new { html_url = "https://github.com/x/y/issues/1" });
        var handler = new CapturingHttpMessageHandler(response);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        var service = new GitHubIssueService(httpClient, CreateConfig());

        await service.CreateIssueAsync("My Title", "My Body");

        var content = await handler.CapturedRequest!.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        root.GetProperty("title").GetString().Should().Be("My Title");
        root.GetProperty("body").GetString().Should().Be("My Body");
        root.GetProperty("labels").EnumerateArray().Select(e => e.GetString()).Should().ContainSingle("user-report");
    }

    [Fact]
    public async Task CreateIssueAsync_ContentTypeIsJson()
    {
        var response = CreateJsonResponse(new { html_url = "https://github.com/x/y/issues/1" });
        var handler = new CapturingHttpMessageHandler(response);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        var service = new GitHubIssueService(httpClient, CreateConfig());

        await service.CreateIssueAsync("Title", "Body");

        handler.CapturedRequest!.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    // --- CreateIssueAsync: Config Missing ---

    [Fact]
    public async Task CreateIssueAsync_MissingRepoOwner_ThrowsInvalidOperationException()
    {
        var response = CreateJsonResponse(new { html_url = "unused" });
        var config = CreateConfig(owner: null);
        var service = CreateService(response, config);

        var act = () => service.CreateIssueAsync("Title", "Body");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*RepoOwner*");
    }

    [Fact]
    public async Task CreateIssueAsync_MissingRepoName_ThrowsInvalidOperationException()
    {
        var response = CreateJsonResponse(new { html_url = "unused" });
        var config = CreateConfig(repo: null);
        var service = CreateService(response, config);

        var act = () => service.CreateIssueAsync("Title", "Body");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*RepoName*");
    }

    // --- CreateIssueAsync: HTTP Error Responses ---

    [Fact]
    public async Task CreateIssueAsync_NotFound_ThrowsHttpRequestException()
    {
        var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("Not Found")
        };
        var service = CreateService(response);

        var act = () => service.CreateIssueAsync("Title", "Body");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task CreateIssueAsync_InternalServerError_ThrowsHttpRequestException()
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Server Error")
        };
        var service = CreateService(response);

        var act = () => service.CreateIssueAsync("Title", "Body");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task CreateIssueAsync_Unauthorized_ThrowsHttpRequestException()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("Bad credentials")
        };
        var service = CreateService(response);

        var act = () => service.CreateIssueAsync("Title", "Body");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task CreateIssueAsync_Forbidden_ThrowsHttpRequestException()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("Forbidden")
        };
        var service = CreateService(response);

        var act = () => service.CreateIssueAsync("Title", "Body");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task CreateIssueAsync_UnprocessableEntity_ThrowsHttpRequestException()
    {
        var response = new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("Validation failed")
        };
        var service = CreateService(response);

        var act = () => service.CreateIssueAsync("Title", "Body");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // --- CreateIssueAsync: Malformed Response ---

    [Fact]
    public async Task CreateIssueAsync_ResponseMissingHtmlUrl_ThrowsKeyNotFoundException()
    {
        var response = CreateJsonResponse(new { id = 42, title = "Bug" });
        var service = CreateService(response);

        var act = () => service.CreateIssueAsync("Title", "Body");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task CreateIssueAsync_ResponseInvalidJson_ThrowsJsonException()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json", Encoding.UTF8, "application/json")
        };
        var service = CreateService(response);

        var act = () => service.CreateIssueAsync("Title", "Body");

        await act.Should().ThrowAsync<JsonException>();
    }

    // --- CreateIssueAsync: Cancellation ---

    [Fact]
    public async Task CreateIssueAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var response = CreateJsonResponse(new { html_url = "https://github.com/x/y/issues/1" });
        var handler = new CancellingHttpMessageHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        var service = new GitHubIssueService(httpClient, CreateConfig());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => service.CreateIssueAsync("Title", "Body", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // --- CreateIssueAsync: Config Values Used Correctly ---

    [Fact]
    public async Task CreateIssueAsync_UsesConfiguredOwnerAndRepo()
    {
        var response = CreateJsonResponse(new { html_url = "https://github.com/my-org/my-app/issues/99" });
        var handler = new CapturingHttpMessageHandler(response);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        var config = CreateConfig(owner: "my-org", repo: "my-app");
        var service = new GitHubIssueService(httpClient, config);

        await service.CreateIssueAsync("Title", "Body");

        handler.CapturedRequest!.RequestUri!.PathAndQuery.Should().Be("/repos/my-org/my-app/issues");
    }

    // --- Test Helpers ---

    private class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }
    }

    private class CapturingHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? CapturedRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequest = request;
            return Task.FromResult(response);
        }
    }

    private class CancellingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
