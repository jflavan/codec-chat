using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Codec.Api.IntegrationTests.Tests;

public class FriendshipTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task SendFriendRequest_Succeeds()
    {
        var user1 = CreateClient("fr-send-1", "Sender");
        var user2 = CreateClient("fr-send-2", "Receiver");
        var user2Id = await GetUserIdAsync(user2);

        var response = await user1.PostAsJsonAsync("/friends/requests", new { recipientUserId = user2Id });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task SendFriendRequest_Self_ReturnsBadRequest()
    {
        var user = CreateClient("fr-self", "SelfFriender");
        var userId = await GetUserIdAsync(user);

        var response = await user.PostAsJsonAsync("/friends/requests", new { recipientUserId = userId });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SendFriendRequest_Duplicate_ReturnsConflict()
    {
        var user1 = CreateClient("fr-dup-1", "DupSender");
        var user2 = CreateClient("fr-dup-2", "DupReceiver");
        var user2Id = await GetUserIdAsync(user2);

        await user1.PostAsJsonAsync("/friends/requests", new { recipientUserId = user2Id });
        var response = await user1.PostAsJsonAsync("/friends/requests", new { recipientUserId = user2Id });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AcceptFriendRequest_CreatesFriendship()
    {
        var user1 = CreateClient("fr-accept-1", "Accepter1");
        var user2 = CreateClient("fr-accept-2", "Accepter2");
        await EstablishFriendshipAsync(user1, user2);

        var friends = await user1.GetFromJsonAsync<JsonElement>("/friends");
        friends.EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task DeclineFriendRequest_Succeeds()
    {
        var user1 = CreateClient("fr-decline-1", "Decliner1");
        var user2 = CreateClient("fr-decline-2", "Decliner2");
        var user2Id = await GetUserIdAsync(user2);

        var sendResponse = await user1.PostAsJsonAsync("/friends/requests", new { recipientUserId = user2Id });
        var requestId = (await sendResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await user2.PutAsJsonAsync($"/friends/requests/{requestId}", new { action = "decline" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CancelFriendRequest_Succeeds()
    {
        var user1 = CreateClient("fr-cancel-1", "Canceler1");
        var user2 = CreateClient("fr-cancel-2", "Canceler2");
        var user2Id = await GetUserIdAsync(user2);

        var sendResponse = await user1.PostAsJsonAsync("/friends/requests", new { recipientUserId = user2Id });
        var requestId = (await sendResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await user1.DeleteAsync($"/friends/requests/{requestId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RemoveFriend_DeletesFriendship()
    {
        var user1 = CreateClient("fr-remove-1", "Remover1");
        var user2 = CreateClient("fr-remove-2", "Remover2");
        await EstablishFriendshipAsync(user1, user2);

        var friends = await user1.GetFromJsonAsync<JsonElement>("/friends");
        var friendshipId = friends.EnumerateArray().First().GetProperty("friendshipId").GetGuid();

        var response = await user1.DeleteAsync($"/friends/{friendshipId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetFriendRequests_Received()
    {
        var user1 = CreateClient("fr-recv-1", "RecvSender");
        var user2 = CreateClient("fr-recv-2", "RecvTarget");
        var user2Id = await GetUserIdAsync(user2);

        await user1.PostAsJsonAsync("/friends/requests", new { recipientUserId = user2Id });

        var response = await user2.GetFromJsonAsync<JsonElement>("/friends/requests");
        response.EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetFriendRequests_Sent()
    {
        var user1 = CreateClient("fr-sent-1", "SentSender");
        var user2 = CreateClient("fr-sent-2", "SentTarget");
        var user2Id = await GetUserIdAsync(user2);

        await user1.PostAsJsonAsync("/friends/requests", new { recipientUserId = user2Id });

        var response = await user1.GetFromJsonAsync<JsonElement>("/friends/requests?direction=sent");
        response.EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task RespondToRequest_InvalidAction_ReturnsBadRequest()
    {
        var user1 = CreateClient("fr-badaction-1", "BadAction1");
        var user2 = CreateClient("fr-badaction-2", "BadAction2");
        var user2Id = await GetUserIdAsync(user2);

        var sendResponse = await user1.PostAsJsonAsync("/friends/requests", new { recipientUserId = user2Id });
        var requestId = (await sendResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await user2.PutAsJsonAsync($"/friends/requests/{requestId}", new { action = "maybe" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
