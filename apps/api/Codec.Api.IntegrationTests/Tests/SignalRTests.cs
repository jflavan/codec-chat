using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;

namespace Codec.Api.IntegrationTests.Tests;

public class SignalRTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    private HubConnection CreateHubConnection(string googleSubject = "sr-user", string name = "HubUser")
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
    public async Task HubConnection_Succeeds()
    {
        var hub = CreateHubConnection("sr-connect", "Connector");
        await hub.StartAsync();
        hub.State.Should().Be(HubConnectionState.Connected);
        await hub.DisposeAsync();
    }

    [Fact]
    public async Task JoinChannel_And_ReceiveMessage()
    {
        // Set up a server + channel via HTTP
        var client = CreateClient("sr-msg-sender", "MsgSender");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId);

        // Create a hub connection that listens for messages
        var receivedMessages = new List<object>();
        var hub = CreateHubConnection("sr-msg-sender", "MsgSender");
        hub.On<object>("ReceiveMessage", msg => receivedMessages.Add(msg));

        await hub.StartAsync();
        await hub.InvokeAsync("JoinChannel", channelId.ToString());

        // Post a message via HTTP
        await PostMessageAsync(client, channelId, "Hello from test!");

        // Wait briefly for the SignalR message
        await Task.Delay(500);

        receivedMessages.Should().NotBeEmpty();
        await hub.DisposeAsync();
    }

    [Fact]
    public async Task StopTyping_Succeeds()
    {
        var client = CreateClient("sr-stoptyping", "StopTyper");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId);

        var hub = CreateHubConnection("sr-stoptyping", "StopTyper");
        await hub.StartAsync();
        await hub.InvokeAsync("JoinChannel", channelId.ToString());
        await hub.InvokeAsync("StartTyping", channelId.ToString(), "StopTyper");
        await hub.InvokeAsync("StopTyping", channelId.ToString(), "StopTyper");
        // No exception means success
        await hub.DisposeAsync();
    }

    [Fact]
    public async Task JoinServer_And_LeaveServer()
    {
        var hub = CreateHubConnection("sr-joinleave", "JoinLeaver");
        // Ensure user exists
        var client = CreateClient("sr-joinleave", "JoinLeaver");
        var (serverId, _) = await CreateServerAsync(client);

        await hub.StartAsync();
        await hub.InvokeAsync("JoinServer", serverId.ToString());
        await hub.InvokeAsync("LeaveServer", serverId.ToString());
        // No exceptions means success
        await hub.DisposeAsync();
    }

    [Fact]
    public async Task JoinDmChannel_And_LeaveDmChannel()
    {
        var user1 = CreateClient("sr-dm-1", "DmHub1");
        var user2 = CreateClient("sr-dm-2", "DmHub2");
        await EstablishFriendshipAsync(user1, user2);

        var user2Id = await GetUserIdAsync(user2);
        var createResponse = await user1.PostAsJsonAsync("/dm/channels", new { recipientUserId = user2Id });
        var channelId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var hub = CreateHubConnection("sr-dm-1", "DmHub1");
        await hub.StartAsync();
        await hub.InvokeAsync("JoinDmChannel", channelId.ToString());
        await hub.InvokeAsync("LeaveDmChannel", channelId.ToString());
        await hub.DisposeAsync();
    }

    [Fact]
    public async Task PresenceHeartbeat_Succeeds()
    {
        var client = CreateClient("sr-heartbeat", "HeartbeatUser");
        await client.GetAsync("/me"); // ensure user exists

        var hub = CreateHubConnection("sr-heartbeat", "HeartbeatUser");
        await hub.StartAsync();
        await hub.InvokeAsync("Heartbeat", true);
        // No exceptions means success
        await hub.DisposeAsync();
    }
}
