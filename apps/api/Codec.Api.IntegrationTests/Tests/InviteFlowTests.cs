using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Codec.Api.IntegrationTests.Tests;

public class InviteFlowTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task CreateInvite_ReturnsCode()
    {
        var client = CreateClient("if-create", "InviteCreator");
        var (serverId, _) = await CreateServerAsync(client);

        var response = await client.PostAsJsonAsync($"/servers/{serverId}/invites", new { });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task JoinViaInvite_NewMember_Succeeds()
    {
        var owner = CreateClient("if-owner", "Owner");
        var (serverId, _) = await CreateServerAsync(owner, "Invite Server");

        var inviteResponse = await owner.PostAsJsonAsync($"/servers/{serverId}/invites", new { });
        var invite = await inviteResponse.Content.ReadFromJsonAsync<JsonElement>();
        var code = invite.GetProperty("code").GetString()!;

        var joiner = CreateClient("if-joiner", "Joiner");
        var joinResponse = await joiner.PostAsync($"/invites/{code}", null);
        joinResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Verify they can see the server now
        var servers = await joiner.GetFromJsonAsync<JsonElement>("/servers");
        servers.EnumerateArray().Should().Contain(s =>
            s.GetProperty("name").GetString() == "Invite Server");
    }

    [Fact]
    public async Task JoinViaInvite_AlreadyMember_ReturnsOk()
    {
        var client = CreateClient("if-already", "AlreadyMember");
        var (serverId, _) = await CreateServerAsync(client);

        var inviteResponse = await client.PostAsJsonAsync($"/servers/{serverId}/invites", new { });
        var invite = await inviteResponse.Content.ReadFromJsonAsync<JsonElement>();
        var code = invite.GetProperty("code").GetString()!;

        var response = await client.PostAsync($"/invites/{code}", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task JoinViaInvite_InvalidCode_Returns404()
    {
        var client = CreateClient("if-invalid", "InvalidInvite");
        var response = await client.PostAsync("/invites/nonexistent-code", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetInvites_ReturnsCreatedInvites()
    {
        var client = CreateClient("if-list", "InviteLister");
        var (serverId, _) = await CreateServerAsync(client);

        await client.PostAsJsonAsync($"/servers/{serverId}/invites", new { });

        var response = await client.GetFromJsonAsync<JsonElement>($"/servers/{serverId}/invites");
        response.EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task RevokeInvite_RemovesIt()
    {
        var client = CreateClient("if-revoke", "InviteRevoker");
        var (serverId, _) = await CreateServerAsync(client);

        var createResponse = await client.PostAsJsonAsync($"/servers/{serverId}/invites", new { });
        var invite = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var inviteId = invite.GetProperty("id").GetGuid();

        var revokeResponse = await client.DeleteAsync($"/servers/{serverId}/invites/{inviteId}");
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CreateInvite_WithMaxUses()
    {
        var client = CreateClient("if-maxuses", "MaxUsesCreator");
        var (serverId, _) = await CreateServerAsync(client);

        var response = await client.PostAsJsonAsync($"/servers/{serverId}/invites", new { maxUses = 5, expiresInHours = 24 });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("maxUses").GetInt32().Should().Be(5);
    }
}
