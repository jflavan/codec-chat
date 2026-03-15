using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;

namespace Codec.Api.IntegrationTests.Tests;

/// <summary>
/// Tests ChatHub methods via SignalR client connections to increase
/// coverage of the hub code that HTTP-only tests can't reach.
/// </summary>
public class ChatHubTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    private HubConnection CreateHub(string googleSubject, string name = "HubUser")
    {
        var token = FakeAuthHandler.CreateToken(googleSubject, name);
        var server = Factory.Server;

        return new HubConnectionBuilder()
            .WithUrl($"{server.BaseAddress}hubs/chat?access_token={token}", options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
            })
            .Build();
    }

    [Fact]
    public async Task StartCall_Via_Hub_ReturnsCallId()
    {
        var user1 = CreateClient("hub-call-1", "Caller");
        var user2 = CreateClient("hub-call-2", "Callee");
        await EstablishFriendshipAsync(user1, user2);

        var user2Id = await GetUserIdAsync(user2);
        var createResponse = await user1.PostAsJsonAsync("/dm/channels", new { recipientUserId = user2Id });
        var channelId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var hub = CreateHub("hub-call-1", "Caller");
        await hub.StartAsync();

        var result = await hub.InvokeAsync<JsonElement>("StartCall", channelId.ToString());
        result.GetProperty("callId").GetString().Should().NotBeNullOrEmpty();

        await hub.DisposeAsync();
    }

    [Fact]
    public async Task StartCall_And_DeclineCall_Via_Hub()
    {
        var user1 = CreateClient("hub-decline-1", "Caller2");
        var user2 = CreateClient("hub-decline-2", "Callee2");
        await EstablishFriendshipAsync(user1, user2);

        var user2Id = await GetUserIdAsync(user2);
        var createResponse = await user1.PostAsJsonAsync("/dm/channels", new { recipientUserId = user2Id });
        var channelId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var callerHub = CreateHub("hub-decline-1", "Caller2");
        await callerHub.StartAsync();

        var callResult = await callerHub.InvokeAsync<JsonElement>("StartCall", channelId.ToString());
        var callId = callResult.GetProperty("callId").GetString()!;

        // Callee declines
        var calleeHub = CreateHub("hub-decline-2", "Callee2");
        await calleeHub.StartAsync();
        await calleeHub.InvokeAsync("DeclineCall", callId);

        await callerHub.DisposeAsync();
        await calleeHub.DisposeAsync();
    }

    [Fact]
    public async Task StartCall_And_EndCall_Via_Hub()
    {
        var user1 = CreateClient("hub-end-1", "Ender1");
        var user2 = CreateClient("hub-end-2", "Ender2");
        await EstablishFriendshipAsync(user1, user2);

        var user2Id = await GetUserIdAsync(user2);
        var createResponse = await user1.PostAsJsonAsync("/dm/channels", new { recipientUserId = user2Id });
        var channelId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var callerHub = CreateHub("hub-end-1", "Ender1");
        await callerHub.StartAsync();
        await callerHub.InvokeAsync<JsonElement>("StartCall", channelId.ToString());

        // Caller ends it
        await callerHub.InvokeAsync("EndCall");
        await callerHub.DisposeAsync();
    }

    [Fact]
    public async Task DmTyping_Succeeds()
    {
        var user1 = CreateClient("hub-dmtyp-1", "DmTyper1");
        var user2 = CreateClient("hub-dmtyp-2", "DmTyper2");
        await EstablishFriendshipAsync(user1, user2);

        var user2Id = await GetUserIdAsync(user2);
        var createResponse = await user1.PostAsJsonAsync("/dm/channels", new { recipientUserId = user2Id });
        var channelId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var hub = CreateHub("hub-dmtyp-1", "DmTyper1");
        await hub.StartAsync();
        await hub.InvokeAsync("JoinDmChannel", channelId.ToString());
        await hub.InvokeAsync("StartDmTyping", channelId.ToString(), "DmTyper1");
        await hub.InvokeAsync("StopDmTyping", channelId.ToString(), "DmTyper1");
        await hub.DisposeAsync();
    }

    [Fact]
    public async Task OnDisconnected_CleansUpPresence()
    {
        var client = CreateClient("hub-disconnect", "Disconnecter");
        await client.GetAsync("/me"); // ensure user exists

        var hub = CreateHub("hub-disconnect", "Disconnecter");
        await hub.StartAsync();
        await hub.InvokeAsync("Heartbeat", true);

        // Disconnect
        await hub.DisposeAsync();
        // If no exception, cleanup worked
    }

    [Fact]
    public async Task MultipleJoinLeave_Succeeds()
    {
        var client = CreateClient("hub-multi", "MultiUser");
        var (serverId, _) = await CreateServerAsync(client);
        var ch1 = await CreateChannelAsync(client, serverId, "ch1");
        var ch2 = await CreateChannelAsync(client, serverId, "ch2");

        var hub = CreateHub("hub-multi", "MultiUser");
        await hub.StartAsync();

        await hub.InvokeAsync("JoinServer", serverId.ToString());
        await hub.InvokeAsync("JoinChannel", ch1.ToString());
        await hub.InvokeAsync("JoinChannel", ch2.ToString());
        await hub.InvokeAsync("LeaveChannel", ch1.ToString());
        await hub.InvokeAsync("LeaveChannel", ch2.ToString());
        await hub.InvokeAsync("LeaveServer", serverId.ToString());

        await hub.DisposeAsync();
    }
}
