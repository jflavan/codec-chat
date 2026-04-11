# Discord Server Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow server admins to import Discord server content (structure, messages, roles, emojis, members) into Codec via a Discord bot token, with re-sync and identity claiming support.

**Architecture:** Background job in the API processes Discord API data and writes to Postgres. A `DiscordImportController` handles admin-facing endpoints. A `DiscordImportWorker` (hosted service) consumes from a `Channel<T>` queue. Progress is pushed to the frontend via SignalR. The frontend adds an "Import from Discord" tab in server settings.

**Tech Stack:** ASP.NET Core 10, EF Core, SignalR, `System.Threading.Channels`, `HttpClient` (Discord API), SvelteKit 5, Svelte runes

**Spec:** `docs/superpowers/specs/2026-04-11-discord-import-design.md`

---

## File Structure

### New Files (API)

| File | Responsibility |
|------|---------------|
| `apps/api/Codec.Api/Models/DiscordImport.cs` | Entity + `DiscordImportStatus` enum |
| `apps/api/Codec.Api/Models/DiscordUserMapping.cs` | Discord-to-Codec user mapping entity |
| `apps/api/Codec.Api/Models/DiscordEntityMapping.cs` | Discord-to-Codec entity mapping + `DiscordEntityType` enum |
| `apps/api/Codec.Api/Models/StartDiscordImportRequest.cs` | Request DTO |
| `apps/api/Codec.Api/Models/ClaimDiscordIdentityRequest.cs` | Request DTO |
| `apps/api/Codec.Api/Services/DiscordApiClient.cs` | Typed HTTP client wrapping Discord REST API |
| `apps/api/Codec.Api/Services/DiscordPermissionMapper.cs` | Static Discord-to-Codec permission bit translation |
| `apps/api/Codec.Api/Services/DiscordImportService.cs` | Orchestrates import logic (called by worker) |
| `apps/api/Codec.Api/Services/DiscordRateLimitHandler.cs` | `DelegatingHandler` for Discord rate limits |
| `apps/api/Codec.Api/Services/DiscordImportWorker.cs` | `BackgroundService` consuming from `Channel<Guid>` |
| `apps/api/Codec.Api/Controllers/DiscordImportController.cs` | REST endpoints for import management |
| `apps/api/Codec.Api/Migrations/<timestamp>_AddDiscordImport.cs` | EF migration |
| `apps/api/Codec.Api.Tests/DiscordPermissionMapperTests.cs` | Unit tests for permission mapping |
| `apps/api/Codec.Api.Tests/DiscordRateLimitHandlerTests.cs` | Unit tests for rate limit handler |

### New Files (Web)

| File | Responsibility |
|------|---------------|
| `apps/web/src/lib/components/server-settings/ServerDiscordImport.svelte` | Import from Discord settings tab |
| `apps/web/src/lib/components/chat/ImportedAuthorBadge.svelte` | "Imported" badge on messages |

### Modified Files (API)

| File | Change |
|------|--------|
| `apps/api/Codec.Api/Models/Message.cs` | Add `ImportedAuthorName`, `ImportedAuthorAvatarUrl` |
| `apps/api/Codec.Api/Data/CodecDbContext.cs` | Add DbSets + OnModelCreating for 3 new entities |
| `apps/api/Codec.Api/Program.cs` | Register services, HttpClient, hosted service, Channel queue |

### Modified Files (Web)

| File | Change |
|------|--------|
| `apps/web/src/lib/types/models.ts` | Add `DiscordImport`, `DiscordUserMapping` types, extend `Message` |
| `apps/web/src/lib/api/client.ts` | Add discord import API methods |
| `apps/web/src/lib/services/chat-hub.ts` | Add `ImportProgress`/`ImportCompleted`/`ImportFailed` callbacks |
| `apps/web/src/lib/state/server-store.svelte.ts` | Add import state and methods |
| `apps/web/src/lib/state/ui-store.svelte.ts` | Add `'discord-import'` to `serverSettingsCategory` union |
| `apps/web/src/lib/components/server-settings/ServerSettingsModal.svelte` | Add `discord-import` tab rendering |
| `apps/web/src/lib/components/server-settings/ServerSettingsSidebar.svelte` | Add Discord Import nav item |
| `apps/web/src/lib/components/chat/MessageItem.svelte` | Show imported author info when `importedAuthorName` is set |

---

## Task 1: Data Model — New Entities

**Files:**
- Create: `apps/api/Codec.Api/Models/DiscordImport.cs`
- Create: `apps/api/Codec.Api/Models/DiscordUserMapping.cs`
- Create: `apps/api/Codec.Api/Models/DiscordEntityMapping.cs`
- Modify: `apps/api/Codec.Api/Models/Message.cs`
- Modify: `apps/api/Codec.Api/Data/CodecDbContext.cs`

- [ ] **Step 1: Create DiscordImport entity and enum**

Create `apps/api/Codec.Api/Models/DiscordImport.cs`:

```csharp
namespace Codec.Api.Models;

public enum DiscordImportStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

public class DiscordImport
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public string DiscordGuildId { get; set; } = string.Empty;
    public string? EncryptedBotToken { get; set; }
    public DiscordImportStatus Status { get; set; } = DiscordImportStatus.Pending;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public int ImportedChannels { get; set; }
    public int ImportedMessages { get; set; }
    public int ImportedMembers { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public Guid InitiatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public Server? Server { get; set; }
    public User? InitiatedByUser { get; set; }
}
```

- [ ] **Step 2: Create DiscordUserMapping entity**

Create `apps/api/Codec.Api/Models/DiscordUserMapping.cs`:

```csharp
namespace Codec.Api.Models;

public class DiscordUserMapping
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public string DiscordUserId { get; set; } = string.Empty;
    public string DiscordUsername { get; set; } = string.Empty;
    public string? DiscordAvatarUrl { get; set; }
    public Guid? CodecUserId { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public Server? Server { get; set; }
    public User? CodecUser { get; set; }
}
```

- [ ] **Step 3: Create DiscordEntityMapping entity and enum**

Create `apps/api/Codec.Api/Models/DiscordEntityMapping.cs`:

```csharp
namespace Codec.Api.Models;

public enum DiscordEntityType
{
    Role = 0,
    Category = 1,
    Channel = 2,
    Message = 3,
    Emoji = 4,
    PinnedMessage = 5
}

public class DiscordEntityMapping
{
    public Guid Id { get; set; }
    public Guid DiscordImportId { get; set; }
    public Guid ServerId { get; set; }
    public string DiscordEntityId { get; set; } = string.Empty;
    public DiscordEntityType EntityType { get; set; }
    public Guid CodecEntityId { get; set; }

    // Navigation properties
    public DiscordImport? DiscordImport { get; set; }
    public Server? Server { get; set; }
}
```

- [ ] **Step 4: Add imported author fields to Message**

In `apps/api/Codec.Api/Models/Message.cs`, add two nullable properties after `MessageType`:

```csharp
public string? ImportedAuthorName { get; set; }
public string? ImportedAuthorAvatarUrl { get; set; }
```

The full file should read:

```csharp
namespace Codec.Api.Models;

public class Message
{
    public Guid Id { get; set; }
    public Guid ChannelId { get; set; }
    public Guid? AuthorUserId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? FileUrl { get; set; }
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public string? FileContentType { get; set; }
    public Guid? ReplyToMessageId { get; set; }
    public MessageType MessageType { get; set; } = MessageType.Regular;
    public string? ImportedAuthorName { get; set; }
    public string? ImportedAuthorAvatarUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EditedAt { get; set; }
    public User? AuthorUser { get; set; }
    public Channel? Channel { get; set; }
    public List<Reaction> Reactions { get; set; } = new();
    public List<LinkPreview> LinkPreviews { get; set; } = new();
}
```

- [ ] **Step 5: Add DbSets and entity configuration to CodecDbContext**

In `apps/api/Codec.Api/Data/CodecDbContext.cs`, add three DbSets after the existing ones (around line 43):

```csharp
public DbSet<DiscordImport> DiscordImports => Set<DiscordImport>();
public DbSet<DiscordUserMapping> DiscordUserMappings => Set<DiscordUserMapping>();
public DbSet<DiscordEntityMapping> DiscordEntityMappings => Set<DiscordEntityMapping>();
```

Then at the end of `OnModelCreating` (before the closing `}`), add:

```csharp
modelBuilder.Entity<DiscordImport>(e =>
{
    e.HasOne(d => d.Server)
        .WithMany()
        .HasForeignKey(d => d.ServerId)
        .OnDelete(DeleteBehavior.Cascade);
    e.HasOne(d => d.InitiatedByUser)
        .WithMany()
        .HasForeignKey(d => d.InitiatedByUserId)
        .OnDelete(DeleteBehavior.Restrict);
    e.HasIndex(d => d.ServerId);
    e.Property(d => d.DiscordGuildId).HasMaxLength(20);
    e.Property(d => d.ErrorMessage).HasMaxLength(2000);
    e.Property(d => d.Status).HasConversion<string>();
});

modelBuilder.Entity<DiscordUserMapping>(e =>
{
    e.HasOne(m => m.Server)
        .WithMany()
        .HasForeignKey(m => m.ServerId)
        .OnDelete(DeleteBehavior.Cascade);
    e.HasOne(m => m.CodecUser)
        .WithMany()
        .HasForeignKey(m => m.CodecUserId)
        .OnDelete(DeleteBehavior.SetNull);
    e.HasIndex(m => new { m.ServerId, m.DiscordUserId }).IsUnique();
    e.Property(m => m.DiscordUserId).HasMaxLength(20);
    e.Property(m => m.DiscordUsername).HasMaxLength(100);
    e.Property(m => m.DiscordAvatarUrl).HasMaxLength(512);
});

modelBuilder.Entity<DiscordEntityMapping>(e =>
{
    e.HasOne(m => m.DiscordImport)
        .WithMany()
        .HasForeignKey(m => m.DiscordImportId)
        .OnDelete(DeleteBehavior.Cascade);
    e.HasOne(m => m.Server)
        .WithMany()
        .HasForeignKey(m => m.ServerId)
        .OnDelete(DeleteBehavior.Cascade);
    e.HasIndex(m => new { m.ServerId, m.DiscordEntityId, m.EntityType }).IsUnique();
    e.Property(m => m.DiscordEntityId).HasMaxLength(20);
    e.Property(m => m.EntityType).HasConversion<string>();
});

modelBuilder.Entity<Message>()
    .Property(m => m.ImportedAuthorName)
    .HasMaxLength(100);

modelBuilder.Entity<Message>()
    .Property(m => m.ImportedAuthorAvatarUrl)
    .HasMaxLength(512);
```

- [ ] **Step 6: Create EF Core migration**

```bash
cd apps/api/Codec.Api
dotnet ef migrations add AddDiscordImport
```

If `dotnet ef` is unavailable, manually create the migration file. The migration should add:
- `DiscordImports` table
- `DiscordUserMappings` table with unique index on `(ServerId, DiscordUserId)`
- `DiscordEntityMappings` table with unique index on `(ServerId, DiscordEntityId, EntityType)`
- `ImportedAuthorName` and `ImportedAuthorAvatarUrl` columns to `Messages` table

- [ ] **Step 7: Verify the API builds**

```bash
cd apps/api/Codec.Api
dotnet build
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 8: Commit**

```bash
git add apps/api/Codec.Api/Models/DiscordImport.cs apps/api/Codec.Api/Models/DiscordUserMapping.cs apps/api/Codec.Api/Models/DiscordEntityMapping.cs apps/api/Codec.Api/Models/Message.cs apps/api/Codec.Api/Data/CodecDbContext.cs apps/api/Codec.Api/Migrations/
git commit -m "feat(api): add Discord import data model and migration"
```

---

## Task 2: Discord Permission Mapper + Tests

**Files:**
- Create: `apps/api/Codec.Api/Services/DiscordPermissionMapper.cs`
- Create: `apps/api/Codec.Api.Tests/DiscordPermissionMapperTests.cs`

- [ ] **Step 1: Write failing tests**

Create `apps/api/Codec.Api.Tests/DiscordPermissionMapperTests.cs`:

```csharp
using Codec.Api.Models;
using Codec.Api.Services;

namespace Codec.Api.Tests;

public class DiscordPermissionMapperTests
{
    [Fact]
    public void MapPermissions_ViewChannel_MapsToViewChannels()
    {
        // Discord VIEW_CHANNEL = 1 << 10 = 0x400
        long discordPerms = 1L << 10;
        var result = DiscordPermissionMapper.MapPermissions(discordPerms);
        Assert.True(result.HasFlag(Permission.ViewChannels));
    }

    [Fact]
    public void MapPermissions_SendMessages_MapsToSendMessages()
    {
        // Discord SEND_MESSAGES = 1 << 11 = 0x800
        long discordPerms = 1L << 11;
        var result = DiscordPermissionMapper.MapPermissions(discordPerms);
        Assert.True(result.HasFlag(Permission.SendMessages));
    }

    [Fact]
    public void MapPermissions_Administrator_MapsToAdministrator()
    {
        // Discord ADMINISTRATOR = 1 << 3
        long discordPerms = 1L << 3;
        var result = DiscordPermissionMapper.MapPermissions(discordPerms);
        Assert.True(result.HasFlag(Permission.Administrator));
    }

    [Fact]
    public void MapPermissions_MultiplePermissions_CombinesCorrectly()
    {
        // Discord VIEW_CHANNEL (1<<10) | SEND_MESSAGES (1<<11) | ATTACH_FILES (1<<15)
        long discordPerms = (1L << 10) | (1L << 11) | (1L << 15);
        var result = DiscordPermissionMapper.MapPermissions(discordPerms);
        Assert.True(result.HasFlag(Permission.ViewChannels));
        Assert.True(result.HasFlag(Permission.SendMessages));
        Assert.True(result.HasFlag(Permission.AttachFiles));
    }

    [Fact]
    public void MapPermissions_UnknownBits_DroppedSilently()
    {
        // Discord USE_APPLICATION_COMMANDS = 1 << 31 (no Codec equivalent)
        long discordPerms = 1L << 31;
        var result = DiscordPermissionMapper.MapPermissions(discordPerms);
        Assert.Equal(Permission.None, result);
    }

    [Fact]
    public void MapPermissions_Zero_ReturnsNone()
    {
        var result = DiscordPermissionMapper.MapPermissions(0);
        Assert.Equal(Permission.None, result);
    }

    [Fact]
    public void MapPermissions_ManageGuild_MapsToManageServer()
    {
        // Discord MANAGE_GUILD = 1 << 5
        long discordPerms = 1L << 5;
        var result = DiscordPermissionMapper.MapPermissions(discordPerms);
        Assert.True(result.HasFlag(Permission.ManageServer));
    }

    [Fact]
    public void MapPermissions_ManageRoles_MapsToManageRoles()
    {
        // Discord MANAGE_ROLES = 1 << 28
        long discordPerms = 1L << 28;
        var result = DiscordPermissionMapper.MapPermissions(discordPerms);
        Assert.True(result.HasFlag(Permission.ManageRoles));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd apps/api
dotnet test Codec.Api.Tests/Codec.Api.Tests.csproj --filter "FullyQualifiedName~DiscordPermissionMapperTests" --no-restore
```

Expected: Compilation error — `DiscordPermissionMapper` does not exist.

- [ ] **Step 3: Implement DiscordPermissionMapper**

Create `apps/api/Codec.Api/Services/DiscordPermissionMapper.cs`:

```csharp
using Codec.Api.Models;

namespace Codec.Api.Services;

/// <summary>
/// Translates Discord permission bitfield values to Codec <see cref="Permission"/> flags.
/// Discord permission bits: https://discord.com/developers/docs/topics/permissions#permissions-bitwise-permission-flags
/// </summary>
public static class DiscordPermissionMapper
{
    private static readonly (long DiscordBit, Permission CodecFlag)[] Mappings =
    [
        (1L << 10,  Permission.ViewChannels),      // VIEW_CHANNEL
        (1L << 11,  Permission.SendMessages),      // SEND_MESSAGES
        (1L << 4,   Permission.ManageChannels),     // MANAGE_CHANNELS
        (1L << 5,   Permission.ManageServer),       // MANAGE_GUILD
        (1L << 28,  Permission.ManageRoles),        // MANAGE_ROLES
        (1L << 2,   Permission.KickMembers),        // KICK_MEMBERS
        (1L << 1,   Permission.BanMembers),         // BAN_MEMBERS
        (1L << 13,  Permission.ManageMessages),     // MANAGE_MESSAGES
        (1L << 15,  Permission.AttachFiles),        // ATTACH_FILES
        (1L << 6,   Permission.AddReactions),       // ADD_REACTIONS
        (1L << 17,  Permission.MentionEveryone),    // MENTION_EVERYONE
        (1L << 20,  Permission.Connect),            // CONNECT
        (1L << 21,  Permission.Speak),              // SPEAK
        (1L << 22,  Permission.MuteMembers),        // MUTE_MEMBERS
        (1L << 23,  Permission.DeafenMembers),      // DEAFEN_MEMBERS
        (1L << 3,   Permission.Administrator),      // ADMINISTRATOR
        (1L << 14,  Permission.EmbedLinks),         // EMBED_LINKS
    ];

    public static Permission MapPermissions(long discordPermissions)
    {
        var result = Permission.None;
        foreach (var (discordBit, codecFlag) in Mappings)
        {
            if ((discordPermissions & discordBit) != 0)
                result |= codecFlag;
        }
        return result;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
cd apps/api
dotnet test Codec.Api.Tests/Codec.Api.Tests.csproj --filter "FullyQualifiedName~DiscordPermissionMapperTests" --no-restore -v n
```

Expected: All 8 tests pass.

- [ ] **Step 5: Commit**

```bash
git add apps/api/Codec.Api/Services/DiscordPermissionMapper.cs apps/api/Codec.Api.Tests/DiscordPermissionMapperTests.cs
git commit -m "feat(api): add Discord-to-Codec permission mapper with tests"
```

---

## Task 3: Discord Rate Limit Handler + Tests

**Files:**
- Create: `apps/api/Codec.Api/Services/DiscordRateLimitHandler.cs`
- Create: `apps/api/Codec.Api.Tests/DiscordRateLimitHandlerTests.cs`

- [ ] **Step 1: Write failing tests**

Create `apps/api/Codec.Api.Tests/DiscordRateLimitHandlerTests.cs`:

```csharp
using System.Net;
using Codec.Api.Services;

namespace Codec.Api.Tests;

public class DiscordRateLimitHandlerTests
{
    private static HttpClient CreateClient(HttpMessageHandler inner)
    {
        var handler = new DiscordRateLimitHandler { InnerHandler = inner };
        return new HttpClient(handler);
    }

    [Fact]
    public async Task SendAsync_NormalResponse_PassesThrough()
    {
        var inner = new MockHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(inner);

        var response = await client.GetAsync("https://discord.com/api/test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task SendAsync_RateLimited_WaitsAndRetries()
    {
        var rateLimitResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        rateLimitResponse.Headers.Add("Retry-After", "0.01"); // 10ms
        var okResponse = new HttpResponseMessage(HttpStatusCode.OK);

        var inner = new MockHandler(rateLimitResponse, okResponse);
        var client = CreateClient(inner);

        var response = await client.GetAsync("https://discord.com/api/test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task SendAsync_RateLimitedExceedsMaxRetries_ReturnsLastResponse()
    {
        var rateLimitResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        rateLimitResponse.Headers.Add("Retry-After", "0.01");

        // Return 429 for every call (6 calls = 1 original + 5 retries)
        var responses = Enumerable.Range(0, 6)
            .Select(_ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                r.Headers.Add("Retry-After", "0.01");
                return r;
            })
            .ToArray();

        var inner = new MockHandler(responses);
        var client = CreateClient(inner);

        var response = await client.GetAsync("https://discord.com/api/test");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(6, inner.CallCount); // 1 original + 5 retries
    }

    private class MockHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage[] _responses;
        private int _callIndex;
        public int CallCount => _callIndex;

        public MockHandler(params HttpResponseMessage[] responses) => _responses = responses;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var idx = Math.Min(_callIndex, _responses.Length - 1);
            _callIndex++;
            return Task.FromResult(_responses[idx]);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd apps/api
dotnet test Codec.Api.Tests/Codec.Api.Tests.csproj --filter "FullyQualifiedName~DiscordRateLimitHandlerTests" --no-restore
```

Expected: Compilation error — `DiscordRateLimitHandler` does not exist.

- [ ] **Step 3: Implement DiscordRateLimitHandler**

Create `apps/api/Codec.Api/Services/DiscordRateLimitHandler.cs`:

```csharp
using System.Globalization;

namespace Codec.Api.Services;

/// <summary>
/// DelegatingHandler that respects Discord's rate limit headers.
/// Waits on 429 responses using Retry-After, with a max of 5 retries.
/// </summary>
public class DiscordRateLimitHandler : DelegatingHandler
{
    private const int MaxRetries = 5;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = null!;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            response = await base.SendAsync(request, cancellationToken);

            if ((int)response.StatusCode != 429)
                return response;

            if (attempt == MaxRetries)
                break;

            var retryAfter = ParseRetryAfter(response);
            if (retryAfter > TimeSpan.Zero)
                await Task.Delay(retryAfter, cancellationToken);
        }

        return response;
    }

    private static TimeSpan ParseRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var value = values.FirstOrDefault();
            if (value is not null &&
                double.TryParse(value, CultureInfo.InvariantCulture, out var seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }
        return TimeSpan.FromSeconds(1); // fallback
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
cd apps/api
dotnet test Codec.Api.Tests/Codec.Api.Tests.csproj --filter "FullyQualifiedName~DiscordRateLimitHandlerTests" --no-restore -v n
```

Expected: All 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add apps/api/Codec.Api/Services/DiscordRateLimitHandler.cs apps/api/Codec.Api.Tests/DiscordRateLimitHandlerTests.cs
git commit -m "feat(api): add Discord rate limit handler with tests"
```

---

## Task 4: Discord API Client

**Files:**
- Create: `apps/api/Codec.Api/Services/DiscordApiClient.cs`

- [ ] **Step 1: Create the Discord API client**

Create `apps/api/Codec.Api/Services/DiscordApiClient.cs`:

```csharp
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codec.Api.Services;

/// <summary>
/// Typed HTTP client for the Discord REST API (v10).
/// Injected via HttpClientFactory with the "discord" named client.
/// </summary>
public class DiscordApiClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public DiscordApiClient(HttpClient http)
    {
        _http = http;
        _http.BaseAddress = new Uri("https://discord.com/api/v10/");
    }

    public void SetBotToken(string token)
    {
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", token);
    }

    public async Task<DiscordGuild> GetGuildAsync(string guildId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"guilds/{guildId}?with_counts=true", ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DiscordGuild>(JsonOptions, ct))!;
    }

    public async Task<List<DiscordRole>> GetGuildRolesAsync(string guildId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"guilds/{guildId}/roles", ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<DiscordRole>>(JsonOptions, ct))!;
    }

    public async Task<List<DiscordChannel>> GetGuildChannelsAsync(string guildId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"guilds/{guildId}/channels", ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<DiscordChannel>>(JsonOptions, ct))!;
    }

    public async Task<List<DiscordGuildMember>> GetGuildMembersAsync(
        string guildId, int limit = 1000, string? after = null, CancellationToken ct = default)
    {
        var url = $"guilds/{guildId}/members?limit={limit}";
        if (after is not null)
            url += $"&after={after}";
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<DiscordGuildMember>>(JsonOptions, ct))!;
    }

    public async Task<List<DiscordMessage>> GetChannelMessagesAsync(
        string channelId, int limit = 100, string? after = null, CancellationToken ct = default)
    {
        var url = $"channels/{channelId}/messages?limit={limit}";
        if (after is not null)
            url += $"&after={after}";
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<DiscordMessage>>(JsonOptions, ct))!;
    }

    public async Task<List<DiscordMessage>> GetPinnedMessagesAsync(
        string channelId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"channels/{channelId}/pins", ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<DiscordMessage>>(JsonOptions, ct))!;
    }

    public async Task<List<DiscordEmoji>> GetGuildEmojisAsync(string guildId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"guilds/{guildId}/emojis", ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<DiscordEmoji>>(JsonOptions, ct))!;
    }

    public async Task<Stream> DownloadFileAsync(string url, CancellationToken ct = default)
    {
        var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(ct);
    }
}

// Discord API response DTOs

public record DiscordGuild(
    string Id,
    string Name,
    string? Icon,
    int? ApproximateMemberCount);

public record DiscordRole(
    string Id,
    string Name,
    int Color,
    bool Hoist,
    int Position,
    long Permissions,
    bool Managed,
    bool Mentionable);

public record DiscordChannel(
    string Id,
    int Type,       // 0=text, 2=voice, 4=category
    string? Name,
    int? Position,
    string? ParentId,
    List<DiscordPermissionOverwrite>? PermissionOverwrites);

public record DiscordPermissionOverwrite(
    string Id,
    int Type,        // 0=role, 1=member
    string Allow,
    string Deny);

public record DiscordGuildMember(
    DiscordUser? User,
    string? Nick,
    string? Avatar,
    List<string>? Roles,
    string JoinedAt);

public record DiscordUser(
    string Id,
    string Username,
    string? GlobalName,
    string? Avatar,
    string? Discriminator);

public record DiscordMessage(
    string Id,
    string? Content,
    DiscordUser Author,
    string Timestamp,
    string? EditedTimestamp,
    List<DiscordAttachment>? Attachments,
    List<DiscordReaction>? Reactions,
    DiscordMessageReference? MessageReference,
    int Type);      // 0=DEFAULT, 19=REPLY

public record DiscordAttachment(
    string Id,
    string Filename,
    int Size,
    string Url,
    string? ContentType);

public record DiscordReaction(
    int Count,
    DiscordReactionEmoji Emoji);

public record DiscordReactionEmoji(
    string? Id,
    string? Name);

public record DiscordMessageReference(
    string? MessageId,
    string? ChannelId,
    string? GuildId);

public record DiscordEmoji(
    string? Id,
    string? Name,
    bool? Animated,
    DiscordUser? User);
```

- [ ] **Step 2: Verify the API builds**

```bash
cd apps/api/Codec.Api
dotnet build
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add apps/api/Codec.Api/Services/DiscordApiClient.cs
git commit -m "feat(api): add Discord REST API client with DTOs"
```

---

## Task 5: Request DTOs

**Files:**
- Create: `apps/api/Codec.Api/Models/StartDiscordImportRequest.cs`
- Create: `apps/api/Codec.Api/Models/ClaimDiscordIdentityRequest.cs`

- [ ] **Step 1: Create request DTOs**

Create `apps/api/Codec.Api/Models/StartDiscordImportRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class StartDiscordImportRequest
{
    [Required]
    public string BotToken { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string DiscordGuildId { get; set; } = string.Empty;
}
```

Create `apps/api/Codec.Api/Models/ClaimDiscordIdentityRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class ClaimDiscordIdentityRequest
{
    [Required]
    [MaxLength(20)]
    public string DiscordUserId { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Commit**

```bash
git add apps/api/Codec.Api/Models/StartDiscordImportRequest.cs apps/api/Codec.Api/Models/ClaimDiscordIdentityRequest.cs
git commit -m "feat(api): add Discord import request DTOs"
```

---

## Task 6: Discord Import Service

**Files:**
- Create: `apps/api/Codec.Api/Services/DiscordImportService.cs`

This is the core orchestration service. It's long but straightforward — each method handles one import stage.

- [ ] **Step 1: Create DiscordImportService**

Create `apps/api/Codec.Api/Services/DiscordImportService.cs`:

```csharp
using Codec.Api.Data;
using Codec.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Codec.Api.Hubs;

namespace Codec.Api.Services;

public class DiscordImportService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<ChatHub> _hub;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<DiscordImportService> _logger;

    public DiscordImportService(
        IServiceScopeFactory scopeFactory,
        IHubContext<ChatHub> hub,
        IFileStorageService fileStorage,
        ILogger<DiscordImportService> logger)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task RunImportAsync(Guid importId, string botToken, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CodecDbContext>();
        var discordClient = scope.ServiceProvider.GetRequiredService<DiscordApiClient>();
        discordClient.SetBotToken(botToken);

        var import = await db.DiscordImports.FindAsync([importId], ct);
        if (import is null) return;

        import.Status = DiscordImportStatus.InProgress;
        import.StartedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        try
        {
            var serverId = import.ServerId;
            var guildId = import.DiscordGuildId;
            var group = _hub.Clients.Group($"server-{serverId}");

            // 1. Roles
            await group.SendAsync("ImportProgress", new { stage = "Roles", completed = 0, total = 0, percentComplete = 0f }, ct);
            var roleMap = await ImportRolesAsync(db, discordClient, serverId, guildId, importId, ct);
            await db.SaveChangesAsync(ct);

            // 2. Categories
            await group.SendAsync("ImportProgress", new { stage = "Categories", completed = 0, total = 0, percentComplete = 0f }, ct);
            var categoryMap = await ImportCategoriesAsync(db, discordClient, serverId, guildId, importId, ct);
            await db.SaveChangesAsync(ct);

            // 3. Channels
            await group.SendAsync("ImportProgress", new { stage = "Channels", completed = 0, total = 0, percentComplete = 0f }, ct);
            var channelMap = await ImportChannelsAsync(db, discordClient, serverId, guildId, importId, categoryMap, ct);
            import.ImportedChannels = channelMap.Count;
            await db.SaveChangesAsync(ct);

            // 4. Channel permission overrides
            await ImportChannelPermissionOverridesAsync(db, discordClient, guildId, channelMap, roleMap, ct);
            await db.SaveChangesAsync(ct);

            // 5. Custom emojis
            await group.SendAsync("ImportProgress", new { stage = "Emojis", completed = 0, total = 0, percentComplete = 0f }, ct);
            await ImportEmojisAsync(db, discordClient, serverId, guildId, importId, ct);
            await db.SaveChangesAsync(ct);

            // 6. Members
            await group.SendAsync("ImportProgress", new { stage = "Members", completed = 0, total = 0, percentComplete = 0f }, ct);
            var memberCount = await ImportMembersAsync(db, discordClient, serverId, guildId, ct);
            import.ImportedMembers = memberCount;
            await db.SaveChangesAsync(ct);

            // 7. Messages (per channel)
            var textChannelIds = channelMap.Where(kv => true).ToList(); // all mapped channels
            var totalMessages = 0;
            for (var i = 0; i < textChannelIds.Count; i++)
            {
                var (discordChannelId, codecChannelId) = textChannelIds[i];
                await group.SendAsync("ImportProgress", new
                {
                    stage = $"Messages ({i + 1}/{textChannelIds.Count})",
                    completed = totalMessages,
                    total = 0,
                    percentComplete = (float)i / textChannelIds.Count * 100
                }, ct);

                var count = await ImportChannelMessagesAsync(
                    db, discordClient, serverId, codecChannelId, discordChannelId,
                    importId, import.LastSyncedAt, group, ct);
                totalMessages += count;
            }
            import.ImportedMessages = totalMessages;
            await db.SaveChangesAsync(ct);

            // 8. Pinned messages
            await group.SendAsync("ImportProgress", new { stage = "Pins", completed = 0, total = 0, percentComplete = 95f }, ct);
            await ImportPinnedMessagesAsync(db, discordClient, serverId, channelMap, importId, ct);
            await db.SaveChangesAsync(ct);

            // Complete
            import.Status = DiscordImportStatus.Completed;
            import.CompletedAt = DateTimeOffset.UtcNow;
            import.LastSyncedAt = DateTimeOffset.UtcNow;
            import.EncryptedBotToken = null; // clear token
            await db.SaveChangesAsync(ct);

            await group.SendAsync("ImportCompleted", new
            {
                importedChannels = import.ImportedChannels,
                importedMessages = import.ImportedMessages,
                importedMembers = import.ImportedMembers
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Discord import {ImportId} failed", importId);
            import.Status = DiscordImportStatus.Failed;
            import.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            import.EncryptedBotToken = null;
            await db.SaveChangesAsync(CancellationToken.None);

            var group = _hub.Clients.Group($"server-{import.ServerId}");
            await group.SendAsync("ImportFailed", new { errorMessage = import.ErrorMessage }, CancellationToken.None);
        }
    }

    private async Task<Dictionary<string, Guid>> ImportRolesAsync(
        CodecDbContext db, DiscordApiClient discord, Guid serverId, string guildId, Guid importId, CancellationToken ct)
    {
        var discordRoles = await discord.GetGuildRolesAsync(guildId, ct);
        var roleMap = new Dictionary<string, Guid>();

        // Find existing @everyone role
        var everyoneRole = await db.ServerRoles
            .FirstOrDefaultAsync(r => r.ServerId == serverId && r.Name == "@everyone" && r.IsSystemRole, ct);

        foreach (var dr in discordRoles.OrderBy(r => r.Position))
        {
            // Check if already imported
            var existing = await db.DiscordEntityMappings
                .FirstOrDefaultAsync(m => m.ServerId == serverId && m.DiscordEntityId == dr.Id && m.EntityType == DiscordEntityType.Role, ct);
            if (existing is not null)
            {
                roleMap[dr.Id] = existing.CodecEntityId;
                continue;
            }

            // Map @everyone
            if (dr.Name == "@everyone" && everyoneRole is not null)
            {
                everyoneRole.Permissions = DiscordPermissionMapper.MapPermissions(dr.Permissions);
                roleMap[dr.Id] = everyoneRole.Id;
                db.DiscordEntityMappings.Add(new DiscordEntityMapping
                {
                    Id = Guid.NewGuid(),
                    DiscordImportId = importId,
                    ServerId = serverId,
                    DiscordEntityId = dr.Id,
                    EntityType = DiscordEntityType.Role,
                    CodecEntityId = everyoneRole.Id
                });
                continue;
            }

            // Skip managed roles (bots)
            if (dr.Managed) continue;

            // Ensure unique name
            var roleName = dr.Name;
            var nameExists = await db.ServerRoles.AnyAsync(r => r.ServerId == serverId && r.Name == roleName, ct);
            if (nameExists) roleName = $"{roleName} (imported)";

            var role = new ServerRoleEntity
            {
                Id = Guid.NewGuid(),
                ServerId = serverId,
                Name = roleName,
                Color = dr.Color != 0 ? $"#{dr.Color:X6}" : null,
                Position = dr.Position + 10, // offset to avoid collision with system roles
                Permissions = DiscordPermissionMapper.MapPermissions(dr.Permissions),
                IsSystemRole = false,
                IsHoisted = dr.Hoist,
                IsMentionable = dr.Mentionable
            };
            db.ServerRoles.Add(role);
            roleMap[dr.Id] = role.Id;

            db.DiscordEntityMappings.Add(new DiscordEntityMapping
            {
                Id = Guid.NewGuid(),
                DiscordImportId = importId,
                ServerId = serverId,
                DiscordEntityId = dr.Id,
                EntityType = DiscordEntityType.Role,
                CodecEntityId = role.Id
            });
        }

        return roleMap;
    }

    private async Task<Dictionary<string, Guid>> ImportCategoriesAsync(
        CodecDbContext db, DiscordApiClient discord, Guid serverId, string guildId, Guid importId, CancellationToken ct)
    {
        var channels = await discord.GetGuildChannelsAsync(guildId, ct);
        var categories = channels.Where(c => c.Type == 4).OrderBy(c => c.Position).ToList();
        var categoryMap = new Dictionary<string, Guid>();

        foreach (var dc in categories)
        {
            var existing = await db.DiscordEntityMappings
                .FirstOrDefaultAsync(m => m.ServerId == serverId && m.DiscordEntityId == dc.Id && m.EntityType == DiscordEntityType.Category, ct);
            if (existing is not null)
            {
                categoryMap[dc.Id] = existing.CodecEntityId;
                continue;
            }

            var category = new ChannelCategory
            {
                Id = Guid.NewGuid(),
                ServerId = serverId,
                Name = dc.Name ?? "Unnamed",
                Position = dc.Position ?? 0
            };
            db.ChannelCategories.Add(category);
            categoryMap[dc.Id] = category.Id;

            db.DiscordEntityMappings.Add(new DiscordEntityMapping
            {
                Id = Guid.NewGuid(),
                DiscordImportId = importId,
                ServerId = serverId,
                DiscordEntityId = dc.Id,
                EntityType = DiscordEntityType.Category,
                CodecEntityId = category.Id
            });
        }

        return categoryMap;
    }

    private async Task<Dictionary<string, Guid>> ImportChannelsAsync(
        CodecDbContext db, DiscordApiClient discord, Guid serverId, string guildId, Guid importId,
        Dictionary<string, Guid> categoryMap, CancellationToken ct)
    {
        var allChannels = await discord.GetGuildChannelsAsync(guildId, ct);
        var channels = allChannels
            .Where(c => c.Type is 0 or 2) // text or voice
            .OrderBy(c => c.Position)
            .ToList();
        var channelMap = new Dictionary<string, Guid>();

        foreach (var dc in channels)
        {
            var existing = await db.DiscordEntityMappings
                .FirstOrDefaultAsync(m => m.ServerId == serverId && m.DiscordEntityId == dc.Id && m.EntityType == DiscordEntityType.Channel, ct);
            if (existing is not null)
            {
                channelMap[dc.Id] = existing.CodecEntityId;
                continue;
            }

            Guid? categoryId = null;
            if (dc.ParentId is not null && categoryMap.TryGetValue(dc.ParentId, out var catId))
                categoryId = catId;

            var channel = new Channel
            {
                Id = Guid.NewGuid(),
                ServerId = serverId,
                Name = dc.Name ?? "unnamed",
                Type = dc.Type == 2 ? ChannelType.Voice : ChannelType.Text,
                Position = dc.Position ?? 0,
                CategoryId = categoryId
            };
            db.Channels.Add(channel);
            channelMap[dc.Id] = channel.Id;

            db.DiscordEntityMappings.Add(new DiscordEntityMapping
            {
                Id = Guid.NewGuid(),
                DiscordImportId = importId,
                ServerId = serverId,
                DiscordEntityId = dc.Id,
                EntityType = DiscordEntityType.Channel,
                CodecEntityId = channel.Id
            });
        }

        return channelMap;
    }

    private async Task ImportChannelPermissionOverridesAsync(
        CodecDbContext db, DiscordApiClient discord, string guildId,
        Dictionary<string, Guid> channelMap, Dictionary<string, Guid> roleMap, CancellationToken ct)
    {
        var allChannels = await discord.GetGuildChannelsAsync(guildId, ct);

        foreach (var dc in allChannels.Where(c => c.Type is 0 or 2))
        {
            if (dc.PermissionOverwrites is null || !channelMap.TryGetValue(dc.Id, out var codecChannelId))
                continue;

            foreach (var overwrite in dc.PermissionOverwrites)
            {
                // Only import role overrides (type 0), skip member overrides (type 1)
                if (overwrite.Type != 0) continue;
                if (!roleMap.TryGetValue(overwrite.Id, out var codecRoleId)) continue;

                var exists = await db.ChannelPermissionOverrides
                    .AnyAsync(o => o.ChannelId == codecChannelId && o.RoleId == codecRoleId, ct);
                if (exists) continue;

                var allow = long.TryParse(overwrite.Allow, out var a) ? a : 0;
                var deny = long.TryParse(overwrite.Deny, out var d) ? d : 0;

                db.ChannelPermissionOverrides.Add(new ChannelPermissionOverride
                {
                    Id = Guid.NewGuid(),
                    ChannelId = codecChannelId,
                    RoleId = codecRoleId,
                    Allow = DiscordPermissionMapper.MapPermissions(allow),
                    Deny = DiscordPermissionMapper.MapPermissions(deny)
                });
            }
        }
    }

    private async Task ImportEmojisAsync(
        CodecDbContext db, DiscordApiClient discord, Guid serverId, string guildId, Guid importId, CancellationToken ct)
    {
        var emojis = await discord.GetGuildEmojisAsync(guildId, ct);

        foreach (var de in emojis)
        {
            if (de.Id is null || de.Name is null) continue;

            var existing = await db.DiscordEntityMappings
                .FirstOrDefaultAsync(m => m.ServerId == serverId && m.DiscordEntityId == de.Id && m.EntityType == DiscordEntityType.Emoji, ct);
            if (existing is not null) continue;

            // Check for name conflict
            var nameExists = await db.CustomEmojis.AnyAsync(e => e.ServerId == serverId && e.Name == de.Name, ct);
            var emojiName = nameExists ? $"{de.Name}_imported" : de.Name;

            // Download emoji image
            var ext = de.Animated == true ? "gif" : "png";
            var emojiUrl = $"https://cdn.discordapp.com/emojis/{de.Id}.{ext}";
            string storedUrl;
            try
            {
                using var stream = await discord.DownloadFileAsync(emojiUrl, ct);
                var fileName = $"emoji_{de.Id}.{ext}";
                storedUrl = await _fileStorage.SaveFileAsync(stream, fileName, $"emojis/{serverId}", ct);
            }
            catch
            {
                storedUrl = emojiUrl; // fallback to Discord CDN
            }

            var emoji = new CustomEmoji
            {
                Id = Guid.NewGuid(),
                ServerId = serverId,
                Name = emojiName,
                ImageUrl = storedUrl,
                ContentType = ext == "gif" ? "image/gif" : "image/png",
                IsAnimated = de.Animated == true
            };
            db.CustomEmojis.Add(emoji);

            db.DiscordEntityMappings.Add(new DiscordEntityMapping
            {
                Id = Guid.NewGuid(),
                DiscordImportId = importId,
                ServerId = serverId,
                DiscordEntityId = de.Id,
                EntityType = DiscordEntityType.Emoji,
                CodecEntityId = emoji.Id
            });
        }
    }

    private async Task<int> ImportMembersAsync(
        CodecDbContext db, DiscordApiClient discord, Guid serverId, string guildId, CancellationToken ct)
    {
        var count = 0;
        string? after = null;

        while (true)
        {
            var members = await discord.GetGuildMembersAsync(guildId, 1000, after, ct);
            if (members.Count == 0) break;

            foreach (var dm in members)
            {
                if (dm.User is null) continue;

                var exists = await db.DiscordUserMappings
                    .AnyAsync(m => m.ServerId == serverId && m.DiscordUserId == dm.User.Id, ct);
                if (exists) continue;

                var displayName = dm.Nick ?? dm.User.GlobalName ?? dm.User.Username;
                string? avatarUrl = null;
                if (dm.User.Avatar is not null)
                    avatarUrl = $"https://cdn.discordapp.com/avatars/{dm.User.Id}/{dm.User.Avatar}.png";

                db.DiscordUserMappings.Add(new DiscordUserMapping
                {
                    Id = Guid.NewGuid(),
                    ServerId = serverId,
                    DiscordUserId = dm.User.Id,
                    DiscordUsername = displayName,
                    DiscordAvatarUrl = avatarUrl
                });
                count++;
            }

            await db.SaveChangesAsync(ct);
            after = members[^1].User?.Id;
            if (members.Count < 1000) break;
        }

        return count;
    }

    private async Task<int> ImportChannelMessagesAsync(
        CodecDbContext db, DiscordApiClient discord, Guid serverId, Guid codecChannelId, string discordChannelId,
        Guid importId, DateTimeOffset? lastSyncedAt, IClientProxy group, CancellationToken ct)
    {
        var count = 0;
        string? after = null;

        // For re-sync, find the last imported message's Discord ID
        if (lastSyncedAt is not null)
        {
            var lastMapping = await db.DiscordEntityMappings
                .Where(m => m.ServerId == serverId && m.EntityType == DiscordEntityType.Message)
                .OrderByDescending(m => m.DiscordEntityId) // snowflake = chronological
                .FirstOrDefaultAsync(ct);
            if (lastMapping is not null)
                after = lastMapping.DiscordEntityId;
        }

        while (true)
        {
            List<DiscordMessage> messages;
            try
            {
                messages = await discord.GetChannelMessagesAsync(discordChannelId, 100, after, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("Bot lacks access to channel {ChannelId}, skipping", discordChannelId);
                break;
            }

            if (messages.Count == 0) break;

            // Discord returns newest first when using "before", but oldest first when using "after"
            // We use "after" for forward pagination
            foreach (var dm in messages)
            {
                // Skip non-default message types (joins, boosts, etc.) — only import DEFAULT (0) and REPLY (19)
                if (dm.Type is not (0 or 19)) continue;

                var existing = await db.DiscordEntityMappings
                    .AnyAsync(m => m.ServerId == serverId && m.DiscordEntityId == dm.Id && m.EntityType == DiscordEntityType.Message, ct);
                if (existing) continue;

                // Look up reply target
                Guid? replyToMessageId = null;
                if (dm.MessageReference?.MessageId is not null)
                {
                    var replyMapping = await db.DiscordEntityMappings
                        .FirstOrDefaultAsync(m => m.ServerId == serverId && m.DiscordEntityId == dm.MessageReference.MessageId && m.EntityType == DiscordEntityType.Message, ct);
                    replyToMessageId = replyMapping?.CodecEntityId;
                }

                // Handle attachments — download first one as the message's file
                string? fileUrl = null, fileName = null, fileContentType = null, imageUrl = null;
                long? fileSize = null;
                if (dm.Attachments is { Count: > 0 })
                {
                    var att = dm.Attachments[0];
                    try
                    {
                        using var stream = await discord.DownloadFileAsync(att.Url, ct);
                        var storedPath = await _fileStorage.SaveFileAsync(
                            stream, att.Filename, $"imports/{serverId}/{codecChannelId}", ct);

                        if (att.ContentType?.StartsWith("image/") == true)
                            imageUrl = storedPath;
                        else
                        {
                            fileUrl = storedPath;
                            fileName = att.Filename;
                            fileContentType = att.ContentType;
                            fileSize = att.Size;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to download attachment {AttachmentId}", att.Id);
                        // Fall back to Discord URL
                        if (att.ContentType?.StartsWith("image/") == true)
                            imageUrl = att.Url;
                        else
                        {
                            fileUrl = att.Url;
                            fileName = att.Filename;
                            fileContentType = att.ContentType;
                            fileSize = att.Size;
                        }
                    }
                }

                var message = new Message
                {
                    Id = Guid.NewGuid(),
                    ChannelId = codecChannelId,
                    AuthorUserId = null, // no Codec user yet
                    AuthorName = dm.Author.GlobalName ?? dm.Author.Username,
                    ImportedAuthorName = dm.Author.GlobalName ?? dm.Author.Username,
                    ImportedAuthorAvatarUrl = dm.Author.Avatar is not null
                        ? $"https://cdn.discordapp.com/avatars/{dm.Author.Id}/{dm.Author.Avatar}.png"
                        : null,
                    Body = dm.Content ?? string.Empty,
                    ImageUrl = imageUrl,
                    FileUrl = fileUrl,
                    FileName = fileName,
                    FileSize = fileSize,
                    FileContentType = fileContentType,
                    ReplyToMessageId = replyToMessageId,
                    CreatedAt = DateTimeOffset.Parse(dm.Timestamp),
                    EditedAt = dm.EditedTimestamp is not null ? DateTimeOffset.Parse(dm.EditedTimestamp) : null
                };
                db.Messages.Add(message);

                db.DiscordEntityMappings.Add(new DiscordEntityMapping
                {
                    Id = Guid.NewGuid(),
                    DiscordImportId = importId,
                    ServerId = serverId,
                    DiscordEntityId = dm.Id,
                    EntityType = DiscordEntityType.Message,
                    CodecEntityId = message.Id
                });

                // Import reactions (without user attribution — Discord API doesn't give us per-user reactions in bulk)
                // We skip reactions for now since Codec requires a UserId for each Reaction record
                // and we don't have Codec users for imported members.

                count++;
            }

            await db.SaveChangesAsync(ct);

            if (count % 100 == 0)
            {
                await group.SendAsync("ImportProgress", new
                {
                    stage = "Messages",
                    completed = count,
                    total = 0,
                    percentComplete = 0f
                }, ct);
            }

            after = messages[^1].Id;
            if (messages.Count < 100) break;
        }

        return count;
    }

    private async Task ImportPinnedMessagesAsync(
        CodecDbContext db, DiscordApiClient discord, Guid serverId,
        Dictionary<string, Guid> channelMap, Guid importId, CancellationToken ct)
    {
        foreach (var (discordChannelId, codecChannelId) in channelMap)
        {
            List<DiscordMessage> pins;
            try
            {
                pins = await discord.GetPinnedMessagesAsync(discordChannelId, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                continue;
            }

            foreach (var pin in pins)
            {
                var messageMapping = await db.DiscordEntityMappings
                    .FirstOrDefaultAsync(m => m.ServerId == serverId && m.DiscordEntityId == pin.Id && m.EntityType == DiscordEntityType.Message, ct);
                if (messageMapping is null) continue;

                var alreadyPinned = await db.PinnedMessages
                    .AnyAsync(p => p.ChannelId == codecChannelId && p.MessageId == messageMapping.CodecEntityId, ct);
                if (alreadyPinned) continue;

                db.PinnedMessages.Add(new PinnedMessage
                {
                    Id = Guid.NewGuid(),
                    MessageId = messageMapping.CodecEntityId,
                    ChannelId = codecChannelId,
                    PinnedByUserId = null, // unknown for imports
                    PinnedAt = DateTimeOffset.UtcNow
                });
            }
        }
    }
}
```

- [ ] **Step 2: Verify the API builds**

```bash
cd apps/api/Codec.Api
dotnet build
```

Expected: Build succeeded. If `IFileStorageService.SaveFileAsync` has a different signature, adapt accordingly — check the interface definition and adjust the call.

- [ ] **Step 3: Commit**

```bash
git add apps/api/Codec.Api/Services/DiscordImportService.cs
git commit -m "feat(api): add Discord import orchestration service"
```

---

## Task 7: Background Worker + Service Registration

**Files:**
- Create: `apps/api/Codec.Api/Services/DiscordImportWorker.cs`
- Modify: `apps/api/Codec.Api/Program.cs`

- [ ] **Step 1: Create DiscordImportWorker**

Create `apps/api/Codec.Api/Services/DiscordImportWorker.cs`:

```csharp
using System.Threading.Channels;
using Codec.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Services;

public class DiscordImportWorker : BackgroundService
{
    private readonly Channel<Guid> _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DiscordImportWorker> _logger;

    public DiscordImportWorker(
        Channel<Guid> queue,
        IServiceScopeFactory scopeFactory,
        ILogger<DiscordImportWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Discord import worker started");

        await foreach (var importId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                _logger.LogInformation("Processing Discord import {ImportId}", importId);

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CodecDbContext>();
                var importService = scope.ServiceProvider.GetRequiredService<DiscordImportService>();

                var import = await db.DiscordImports.FindAsync([importId], stoppingToken);
                if (import?.EncryptedBotToken is null)
                {
                    _logger.LogWarning("Import {ImportId} not found or has no bot token", importId);
                    continue;
                }

                // For now, bot token is stored as-is (encryption is a follow-up).
                // In production, decrypt here using Data Protection API.
                await importService.RunImportAsync(importId, import.EncryptedBotToken, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error processing import {ImportId}", importId);
            }
        }
    }
}
```

- [ ] **Step 2: Register services in Program.cs**

In `apps/api/Codec.Api/Program.cs`, add these registrations near the other service registrations (around line 390):

```csharp
// Discord import
builder.Services.AddSingleton(Channel.CreateUnbounded<Guid>(
    new UnboundedChannelOptions { SingleReader = true }));
builder.Services.AddScoped<DiscordImportService>();
builder.Services.AddHostedService<DiscordImportWorker>();
builder.Services.AddHttpClient<DiscordApiClient>()
    .AddHttpMessageHandler<DiscordRateLimitHandler>();
builder.Services.AddTransient<DiscordRateLimitHandler>();
```

Also add this using statement at the top of Program.cs:

```csharp
using System.Threading.Channels;
```

- [ ] **Step 3: Verify the API builds**

```bash
cd apps/api/Codec.Api
dotnet build
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add apps/api/Codec.Api/Services/DiscordImportWorker.cs apps/api/Codec.Api/Program.cs
git commit -m "feat(api): add Discord import background worker and service registration"
```

---

## Task 8: Discord Import Controller

**Files:**
- Create: `apps/api/Codec.Api/Controllers/DiscordImportController.cs`

- [ ] **Step 1: Create the controller**

Create `apps/api/Codec.Api/Controllers/DiscordImportController.cs`:

```csharp
using System.Security.Claims;
using System.Threading.Channels;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Controllers;

[ApiController]
[Route("servers/{serverId}/discord-import")]
[Authorize]
public class DiscordImportController : ControllerBase
{
    private readonly CodecDbContext _db;
    private readonly Channel<Guid> _importQueue;
    private readonly DiscordApiClient _discordClient;
    private readonly ILogger<DiscordImportController> _logger;

    public DiscordImportController(
        CodecDbContext db,
        Channel<Guid> importQueue,
        DiscordApiClient discordClient,
        ILogger<DiscordImportController> logger)
    {
        _db = db;
        _importQueue = importQueue;
        _discordClient = discordClient;
        _logger = logger;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<bool> HasManageServerPermission(Guid serverId)
    {
        var userId = GetUserId();
        var user = await _db.Users.FindAsync(userId);
        if (user?.IsGlobalAdmin == true) return true;

        var memberRoles = await _db.ServerMemberRoles
            .Where(mr => mr.UserId == userId && mr.Role!.ServerId == serverId)
            .Select(mr => mr.Role!.Permissions)
            .ToListAsync();

        var combined = memberRoles.Aggregate(Permission.None, (a, b) => a | b);
        return combined.Has(Permission.ManageServer);
    }

    [HttpPost]
    public async Task<IActionResult> StartImport(Guid serverId, [FromBody] StartDiscordImportRequest request)
    {
        if (!await HasManageServerPermission(serverId))
            return Forbid();

        // Check for existing in-progress import
        var existing = await _db.DiscordImports
            .FirstOrDefaultAsync(d => d.ServerId == serverId &&
                (d.Status == DiscordImportStatus.Pending || d.Status == DiscordImportStatus.InProgress));
        if (existing is not null)
            return Conflict(new { error = "An import is already in progress for this server." });

        // Validate bot token against Discord API
        _discordClient.SetBotToken(request.BotToken);
        DiscordGuild guild;
        try
        {
            guild = await _discordClient.GetGuildAsync(request.DiscordGuildId);
        }
        catch (HttpRequestException)
        {
            return BadRequest(new { error = "Invalid bot token or guild ID. Ensure the bot has been added to the Discord server." });
        }

        var import = new DiscordImport
        {
            Id = Guid.NewGuid(),
            ServerId = serverId,
            DiscordGuildId = request.DiscordGuildId,
            EncryptedBotToken = request.BotToken, // TODO: encrypt with Data Protection API
            Status = DiscordImportStatus.Pending,
            InitiatedByUserId = GetUserId()
        };

        _db.DiscordImports.Add(import);
        await _db.SaveChangesAsync();

        await _importQueue.Writer.WriteAsync(import.Id);

        _logger.LogInformation("Discord import {ImportId} queued for server {ServerId} from guild {GuildName}",
            import.Id, serverId, guild.Name);

        return Accepted(new { id = import.Id });
    }

    [HttpGet]
    public async Task<IActionResult> GetStatus(Guid serverId)
    {
        if (!await HasManageServerPermission(serverId))
            return Forbid();

        var import = await _db.DiscordImports
            .Where(d => d.ServerId == serverId)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync();

        if (import is null)
            return NotFound(new { error = "No import found for this server." });

        return Ok(new
        {
            id = import.Id,
            status = import.Status.ToString(),
            importedChannels = import.ImportedChannels,
            importedMessages = import.ImportedMessages,
            importedMembers = import.ImportedMembers,
            startedAt = import.StartedAt,
            completedAt = import.CompletedAt,
            errorMessage = import.ErrorMessage,
            discordGuildId = import.DiscordGuildId
        });
    }

    [HttpPost("resync")]
    public async Task<IActionResult> Resync(Guid serverId, [FromBody] StartDiscordImportRequest request)
    {
        if (!await HasManageServerPermission(serverId))
            return Forbid();

        var lastImport = await _db.DiscordImports
            .Where(d => d.ServerId == serverId && d.Status == DiscordImportStatus.Completed)
            .OrderByDescending(d => d.CompletedAt)
            .FirstOrDefaultAsync();

        if (lastImport is null)
            return BadRequest(new { error = "No completed import found to re-sync." });

        // Validate bot token
        _discordClient.SetBotToken(request.BotToken);
        try
        {
            await _discordClient.GetGuildAsync(request.DiscordGuildId);
        }
        catch (HttpRequestException)
        {
            return BadRequest(new { error = "Invalid bot token or guild ID." });
        }

        var import = new DiscordImport
        {
            Id = Guid.NewGuid(),
            ServerId = serverId,
            DiscordGuildId = request.DiscordGuildId,
            EncryptedBotToken = request.BotToken,
            Status = DiscordImportStatus.Pending,
            InitiatedByUserId = GetUserId(),
            LastSyncedAt = lastImport.LastSyncedAt
        };

        _db.DiscordImports.Add(import);
        await _db.SaveChangesAsync();

        await _importQueue.Writer.WriteAsync(import.Id);

        return Accepted(new { id = import.Id });
    }

    [HttpDelete]
    public async Task<IActionResult> CancelImport(Guid serverId)
    {
        if (!await HasManageServerPermission(serverId))
            return Forbid();

        var import = await _db.DiscordImports
            .FirstOrDefaultAsync(d => d.ServerId == serverId &&
                (d.Status == DiscordImportStatus.Pending || d.Status == DiscordImportStatus.InProgress));

        if (import is null)
            return NotFound(new { error = "No in-progress import to cancel." });

        import.Status = DiscordImportStatus.Cancelled;
        import.EncryptedBotToken = null;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("mappings")]
    public async Task<IActionResult> GetUserMappings(Guid serverId)
    {
        if (!await HasManageServerPermission(serverId))
            return Forbid();

        var mappings = await _db.DiscordUserMappings
            .Where(m => m.ServerId == serverId)
            .OrderBy(m => m.DiscordUsername)
            .Select(m => new
            {
                discordUserId = m.DiscordUserId,
                discordUsername = m.DiscordUsername,
                discordAvatarUrl = m.DiscordAvatarUrl,
                codecUserId = m.CodecUserId,
                claimedAt = m.ClaimedAt
            })
            .ToListAsync();

        return Ok(mappings);
    }

    [HttpPost("claim")]
    public async Task<IActionResult> ClaimIdentity(Guid serverId, [FromBody] ClaimDiscordIdentityRequest request)
    {
        var userId = GetUserId();

        // Check user is a member of this server
        var isMember = await _db.ServerMembers.AnyAsync(m => m.ServerId == serverId && m.UserId == userId);
        if (!isMember)
            return Forbid();

        var mapping = await _db.DiscordUserMappings
            .FirstOrDefaultAsync(m => m.ServerId == serverId && m.DiscordUserId == request.DiscordUserId);

        if (mapping is null)
            return NotFound(new { error = "Discord user mapping not found." });

        if (mapping.CodecUserId is not null)
            return Conflict(new { error = "This Discord identity has already been claimed." });

        // Optional: auto-verify via DiscordSubject
        var user = await _db.Users.FindAsync(userId);
        if (user?.DiscordSubject is not null && user.DiscordSubject != request.DiscordUserId)
            return BadRequest(new { error = "Your linked Discord account doesn't match this identity." });

        // Claim the mapping
        mapping.CodecUserId = userId;
        mapping.ClaimedAt = DateTimeOffset.UtcNow;

        // Reassign all messages from this Discord user in this server
        var channelIds = await _db.Channels
            .Where(c => c.ServerId == serverId)
            .Select(c => c.Id)
            .ToListAsync();

        await _db.Messages
            .Where(m => channelIds.Contains(m.ChannelId) &&
                        m.ImportedAuthorName != null &&
                        m.AuthorUserId == null &&
                        m.AuthorName == mapping.DiscordUsername)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.AuthorUserId, userId)
                .SetProperty(m => m.AuthorName, user!.DisplayName)
                .SetProperty(m => m.ImportedAuthorName, (string?)null)
                .SetProperty(m => m.ImportedAuthorAvatarUrl, (string?)null));

        await _db.SaveChangesAsync();

        return Ok(new { claimed = true });
    }
}
```

- [ ] **Step 2: Verify the API builds**

```bash
cd apps/api/Codec.Api
dotnet build
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add apps/api/Codec.Api/Controllers/DiscordImportController.cs
git commit -m "feat(api): add Discord import controller with claim flow"
```

---

## Task 9: Frontend Types + API Client

**Files:**
- Modify: `apps/web/src/lib/types/models.ts`
- Modify: `apps/web/src/lib/api/client.ts`

- [ ] **Step 1: Add TypeScript types**

In `apps/web/src/lib/types/models.ts`, add these types:

```typescript
/** Discord import job status. */
export type DiscordImportStatus = 'Pending' | 'InProgress' | 'Completed' | 'Failed' | 'Cancelled';

/** Discord import job. */
export type DiscordImport = {
	id: string;
	status: DiscordImportStatus;
	importedChannels: number;
	importedMessages: number;
	importedMembers: number;
	startedAt?: string | null;
	completedAt?: string | null;
	errorMessage?: string | null;
	discordGuildId: string;
};

/** Discord user mapping for identity claiming. */
export type DiscordUserMapping = {
	discordUserId: string;
	discordUsername: string;
	discordAvatarUrl?: string | null;
	codecUserId?: string | null;
	claimedAt?: string | null;
};
```

Also extend the existing `Message` type — add these two optional fields:

```typescript
importedAuthorName?: string | null;
importedAuthorAvatarUrl?: string | null;
```

- [ ] **Step 2: Add API client methods**

In `apps/web/src/lib/api/client.ts`, add these methods to the `ApiClient` class:

```typescript
startDiscordImport(
	token: string,
	serverId: string,
	botToken: string,
	discordGuildId: string
): Promise<{ id: string }> {
	return this.request(`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/discord-import`, {
		method: 'POST',
		headers: this.headers(token, true),
		body: JSON.stringify({ botToken, discordGuildId })
	});
}

getDiscordImportStatus(token: string, serverId: string): Promise<DiscordImport> {
	return this.request<DiscordImport>(
		`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/discord-import`,
		{ headers: this.headers(token) }
	);
}

resyncDiscordImport(
	token: string,
	serverId: string,
	botToken: string,
	discordGuildId: string
): Promise<{ id: string }> {
	return this.request(`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/discord-import/resync`, {
		method: 'POST',
		headers: this.headers(token, true),
		body: JSON.stringify({ botToken, discordGuildId })
	});
}

cancelDiscordImport(token: string, serverId: string): Promise<void> {
	return this.requestVoid(
		`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/discord-import`,
		{ method: 'DELETE', headers: this.headers(token) }
	);
}

getDiscordUserMappings(token: string, serverId: string): Promise<DiscordUserMapping[]> {
	return this.request<DiscordUserMapping[]>(
		`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/discord-import/mappings`,
		{ headers: this.headers(token) }
	);
}

claimDiscordIdentity(token: string, serverId: string, discordUserId: string): Promise<{ claimed: boolean }> {
	return this.request(`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/discord-import/claim`, {
		method: 'POST',
		headers: this.headers(token, true),
		body: JSON.stringify({ discordUserId })
	});
}
```

Add the import for `DiscordImport` and `DiscordUserMapping` types at the top of the file.

- [ ] **Step 3: Verify frontend builds**

```bash
cd apps/web
npm run check
```

Expected: No errors.

- [ ] **Step 4: Commit**

```bash
git add apps/web/src/lib/types/models.ts apps/web/src/lib/api/client.ts
git commit -m "feat(web): add Discord import types and API client methods"
```

---

## Task 10: SignalR Event Callbacks

**Files:**
- Modify: `apps/web/src/lib/services/chat-hub.ts`

- [ ] **Step 1: Add import event types and callbacks**

In `apps/web/src/lib/services/chat-hub.ts`, add these types alongside the existing event types:

```typescript
export type ImportProgressEvent = {
	stage: string;
	completed: number;
	total: number;
	percentComplete: number;
};

export type ImportCompletedEvent = {
	importedChannels: number;
	importedMessages: number;
	importedMembers: number;
};

export type ImportFailedEvent = {
	errorMessage: string;
};
```

Add to the `SignalRCallbacks` type:

```typescript
onImportProgress?: (event: ImportProgressEvent) => void;
onImportCompleted?: (event: ImportCompletedEvent) => void;
onImportFailed?: (event: ImportFailedEvent) => void;
```

In the `buildAndStart` method, add the callback registration alongside the other optional callbacks:

```typescript
if (callbacks.onImportProgress) {
	connection.on('ImportProgress', callbacks.onImportProgress);
}
if (callbacks.onImportCompleted) {
	connection.on('ImportCompleted', callbacks.onImportCompleted);
}
if (callbacks.onImportFailed) {
	connection.on('ImportFailed', callbacks.onImportFailed);
}
```

- [ ] **Step 2: Verify frontend builds**

```bash
cd apps/web
npm run check
```

Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add apps/web/src/lib/services/chat-hub.ts
git commit -m "feat(web): add SignalR callbacks for Discord import progress"
```

---

## Task 11: Server Store — Import State

**Files:**
- Modify: `apps/web/src/lib/state/server-store.svelte.ts`
- Modify: `apps/web/src/lib/state/ui-store.svelte.ts`

- [ ] **Step 1: Add `discord-import` to UI store category type**

In `apps/web/src/lib/state/ui-store.svelte.ts`, find the `serverSettingsCategory` type and add `'discord-import'`:

Change:
```typescript
serverSettingsCategory = $state<'general' | 'channels' | 'invites' | 'webhooks' | 'emojis' | 'roles' | 'members' | 'bans' | 'audit-log'>('general');
```

To:
```typescript
serverSettingsCategory = $state<'general' | 'channels' | 'invites' | 'webhooks' | 'emojis' | 'roles' | 'members' | 'bans' | 'audit-log' | 'discord-import'>('general');
```

- [ ] **Step 2: Add import state and methods to server store**

In `apps/web/src/lib/state/server-store.svelte.ts`, add these state fields:

```typescript
discordImport = $state<DiscordImport | null>(null);
discordUserMappings = $state<DiscordUserMapping[]>([]);
isLoadingImport = $state(false);
isStartingImport = $state(false);
```

Add these methods:

```typescript
async loadDiscordImport(serverId: string): Promise<void> {
	if (!this.auth.idToken) return;
	this.isLoadingImport = true;
	try {
		this.discordImport = await this.api.getDiscordImportStatus(this.auth.idToken, serverId);
	} catch {
		this.discordImport = null; // no import found
	} finally {
		this.isLoadingImport = false;
	}
}

async startDiscordImport(serverId: string, botToken: string, guildId: string): Promise<void> {
	if (!this.auth.idToken) return;
	this.isStartingImport = true;
	try {
		await this.api.startDiscordImport(this.auth.idToken, serverId, botToken, guildId);
		await this.loadDiscordImport(serverId);
	} catch (e) {
		this.ui.setError(e);
	} finally {
		this.isStartingImport = false;
	}
}

async resyncDiscordImport(serverId: string, botToken: string, guildId: string): Promise<void> {
	if (!this.auth.idToken) return;
	this.isStartingImport = true;
	try {
		await this.api.resyncDiscordImport(this.auth.idToken, serverId, botToken, guildId);
		await this.loadDiscordImport(serverId);
	} catch (e) {
		this.ui.setError(e);
	} finally {
		this.isStartingImport = false;
	}
}

async cancelDiscordImport(serverId: string): Promise<void> {
	if (!this.auth.idToken) return;
	try {
		await this.api.cancelDiscordImport(this.auth.idToken, serverId);
		await this.loadDiscordImport(serverId);
	} catch (e) {
		this.ui.setError(e);
	}
}

async loadDiscordUserMappings(serverId: string): Promise<void> {
	if (!this.auth.idToken) return;
	try {
		this.discordUserMappings = await this.api.getDiscordUserMappings(this.auth.idToken, serverId);
	} catch {
		this.discordUserMappings = [];
	}
}

async claimDiscordIdentity(serverId: string, discordUserId: string): Promise<void> {
	if (!this.auth.idToken) return;
	try {
		await this.api.claimDiscordIdentity(this.auth.idToken, serverId, discordUserId);
		await this.loadDiscordUserMappings(serverId);
	} catch (e) {
		this.ui.setError(e);
	}
}
```

Add the `DiscordImport` and `DiscordUserMapping` imports at the top of the file.

- [ ] **Step 3: Verify frontend builds**

```bash
cd apps/web
npm run check
```

Expected: No errors.

- [ ] **Step 4: Commit**

```bash
git add apps/web/src/lib/state/server-store.svelte.ts apps/web/src/lib/state/ui-store.svelte.ts
git commit -m "feat(web): add Discord import state management"
```

---

## Task 12: Server Settings — Discord Import Tab

**Files:**
- Create: `apps/web/src/lib/components/server-settings/ServerDiscordImport.svelte`
- Modify: `apps/web/src/lib/components/server-settings/ServerSettingsModal.svelte`
- Modify: `apps/web/src/lib/components/server-settings/ServerSettingsSidebar.svelte`

- [ ] **Step 1: Create the ServerDiscordImport component**

Create `apps/web/src/lib/components/server-settings/ServerDiscordImport.svelte`:

```svelte
<script lang="ts">
	import { getServerStore } from '$lib/state/server-store.svelte.js';
	import { getUIStore } from '$lib/state/ui-store.svelte.js';

	const servers = getServerStore();
	const ui = getUIStore();

	let botToken = $state('');
	let guildId = $state('');

	const serverId = $derived(servers.selectedServerId);
	const importStatus = $derived(servers.discordImport);
	const isInProgress = $derived(
		importStatus?.status === 'Pending' || importStatus?.status === 'InProgress'
	);
	const isCompleted = $derived(importStatus?.status === 'Completed');
	const isFailed = $derived(importStatus?.status === 'Failed');
	const mappings = $derived(servers.discordUserMappings);

	$effect(() => {
		if (serverId) {
			servers.loadDiscordImport(serverId);
		}
	});

	$effect(() => {
		if (isCompleted && serverId) {
			servers.loadDiscordUserMappings(serverId);
		}
	});

	async function handleStart() {
		if (!serverId || !botToken.trim() || !guildId.trim()) return;
		await servers.startDiscordImport(serverId, botToken.trim(), guildId.trim());
		botToken = '';
	}

	async function handleResync() {
		if (!serverId || !botToken.trim()) return;
		const resolvedGuildId = importStatus?.discordGuildId ?? guildId.trim();
		if (!resolvedGuildId) return;
		await servers.resyncDiscordImport(serverId, botToken.trim(), resolvedGuildId);
		botToken = '';
	}

	async function handleCancel() {
		if (!serverId) return;
		await servers.cancelDiscordImport(serverId);
	}

	async function handleClaim(discordUserId: string) {
		if (!serverId) return;
		await servers.claimDiscordIdentity(serverId, discordUserId);
	}
</script>

<div class="discord-import">
	<h2>Import from Discord</h2>
	<p class="description">
		Import channels, messages, roles, emojis, and members from a Discord server using a bot token.
	</p>

	{#if servers.isLoadingImport}
		<p class="loading">Loading import status...</p>
	{:else if isInProgress}
		<div class="status-card in-progress">
			<h3>Import in Progress</h3>
			<div class="progress-stats">
				<span>{importStatus?.importedChannels ?? 0} channels</span>
				<span>{importStatus?.importedMessages ?? 0} messages</span>
				<span>{importStatus?.importedMembers ?? 0} members</span>
			</div>
			<div class="progress-bar">
				<div class="progress-fill" style="width: 50%"></div>
			</div>
			<button class="cancel-btn" onclick={handleCancel}>Cancel Import</button>
		</div>
	{:else if isFailed}
		<div class="status-card failed">
			<h3>Import Failed</h3>
			<p class="error-msg">{importStatus?.errorMessage}</p>
			<p class="partial-stats">
				Imported so far: {importStatus?.importedChannels ?? 0} channels,
				{importStatus?.importedMessages ?? 0} messages,
				{importStatus?.importedMembers ?? 0} members
			</p>
		</div>

		<div class="import-form">
			<h3>Retry Import</h3>
			<label class="form-label">
				Bot Token
				<input type="password" bind:value={botToken} placeholder="Paste your Discord bot token" class="form-input" />
			</label>
			<label class="form-label">
				Discord Guild ID
				<input type="text" bind:value={guildId} placeholder="e.g. 123456789012345678" class="form-input" />
			</label>
			<button
				class="start-btn"
				disabled={servers.isStartingImport || !botToken.trim() || !guildId.trim()}
				onclick={handleStart}
			>
				{servers.isStartingImport ? 'Starting...' : 'Retry Import'}
			</button>
		</div>
	{:else if isCompleted}
		<div class="status-card completed">
			<h3>Import Complete</h3>
			<div class="progress-stats">
				<span>{importStatus?.importedChannels} channels</span>
				<span>{importStatus?.importedMessages} messages</span>
				<span>{importStatus?.importedMembers} members</span>
			</div>
			<p class="completed-at">Completed {importStatus?.completedAt ? new Date(importStatus.completedAt).toLocaleString() : ''}</p>
		</div>

		<div class="import-form">
			<h3>Re-sync</h3>
			<p class="description">Pull in new messages since the last import. Requires a fresh bot token.</p>
			<label class="form-label">
				Bot Token
				<input type="password" bind:value={botToken} placeholder="Paste your Discord bot token" class="form-input" />
			</label>
			<button
				class="start-btn"
				disabled={servers.isStartingImport || !botToken.trim()}
				onclick={handleResync}
			>
				{servers.isStartingImport ? 'Starting...' : 'Re-sync'}
			</button>
		</div>

		{#if mappings.length > 0}
			<div class="claim-section">
				<h3>Claim Discord Identity</h3>
				<p class="description">Members can claim their Discord identity to reassign their imported messages.</p>
				<ul class="mapping-list">
					{#each mappings as mapping (mapping.discordUserId)}
						<li class="mapping-item">
							<div class="mapping-user">
								{#if mapping.discordAvatarUrl}
									<img class="mapping-avatar" src={mapping.discordAvatarUrl} alt="" />
								{:else}
									<div class="mapping-avatar-placeholder">
										{mapping.discordUsername.slice(0, 1).toUpperCase()}
									</div>
								{/if}
								<span class="mapping-name">{mapping.discordUsername}</span>
							</div>
							{#if mapping.codecUserId}
								<span class="claimed-badge">Claimed</span>
							{:else}
								<button class="claim-btn" onclick={() => handleClaim(mapping.discordUserId)}>
									This is me
								</button>
							{/if}
						</li>
					{/each}
				</ul>
			</div>
		{/if}
	{:else}
		<div class="import-form">
			<h3>Start Import</h3>
			<p class="description">
				Create a Discord bot, add it to your server with read permissions, then paste its token below.
			</p>
			<label class="form-label">
				Bot Token
				<input type="password" bind:value={botToken} placeholder="Paste your Discord bot token" class="form-input" />
			</label>
			<label class="form-label">
				Discord Guild ID
				<input type="text" bind:value={guildId} placeholder="e.g. 123456789012345678" class="form-input" />
			</label>
			<button
				class="start-btn"
				disabled={servers.isStartingImport || !botToken.trim() || !guildId.trim()}
				onclick={handleStart}
			>
				{servers.isStartingImport ? 'Starting...' : 'Start Import'}
			</button>
		</div>
	{/if}
</div>

<style>
	.discord-import {
		max-width: 600px;
	}

	h2 {
		margin: 0 0 8px;
		font-size: 20px;
		color: var(--text-header);
	}

	h3 {
		margin: 0 0 8px;
		font-size: 16px;
		color: var(--text-header);
	}

	.description {
		color: var(--text-muted);
		font-size: 14px;
		margin: 0 0 20px;
		line-height: 1.4;
	}

	.loading {
		color: var(--text-muted);
		font-size: 14px;
	}

	.status-card {
		padding: 16px;
		border-radius: 8px;
		margin-bottom: 24px;
	}

	.status-card.in-progress { background: var(--bg-secondary); }
	.status-card.completed { background: var(--bg-secondary); }
	.status-card.failed { background: rgba(237, 66, 69, 0.1); }

	.progress-stats {
		display: flex;
		gap: 16px;
		font-size: 14px;
		color: var(--text-normal);
		margin: 8px 0 12px;
	}

	.progress-bar {
		height: 8px;
		background: var(--bg-tertiary);
		border-radius: 4px;
		overflow: hidden;
		margin-bottom: 12px;
	}

	.progress-fill {
		height: 100%;
		background: var(--accent);
		border-radius: 4px;
		transition: width 300ms ease;
	}

	.error-msg {
		color: var(--status-danger);
		font-size: 14px;
		margin: 8px 0;
	}

	.partial-stats {
		font-size: 13px;
		color: var(--text-muted);
		margin: 4px 0 0;
	}

	.completed-at {
		font-size: 13px;
		color: var(--text-muted);
		margin: 8px 0 0;
	}

	.import-form {
		margin-bottom: 24px;
	}

	.form-label {
		display: block;
		font-size: 13px;
		font-weight: 600;
		color: var(--text-muted);
		text-transform: uppercase;
		margin-bottom: 12px;
	}

	.form-input {
		display: block;
		width: 100%;
		margin-top: 6px;
		padding: 10px 12px;
		font-size: 14px;
		color: var(--text-normal);
		background: var(--bg-tertiary);
		border: 1px solid var(--bg-tertiary);
		border-radius: 4px;
		outline: none;
		box-sizing: border-box;
	}

	.form-input:focus {
		border-color: var(--accent);
	}

	.start-btn {
		padding: 10px 24px;
		font-size: 14px;
		font-weight: 600;
		color: #fff;
		background: var(--accent);
		border: none;
		border-radius: 4px;
		cursor: pointer;
		transition: opacity 150ms ease;
	}

	.start-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.start-btn:hover:not(:disabled) {
		opacity: 0.9;
	}

	.cancel-btn {
		padding: 8px 16px;
		font-size: 13px;
		color: var(--text-normal);
		background: var(--bg-tertiary);
		border: none;
		border-radius: 4px;
		cursor: pointer;
	}

	.cancel-btn:hover {
		background: var(--bg-message-hover);
	}

	.claim-section {
		margin-top: 24px;
	}

	.mapping-list {
		list-style: none;
		padding: 0;
		margin: 0;
	}

	.mapping-item {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 8px 12px;
		border-radius: 4px;
		transition: background-color 150ms ease;
	}

	.mapping-item:hover {
		background: var(--bg-message-hover);
	}

	.mapping-user {
		display: flex;
		align-items: center;
		gap: 10px;
	}

	.mapping-avatar, .mapping-avatar-placeholder {
		width: 32px;
		height: 32px;
		border-radius: 50%;
	}

	.mapping-avatar-placeholder {
		display: grid;
		place-items: center;
		background: var(--accent);
		color: #fff;
		font-size: 14px;
		font-weight: 600;
	}

	.mapping-name {
		font-size: 14px;
		color: var(--text-normal);
	}

	.claimed-badge {
		font-size: 12px;
		color: var(--text-positive, #3ba55d);
		font-weight: 600;
	}

	.claim-btn {
		padding: 6px 12px;
		font-size: 13px;
		color: #fff;
		background: var(--accent);
		border: none;
		border-radius: 4px;
		cursor: pointer;
	}

	.claim-btn:hover {
		opacity: 0.9;
	}
</style>
```

- [ ] **Step 2: Add tab to ServerSettingsModal**

In `apps/web/src/lib/components/server-settings/ServerSettingsModal.svelte`, add the import and tab:

Add the import at the top:
```svelte
import ServerDiscordImport from './ServerDiscordImport.svelte';
```

Add a new `{:else if}` block in the conditional rendering, before the final `{:else}`:
```svelte
{:else if ui.serverSettingsCategory === 'discord-import'}
	<ServerDiscordImport />
```

- [ ] **Step 3: Add nav item to ServerSettingsSidebar**

In `apps/web/src/lib/components/server-settings/ServerSettingsSidebar.svelte`, add `discord-import` to the categories array. Add it in the `canManageChannels` section (since it requires ManageServer, which implies ManageChannels):

After the audit-log entry in the categories builder, add:

```typescript
if (servers.canManageChannels) {
	// ... existing entries ...
}
// Add this block:
if (servers.canManageServer) {
	cats.push({ id: 'discord-import', label: 'Discord Import' });
}
```

Note: Check what property is used for ManageServer permission. It may be `canManageChannels` with ManageServer checking — look at the existing code and find or add a `canManageServer` derived property. If `canManageServer` doesn't exist, check `servers.canManageChannels` is appropriate or add:

```typescript
readonly canManageServer = $derived(
	this.auth.isGlobalAdmin || hasPermission(this.currentServerPermissions, Permission.ManageServer)
);
```

to `server-store.svelte.ts`.

Update the `serverSettingsCategory` type in the sidebar to include `'discord-import'` in the categories type annotation.

- [ ] **Step 4: Verify frontend builds**

```bash
cd apps/web
npm run check
```

Expected: No errors.

- [ ] **Step 5: Commit**

```bash
git add apps/web/src/lib/components/server-settings/ServerDiscordImport.svelte apps/web/src/lib/components/server-settings/ServerSettingsModal.svelte apps/web/src/lib/components/server-settings/ServerSettingsSidebar.svelte
git commit -m "feat(web): add Discord Import tab in server settings"
```

---

## Task 13: Message Display — Imported Author Badge

**Files:**
- Create: `apps/web/src/lib/components/chat/ImportedAuthorBadge.svelte`
- Modify: `apps/web/src/lib/components/chat/MessageItem.svelte`

- [ ] **Step 1: Create ImportedAuthorBadge component**

Create `apps/web/src/lib/components/chat/ImportedAuthorBadge.svelte`:

```svelte
<span class="imported-badge" title="This message was imported from Discord">imported</span>

<style>
	.imported-badge {
		font-size: 10px;
		font-weight: 600;
		color: var(--text-muted);
		background: var(--bg-tertiary);
		padding: 1px 6px;
		border-radius: 3px;
		text-transform: uppercase;
		letter-spacing: 0.3px;
		vertical-align: middle;
		margin-left: 6px;
	}
</style>
```

- [ ] **Step 2: Update MessageItem to show imported author**

In `apps/web/src/lib/components/chat/MessageItem.svelte`, import the badge:

```svelte
import ImportedAuthorBadge from './ImportedAuthorBadge.svelte';
```

Find where the author name and avatar are displayed. Update the logic:

For the avatar display, update to use `importedAuthorAvatarUrl` when present:
```svelte
{#if message.importedAuthorAvatarUrl}
	<img class="message-avatar-img" src={message.importedAuthorAvatarUrl} alt="" />
{:else if message.authorAvatarUrl}
	<img class="message-avatar-img" src={message.authorAvatarUrl} alt="" />
{:else}
	<!-- existing fallback -->
{/if}
```

For the author name display, add the imported badge after the name when `importedAuthorName` is set:
```svelte
<strong class="message-author" class:deleted-user={!message.authorUserId && !message.importedAuthorName}>
	{message.importedAuthorName ?? (message.authorUserId ? message.authorName : 'Deleted User')}
</strong>
{#if message.importedAuthorName}
	<ImportedAuthorBadge />
{/if}
```

- [ ] **Step 3: Verify frontend builds**

```bash
cd apps/web
npm run check
```

Expected: No errors.

- [ ] **Step 4: Commit**

```bash
git add apps/web/src/lib/components/chat/ImportedAuthorBadge.svelte apps/web/src/lib/components/chat/MessageItem.svelte
git commit -m "feat(web): show imported author badge on Discord-imported messages"
```

---

## Task 14: Wire SignalR Import Events to Store

**Files:**
- Modify: `apps/web/src/routes/+page.svelte` (or wherever SignalR callbacks are wired)

- [ ] **Step 1: Find where SignalR callbacks are wired**

Search for `onMessage:` or `SignalRCallbacks` in `apps/web/src/routes/+page.svelte` to find where callbacks are passed to the hub service.

- [ ] **Step 2: Add import event callbacks**

In the callbacks object, add:

```typescript
onImportProgress: (event) => {
	if (servers.discordImport) {
		servers.discordImport = {
			...servers.discordImport,
			status: 'InProgress',
			importedChannels: event.completed, // or keep existing
		};
	}
},
onImportCompleted: (event) => {
	if (servers.discordImport) {
		servers.discordImport = {
			...servers.discordImport,
			status: 'Completed',
			importedChannels: event.importedChannels,
			importedMessages: event.importedMessages,
			importedMembers: event.importedMembers,
			completedAt: new Date().toISOString()
		};
	}
},
onImportFailed: (event) => {
	if (servers.discordImport) {
		servers.discordImport = {
			...servers.discordImport,
			status: 'Failed',
			errorMessage: event.errorMessage
		};
	}
},
```

- [ ] **Step 3: Verify frontend builds**

```bash
cd apps/web
npm run check
```

Expected: No errors.

- [ ] **Step 4: Commit**

```bash
git add apps/web/src/routes/+page.svelte
git commit -m "feat(web): wire SignalR import events to server store"
```

---

## Task 15: Final Build Verification + Run Tests

- [ ] **Step 1: Build the API**

```bash
cd apps/api/Codec.Api
dotnet build
```

Expected: Build succeeded.

- [ ] **Step 2: Run API unit tests**

```bash
cd apps/api
dotnet test Codec.Api.Tests/Codec.Api.Tests.csproj -v n
```

Expected: All tests pass, including the new permission mapper and rate limit handler tests.

- [ ] **Step 3: Build the frontend**

```bash
cd apps/web
npm run check
```

Expected: No errors.

- [ ] **Step 4: Run frontend tests**

```bash
cd apps/web
npm test
```

Expected: All existing tests pass (new component tests are out of scope for this plan).

- [ ] **Step 5: Commit any remaining changes**

If any fixes were needed during verification, commit them:

```bash
git add -A
git commit -m "fix: address build issues from Discord import feature"
```
