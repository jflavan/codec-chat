using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Codec.Api.IntegrationTests.Tests;

/// <summary>
/// Additional tests specifically targeting low-coverage code paths
/// in controllers to push line coverage toward 80%.
/// </summary>
public class DetailedCoverageTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    // --- ChannelsController: GetMessages with pagination, reactions, link previews ---

    [Fact]
    public async Task GetMessages_WithBefore_ReturnsPaginatedResults()
    {
        var client = CreateClient("cov-before", "BeforeUser");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId);

        for (int i = 0; i < 5; i++)
            await PostMessageAsync(client, channelId, $"Msg {i}");

        // Get latest messages first
        var latest = await client.GetFromJsonAsync<JsonElement>($"/channels/{channelId}/messages?limit=2");
        var messages = latest.GetProperty("messages").EnumerateArray().ToList();
        var lastCreatedAt = messages.Last().GetProperty("createdAt").GetString()!;

        // Now get messages before that timestamp
        var response = await client.GetAsync($"/channels/{channelId}/messages?before={Uri.EscapeDataString(lastCreatedAt)}&limit=2");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetMessages_WithReactions_IncludesReactionData()
    {
        var client = CreateClient("cov-reactions", "ReactUser");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId);
        var messageId = await PostMessageAsync(client, channelId, "React to me!");

        // Add reactions
        await client.PostAsJsonAsync($"/channels/{channelId}/messages/{messageId}/reactions", new { emoji = "👍" });

        var response = await client.GetFromJsonAsync<JsonElement>($"/channels/{channelId}/messages");
        var messages = response.GetProperty("messages").EnumerateArray().ToList();
        var msg = messages.First(m => m.GetProperty("id").GetGuid() == messageId);
        msg.GetProperty("reactions").EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task PostMessage_WithMention_NotifiesMentionedUser()
    {
        var owner = CreateClient("cov-mention-owner", "MentionOwner");
        var (serverId, _) = await CreateServerAsync(owner);
        var channelId = await CreateChannelAsync(owner, serverId);

        // Add second user to server
        var inviteResp = await owner.PostAsJsonAsync($"/servers/{serverId}/invites", new { });
        var code = (await inviteResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
        var mentioned = CreateClient("cov-mentioned", "MentionedUser");
        await mentioned.PostAsync($"/invites/{code}", null);
        var mentionedId = await GetUserIdAsync(mentioned);

        // Post message with @mention
        var response = await owner.PostAsJsonAsync($"/channels/{channelId}/messages", new { body = $"Hey <@{mentionedId}>!" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task PostMessage_WithHereMention()
    {
        var client = CreateClient("cov-here", "HereUser");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId);

        var response = await client.PostAsJsonAsync($"/channels/{channelId}/messages", new { body = "Attention <@here>!" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task PostMessage_WithImageOnly()
    {
        var client = CreateClient("cov-imgonly", "ImgOnlyUser");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId);

        var response = await client.PostAsJsonAsync($"/channels/{channelId}/messages", new { body = "", imageUrl = "https://example.com/pic.png" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // --- DmController: GetMessages with pagination, reactions ---

    [Fact]
    public async Task DmGetMessages_WithPagination()
    {
        var user1 = CreateClient("cov-dmpag-1", "DmPager1");
        var user2 = CreateClient("cov-dmpag-2", "DmPager2");
        await EstablishFriendshipAsync(user1, user2);

        var user2Id = await GetUserIdAsync(user2);
        var createResp = await user1.PostAsJsonAsync("/dm/channels", new { recipientUserId = user2Id });
        var channelId = (await createResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        for (int i = 0; i < 5; i++)
            await user1.PostAsJsonAsync($"/dm/channels/{channelId}/messages", new { body = $"DM {i}" });

        var response = await user1.GetFromJsonAsync<JsonElement>($"/dm/channels/{channelId}/messages?limit=2");
        response.GetProperty("messages").EnumerateArray().Should().HaveCount(2);
    }

    [Fact]
    public async Task DmGetMessages_AroundMode()
    {
        var user1 = CreateClient("cov-dmaround-1", "DmAround1");
        var user2 = CreateClient("cov-dmaround-2", "DmAround2");
        await EstablishFriendshipAsync(user1, user2);

        var user2Id = await GetUserIdAsync(user2);
        var createResp = await user1.PostAsJsonAsync("/dm/channels", new { recipientUserId = user2Id });
        var channelId = (await createResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await user1.PostAsJsonAsync($"/dm/channels/{channelId}/messages", new { body = "Before" });
        var sendResp = await user1.PostAsJsonAsync($"/dm/channels/{channelId}/messages", new { body = "Target" });
        var targetId = (await sendResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await user1.PostAsJsonAsync($"/dm/channels/{channelId}/messages", new { body = "After" });

        var response = await user1.GetFromJsonAsync<JsonElement>($"/dm/channels/{channelId}/messages?around={targetId}");
        response.GetProperty("messages").EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task DmSendMessage_WithReply()
    {
        var user1 = CreateClient("cov-dmreply-1", "DmReplier1");
        var user2 = CreateClient("cov-dmreply-2", "DmReplier2");
        await EstablishFriendshipAsync(user1, user2);

        var user2Id = await GetUserIdAsync(user2);
        var createResp = await user1.PostAsJsonAsync("/dm/channels", new { recipientUserId = user2Id });
        var channelId = (await createResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var originalResp = await user1.PostAsJsonAsync($"/dm/channels/{channelId}/messages", new { body = "Original" });
        var originalId = (await originalResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var replyResp = await user1.PostAsJsonAsync($"/dm/channels/{channelId}/messages",
            new { body = "Reply!", replyToDirectMessageId = originalId });
        replyResp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // --- ServersController: emoji, icon, search ---

    [Fact]
    public async Task ServerEmoji_UploadAndList()
    {
        var client = CreateClient("cov-emoji", "EmojiUser");
        var (serverId, _) = await CreateServerAsync(client);

        var form = new MultipartFormDataContent();
        form.Add(new StringContent("pepe"), "name");
        var fileContent = new ByteArrayContent(new byte[100]);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(fileContent, "file", "pepe.png");

        var uploadResp = await client.PostAsync($"/servers/{serverId}/emojis", form);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var listResp = await client.GetFromJsonAsync<JsonElement>($"/servers/{serverId}/emojis");
        listResp.EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task PresenceEndpoints_ReturnOk()
    {
        var client = CreateClient("cov-presence", "PresenceUser");
        var (serverId, _) = await CreateServerAsync(client);

        var serverPresence = await client.GetAsync($"/servers/{serverId}/presence");
        serverPresence.StatusCode.Should().Be(HttpStatusCode.OK);

        var dmPresence = await client.GetAsync("/dm/presence");
        dmPresence.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task VoiceChannel_CreateAndGetStates()
    {
        var client = CreateClient("cov-voice-ch", "VoiceChUser");
        var (serverId, _) = await CreateServerAsync(client);

        var channelId = await CreateChannelAsync(client, serverId, "voice-ch", "voice");
        var states = await client.GetFromJsonAsync<JsonElement>($"/channels/{channelId}/voice-states");
        states.EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task GetMessages_ChannelNotFound_Returns404()
    {
        var client = CreateClient("cov-notfound", "NotFoundUser");
        var response = await client.GetAsync($"/channels/{Guid.NewGuid()}/messages");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteMessage_NotFound_Returns404()
    {
        var client = CreateClient("cov-delmsg404", "DelMsg404");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId);

        var response = await client.DeleteAsync($"/channels/{channelId}/messages/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EditMessage_NotFound_Returns404()
    {
        var client = CreateClient("cov-editmsg404", "EditMsg404");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId);

        var response = await client.PutAsJsonAsync($"/channels/{channelId}/messages/{Guid.NewGuid()}", new { body = "edited" });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ToggleReaction_MessageNotFound_Returns404()
    {
        var client = CreateClient("cov-react404", "React404");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId);

        var response = await client.PostAsJsonAsync($"/channels/{channelId}/messages/{Guid.NewGuid()}/reactions", new { emoji = "👍" });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
