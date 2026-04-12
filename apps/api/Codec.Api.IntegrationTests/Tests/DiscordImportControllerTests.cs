using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;
using Codec.Api.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.IntegrationTests.Tests;

public class DiscordImportControllerTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    // ── GET /servers/{serverId}/discord-import ──────────────────────────────

    [Fact]
    public async Task GetStatus_NoImportExists_ReturnsNotFound()
    {
        var client = CreateClient("di-get-404", "GetStatus404");
        var (serverId, _) = await CreateServerAsync(client);

        var response = await client.GetAsync($"/servers/{serverId}/discord-import");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetStatus_NonAdminUser_ReturnsForbidden()
    {
        // Owner creates the server
        var owner = CreateClient("di-get-403-owner", "Owner");
        var (serverId, _) = await CreateServerAsync(owner);

        // Non-member user tries to access
        var nonAdmin = CreateClient("di-get-403-nonadmin", "NonAdmin");

        var response = await nonAdmin.GetAsync($"/servers/{serverId}/discord-import");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetStatus_WithExistingImport_ReturnsOk()
    {
        var client = CreateClient("di-get-ok", "GetStatusOk");
        var (serverId, _) = await CreateServerAsync(client);
        var userId = await GetUserIdAsync(client);

        // Insert a completed import record directly via DB
        await WithDbAsync(async db =>
        {
            db.DiscordImports.Add(new DiscordImport
            {
                Id = Guid.NewGuid(),
                ServerId = serverId,
                DiscordGuildId = "123456789",
                Status = DiscordImportStatus.Completed,
                InitiatedByUserId = userId,
                CompletedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        });

        var response = await client.GetAsync($"/servers/{serverId}/discord-import");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("Completed");
        body.GetProperty("discordGuildId").GetString().Should().Be("123456789");
    }

    // ── POST /servers/{serverId}/discord-import ─────────────────────────────

    [Fact]
    public async Task StartImport_NonAdminUser_ReturnsForbidden()
    {
        var owner = CreateClient("di-start-403-owner", "Owner");
        var (serverId, _) = await CreateServerAsync(owner);

        // A non-member user tries to start an import
        var nonAdmin = CreateClient("di-start-403-nonadmin", "NonAdmin");

        var response = await nonAdmin.PostAsJsonAsync(
            $"/servers/{serverId}/discord-import",
            new { botToken = "fake-token", discordGuildId = "123456789" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task StartImport_InvalidBotToken_ReturnsBadRequest()
    {
        var client = CreateClient("di-start-400", "StartBad");
        var (serverId, _) = await CreateServerAsync(client);

        // The DiscordApiClient will fail to reach Discord's API in the test environment,
        // resulting in an HttpRequestException which the controller maps to 400.
        var response = await client.PostAsJsonAsync(
            $"/servers/{serverId}/discord-import",
            new { botToken = "invalid-bot-token", discordGuildId = "000000000000000001" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Contain("bot token");
    }

    [Fact]
    public async Task StartImport_AlreadyInProgress_ReturnsConflict()
    {
        var client = CreateClient("di-start-conflict", "StartConflict");
        var (serverId, _) = await CreateServerAsync(client);
        var userId = await GetUserIdAsync(client);

        // Insert a pending import to simulate one already in progress
        await WithDbAsync(async db =>
        {
            db.DiscordImports.Add(new DiscordImport
            {
                Id = Guid.NewGuid(),
                ServerId = serverId,
                DiscordGuildId = "999888777",
                Status = DiscordImportStatus.Pending,
                InitiatedByUserId = userId
            });
            await db.SaveChangesAsync();
        });

        // The Discord API call would still fail in test, but conflict check happens first
        // so we just verify the controller is protecting against the duplicate correctly.
        // Since the in-progress check runs before the Discord API call, we expect Conflict.
        var response = await client.PostAsJsonAsync(
            $"/servers/{serverId}/discord-import",
            new { botToken = "any-token", discordGuildId = "999888777" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Contain("already in progress");
    }

    // ── DELETE /servers/{serverId}/discord-import ───────────────────────────

    [Fact]
    public async Task CancelImport_NoImportInProgress_ReturnsNotFound()
    {
        var client = CreateClient("di-cancel-404", "CancelNone");
        var (serverId, _) = await CreateServerAsync(client);

        var response = await client.DeleteAsync($"/servers/{serverId}/discord-import");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CancelImport_WithPendingImport_ReturnsNoContent()
    {
        var client = CreateClient("di-cancel-ok", "CancelOk");
        var (serverId, _) = await CreateServerAsync(client);
        var userId = await GetUserIdAsync(client);

        await WithDbAsync(async db =>
        {
            db.DiscordImports.Add(new DiscordImport
            {
                Id = Guid.NewGuid(),
                ServerId = serverId,
                DiscordGuildId = "111222333",
                Status = DiscordImportStatus.Pending,
                InitiatedByUserId = userId
            });
            await db.SaveChangesAsync();
        });

        var response = await client.DeleteAsync($"/servers/{serverId}/discord-import");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify the import was marked cancelled in DB
        await WithDbAsync(async db =>
        {
            var import = await db.DiscordImports
                .FirstOrDefaultAsync(d => d.ServerId == serverId);
            import.Should().NotBeNull();
            import!.Status.Should().Be(DiscordImportStatus.Cancelled);
            import.EncryptedBotToken.Should().BeNull();
        });
    }

    // ── GET /servers/{serverId}/discord-import/mappings ─────────────────────

    [Fact]
    public async Task GetMappings_NoMappingsExist_ReturnsEmptyArray()
    {
        var client = CreateClient("di-map-empty", "MapEmpty");
        var (serverId, _) = await CreateServerAsync(client);

        var response = await client.GetAsync($"/servers/{serverId}/discord-import/mappings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetMappings_NonAdminUser_ReturnsForbidden()
    {
        var owner = CreateClient("di-map-403-owner", "Owner");
        var (serverId, _) = await CreateServerAsync(owner);

        var nonAdmin = CreateClient("di-map-403-nonadmin", "NonAdmin");

        var response = await nonAdmin.GetAsync($"/servers/{serverId}/discord-import/mappings");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetMappings_WithExistingMappings_ReturnsList()
    {
        var client = CreateClient("di-map-list", "MapList");
        var (serverId, _) = await CreateServerAsync(client);

        await WithDbAsync(async db =>
        {
            db.DiscordUserMappings.Add(new DiscordUserMapping
            {
                Id = Guid.NewGuid(),
                ServerId = serverId,
                DiscordUserId = "22222222222222222",
                DiscordUsername = "TestDiscordUser#1234"
            });
            await db.SaveChangesAsync();
        });

        var response = await client.GetAsync($"/servers/{serverId}/discord-import/mappings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetArrayLength().Should().Be(1);
        body[0].GetProperty("discordUsername").GetString().Should().Be("TestDiscordUser#1234");
    }

    // ── POST /servers/{serverId}/discord-import/claim ───────────────────────

    [Fact]
    public async Task ClaimIdentity_MappingNotFound_ReturnsNotFound()
    {
        var client = CreateClient("di-claim-404", "ClaimNone");
        var (serverId, _) = await CreateServerAsync(client);

        // User is a server member (they created it), but no mapping exists
        var response = await client.PostAsJsonAsync(
            $"/servers/{serverId}/discord-import/claim",
            new { discordUserId = "99999999999999999" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ClaimIdentity_NonMember_ReturnsForbidden()
    {
        var owner = CreateClient("di-claim-403-owner", "Owner");
        var (serverId, _) = await CreateServerAsync(owner);

        // A user who hasn't joined the server tries to claim
        var nonMember = CreateClient("di-claim-403-nonmember", "NonMember");

        var response = await nonMember.PostAsJsonAsync(
            $"/servers/{serverId}/discord-import/claim",
            new { discordUserId = "11111111111111111" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ClaimIdentity_AlreadyClaimed_ReturnsConflict()
    {
        var owner = CreateClient("di-claim-conflict-owner", "Owner");
        var (serverId, _) = await CreateServerAsync(owner);

        // Add another member via invite
        var member = CreateClient("di-claim-conflict-member", "Member");
        var inviteResponse = await owner.PostAsJsonAsync($"/servers/{serverId}/invites", new { });
        inviteResponse.EnsureSuccessStatusCode();
        var invite = await inviteResponse.Content.ReadFromJsonAsync<JsonElement>();
        var code = invite.GetProperty("code").GetString();
        await member.PostAsync($"/invites/{code}", null);

        var memberId = await GetUserIdAsync(member);

        // Insert a mapping that is already claimed by memberId
        await WithDbAsync(async db =>
        {
            db.DiscordUserMappings.Add(new DiscordUserMapping
            {
                Id = Guid.NewGuid(),
                ServerId = serverId,
                DiscordUserId = "33333333333333333",
                DiscordUsername = "AlreadyClaimed#0001",
                CodecUserId = memberId,
                ClaimedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        });

        // Owner (also a member) tries to claim the same Discord identity
        var response = await owner.PostAsJsonAsync(
            $"/servers/{serverId}/discord-import/claim",
            new { discordUserId = "33333333333333333" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Contain("already been claimed");
    }

    [Fact]
    public async Task ClaimIdentity_ValidUnclaimed_ReturnsOk()
    {
        var client = CreateClient("di-claim-ok", "ClaimOk");
        var (serverId, _) = await CreateServerAsync(client);

        // Insert an unclaimed mapping
        await WithDbAsync(async db =>
        {
            db.DiscordUserMappings.Add(new DiscordUserMapping
            {
                Id = Guid.NewGuid(),
                ServerId = serverId,
                DiscordUserId = "44444444444444444",
                DiscordUsername = "UnclaimedUser#5678"
            });
            await db.SaveChangesAsync();
        });

        var response = await client.PostAsJsonAsync(
            $"/servers/{serverId}/discord-import/claim",
            new { discordUserId = "44444444444444444" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("claimed").GetBoolean().Should().BeTrue();
    }

    // ── POST /servers/{serverId}/discord-import/resync ──────────────────────

    [Fact]
    public async Task Resync_NoCompletedImport_ReturnsBadRequest()
    {
        var client = CreateClient("di-resync-400", "ResyncNone");
        var (serverId, _) = await CreateServerAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/servers/{serverId}/discord-import/resync",
            new { botToken = "some-token", discordGuildId = "123456789" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Contain("No completed import");
    }

    [Fact]
    public async Task Resync_NonAdminUser_ReturnsForbidden()
    {
        var owner = CreateClient("di-resync-403-owner", "Owner");
        var (serverId, _) = await CreateServerAsync(owner);

        var nonAdmin = CreateClient("di-resync-403-nonadmin", "NonAdmin");

        var response = await nonAdmin.PostAsJsonAsync(
            $"/servers/{serverId}/discord-import/resync",
            new { botToken = "some-token", discordGuildId = "123456789" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Resync_WithCompletedImport_InvalidToken_ReturnsBadRequest()
    {
        var client = CreateClient("di-resync-badtoken", "ResyncBadToken");
        var (serverId, _) = await CreateServerAsync(client);
        var userId = await GetUserIdAsync(client);

        // Insert a completed import
        await WithDbAsync(async db =>
        {
            db.DiscordImports.Add(new DiscordImport
            {
                Id = Guid.NewGuid(),
                ServerId = serverId,
                DiscordGuildId = "555666777",
                Status = DiscordImportStatus.Completed,
                InitiatedByUserId = userId,
                CompletedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        });

        // Resync with a bad token — Discord API call will fail with HttpRequestException → 400
        var response = await client.PostAsJsonAsync(
            $"/servers/{serverId}/discord-import/resync",
            new { botToken = "bad-bot-token", discordGuildId = "555666777" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
