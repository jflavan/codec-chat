using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Codec.Api.IntegrationTests.Tests;

public class VoiceStateTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task GetToken_ReturnsLiveKitToken()
    {
        var client = CreateClient("vs-token", "TokenUser");
        var response = await client.GetFromJsonAsync<JsonElement>("/voice/token?roomName=test-room");

        response.GetProperty("token").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetToken_MissingRoomName_ReturnsBadRequest()
    {
        var client = CreateClient("vs-token-bad", "TokenBadUser");
        var response = await client.GetAsync("/voice/token");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetActiveCall_NoCall_Returns204()
    {
        var client = CreateClient("vs-nocall", "NoCallUser");
        var response = await client.GetAsync("/voice/active-call");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetVoiceStates_VoiceChannel_ReturnsEmpty()
    {
        var client = CreateClient("vs-states", "VoiceUser");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId, "voice-test", "voice");

        var response = await client.GetFromJsonAsync<JsonElement>($"/channels/{channelId}/voice-states");
        response.EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task GetVoiceStates_TextChannel_ReturnsBadRequest()
    {
        var client = CreateClient("vs-text", "TextUser");
        var (serverId, _) = await CreateServerAsync(client);
        var channelId = await CreateChannelAsync(client, serverId, "text-test", "text");

        var response = await client.GetAsync($"/channels/{channelId}/voice-states");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateVoiceState_NotInVoice_ReturnsBadRequest()
    {
        var client = CreateClient("vs-notinvoice", "NotInVoice");

        var response = await client.PatchAsJsonAsync("/voice/state", new { isMuted = true, isDeafened = false });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
