using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;
using Codec.Api.Models;

namespace Codec.Api.IntegrationTests.Tests;

/// <summary>
/// Integration tests for multi-role assignment: adding/removing roles to members
/// and verifying that permissions aggregate correctly across multiple roles.
/// </summary>
public class MultiRoleTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    private async Task<(Guid ServerId, string InviteCode)> CreateServerWithInviteAsync(HttpClient owner, string name = "Test Server")
    {
        var (serverId, _) = await CreateServerAsync(owner, name);
        var inviteResponse = await owner.PostAsJsonAsync($"/servers/{serverId}/invites", new { });
        inviteResponse.EnsureSuccessStatusCode();
        var invite = await inviteResponse.Content.ReadFromJsonAsync<JsonElement>();
        return (serverId, invite.GetProperty("code").GetString()!);
    }

    private async Task<Guid> GetMemberRoleIdAsync(HttpClient client, Guid serverId)
    {
        var rolesResponse = await client.GetFromJsonAsync<JsonElement>($"/servers/{serverId}/roles");
        foreach (var role in rolesResponse.EnumerateArray())
        {
            if (role.GetProperty("name").GetString() == "Member" && role.GetProperty("isSystemRole").GetBoolean())
            {
                return role.GetProperty("id").GetGuid();
            }
        }
        throw new InvalidOperationException("Member role not found.");
    }

    [Fact]
    public async Task AddMemberRole_AssignsRole()
    {
        var owner = CreateClient("mr-add-owner", "AddRoleOwner");
        var member = CreateClient("mr-add-member", "AddRoleMember");

        var (serverId, code) = await CreateServerWithInviteAsync(owner, "AddRole Server");
        await member.PostAsync($"/invites/{code}", null);
        var memberId = await GetUserIdAsync(member);

        // Create a custom role
        var createRoleResp = await owner.PostAsJsonAsync($"/servers/{serverId}/roles", new
        {
            name = "Tester",
            color = "#aabbcc"
        });
        Assert.Equal(HttpStatusCode.Created, createRoleResp.StatusCode);
        var createdRole = await createRoleResp.Content.ReadFromJsonAsync<JsonElement>();
        var roleId = createdRole.GetProperty("id").GetGuid();

        // Add the role to the member
        var addResponse = await owner.PostAsync($"/servers/{serverId}/members/{memberId}/roles/{roleId}", null);
        addResponse.EnsureSuccessStatusCode();

        // Verify member now has the new role
        var membersResponse = await owner.GetFromJsonAsync<JsonElement[]>($"/servers/{serverId}/members");
        Assert.NotNull(membersResponse);

        var targetMember = membersResponse.FirstOrDefault(m => m.GetProperty("userId").GetGuid() == memberId);
        Assert.True(targetMember.ValueKind != JsonValueKind.Undefined, "Target member not found in members list.");

        var roles = targetMember.GetProperty("roles");
        var roleNames = new List<string>();
        foreach (var r in roles.EnumerateArray())
        {
            roleNames.Add(r.GetProperty("name").GetString()!);
        }

        Assert.Contains("Tester", roleNames);
    }

    [Fact]
    public async Task RemoveMemberRole_AutoAssignsMemberWhenEmpty()
    {
        var owner = CreateClient("mr-rm-owner", "RemoveRoleOwner");
        var member = CreateClient("mr-rm-member", "RemoveRoleMember");

        var (serverId, code) = await CreateServerWithInviteAsync(owner, "RemoveRole Server");
        await member.PostAsync($"/invites/{code}", null);
        var memberId = await GetUserIdAsync(member);

        // Get the Member system role ID
        var memberRoleId = await GetMemberRoleIdAsync(owner, serverId);

        // Remove the Member role from the member
        var removeResponse = await owner.DeleteAsync($"/servers/{serverId}/members/{memberId}/roles/{memberRoleId}");
        removeResponse.EnsureSuccessStatusCode();

        // Verify member still has the Member role (auto-reassigned because they had no other roles)
        var membersResponse = await owner.GetFromJsonAsync<JsonElement[]>($"/servers/{serverId}/members");
        Assert.NotNull(membersResponse);

        var targetMember = membersResponse.FirstOrDefault(m => m.GetProperty("userId").GetGuid() == memberId);
        Assert.True(targetMember.ValueKind != JsonValueKind.Undefined, "Target member not found in members list.");

        var roles = targetMember.GetProperty("roles");
        var roleNames = new List<string>();
        foreach (var r in roles.EnumerateArray())
        {
            roleNames.Add(r.GetProperty("name").GetString()!);
        }

        Assert.Contains("Member", roleNames);
    }

    [Fact]
    public async Task MultiRole_PermissionsCombine()
    {
        var owner = CreateClient("mr-perm-owner", "MultiPermOwner");
        var member = CreateClient("mr-perm-member", "MultiPermMember");

        var (serverId, code) = await CreateServerWithInviteAsync(owner, "MultiPerm Server");
        await member.PostAsync($"/invites/{code}", null);
        var memberId = await GetUserIdAsync(member);

        // Create a Moderator role with ManageMessages | KickMembers
        long moderatorPerms = (long)(Permission.ManageMessages | Permission.KickMembers);
        var createRoleResp = await owner.PostAsJsonAsync($"/servers/{serverId}/roles", new
        {
            name = "Moderator",
            permissions = moderatorPerms
        });
        Assert.Equal(HttpStatusCode.Created, createRoleResp.StatusCode);
        var createdRole = await createRoleResp.Content.ReadFromJsonAsync<JsonElement>();
        var moderatorRoleId = createdRole.GetProperty("id").GetGuid();

        // Add the Moderator role to the member
        var addResponse = await owner.PostAsync($"/servers/{serverId}/members/{memberId}/roles/{moderatorRoleId}", null);
        addResponse.EnsureSuccessStatusCode();

        // Verify member has both Member defaults AND Moderator perms combined
        var membersResponse = await owner.GetFromJsonAsync<JsonElement[]>($"/servers/{serverId}/members");
        Assert.NotNull(membersResponse);

        var targetMember = membersResponse.FirstOrDefault(m => m.GetProperty("userId").GetGuid() == memberId);
        Assert.True(targetMember.ValueKind != JsonValueKind.Undefined, "Target member not found in members list.");

        var aggregatedPermissions = targetMember.GetProperty("permissions").GetInt64();

        // Should include Member defaults
        long memberDefaultPerms = (long)PermissionExtensions.MemberDefaults;
        Assert.True((aggregatedPermissions & memberDefaultPerms) == memberDefaultPerms,
            $"Expected member defaults ({memberDefaultPerms}) to be included in aggregated permissions ({aggregatedPermissions}).");

        // Should include Moderator perms
        Assert.True((aggregatedPermissions & (long)Permission.ManageMessages) == (long)Permission.ManageMessages,
            "Expected ManageMessages permission to be included.");
        Assert.True((aggregatedPermissions & (long)Permission.KickMembers) == (long)Permission.KickMembers,
            "Expected KickMembers permission to be included.");
    }
}
