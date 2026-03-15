using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Codec.Api.IntegrationTests.Tests;

public class SearchTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task SearchServerMessages_FindsMessage()
    {
        var client = CreateClient("search-server", "ServerSearcher");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId);
        await PostMessageAsync(client, channelId, "unique searchable content xyz123");

        var response = await client.GetAsync($"/servers/{serverId}/search?q=xyz123");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SearchServerMessages_EmptyQuery_ReturnsBadRequest()
    {
        var client = CreateClient("search-empty", "EmptySearcher");
        var (serverId, _) = await CreateServerAsync(client);

        var response = await client.GetAsync($"/servers/{serverId}/search?q=");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SearchDmMessages_ReturnsResults()
    {
        var user1 = CreateClient("search-dm-1", "DmSearcher1");
        var user2 = CreateClient("search-dm-2", "DmSearcher2");
        await EstablishFriendshipAsync(user1, user2);

        var user2Id = await GetUserIdAsync(user2);
        var createResponse = await user1.PostAsJsonAsync("/dm/channels", new { recipientUserId = user2Id });
        var channelId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await user1.PostAsJsonAsync($"/dm/channels/{channelId}/messages", new { body = "searchable dm content abc789" });

        var response = await user1.GetAsync("/dm/search?q=abc789");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SearchServerMessages_WithChannelFilter()
    {
        var client = CreateClient("search-filter", "FilterSearcher");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId, "filtered-channel");
        await PostMessageAsync(client, channelId, "filtered message content");

        var response = await client.GetAsync($"/servers/{serverId}/search?q=filtered&channelId={channelId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
