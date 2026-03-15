using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Codec.Api.IntegrationTests.Tests;

public class MemberManagementTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    private async Task<(Guid ServerId, string InviteCode)> CreateServerWithInviteAsync(HttpClient owner, string name = "Test Server")
    {
        var (serverId, _) = await CreateServerAsync(owner, name);
        var inviteResponse = await owner.PostAsJsonAsync($"/servers/{serverId}/invites", new { });
        inviteResponse.EnsureSuccessStatusCode();
        var invite = await inviteResponse.Content.ReadFromJsonAsync<JsonElement>();
        return (serverId, invite.GetProperty("code").GetString()!);
    }

    [Fact]
    public async Task KickMember_RemovesThem()
    {
        var owner = CreateClient("mm-kick-owner", "Owner");
        var (serverId, code) = await CreateServerWithInviteAsync(owner);

        var target = CreateClient("mm-kick-target", "Target");
        await target.PostAsync($"/invites/{code}", null);
        var targetId = await GetUserIdAsync(target);

        var response = await owner.DeleteAsync($"/servers/{serverId}/members/{targetId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task KickMember_Self_ReturnsBadRequest()
    {
        var owner = CreateClient("mm-kick-self", "SelfKicker");
        var (serverId, _) = await CreateServerAsync(owner);
        var ownerId = await GetUserIdAsync(owner);

        var response = await owner.DeleteAsync($"/servers/{serverId}/members/{ownerId}");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateMemberRole_PromoteToAdmin()
    {
        var owner = CreateClient("mm-promote-owner", "Promoter");
        var (serverId, code) = await CreateServerWithInviteAsync(owner);

        var member = CreateClient("mm-promote-target", "Promotee");
        await member.PostAsync($"/invites/{code}", null);
        var memberId = await GetUserIdAsync(member);

        var response = await owner.PatchAsJsonAsync($"/servers/{serverId}/members/{memberId}/role", new { role = "Admin" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateMemberRole_InvalidRole_ReturnsBadRequest()
    {
        var owner = CreateClient("mm-badrole-owner", "BadRoleOwner");
        var (serverId, code) = await CreateServerWithInviteAsync(owner);

        var member = CreateClient("mm-badrole-target", "BadRoleTarget");
        await member.PostAsync($"/invites/{code}", null);
        var memberId = await GetUserIdAsync(member);

        var response = await owner.PatchAsJsonAsync($"/servers/{serverId}/members/{memberId}/role", new { role = "Owner" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task KickMember_OwnerCannotBeKicked()
    {
        var owner = CreateClient("mm-kick-owner2", "KickOwner2");
        var (serverId, code) = await CreateServerWithInviteAsync(owner);
        var ownerId = await GetUserIdAsync(owner);

        // Promote a member to admin
        var admin = CreateClient("mm-kick-admin", "KickAdmin");
        await admin.PostAsync($"/invites/{code}", null);
        var adminId = await GetUserIdAsync(admin);
        await owner.PatchAsJsonAsync($"/servers/{serverId}/members/{adminId}/role", new { role = "Admin" });

        // Admin tries to kick owner
        var response = await admin.DeleteAsync($"/servers/{serverId}/members/{ownerId}");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
