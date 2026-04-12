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

    [Fact]
    public async Task SendAsync_TracksBucketHeaders()
    {
        var response1 = new HttpResponseMessage(HttpStatusCode.OK);
        response1.Headers.TryAddWithoutValidation("X-RateLimit-Bucket", "test-bucket");
        response1.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
        response1.Headers.TryAddWithoutValidation("X-RateLimit-Reset-After", "0.5");

        var response2 = new HttpResponseMessage(HttpStatusCode.OK);

        var inner = new MockHandler(response1, response2);
        var client = CreateClient(inner);

        // First request should succeed and track the bucket
        var r1 = await client.GetAsync("https://discord.com/api/test1");
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        // Second request should also succeed (global limiter allows it)
        var r2 = await client.GetAsync("https://discord.com/api/test2");
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task SendAsync_GlobalLimiter_AllowsBurstUpTo50()
    {
        // Create 10 requests in rapid succession — all should succeed
        var responses = Enumerable.Range(0, 10)
            .Select(_ => new HttpResponseMessage(HttpStatusCode.OK))
            .ToArray();
        var inner = new MockHandler(responses);
        var client = CreateClient(inner);

        var tasks = Enumerable.Range(0, 10)
            .Select(i => client.GetAsync($"https://discord.com/api/test{i}"));

        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(10, inner.CallCount);
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
            var idx = Math.Min(Interlocked.Increment(ref _callIndex) - 1, _responses.Length - 1);
            return Task.FromResult(_responses[idx]);
        }
    }
}
