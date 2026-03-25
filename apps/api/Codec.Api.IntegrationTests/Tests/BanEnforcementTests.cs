using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;

namespace Codec.Api.IntegrationTests.Tests;

/// <summary>
/// Integration tests for ban enforcement: ban a user, verify they can't rejoin via invite,
/// unban and verify they can rejoin, list bans, and ban edge cases.
/// </summary>
public class BanEnforcementTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    private async Task<string> CreateInviteCodeAsync(HttpClient client, Guid serverId)
    {
        var response = await client.PostAsJsonAsync($"/servers/{serverId}/invites", new { });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("code").GetString()!;
    }

    [Fact]
    public async Task BannedUser_CannotRejoinViaInvite()
    {
        var owner = CreateClient("ban-owner-1", "BanOwner");
        var target = CreateClient("ban-target-1", "BanTarget");

        var (serverId, _) = await CreateServerAsync(owner, "Ban Server");
        var code = await CreateInviteCodeAsync(owner, serverId);

        // Target joins the server
        var joinResponse = await target.PostAsync($"/invites/{code}/join", null);
        joinResponse.EnsureSuccessStatusCode();

        // Get target user ID
        var targetId = await GetUserIdAsync(target);

        // Owner bans the target
        var banResponse = await owner.PostAsJsonAsync($"/servers/{serverId}/bans/{targetId}", new
        {
            reason = "Test ban"
        });
        Assert.Equal(HttpStatusCode.NoContent, banResponse.StatusCode);

        // Target tries to rejoin via the same invite
        var rejoinResponse = await target.PostAsync($"/invites/{code}/join", null);
        Assert.Equal(HttpStatusCode.Forbidden, rejoinResponse.StatusCode);

        var rejoinBody = await rejoinResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("banned", rejoinBody.GetProperty("error").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnbannedUser_CanRejoinViaInvite()
    {
        var owner = CreateClient("ban-unban-owner", "UnbanOwner");
        var target = CreateClient("ban-unban-target", "UnbanTarget");

        var (serverId, _) = await CreateServerAsync(owner, "Unban Server");
        var code = await CreateInviteCodeAsync(owner, serverId);

        // Join, get banned
        await target.PostAsync($"/invites/{code}/join", null);
        var targetId = await GetUserIdAsync(target);
        await owner.PostAsJsonAsync($"/servers/{serverId}/bans/{targetId}", new { reason = "temp ban" });

        // Verify banned
        var bannedJoin = await target.PostAsync($"/invites/{code}/join", null);
        Assert.Equal(HttpStatusCode.Forbidden, bannedJoin.StatusCode);

        // Unban
        var unbanResponse = await owner.DeleteAsync($"/servers/{serverId}/bans/{targetId}");
        Assert.Equal(HttpStatusCode.NoContent, unbanResponse.StatusCode);

        // Now can rejoin
        var rejoinResponse = await target.PostAsync($"/invites/{code}/join", null);
        rejoinResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Ban_WithDeleteMessages_RemovesMessages()
    {
        var owner = CreateClient("ban-delmsg-owner", "DelMsgOwner");
        var target = CreateClient("ban-delmsg-target", "DelMsgTarget");

        var (serverId, _) = await CreateServerAsync(owner, "DelMsg Server");
        var channelId = await CreateChannelAsync(owner, serverId);
        var code = await CreateInviteCodeAsync(owner, serverId);

        // Target joins and posts messages
        await target.PostAsync($"/invites/{code}/join", null);
        await PostMessageAsync(target, channelId, "Message from target 1");
        await PostMessageAsync(target, channelId, "Message from target 2");

        // Verify messages exist
        var msgsBefore = await owner.GetFromJsonAsync<JsonElement>($"/channels/{channelId}/messages");
        var beforeCount = msgsBefore.GetArrayLength();
        Assert.True(beforeCount >= 2);

        // Ban with deleteMessages
        var targetId = await GetUserIdAsync(target);
        var banResponse = await owner.PostAsJsonAsync($"/servers/{serverId}/bans/{targetId}", new
        {
            reason = "Spam",
            deleteMessages = true
        });
        Assert.Equal(HttpStatusCode.NoContent, banResponse.StatusCode);

        // Verify messages are deleted
        var msgsAfter = await owner.GetFromJsonAsync<JsonElement>($"/channels/{channelId}/messages");
        var afterCount = msgsAfter.GetArrayLength();
        Assert.True(afterCount < beforeCount);
    }

    [Fact]
    public async Task Ban_CannotBanSelf()
    {
        var owner = CreateClient("ban-self-owner", "SelfBanOwner");
        var (serverId, _) = await CreateServerAsync(owner, "SelfBan Server");
        var ownerId = await GetUserIdAsync(owner);

        var response = await owner.PostAsJsonAsync($"/servers/{serverId}/bans/{ownerId}", new
        {
            reason = "Self ban attempt"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ban_CannotBanOwner()
    {
        var owner = CreateClient("ban-noowner-owner", "NoOwnerBan");
        var admin = CreateClient("ban-noowner-admin", "AdminBan");

        var (serverId, _) = await CreateServerAsync(owner, "NoOwnerBan Server");
        var code = await CreateInviteCodeAsync(owner, serverId);

        // Admin joins
        await admin.PostAsync($"/invites/{code}/join", null);
        var adminId = await GetUserIdAsync(admin);

        // Promote admin (update their role to Admin)
        var updateResponse = await owner.PatchAsJsonAsync($"/servers/{serverId}/members/{adminId}/role", new { role = "Admin" });
        updateResponse.EnsureSuccessStatusCode();

        // Admin tries to ban the owner
        var ownerId = await GetUserIdAsync(owner);
        var banResponse = await admin.PostAsJsonAsync($"/servers/{serverId}/bans/{ownerId}", new
        {
            reason = "Attempt to ban owner"
        });

        Assert.Equal(HttpStatusCode.BadRequest, banResponse.StatusCode);
    }

    [Fact]
    public async Task Ban_AlreadyBanned_ReturnsConflict()
    {
        var owner = CreateClient("ban-dup-owner", "DupBanOwner");
        var target = CreateClient("ban-dup-target", "DupBanTarget");

        var (serverId, _) = await CreateServerAsync(owner, "DupBan Server");
        var code = await CreateInviteCodeAsync(owner, serverId);
        await target.PostAsync($"/invites/{code}/join", null);
        var targetId = await GetUserIdAsync(target);

        // First ban
        await owner.PostAsJsonAsync($"/servers/{serverId}/bans/{targetId}", new { reason = "First ban" });

        // Second ban should conflict
        var response = await owner.PostAsJsonAsync($"/servers/{serverId}/bans/{targetId}", new { reason = "Second ban" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ListBans_ReturnsBannedUsers()
    {
        var owner = CreateClient("ban-list-owner", "ListBanOwner");
        var target1 = CreateClient("ban-list-t1", "ListBanTarget1");
        var target2 = CreateClient("ban-list-t2", "ListBanTarget2");

        var (serverId, _) = await CreateServerAsync(owner, "ListBan Server");
        var code = await CreateInviteCodeAsync(owner, serverId);
        await target1.PostAsync($"/invites/{code}/join", null);
        await target2.PostAsync($"/invites/{code}/join", null);

        var t1Id = await GetUserIdAsync(target1);
        var t2Id = await GetUserIdAsync(target2);

        await owner.PostAsJsonAsync($"/servers/{serverId}/bans/{t1Id}", new { reason = "Reason 1" });
        await owner.PostAsJsonAsync($"/servers/{serverId}/bans/{t2Id}", new { reason = "Reason 2" });

        var response = await owner.GetAsync($"/servers/{serverId}/bans");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetArrayLength() >= 2);
    }

    [Fact]
    public async Task Unban_NonBannedUser_Returns404()
    {
        var owner = CreateClient("ban-unbannf-owner", "UnbanNFOwner");
        var (serverId, _) = await CreateServerAsync(owner, "UnbanNF Server");

        var response = await owner.DeleteAsync($"/servers/{serverId}/bans/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
