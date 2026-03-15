using System.Net;
using Codec.Api.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Codec.Api.IntegrationTests.Tests;

public class HealthEndpointTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task GetHealth_ReturnsOk()
    {
        var client = Factory.CreateClient();
        var response = await client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetRoot_InDevelopment_ReturnsOk()
    {
        var client = Factory.CreateClient();
        var response = await client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unauthenticated_ReturnsUnauthorized()
    {
        var client = Factory.CreateClient();
        var response = await client.GetAsync("/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Authenticated_GetMe_ReturnsOk()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
