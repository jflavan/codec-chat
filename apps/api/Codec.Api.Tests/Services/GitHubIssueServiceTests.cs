using System.Net;
using System.Text.Json;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Codec.Api.Tests.Services;

public class GitHubIssueServiceTests
{
    private readonly Mock<IConfiguration> _config = new();

    private GitHubIssueService CreateService(HttpClient httpClient)
    {
        return new GitHubIssueService(httpClient, _config.Object);
    }

    private void SetupConfig(string owner = "test-owner", string repo = "test-repo")
    {
        _config.Setup(c => c["GitHub:RepoOwner"]).Returns(owner);
        _config.Setup(c => c["GitHub:RepoName"]).Returns(repo);
    }

    [Fact]
    public async Task CreateIssueAsync_SuccessfulResponse_ReturnsHtmlUrl()
    {
        SetupConfig();
        var expectedUrl = "https://github.com/test-owner/test-repo/issues/42";
        var responseBody = JsonSerializer.Serialize(new { html_url = expectedUrl });

        var handler = new FakeHttpMessageHandler(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Created,
            Content = new StringContent(responseBody)
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };

        var service = CreateService(httpClient);
        var result = await service.CreateIssueAsync("Bug report", "Something broke");

        result.Should().Be(expectedUrl);
    }

    [Fact]
    public async Task CreateIssueAsync_SendsCorrectRequestBody()
    {
        SetupConfig("my-org", "my-app");
        var responseBody = JsonSerializer.Serialize(new { html_url = "https://github.com/my-org/my-app/issues/1" });

        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var handler = new FakeHttpMessageHandler(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Created,
            Content = new StringContent(responseBody)
        }, async req =>
        {
            capturedRequest = req;
            capturedBody = await req.Content!.ReadAsStringAsync();
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };

        var service = CreateService(httpClient);
        await service.CreateIssueAsync("Test title", "Test body");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri!.ToString().Should().Contain("/repos/my-org/my-app/issues");

        capturedBody.Should().NotBeNull();
        var parsed = JsonDocument.Parse(capturedBody!);
        parsed.RootElement.GetProperty("title").GetString().Should().Be("Test title");
        parsed.RootElement.GetProperty("body").GetString().Should().Be("Test body");
        parsed.RootElement.GetProperty("labels").GetArrayLength().Should().Be(1);
        parsed.RootElement.GetProperty("labels")[0].GetString().Should().Be("user-report");
    }

    [Fact]
    public async Task CreateIssueAsync_MissingRepoOwner_ThrowsInvalidOperation()
    {
        _config.Setup(c => c["GitHub:RepoOwner"]).Returns((string?)null);
        _config.Setup(c => c["GitHub:RepoName"]).Returns("some-repo");

        var handler = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };

        var service = CreateService(httpClient);

        var act = () => service.CreateIssueAsync("Title", "Body");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*RepoOwner*");
    }

    [Fact]
    public async Task CreateIssueAsync_MissingRepoName_ThrowsInvalidOperation()
    {
        _config.Setup(c => c["GitHub:RepoOwner"]).Returns("owner");
        _config.Setup(c => c["GitHub:RepoName"]).Returns((string?)null);

        var handler = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };

        var service = CreateService(httpClient);

        var act = () => service.CreateIssueAsync("Title", "Body");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*RepoName*");
    }

    [Fact]
    public async Task CreateIssueAsync_HttpError_ThrowsHttpRequestException()
    {
        SetupConfig();

        var handler = new FakeHttpMessageHandler(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Unauthorized,
            Content = new StringContent("Unauthorized")
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };

        var service = CreateService(httpClient);

        var act = () => service.CreateIssueAsync("Title", "Body");
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task CreateIssueAsync_ServerError_ThrowsHttpRequestException()
    {
        SetupConfig();

        var handler = new FakeHttpMessageHandler(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.InternalServerError,
            Content = new StringContent("Server error")
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };

        var service = CreateService(httpClient);

        var act = () => service.CreateIssueAsync("Title", "Body");
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task CreateIssueAsync_CancellationRequested_ThrowsOperationCancelled()
    {
        SetupConfig();

        var handler = new FakeHttpMessageHandler(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Created,
            Content = new StringContent(JsonSerializer.Serialize(new { html_url = "https://example.com" }))
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };

        var service = CreateService(httpClient);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => service.CreateIssueAsync("Title", "Body", cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// A simple test double for HttpMessageHandler that returns a predetermined response.
    /// </summary>
    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        private readonly Func<HttpRequestMessage, Task>? _onSend;

        public FakeHttpMessageHandler(HttpResponseMessage response, Func<HttpRequestMessage, Task>? onSend = null)
        {
            _response = response;
            _onSend = onSend;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_onSend is not null)
                await _onSend(request);
            return _response;
        }
    }
}
