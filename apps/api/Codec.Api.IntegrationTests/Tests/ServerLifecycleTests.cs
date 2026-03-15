using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Codec.Api.IntegrationTests.Tests;

public class ServerLifecycleTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task CreateServer_ReturnsCreated()
    {
        var client = CreateClient("sl-create", "Creator");
        var (serverId, response) = await CreateServerAsync(client, "My Server");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        serverId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetMyServers_IncludesCreatedServer()
    {
        var client = CreateClient("sl-list", "Lister");
        await CreateServerAsync(client, "Listed Server");

        var response = await client.GetFromJsonAsync<JsonElement>("/servers");
        var servers = response.EnumerateArray().ToList();
        servers.Should().Contain(s => s.GetProperty("name").GetString() == "Listed Server");
    }

    [Fact]
    public async Task UpdateServer_ChangesName()
    {
        var client = CreateClient("sl-update", "Updater");
        var (serverId, _) = await CreateServerAsync(client, "OldName");

        var response = await client.PatchAsJsonAsync($"/servers/{serverId}", new { name = "NewName" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("name").GetString().Should().Be("NewName");
    }

    [Fact]
    public async Task DeleteServer_RemovesIt()
    {
        var client = CreateClient("sl-delete", "Deleter");
        var (serverId, _) = await CreateServerAsync(client, "ToDelete");

        var response = await client.DeleteAsync($"/servers/{serverId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify it's gone
        var listResponse = await client.GetFromJsonAsync<JsonElement>("/servers");
        listResponse.EnumerateArray().Should().NotContain(s =>
            s.GetProperty("name").GetString() == "ToDelete");
    }

    [Fact]
    public async Task CreateChannel_And_GetChannels()
    {
        var client = CreateClient("sl-channel", "ChannelMaker");
        var (serverId, _) = await CreateServerAsync(client);

        var channelId = await CreateChannelAsync(client, serverId, "new-channel");
        channelId.Should().NotBeEmpty();

        var channelsResponse = await client.GetFromJsonAsync<JsonElement>($"/servers/{serverId}/channels");
        channelsResponse.EnumerateArray().Should().Contain(c =>
            c.GetProperty("name").GetString() == "new-channel");
    }

    [Fact]
    public async Task DeleteChannel_RemovesIt()
    {
        var client = CreateClient("sl-delchan", "ChanDeleter");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId, "to-delete");

        var response = await client.DeleteAsync($"/servers/{serverId}/channels/{channelId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UpdateChannel_RenamesIt()
    {
        var client = CreateClient("sl-renchan", "Renamer");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId, "old-name");

        var response = await client.PatchAsJsonAsync($"/servers/{serverId}/channels/{channelId}", new { name = "new-name" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetMembers_IncludesOwner()
    {
        var client = CreateClient("sl-members", "MemberLister");
        var (serverId, _) = await CreateServerAsync(client);

        var members = await client.GetFromJsonAsync<JsonElement>($"/servers/{serverId}/members");
        members.EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task ReorderServers_Succeeds()
    {
        var client = CreateClient("sl-reorder", "Reorderer");
        var (id1, _) = await CreateServerAsync(client, "Server A");
        var (id2, _) = await CreateServerAsync(client, "Server B");

        var response = await client.PutAsJsonAsync("/servers/reorder", new { serverIds = new[] { id2, id1 } });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
