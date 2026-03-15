using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Codec.Api.IntegrationTests.Tests;

public class ChannelMessagingTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task PostMessage_ReturnsCreated()
    {
        var client = CreateClient("cm-post", "Poster");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId);

        var response = await client.PostAsJsonAsync($"/channels/{channelId}/messages", new { body = "Hello!" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task GetMessages_ReturnsPostedMessage()
    {
        var client = CreateClient("cm-get", "Getter");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId);

        await PostMessageAsync(client, channelId, "Test message");

        var response = await client.GetFromJsonAsync<JsonElement>($"/channels/{channelId}/messages");
        var messages = response.GetProperty("messages").EnumerateArray().ToList();
        messages.Should().Contain(m => m.GetProperty("body").GetString() == "Test message");
    }

    [Fact]
    public async Task EditMessage_UpdatesBody()
    {
        var client = CreateClient("cm-edit", "Editor");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId);
        var messageId = await PostMessageAsync(client, channelId, "Original");

        var response = await client.PutAsJsonAsync($"/channels/{channelId}/messages/{messageId}", new { body = "Edited" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("body").GetString().Should().Be("Edited");
    }

    [Fact]
    public async Task DeleteMessage_RemovesIt()
    {
        var client = CreateClient("cm-del", "Deleter");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId);
        var messageId = await PostMessageAsync(client, channelId, "Delete me");

        var response = await client.DeleteAsync($"/channels/{channelId}/messages/{messageId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ToggleReaction_AddsAndRemoves()
    {
        var client = CreateClient("cm-react", "Reactor");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId);
        var messageId = await PostMessageAsync(client, channelId, "React to me");

        // Add reaction
        var addResponse = await client.PostAsJsonAsync($"/channels/{channelId}/messages/{messageId}/reactions", new { emoji = "👍" });
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var addBody = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
        addBody.GetProperty("action").GetString().Should().Be("added");

        // Remove reaction (toggle)
        var removeResponse = await client.PostAsJsonAsync($"/channels/{channelId}/messages/{messageId}/reactions", new { emoji = "👍" });
        var removeBody = await removeResponse.Content.ReadFromJsonAsync<JsonElement>();
        removeBody.GetProperty("action").GetString().Should().Be("removed");
    }

    [Fact]
    public async Task PostMessage_EmptyBody_ReturnsBadRequest()
    {
        var client = CreateClient("cm-empty", "EmptyPoster");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId);

        var response = await client.PostAsJsonAsync($"/channels/{channelId}/messages", new { body = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostMessage_WithReply_ReturnsCreated()
    {
        var client = CreateClient("cm-reply", "Replier");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId);
        var originalId = await PostMessageAsync(client, channelId, "Original message");

        var response = await client.PostAsJsonAsync($"/channels/{channelId}/messages",
            new { body = "Reply!", replyToMessageId = originalId });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task GetMessages_WithPagination()
    {
        var client = CreateClient("cm-page", "Paginator");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId);

        // Post a few messages
        for (int i = 0; i < 5; i++)
            await PostMessageAsync(client, channelId, $"Message {i}");

        var response = await client.GetFromJsonAsync<JsonElement>($"/channels/{channelId}/messages?limit=3");
        var messages = response.GetProperty("messages").EnumerateArray().ToList();
        messages.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetMessages_AroundMode()
    {
        var client = CreateClient("cm-around", "Arounder");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId);

        await PostMessageAsync(client, channelId, "Before");
        var targetId = await PostMessageAsync(client, channelId, "Target");
        await PostMessageAsync(client, channelId, "After");

        var response = await client.GetFromJsonAsync<JsonElement>($"/channels/{channelId}/messages?around={targetId}");
        response.GetProperty("messages").EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task PurgeChannel_AsGlobalAdmin_Succeeds()
    {
        // Create a user and make them global admin via DB
        var client = CreateClient("cm-purge-admin", "PurgeAdmin");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId);
        await PostMessageAsync(client, channelId, "To purge");

        // Make user global admin
        await WithDbAsync(async db =>
        {
            var user = db.Users.First(u => u.GoogleSubject == "cm-purge-admin");
            user.IsGlobalAdmin = true;
            await db.SaveChangesAsync();
        });

        var response = await client.DeleteAsync($"/channels/{channelId}/messages");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task PurgeChannel_AsNonAdmin_Returns403()
    {
        var client = CreateClient("cm-purge-nonadmin", "NonAdmin");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId);

        var response = await client.DeleteAsync($"/channels/{channelId}/messages");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteMessage_OtherUsersMessage_Returns403()
    {
        var owner = CreateClient("cm-owner-del", "Owner");
        var (serverId, _) = await CreateServerAsync(owner);
        var channelId = await CreateChannelAsync(owner, serverId);
        var messageId = await PostMessageAsync(owner, channelId, "Owner's message");

        // Add a second user to the server via invite
        var other = CreateClient("cm-other-del", "Other");
        var inviteResponse = await owner.PostAsJsonAsync($"/servers/{serverId}/invites", new { });
        inviteResponse.EnsureSuccessStatusCode();
        var invite = await inviteResponse.Content.ReadFromJsonAsync<JsonElement>();
        var code = invite.GetProperty("code").GetString();
        await other.PostAsync($"/invites/{code}", null);

        // Other tries to delete owner's message
        var deleteResponse = await other.DeleteAsync($"/channels/{channelId}/messages/{messageId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
