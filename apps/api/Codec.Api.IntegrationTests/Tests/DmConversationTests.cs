using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Codec.Api.IntegrationTests.Tests;

public class DmConversationTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task CreateDm_RequiresFriendship()
    {
        var user1 = CreateClient("dm-nofriend-1", "User1");
        var user2 = CreateClient("dm-nofriend-2", "User2");
        var user2Id = await GetUserIdAsync(user2);

        var response = await user1.PostAsJsonAsync("/dm/channels", new { recipientUserId = user2Id });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateDm_WithFriend_Succeeds()
    {
        var user1 = CreateClient("dm-friend-1", "DmUser1");
        var user2 = CreateClient("dm-friend-2", "DmUser2");
        await EstablishFriendshipAsync(user1, user2);

        var user2Id = await GetUserIdAsync(user2);
        var response = await user1.PostAsJsonAsync("/dm/channels", new { recipientUserId = user2Id });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateDm_Existing_ReturnsOk()
    {
        var user1 = CreateClient("dm-existing-1", "ExistUser1");
        var user2 = CreateClient("dm-existing-2", "ExistUser2");
        await EstablishFriendshipAsync(user1, user2);

        var user2Id = await GetUserIdAsync(user2);

        // First creates
        await user1.PostAsJsonAsync("/dm/channels", new { recipientUserId = user2Id });
        // Second returns existing
        var response = await user1.PostAsJsonAsync("/dm/channels", new { recipientUserId = user2Id });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateDm_Self_ReturnsBadRequest()
    {
        var user = CreateClient("dm-self", "SelfDm");
        var userId = await GetUserIdAsync(user);

        var response = await user.PostAsJsonAsync("/dm/channels", new { recipientUserId = userId });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SendDm_And_GetMessages()
    {
        var user1 = CreateClient("dm-msg-1", "MsgUser1");
        var user2 = CreateClient("dm-msg-2", "MsgUser2");
        await EstablishFriendshipAsync(user1, user2);

        var user2Id = await GetUserIdAsync(user2);
        var createResponse = await user1.PostAsJsonAsync("/dm/channels", new { recipientUserId = user2Id });
        var channelId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Send a message
        var sendResponse = await user1.PostAsJsonAsync($"/dm/channels/{channelId}/messages", new { body = "Hey!" });
        sendResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Get messages
        var messagesResponse = await user1.GetFromJsonAsync<JsonElement>($"/dm/channels/{channelId}/messages");
        messagesResponse.GetProperty("messages").EnumerateArray().Should().Contain(m =>
            m.GetProperty("body").GetString() == "Hey!");
    }

    [Fact]
    public async Task EditDm_UpdatesBody()
    {
        var user1 = CreateClient("dm-edit-1", "EditUser1");
        var user2 = CreateClient("dm-edit-2", "EditUser2");
        await EstablishFriendshipAsync(user1, user2);

        var user2Id = await GetUserIdAsync(user2);
        var createResponse = await user1.PostAsJsonAsync("/dm/channels", new { recipientUserId = user2Id });
        var channelId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var sendResponse = await user1.PostAsJsonAsync($"/dm/channels/{channelId}/messages", new { body = "Original" });
        var messageId = (await sendResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var editResponse = await user1.PutAsJsonAsync($"/dm/channels/{channelId}/messages/{messageId}", new { body = "Edited" });
        editResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteDm_RemovesMessage()
    {
        var user1 = CreateClient("dm-del-1", "DelUser1");
        var user2 = CreateClient("dm-del-2", "DelUser2");
        await EstablishFriendshipAsync(user1, user2);

        var user2Id = await GetUserIdAsync(user2);
        var createResponse = await user1.PostAsJsonAsync("/dm/channels", new { recipientUserId = user2Id });
        var channelId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var sendResponse = await user1.PostAsJsonAsync($"/dm/channels/{channelId}/messages", new { body = "Delete me" });
        var messageId = (await sendResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var deleteResponse = await user1.DeleteAsync($"/dm/channels/{channelId}/messages/{messageId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CloseDm_HidesConversation()
    {
        var user1 = CreateClient("dm-close-1", "CloseUser1");
        var user2 = CreateClient("dm-close-2", "CloseUser2");
        await EstablishFriendshipAsync(user1, user2);

        var user2Id = await GetUserIdAsync(user2);
        var createResponse = await user1.PostAsJsonAsync("/dm/channels", new { recipientUserId = user2Id });
        var channelId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var closeResponse = await user1.DeleteAsync($"/dm/channels/{channelId}");
        closeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ListDmChannels_ReturnsOpenConversations()
    {
        var user1 = CreateClient("dm-list-1", "ListUser1");
        var user2 = CreateClient("dm-list-2", "ListUser2");
        await EstablishFriendshipAsync(user1, user2);

        var user2Id = await GetUserIdAsync(user2);
        await user1.PostAsJsonAsync("/dm/channels", new { recipientUserId = user2Id });

        var response = await user1.GetFromJsonAsync<JsonElement>("/dm/channels");
        response.EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task DmReaction_TogglesEmoji()
    {
        var user1 = CreateClient("dm-react-1", "ReactUser1");
        var user2 = CreateClient("dm-react-2", "ReactUser2");
        await EstablishFriendshipAsync(user1, user2);

        var user2Id = await GetUserIdAsync(user2);
        var createResponse = await user1.PostAsJsonAsync("/dm/channels", new { recipientUserId = user2Id });
        var channelId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var sendResponse = await user1.PostAsJsonAsync($"/dm/channels/{channelId}/messages", new { body = "React!" });
        var messageId = (await sendResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var reactResponse = await user1.PostAsJsonAsync($"/dm/channels/{channelId}/messages/{messageId}/reactions", new { emoji = "❤️" });
        reactResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
