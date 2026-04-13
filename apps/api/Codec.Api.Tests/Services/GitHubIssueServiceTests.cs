using System.Net;
using System.Text;
using System.Text.Json;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Codec.Api.Tests.Services;

public class GitHubIssueServiceTests
{
    private readonly Dictionary<string, string?> _configValues = new()
    {
        { "GitHub:RepoOwner", "test-owner" },
        { "GitHub:RepoName", "test-repo" }
    };

    private IConfiguration BuildConfig(Dictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>(_configValues);
        if (overrides is not null)
        {
            foreach (var kvp in overrides)
                values[kvp.Key] = kvp.Value;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static GitHubIssueService CreateService(HttpResponseMessage response, IConfiguration config)
    {
        var handler = new FakeHttpMessageHandler(response);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.github.com")
        };
        return new GitHubIssueService(httpClient, config);
    }

    private static HttpResponseMessage CreateSuccessResponse(string htmlUrl)
    {
        var json = JsonSerializer.Serialize(new { html_url = htmlUrl });
        return new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    [Fact]
    public async Task CreateIssueAsync_Success_ReturnsHtmlUrl()
    {
        var expectedUrl = "https://github.com/test-owner/test-repo/issues/42";
        var response = CreateSuccessResponse(expectedUrl);
        var config = BuildConfig();
        var service = CreateService(response, config);

        var result = await service.CreateIssueAsync("Bug report", "Something is broken");

        result.Should().Be(expectedUrl);
    }

    [Fact]
    public async Task CreateIssueAsync_SendsCorrectPayload()
    {
        var response = CreateSuccessResponse("https://github.com/test-owner/test-repo/issues/1");
        var handler = new CapturingHttpMessageHandler(response);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.github.com")
        };
        var config = BuildConfig();
        var service = new GitHubIssueService(httpClient, config);

        await service.CreateIssueAsync("My Title", "My Body");

        handler.CapturedRequest.Should().NotBeNull();
        handler.CapturedRequest!.Method.Should().Be(HttpMethod.Post);
        handler.CapturedRequest.RequestUri!.PathAndQuery.Should().Be("/repos/test-owner/test-repo/issues");

        var body = await handler.CapturedRequest.Content!.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("title").GetString().Should().Be("My Title");
        doc.RootElement.GetProperty("body").GetString().Should().Be("My Body");
        doc.RootElement.GetProperty("labels").GetArrayLength().Should().Be(1);
        doc.RootElement.GetProperty("labels")[0].GetString().Should().Be("user-report");
    }

    [Fact]
    public async Task CreateIssueAsync_MissingRepoOwner_ThrowsInvalidOperationException()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            { "GitHub:RepoOwner", null }
        });
        var response = CreateSuccessResponse("https://github.com/x/y/issues/1");
        var service = CreateService(response, config);

        var act = () => service.CreateIssueAsync("Title", "Body");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*RepoOwner*");
    }

    [Fact]
    public async Task CreateIssueAsync_MissingRepoName_ThrowsInvalidOperationException()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            { "GitHub:RepoName", null }
        });
        var response = CreateSuccessResponse("https://github.com/x/y/issues/1");
        var service = CreateService(response, config);

        var act = () => service.CreateIssueAsync("Title", "Body");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*RepoName*");
    }

    [Fact]
    public async Task CreateIssueAsync_HttpError_ThrowsHttpRequestException()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"message\":\"Bad credentials\"}")
        };
        var config = BuildConfig();
        var service = CreateService(response, config);

        var act = () => service.CreateIssueAsync("Title", "Body");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task CreateIssueAsync_ServerError_ThrowsHttpRequestException()
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Internal Server Error")
        };
        var config = BuildConfig();
        var service = CreateService(response, config);

        var act = () => service.CreateIssueAsync("Title", "Body");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task CreateIssueAsync_RateLimited_ThrowsHttpRequestException()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"message\":\"API rate limit exceeded\"}")
        };
        var config = BuildConfig();
        var service = CreateService(response, config);

        var act = () => service.CreateIssueAsync("Title", "Body");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task CreateIssueAsync_CancellationRequested_ThrowsOperationCancelledException()
    {
        var response = CreateSuccessResponse("https://github.com/x/y/issues/1");
        var config = BuildConfig();
        var service = CreateService(response, config);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => service.CreateIssueAsync("Title", "Body", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(response);
        }
    }

    private class CapturingHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? CapturedRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Clone the content so we can read it later
            CapturedRequest = new HttpRequestMessage(request.Method, request.RequestUri);
            if (request.Content is not null)
            {
                var content = await request.Content.ReadAsStringAsync(cancellationToken);
                CapturedRequest.Content = new StringContent(content, Encoding.UTF8, "application/json");
            }
            return response;
        }
    }
}
