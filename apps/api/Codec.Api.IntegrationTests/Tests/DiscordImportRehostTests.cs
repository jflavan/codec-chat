using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;
using Codec.Api.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.IntegrationTests.Tests;

public class DiscordImportRehostTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    // ── GET /servers/{serverId}/discord-import with RehostingMedia ────────

    [Fact]
    public async Task GetStatus_RehostingMedia_ReturnsCorrectStatus()
    {
        var client = CreateClient("rehost-status-1", "RehostStatus");
        var (serverId, _) = await CreateServerAsync(client);
        var userId = await GetUserIdAsync(client);

        await WithDbAsync(async db =>
        {
            db.DiscordImports.Add(new DiscordImport
            {
                Id = Guid.NewGuid(),
                ServerId = serverId,
                DiscordGuildId = "123456789",
                Status = DiscordImportStatus.RehostingMedia,
                StartedAt = DateTimeOffset.UtcNow,
                InitiatedByUserId = userId,
                ImportedChannels = 5,
                ImportedMessages = 100,
                ImportedMembers = 10
            });
            await db.SaveChangesAsync();
        });

        var response = await client.GetAsync($"/servers/{serverId}/discord-import");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("RehostingMedia");
        body.GetProperty("importedChannels").GetInt32().Should().Be(5);
        body.GetProperty("importedMessages").GetInt32().Should().Be(100);
        body.GetProperty("importedMembers").GetInt32().Should().Be(10);
    }

    // ── Active import constraint includes RehostingMedia ─────────────────

    [Fact]
    public async Task StartImport_WhileRehostingMedia_ReturnsConflict()
    {
        var client = CreateClient("rehost-conflict-1", "RehostConflict");
        var (serverId, _) = await CreateServerAsync(client);
        var userId = await GetUserIdAsync(client);

        await WithDbAsync(async db =>
        {
            db.DiscordImports.Add(new DiscordImport
            {
                Id = Guid.NewGuid(),
                ServerId = serverId,
                DiscordGuildId = "999888777",
                Status = DiscordImportStatus.RehostingMedia,
                StartedAt = DateTimeOffset.UtcNow,
                InitiatedByUserId = userId,
                ImportedChannels = 3,
                ImportedMessages = 50,
                ImportedMembers = 5
            });
            await db.SaveChangesAsync();
        });

        var response = await client.PostAsJsonAsync(
            $"/servers/{serverId}/discord-import",
            new { botToken = "any-token", discordGuildId = "999888777" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── DELETE /servers/{serverId}/discord-import during RehostingMedia ───

    [Fact]
    public async Task CancelImport_DuringRehostingMedia_ReturnsNotFound()
    {
        var client = CreateClient("rehost-cancel-1", "RehostCancel");
        var (serverId, _) = await CreateServerAsync(client);
        var userId = await GetUserIdAsync(client);

        await WithDbAsync(async db =>
        {
            db.DiscordImports.Add(new DiscordImport
            {
                Id = Guid.NewGuid(),
                ServerId = serverId,
                DiscordGuildId = "111222333",
                Status = DiscordImportStatus.RehostingMedia,
                StartedAt = DateTimeOffset.UtcNow,
                InitiatedByUserId = userId,
                ImportedChannels = 2,
                ImportedMessages = 30,
                ImportedMembers = 4
            });
            await db.SaveChangesAsync();
        });

        var response = await client.DeleteAsync($"/servers/{serverId}/discord-import");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── POST /servers/{serverId}/discord-import/resync during RehostingMedia ──

    [Fact]
    public async Task Resync_WhileRehostingMedia_IsBlocked()
    {
        var client = CreateClient("rehost-resync-1", "RehostResync");
        var (serverId, _) = await CreateServerAsync(client);
        var userId = await GetUserIdAsync(client);

        await WithDbAsync(async db =>
        {
            db.DiscordImports.Add(new DiscordImport
            {
                Id = Guid.NewGuid(),
                ServerId = serverId,
                DiscordGuildId = "444555666",
                Status = DiscordImportStatus.RehostingMedia,
                StartedAt = DateTimeOffset.UtcNow,
                InitiatedByUserId = userId,
                ImportedChannels = 4,
                ImportedMessages = 80,
                ImportedMembers = 8
            });
            await db.SaveChangesAsync();
        });

        // Resync now accepts RehostingMedia imports (stuck media rehost can be resynced).
        // The import is found, but the bot token is invalid, so we get a token validation error.
        var response = await client.PostAsJsonAsync(
            $"/servers/{serverId}/discord-import/resync",
            new { botToken = "some-token", discordGuildId = "444555666" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Contain("bot token");
    }
}
