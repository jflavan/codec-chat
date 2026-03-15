using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Codec.Api.IntegrationTests.Tests;

public class UserTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task GetMe_ReturnsUserProfile()
    {
        var client = CreateClient("ut-me", "TestMe");
        var response = await client.GetFromJsonAsync<JsonElement>("/me");

        response.GetProperty("user").GetProperty("displayName").GetString().Should().Be("TestMe");
    }

    [Fact]
    public async Task SetNickname_And_GetMe_ReflectsNickname()
    {
        var client = CreateClient("ut-nick", "NickUser");

        var response = await client.PutAsJsonAsync("/me/nickname", new { nickname = "CoolNick" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var me = await client.GetFromJsonAsync<JsonElement>("/me");
        me.GetProperty("user").GetProperty("nickname").GetString().Should().Be("CoolNick");
    }

    [Fact]
    public async Task RemoveNickname_ClearsIt()
    {
        var client = CreateClient("ut-remnick", "RemNickUser");

        await client.PutAsJsonAsync("/me/nickname", new { nickname = "TempNick" });
        var response = await client.DeleteAsync("/me/nickname");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RemoveNickname_WhenNone_Returns404()
    {
        var client = CreateClient("ut-nonick", "NoNickUser");

        var response = await client.DeleteAsync("/me/nickname");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SearchUsers_FindsByName()
    {
        // Create a searchable user
        var target = CreateClient("ut-searchable", "SearchableUser");
        await target.GetAsync("/me"); // ensure user exists

        var searcher = CreateClient("ut-searcher", "Searcher");
        var response = await searcher.GetFromJsonAsync<JsonElement>("/users/search?q=Searchable");
        response.EnumerateArray().Should().Contain(u =>
            u.GetProperty("displayName").GetString()!.Contains("Searchable"));
    }

    [Fact]
    public async Task SearchUsers_ShortQuery_ReturnsEmpty()
    {
        var client = CreateClient("ut-shortq", "ShortQuery");
        var response = await client.GetFromJsonAsync<JsonElement>("/users/search?q=a");
        response.EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task SetNickname_EmptyString_ReturnsBadRequest()
    {
        var client = CreateClient("ut-emptynick", "EmptyNick");
        var response = await client.PutAsJsonAsync("/me/nickname", new { nickname = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
