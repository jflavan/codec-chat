using System.Net;
using Codec.Api.Services;

namespace Codec.Api.Tests;

public class DiscordRateLimitHandlerTests
{
    private static HttpClient CreateClient(HttpMessageHandler inner)
    {
        var handler = new DiscordRateLimitHandler { InnerHandler = inner };
        return new HttpClient(handler);
    }

    [Fact]
    public async Task SendAsync_NormalResponse_PassesThrough()
    {
        var inner = new MockHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(inner);
        var response = await client.GetAsync("https://discord.com/api/test");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task SendAsync_RateLimited_WaitsAndRetries()
    {
        var rateLimitResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        rateLimitResponse.Headers.TryAddWithoutValidation("Retry-After", "0.01");
        var okResponse = new HttpResponseMessage(HttpStatusCode.OK);
        var inner = new MockHandler(rateLimitResponse, okResponse);
        var client = CreateClient(inner);
        var response = await client.GetAsync("https://discord.com/api/test");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task SendAsync_RateLimitedExceedsMaxRetries_ReturnsLastResponse()
    {
        var responses = Enumerable.Range(0, 6)
            .Select(_ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                r.Headers.TryAddWithoutValidation("Retry-After", "0.01");
                return r;
            })
            .ToArray();
        var inner = new MockHandler(responses);
        var client = CreateClient(inner);
        var response = await client.GetAsync("https://discord.com/api/test");
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(6, inner.CallCount);
    }

    private class MockHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage[] _responses;
        private int _callIndex;
        public int CallCount => _callIndex;

        public MockHandler(params HttpResponseMessage[] responses) => _responses = responses;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var idx = Math.Min(_callIndex, _responses.Length - 1);
            _callIndex++;
            return Task.FromResult(_responses[idx]);
        }
    }
}
