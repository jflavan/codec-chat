using System.Net;
using System.Text;
using System.Text.Json;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Codec.Api.Tests.Services;

public class GitHubIssueServiceTests
{
    private readonly IConfiguration _validConfig;
    private readonly IConfiguration _missingOwnerConfig;
    private readonly IConfiguration _missingRepoConfig;

    public GitHubIssueServiceTests()
    {
        _validConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitHub:RepoOwner"] = "test-owner",
                ["GitHub:RepoName"] = "test-repo"
            })
            .Build();

        _missingOwnerConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitHub:RepoName"] = "test-repo"
            })
            .Build();

        _missingRepoConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitHub:RepoOwner"] = "test-owner"
            })
            .Build();
    }

    private static GitHubIssueService CreateService(IConfiguration config, HttpResponseMessage response)
    {
        var handler = new FakeHttpMessageHandler(response);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.github.com")
        };
        return new GitHubIssueService(httpClient, config);
    }

    private static HttpResponseMessage CreateJsonResponse(object body, HttpStatusCode statusCode = HttpStatusCode.Created)
    {
        var json = JsonSerializer.Serialize(body);
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    [Fact]
    public async Task CreateIssueAsync_ValidRequest_ReturnsHtmlUrl()
    {
        var expectedUrl = "https://github.com/test-owner/test-repo/issues/42";
        var response = CreateJsonResponse(new { html_url = expectedUrl });

        var service = CreateService(_validConfig, response);
        var result = await service.CreateIssueAsync("Bug title", "Bug description");

        result.Should().Be(expectedUrl);
    }

    [Fact]
    public async Task CreateIssueAsync_SendsCorrectRequest()
    {
        var expectedUrl = "https://github.com/test-owner/test-repo/issues/1";
        HttpRequestMessage? capturedRequest = null;
        var response = CreateJsonResponse(new { html_url = expectedUrl });
        var handler = new CapturingHttpMessageHandler(response, r => capturedRequest = r);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        var service = new GitHubIssueService(httpClient, _validConfig);

        await service.CreateIssueAsync("My Title", "My Body");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri!.PathAndQuery.Should().Be("/repos/test-owner/test-repo/issues");

        var requestBody = await capturedRequest.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(requestBody);
        doc.RootElement.GetProperty("title").GetString().Should().Be("My Title");
        doc.RootElement.GetProperty("body").GetString().Should().Be("My Body");
        doc.RootElement.GetProperty("labels")[0].GetString().Should().Be("user-report");
    }

    [Fact]
    public async Task CreateIssueAsync_MissingRepoOwner_ThrowsInvalidOperationException()
    {
        var response = CreateJsonResponse(new { html_url = "unused" });
        var service = CreateService(_missingOwnerConfig, response);

        var act = () => service.CreateIssueAsync("Title", "Body");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*RepoOwner*");
    }

    [Fact]
    public async Task CreateIssueAsync_MissingRepoName_ThrowsInvalidOperationException()
    {
        var response = CreateJsonResponse(new { html_url = "unused" });
        var service = CreateService(_missingRepoConfig, response);

        var act = () => service.CreateIssueAsync("Title", "Body");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*RepoName*");
    }

    [Fact]
    public async Task CreateIssueAsync_HttpError_ThrowsHttpRequestException()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        var service = CreateService(_validConfig, response);

        var act = () => service.CreateIssueAsync("Title", "Body");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task CreateIssueAsync_ServerError_ThrowsHttpRequestException()
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var service = CreateService(_validConfig, response);

        var act = () => service.CreateIssueAsync("Title", "Body");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task CreateIssueAsync_NotFound_ThrowsHttpRequestException()
    {
        var response = new HttpResponseMessage(HttpStatusCode.NotFound);
        var service = CreateService(_validConfig, response);

        var act = () => service.CreateIssueAsync("Title", "Body");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task CreateIssueAsync_ResponseMissingHtmlUrl_ThrowsKeyNotFoundException()
    {
        var response = CreateJsonResponse(new { id = 42, number = 1 });
        var service = CreateService(_validConfig, response);

        var act = () => service.CreateIssueAsync("Title", "Body");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task CreateIssueAsync_InvalidJsonResponse_ThrowsJsonException()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("not-json", Encoding.UTF8, "application/json")
        };
        var service = CreateService(_validConfig, response);

        var act = () => service.CreateIssueAsync("Title", "Body");

        await act.Should().ThrowAsync<JsonException>();
    }

    [Fact]
    public async Task CreateIssueAsync_CancellationRequested_ThrowsTaskCanceledException()
    {
        var response = CreateJsonResponse(new { html_url = "https://example.com" });
        var service = CreateService(_validConfig, response);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => service.CreateIssueAsync("Title", "Body", cts.Token);

        await act.Should().ThrowAsync<TaskCanceledException>();
    }

    [Fact]
    public async Task CreateIssueAsync_EmptyConfig_ThrowsInvalidOperationException()
    {
        var emptyConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var response = CreateJsonResponse(new { html_url = "unused" });
        var service = CreateService(emptyConfig, response);

        var act = () => service.CreateIssueAsync("Title", "Body");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*RepoOwner*");
    }

    private class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(response);
        }
    }

    private class CapturingHttpMessageHandler(HttpResponseMessage response, Action<HttpRequestMessage> capture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            capture(request);
            return Task.FromResult(response);
        }
    }
}
