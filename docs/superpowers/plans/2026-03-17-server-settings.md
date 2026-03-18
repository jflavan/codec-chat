# Server Settings Enhancement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add invite management tab, server/channel descriptions, channel categories with drag-and-drop, audit log, and notification mute preferences to server settings.

**Architecture:** Six features sharing the server settings modal. Backend extends existing `ServersController` with new endpoints and adds three new entities (`ChannelCategory`, `AuditLogEntry`, `ChannelNotificationOverride`). Frontend adds new settings tabs and extends the channel sidebar with category grouping and mute controls.

**Tech Stack:** ASP.NET Core 10, EF Core, PostgreSQL, SignalR, SvelteKit, Svelte 5 runes, svelte-dnd-action

**Spec:** `docs/superpowers/specs/2026-03-17-server-settings-design.md`

---

## File Map

### Backend — New Files
- `apps/api/Codec.Api/Models/ChannelCategory.cs` — entity
- `apps/api/Codec.Api/Models/AuditLogEntry.cs` — entity + `AuditAction` enum
- `apps/api/Codec.Api/Models/ChannelNotificationOverride.cs` — entity
- `apps/api/Codec.Api/Models/CreateCategoryRequest.cs` — DTO
- `apps/api/Codec.Api/Models/RenameCategoryRequest.cs` — DTO
- `apps/api/Codec.Api/Models/UpdateChannelOrderRequest.cs` — DTO
- `apps/api/Codec.Api/Models/UpdateCategoryOrderRequest.cs` — DTO
- `apps/api/Codec.Api/Models/MuteRequest.cs` — DTO
- `apps/api/Codec.Api/Services/AuditLogCleanupService.cs` — background service
- `apps/api/Codec.Api/Services/AuditService.cs` — helper to write audit entries
- `apps/api/Codec.Api.Tests/Services/AuditLogCleanupServiceTests.cs` — unit tests
- `apps/api/Codec.Api.Tests/Services/AuditServiceTests.cs` — unit tests

### Backend — Modified Files
- `apps/api/Codec.Api/Models/Server.cs` — add `Description`
- `apps/api/Codec.Api/Models/Channel.cs` — add `Description`, `CategoryId`, `Position`, `Category` nav
- `apps/api/Codec.Api/Models/ServerMember.cs` — add `IsMuted`
- `apps/api/Codec.Api/Models/UpdateServerRequest.cs` — add optional `Description`
- `apps/api/Codec.Api/Models/UpdateChannelRequest.cs` — add optional `Description`
- `apps/api/Codec.Api/Data/CodecDbContext.cs` — register new DbSets, configure relationships
- `apps/api/Codec.Api/Controllers/ServersController.cs` — category CRUD, channel-order, mute, audit-log endpoints; audit logging on existing actions
- `apps/api/Codec.Api/Controllers/ChannelsController.cs` — audit logging on admin message delete
- `apps/api/Codec.Api/Program.cs` — register `AuditLogCleanupService`, `AuditService`
- `apps/api/Codec.Api/Migrations/` — new migration file

### Frontend — New Files
- `apps/web/src/lib/components/server-settings/ServerChannels.svelte` — channels tab
- `apps/web/src/lib/components/server-settings/ServerInvites.svelte` — invites tab
- `apps/web/src/lib/components/server-settings/ServerAuditLog.svelte` — audit log tab
- `apps/web/src/lib/components/channel-sidebar/ContextMenu.svelte` — right-click context menu

### Frontend — Modified Files
- `apps/web/src/lib/types/models.ts` — new types + extend existing
- `apps/web/src/lib/api/client.ts` — new API methods
- `apps/web/src/lib/services/chat-hub.ts` — new SignalR events
- `apps/web/src/lib/state/app-state.svelte.ts` — new state fields, methods, callbacks
- `apps/web/src/lib/components/server-settings/ServerSettingsSidebar.svelte` — new tabs
- `apps/web/src/lib/components/server-settings/ServerSettingsModal.svelte` — render new tabs
- `apps/web/src/lib/components/server-settings/ServerSettings.svelte` — move channels section out, add description field
- `apps/web/src/lib/components/channel-sidebar/ChannelSidebar.svelte` — category grouping, collapse, remove invite panel
- `apps/web/src/lib/components/chat/ChatArea.svelte` — server/channel description display
- `apps/web/src/lib/components/server-sidebar/ServerSidebar.svelte` — mute context menu

### Frontend — Delete
- `apps/web/src/lib/components/channel-sidebar/InvitePanel.svelte`

---

## Task 1: Backend entities and migration

**Files:**
- Create: `apps/api/Codec.Api/Models/ChannelCategory.cs`
- Create: `apps/api/Codec.Api/Models/AuditLogEntry.cs`
- Create: `apps/api/Codec.Api/Models/ChannelNotificationOverride.cs`
- Modify: `apps/api/Codec.Api/Models/Server.cs`
- Modify: `apps/api/Codec.Api/Models/Channel.cs`
- Modify: `apps/api/Codec.Api/Models/ServerMember.cs`
- Modify: `apps/api/Codec.Api/Data/CodecDbContext.cs`

- [ ] **Step 1: Create `ChannelCategory` entity**

Create `apps/api/Codec.Api/Models/ChannelCategory.cs`:

```csharp
namespace Codec.Api.Models;

public class ChannelCategory
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Position { get; set; }
    public Server? Server { get; set; }
    public List<Channel> Channels { get; set; } = [];
}
```

- [ ] **Step 2: Create `AuditAction` enum and `AuditLogEntry` entity**

Create `apps/api/Codec.Api/Models/AuditLogEntry.cs`:

```csharp
namespace Codec.Api.Models;

public enum AuditAction
{
    ServerRenamed,
    ServerDescriptionChanged,
    ServerIconChanged,
    ServerDeleted,
    ChannelCreated,
    ChannelRenamed,
    ChannelDescriptionChanged,
    ChannelDeleted,
    ChannelPurged,
    ChannelMoved,
    CategoryCreated,
    CategoryRenamed,
    CategoryDeleted,
    MemberKicked,
    MemberRoleChanged,
    InviteCreated,
    InviteRevoked,
    EmojiUploaded,
    EmojiRenamed,
    EmojiDeleted,
    MessageDeletedByAdmin
}

public class AuditLogEntry
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public Guid? ActorUserId { get; set; }
    public AuditAction Action { get; set; }
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Server? Server { get; set; }
    public User? ActorUser { get; set; }
}
```

- [ ] **Step 3: Create `ChannelNotificationOverride` entity**

Create `apps/api/Codec.Api/Models/ChannelNotificationOverride.cs`:

```csharp
namespace Codec.Api.Models;

public class ChannelNotificationOverride
{
    public Guid UserId { get; set; }
    public Guid ChannelId { get; set; }
    public bool IsMuted { get; set; }
    public User? User { get; set; }
    public Channel? Channel { get; set; }
}
```

- [ ] **Step 4: Modify existing entities**

In `apps/api/Codec.Api/Models/Server.cs`, add:
```csharp
public string? Description { get; set; }
public List<ChannelCategory> Categories { get; set; } = [];
public List<AuditLogEntry> AuditLogEntries { get; set; } = [];
```

In `apps/api/Codec.Api/Models/Channel.cs`, add:
```csharp
public string? Description { get; set; }
public Guid? CategoryId { get; set; }
public int Position { get; set; }
public ChannelCategory? Category { get; set; }
```

In `apps/api/Codec.Api/Models/ServerMember.cs`, add:
```csharp
public bool IsMuted { get; set; }
```

- [ ] **Step 5: Register DbSets and configure relationships in `CodecDbContext`**

Add DbSets:
```csharp
public DbSet<ChannelCategory> ChannelCategories => Set<ChannelCategory>();
public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
public DbSet<ChannelNotificationOverride> ChannelNotificationOverrides => Set<ChannelNotificationOverride>();
```

In `OnModelCreating`, add configuration blocks following existing patterns:

**ChannelCategory:**
```csharp
modelBuilder.Entity<ChannelCategory>(e =>
{
    e.HasOne(c => c.Server)
        .WithMany(s => s.Categories)
        .HasForeignKey(c => c.ServerId)
        .OnDelete(DeleteBehavior.Cascade);
    e.Property(c => c.Name).HasMaxLength(50);
});
```

**AuditLogEntry:**
```csharp
modelBuilder.Entity<AuditLogEntry>(e =>
{
    e.HasOne(a => a.Server)
        .WithMany(s => s.AuditLogEntries)
        .HasForeignKey(a => a.ServerId)
        .OnDelete(DeleteBehavior.Cascade);
    e.HasOne(a => a.ActorUser)
        .WithMany()
        .HasForeignKey(a => a.ActorUserId)
        .OnDelete(DeleteBehavior.SetNull);
    e.HasIndex(a => new { a.ServerId, a.CreatedAt })
        .IsDescending(false, true);
    e.Property(a => a.Action).HasConversion<string>();
});
```

**ChannelNotificationOverride:**
```csharp
modelBuilder.Entity<ChannelNotificationOverride>(e =>
{
    e.HasKey(o => new { o.UserId, o.ChannelId });
    e.HasOne(o => o.User)
        .WithMany()
        .HasForeignKey(o => o.UserId)
        .OnDelete(DeleteBehavior.Cascade);
    e.HasOne(o => o.Channel)
        .WithMany()
        .HasForeignKey(o => o.ChannelId)
        .OnDelete(DeleteBehavior.Cascade);
});
```

**Channel — CategoryId FK:**
```csharp
// Add to existing Channel configuration:
e.HasOne(c => c.Category)
    .WithMany(cat => cat.Channels)
    .HasForeignKey(c => c.CategoryId)
    .OnDelete(DeleteBehavior.SetNull);
```

**Server.Description and Channel.Description max lengths:**
```csharp
// Add to existing Server configuration:
e.Property(s => s.Description).HasMaxLength(256);

// Add to existing Channel configuration:
e.Property(c => c.Description).HasMaxLength(256);
```

- [ ] **Step 6: Create EF Core migration**

Run: `cd apps/api/Codec.Api && dotnet ef migrations add AddServerSettingsEnhancements`

If `dotnet ef` is unavailable, manually create migration files following the existing migration patterns in `apps/api/Codec.Api/Migrations/`.

- [ ] **Step 7: Verify build**

Run: `cd apps/api/Codec.Api && dotnet build`

Expected: Build succeeded. 0 Errors.

- [ ] **Step 8: Commit**

```
git add apps/api/Codec.Api/Models/ apps/api/Codec.Api/Data/CodecDbContext.cs apps/api/Codec.Api/Migrations/
git commit -m "feat: add entities and migration for server settings enhancements

Add ChannelCategory, AuditLogEntry, ChannelNotificationOverride entities.
Extend Server (Description), Channel (Description, CategoryId, Position),
and ServerMember (IsMuted). Configure cascade behaviors."
```

---

## Task 2: Request DTOs

**Files:**
- Modify: `apps/api/Codec.Api/Models/UpdateServerRequest.cs`
- Modify: `apps/api/Codec.Api/Models/UpdateChannelRequest.cs`
- Create: `apps/api/Codec.Api/Models/CreateCategoryRequest.cs`
- Create: `apps/api/Codec.Api/Models/RenameCategoryRequest.cs`
- Create: `apps/api/Codec.Api/Models/UpdateChannelOrderRequest.cs`
- Create: `apps/api/Codec.Api/Models/UpdateCategoryOrderRequest.cs`
- Create: `apps/api/Codec.Api/Models/MuteRequest.cs`

- [ ] **Step 1: Update `UpdateServerRequest`**

Replace `apps/api/Codec.Api/Models/UpdateServerRequest.cs` content:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public record UpdateServerRequest(
    [StringLength(100, MinimumLength = 1)] string? Name,
    [StringLength(256)] string? Description);
```

Note: both fields are now optional. The controller must validate at least one is provided.

- [ ] **Step 2: Update `UpdateChannelRequest`**

Replace `apps/api/Codec.Api/Models/UpdateChannelRequest.cs` content:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public record UpdateChannelRequest(
    [StringLength(100, MinimumLength = 1)] string? Name,
    [StringLength(256)] string? Description);
```

- [ ] **Step 3: Create category DTOs**

Create `apps/api/Codec.Api/Models/CreateCategoryRequest.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public record CreateCategoryRequest([Required, StringLength(50, MinimumLength = 1)] string Name);
```

Create `apps/api/Codec.Api/Models/RenameCategoryRequest.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public record RenameCategoryRequest([Required, StringLength(50, MinimumLength = 1)] string Name);
```

- [ ] **Step 4: Create ordering DTOs**

Create `apps/api/Codec.Api/Models/UpdateChannelOrderRequest.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public record ChannelOrderItem(
    [Required] Guid ChannelId,
    Guid? CategoryId,
    [Required] int Position);

public record UpdateChannelOrderRequest([Required] List<ChannelOrderItem> Channels);
```

Create `apps/api/Codec.Api/Models/UpdateCategoryOrderRequest.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public record CategoryOrderItem([Required] Guid CategoryId, [Required] int Position);

public record UpdateCategoryOrderRequest([Required] List<CategoryOrderItem> Categories);
```

- [ ] **Step 5: Create mute DTO**

Create `apps/api/Codec.Api/Models/MuteRequest.cs`:
```csharp
namespace Codec.Api.Models;

public record MuteRequest(bool IsMuted);
```

- [ ] **Step 6: Verify build**

Run: `cd apps/api/Codec.Api && dotnet build`

Expected: Build succeeded. 0 Errors.

- [ ] **Step 7: Fix existing tests for new DTO signatures**

The `UpdateServerRequest` and `UpdateChannelRequest` constructors changed. Update all test files that construct these DTOs. For example, in `apps/api/Codec.Api.Tests/Controllers/ServersControllerTests.cs`:

```csharp
// Before:
new UpdateServerRequest("New")
// After:
new UpdateServerRequest("New", null)
```

Similarly for `UpdateChannelRequest`. Search all test files for these constructors and update them.

Also update `apps/api/Codec.Api.IntegrationTests/` if any integration tests construct these DTOs directly.

- [ ] **Step 8: Run all existing tests to confirm nothing is broken**

Run: `cd apps/api && dotnet test Codec.Api.Tests/Codec.Api.Tests.csproj`

Expected: All tests PASS.

- [ ] **Step 9: Commit**

```
git add apps/api/Codec.Api/Models/ apps/api/Codec.Api.Tests/ apps/api/Codec.Api.IntegrationTests/
git commit -m "feat: add request DTOs for server settings endpoints"
```

---

## Task 3: AuditService and AuditLogCleanupService

**Files:**
- Create: `apps/api/Codec.Api/Services/AuditService.cs`
- Create: `apps/api/Codec.Api/Services/AuditLogCleanupService.cs`
- Modify: `apps/api/Codec.Api/Program.cs`
- Test: `apps/api/Codec.Api.Tests/Services/AuditServiceTests.cs`
- Test: `apps/api/Codec.Api.Tests/Services/AuditLogCleanupServiceTests.cs`

- [ ] **Step 1: Write failing test for `AuditService`**

Create `apps/api/Codec.Api.Tests/Services/AuditServiceTests.cs`:

```csharp
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Tests.Services;

public class AuditServiceTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly AuditService _service;

    public AuditServiceTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);
        _service = new AuditService(_db);
    }

    [Fact]
    public async Task LogAsync_CreatesEntry()
    {
        var serverId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await _service.LogAsync(serverId, actorId, AuditAction.ServerRenamed,
            "Server", serverId.ToString(), "Renamed to New Name");

        var entries = await _db.AuditLogEntries.ToListAsync();
        entries.Should().HaveCount(1);
        entries[0].ServerId.Should().Be(serverId);
        entries[0].ActorUserId.Should().Be(actorId);
        entries[0].Action.Should().Be(AuditAction.ServerRenamed);
        entries[0].Details.Should().Be("Renamed to New Name");
    }

    public void Dispose() => _db.Dispose();
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd apps/api && dotnet test Codec.Api.Tests/Codec.Api.Tests.csproj --filter "AuditServiceTests"`

Expected: FAIL — `AuditService` does not exist.

- [ ] **Step 3: Create `AuditService`**

Create `apps/api/Codec.Api/Services/AuditService.cs`:

```csharp
using Codec.Api.Data;
using Codec.Api.Models;

namespace Codec.Api.Services;

public class AuditService(CodecDbContext db)
{
    public async Task LogAsync(
        Guid serverId,
        Guid actorUserId,
        AuditAction action,
        string? targetType = null,
        string? targetId = null,
        string? details = null)
    {
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            ServerId = serverId,
            ActorUserId = actorUserId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Details = details,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd apps/api && dotnet test Codec.Api.Tests/Codec.Api.Tests.csproj --filter "AuditServiceTests"`

Expected: PASS.

- [ ] **Step 5: Write failing test for `AuditLogCleanupService`**

Create `apps/api/Codec.Api.Tests/Services/AuditLogCleanupServiceTests.cs`:

```csharp
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Codec.Api.Tests.Services;

public class AuditLogCleanupServiceTests : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly AuditLogCleanupService _service;

    public AuditLogCleanupServiceTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<CodecDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        _sp = services.BuildServiceProvider();

        _service = new AuditLogCleanupService(
            _sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AuditLogCleanupService>.Instance);
    }

    [Fact]
    public async Task CleanupAsync_DeletesOldEntries()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CodecDbContext>();
        var serverId = Guid.NewGuid();

        db.AuditLogEntries.Add(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            ServerId = serverId,
            Action = AuditAction.ServerRenamed,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-91)
        });
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            ServerId = serverId,
            Action = AuditAction.ServerRenamed,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        await _service.CleanupAsync();

        var remaining = await db.AuditLogEntries.ToListAsync();
        remaining.Should().HaveCount(1);
        remaining[0].CreatedAt.Should().BeAfter(DateTimeOffset.UtcNow.AddDays(-2));
    }

    [Fact]
    public async Task CleanupAsync_DoesNotThrow()
    {
        await _service.Invoking(s => s.CleanupAsync()).Should().NotThrowAsync();
    }

    public void Dispose() => _sp.Dispose();
}
```

- [ ] **Step 6: Create `AuditLogCleanupService`**

Create `apps/api/Codec.Api/Services/AuditLogCleanupService.cs`:

```csharp
using Codec.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Services;

public class AuditLogCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<AuditLogCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken);
            await CleanupAsync(stoppingToken);
        }
    }

    internal async Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CodecDbContext>();
            var cutoff = DateTimeOffset.UtcNow.AddDays(-90);

            var deleted = await db.AuditLogEntries
                .Where(e => e.CreatedAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);

            if (deleted > 0)
                logger.LogInformation("Purged {Count} audit log entries older than 90 days", deleted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error during audit log cleanup");
        }
    }
}
```

- [ ] **Step 7: Register services in `Program.cs`**

In `apps/api/Codec.Api/Program.cs`, add near the existing `RefreshTokenCleanupService` registration:

```csharp
builder.Services.AddScoped<AuditService>();
builder.Services.AddHostedService<AuditLogCleanupService>();
```

- [ ] **Step 8: Run all tests**

Run: `cd apps/api && dotnet test Codec.Api.Tests/Codec.Api.Tests.csproj --filter "AuditServiceTests|AuditLogCleanupServiceTests"`

Expected: All tests PASS.

- [ ] **Step 9: Commit**

```
git add apps/api/Codec.Api/Services/AuditService.cs apps/api/Codec.Api/Services/AuditLogCleanupService.cs apps/api/Codec.Api/Program.cs apps/api/Codec.Api.Tests/Services/AuditServiceTests.cs apps/api/Codec.Api.Tests/Services/AuditLogCleanupServiceTests.cs
git commit -m "feat: add AuditService and AuditLogCleanupService with tests"
```

---

## Task 4: Server description API endpoint

**Files:**
- Modify: `apps/api/Codec.Api/Controllers/ServersController.cs`

- [ ] **Step 1: Update `UpdateServer` endpoint to handle description**

In `apps/api/Codec.Api/Controllers/ServersController.cs`, modify the `UpdateServer` method (around line 155):

- Add validation: return `BadRequest` if both `Name` and `Description` are null
- If `Name` is provided, update `server.Name` and log `AuditAction.ServerRenamed`
- If `Description` is provided, update `server.Description` and log `AuditAction.ServerDescriptionChanged`
- Broadcast `ServerDescriptionChanged` via SignalR when description changes
- Include `Description` in the response

The method needs `AuditService` injected. Add it as a constructor parameter to the controller (or method-inject via `[FromServices]`).

```csharp
[HttpPatch("{serverId:guid}")]
public async Task<IActionResult> UpdateServer(Guid serverId, [FromBody] UpdateServerRequest request, [FromServices] AuditService audit)
{
    if (request.Name is null && request.Description is null)
        return BadRequest(new { error = "At least one of Name or Description is required." });

    var (appUser, _) = await userService.GetOrCreateUserAsync(User);
    await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

    var server = (await db.Servers.FindAsync(serverId))!;

    if (request.Name is not null)
    {
        var oldName = server.Name;
        server.Name = request.Name.Trim();
        await db.SaveChangesAsync();
        await audit.LogAsync(serverId, appUser.Id, AuditAction.ServerRenamed,
            "Server", serverId.ToString(), $"Renamed from \"{oldName}\" to \"{server.Name}\"");
        await hub.Clients.Group($"server-{serverId}").SendAsync("ServerNameChanged", new
        {
            serverId,
            name = server.Name
        });
    }

    if (request.Description is not null)
    {
        server.Description = request.Description.Trim();
        await db.SaveChangesAsync();
        await audit.LogAsync(serverId, appUser.Id, AuditAction.ServerDescriptionChanged,
            "Server", serverId.ToString(), server.Description);
        await hub.Clients.Group($"server-{serverId}").SendAsync("ServerDescriptionChanged", new
        {
            serverId,
            description = server.Description
        });
    }

    return Ok(new
    {
        server.Id,
        server.Name,
        server.IconUrl,
        server.Description
    });
}
```

- [ ] **Step 2: Update `GetServers` to include `Description`**

Find the `GetServers` endpoint and add `Description` to the projection (the `Select` or anonymous object).

- [ ] **Step 3: Verify build**

Run: `cd apps/api/Codec.Api && dotnet build`

Expected: Build succeeded. 0 Errors.

- [ ] **Step 4: Commit**

```
git add apps/api/Codec.Api/Controllers/ServersController.cs
git commit -m "feat: add server description support to PATCH /servers and GET /servers"
```

---

## Task 5: Channel description API endpoint

**Files:**
- Modify: `apps/api/Codec.Api/Controllers/ServersController.cs`

- [ ] **Step 1: Update `UpdateChannel` endpoint to handle description**

In `ServersController.cs`, modify the `UpdateChannel` method (around line 424):

- Add validation: return `BadRequest` if both `Name` and `Description` are null
- If `Description` is provided, update `channel.Description`
- Log `AuditAction.ChannelDescriptionChanged` when description changes
- Broadcast `ChannelDescriptionChanged` via SignalR
- Include `Description` in `GetChannels` response

```csharp
if (request.Description is not null)
{
    channel.Description = request.Description.Trim();
    await audit.LogAsync(serverId, appUser.Id, AuditAction.ChannelDescriptionChanged,
        "Channel", channelId.ToString(), $"#{channel.Name}: {channel.Description}");
    await hub.Clients.Group($"server-{serverId}").SendAsync("ChannelDescriptionChanged", new
    {
        serverId,
        channelId,
        description = channel.Description
    });
}
```

- [ ] **Step 2: Update `GetChannels` to include `Description`, `CategoryId`, `Position`**

Add `Description`, `CategoryId`, and `Position` to the channel projection in `GetChannels`.

- [ ] **Step 3: Verify build and commit**

Run: `cd apps/api/Codec.Api && dotnet build`

```
git add apps/api/Codec.Api/Controllers/ServersController.cs
git commit -m "feat: add channel description support and category/position fields to channel responses"
```

---

## Task 6: Category CRUD and channel ordering endpoints

**Files:**
- Modify: `apps/api/Codec.Api/Controllers/ServersController.cs`

- [ ] **Step 1: Add `CreateCategory` endpoint**

```csharp
[HttpPost("{serverId:guid}/categories")]
public async Task<IActionResult> CreateCategory(Guid serverId, [FromBody] CreateCategoryRequest request, [FromServices] AuditService audit)
{
    var (appUser, _) = await userService.GetOrCreateUserAsync(User);
    await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

    var maxPosition = await db.ChannelCategories
        .Where(c => c.ServerId == serverId)
        .Select(c => (int?)c.Position)
        .MaxAsync() ?? -1;

    var category = new ChannelCategory
    {
        Id = Guid.NewGuid(),
        ServerId = serverId,
        Name = request.Name.Trim(),
        Position = maxPosition + 1
    };
    db.ChannelCategories.Add(category);
    await db.SaveChangesAsync();

    await audit.LogAsync(serverId, appUser.Id, AuditAction.CategoryCreated,
        "Category", category.Id.ToString(), category.Name);

    await hub.Clients.Group($"server-{serverId}").SendAsync("CategoryCreated", new
    {
        serverId,
        categoryId = category.Id,
        name = category.Name,
        position = category.Position
    });

    return Created($"/servers/{serverId}/categories/{category.Id}", new
    {
        category.Id,
        category.ServerId,
        category.Name,
        category.Position
    });
}
```

- [ ] **Step 2: Add `RenameCategory` endpoint**

```csharp
[HttpPatch("{serverId:guid}/categories/{categoryId:guid}")]
public async Task<IActionResult> RenameCategory(Guid serverId, Guid categoryId, [FromBody] RenameCategoryRequest request, [FromServices] AuditService audit)
{
    var (appUser, _) = await userService.GetOrCreateUserAsync(User);
    await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

    var category = await db.ChannelCategories
        .FirstOrDefaultAsync(c => c.Id == categoryId && c.ServerId == serverId);
    if (category is null) return NotFound();

    var oldName = category.Name;
    category.Name = request.Name.Trim();
    await db.SaveChangesAsync();

    await audit.LogAsync(serverId, appUser.Id, AuditAction.CategoryRenamed,
        "Category", categoryId.ToString(), $"Renamed from \"{oldName}\" to \"{category.Name}\"");

    await hub.Clients.Group($"server-{serverId}").SendAsync("CategoryRenamed", new
    {
        serverId,
        categoryId,
        name = category.Name
    });

    return Ok(new { category.Id, category.Name, category.Position });
}
```

- [ ] **Step 3: Add `DeleteCategory` endpoint**

```csharp
[HttpDelete("{serverId:guid}/categories/{categoryId:guid}")]
public async Task<IActionResult> DeleteCategory(Guid serverId, Guid categoryId, [FromServices] AuditService audit)
{
    var (appUser, _) = await userService.GetOrCreateUserAsync(User);
    await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

    var category = await db.ChannelCategories
        .FirstOrDefaultAsync(c => c.Id == categoryId && c.ServerId == serverId);
    if (category is null) return NotFound();

    // Channels become uncategorized (CategoryId → null via ON DELETE SET NULL)
    db.ChannelCategories.Remove(category);
    await db.SaveChangesAsync();

    await audit.LogAsync(serverId, appUser.Id, AuditAction.CategoryDeleted,
        "Category", categoryId.ToString(), category.Name);

    await hub.Clients.Group($"server-{serverId}").SendAsync("CategoryDeleted", new
    {
        serverId,
        categoryId
    });

    return NoContent();
}
```

- [ ] **Step 4: Add `UpdateChannelOrder` endpoint**

```csharp
[HttpPut("{serverId:guid}/channel-order")]
public async Task<IActionResult> UpdateChannelOrder(Guid serverId, [FromBody] UpdateChannelOrderRequest request, [FromServices] AuditService audit)
{
    var (appUser, _) = await userService.GetOrCreateUserAsync(User);
    await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

    var channels = await db.Channels.Where(c => c.ServerId == serverId).ToListAsync();

    if (request.Channels.Count != channels.Count)
        return BadRequest(new { error = "Must include all channels in the server." });

    // Validate all CategoryId values belong to this server
    var validCategoryIds = await db.ChannelCategories
        .Where(c => c.ServerId == serverId)
        .Select(c => c.Id)
        .ToHashSetAsync();

    var channelMap = channels.ToDictionary(c => c.Id);
    foreach (var item in request.Channels)
    {
        if (!channelMap.TryGetValue(item.ChannelId, out var channel))
            return BadRequest(new { error = $"Channel {item.ChannelId} not found in server." });

        if (item.CategoryId.HasValue && !validCategoryIds.Contains(item.CategoryId.Value))
            return BadRequest(new { error = $"Category {item.CategoryId} not found in server." });

        channel.CategoryId = item.CategoryId;
        channel.Position = item.Position;
    }
    await db.SaveChangesAsync();

    await audit.LogAsync(serverId, appUser.Id, AuditAction.ChannelMoved,
        "Server", serverId.ToString(), "Channel order updated");

    await hub.Clients.Group($"server-{serverId}").SendAsync("ChannelOrderChanged", new { serverId });

    return NoContent();
}
```

- [ ] **Step 5: Add `UpdateCategoryOrder` endpoint**

```csharp
[HttpPut("{serverId:guid}/category-order")]
public async Task<IActionResult> UpdateCategoryOrder(Guid serverId, [FromBody] UpdateCategoryOrderRequest request, [FromServices] AuditService audit)
{
    var (appUser, _) = await userService.GetOrCreateUserAsync(User);
    await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

    var categories = await db.ChannelCategories
        .Where(c => c.ServerId == serverId)
        .ToListAsync();
    var categoryMap = categories.ToDictionary(c => c.Id);

    foreach (var item in request.Categories)
    {
        if (categoryMap.TryGetValue(item.CategoryId, out var category))
            category.Position = item.Position;
    }
    await db.SaveChangesAsync();

    await hub.Clients.Group($"server-{serverId}").SendAsync("CategoryOrderChanged", new { serverId });

    return NoContent();
}
```

- [ ] **Step 6: Add `GetCategories` endpoint**

```csharp
[HttpGet("{serverId:guid}/categories")]
public async Task<IActionResult> GetCategories(Guid serverId)
{
    var (appUser, _) = await userService.GetOrCreateUserAsync(User);
    await userService.EnsureMemberAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

    var categories = await db.ChannelCategories
        .AsNoTracking()
        .Where(c => c.ServerId == serverId)
        .OrderBy(c => c.Position)
        .Select(c => new { c.Id, c.Name, c.Position })
        .ToListAsync();

    return Ok(categories);
}
```

- [ ] **Step 7: Verify build and commit**

Run: `cd apps/api/Codec.Api && dotnet build`

```
git add apps/api/Codec.Api/Controllers/ServersController.cs
git commit -m "feat: add category CRUD and channel/category ordering endpoints"
```

---

## Task 7: Audit log and notification preference endpoints

**Files:**
- Modify: `apps/api/Codec.Api/Controllers/ServersController.cs`

- [ ] **Step 1: Add `GetAuditLog` endpoint**

```csharp
[HttpGet("{serverId:guid}/audit-log")]
public async Task<IActionResult> GetAuditLog(Guid serverId, [FromQuery] DateTimeOffset? before, [FromQuery] int limit = 50)
{
    var (appUser, _) = await userService.GetOrCreateUserAsync(User);
    await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

    limit = Math.Clamp(limit, 1, 100);
    var query = db.AuditLogEntries
        .AsNoTracking()
        .Where(e => e.ServerId == serverId);

    if (before.HasValue)
        query = query.Where(e => e.CreatedAt < before.Value);

    var entries = await query
        .OrderByDescending(e => e.CreatedAt)
        .Take(limit + 1)
        .Select(e => new
        {
            e.Id,
            Action = e.Action.ToString(),
            e.TargetType,
            e.TargetId,
            e.Details,
            e.CreatedAt,
            Actor = new
            {
                UserId = e.ActorUserId,
                DisplayName = e.ActorUser != null ? e.ActorUser.DisplayName : "Deleted User",
                AvatarUrl = e.ActorUser != null ? e.ActorUser.AvatarUrl : (string?)null
            }
        })
        .ToListAsync();

    var hasMore = entries.Count > limit;
    return Ok(new
    {
        hasMore,
        entries = entries.Take(limit)
    });
}
```

- [ ] **Step 2: Add `MuteServer` endpoint**

```csharp
[HttpPut("{serverId:guid}/mute")]
public async Task<IActionResult> MuteServer(Guid serverId, [FromBody] MuteRequest request)
{
    var (appUser, _) = await userService.GetOrCreateUserAsync(User);
    var member = await db.ServerMembers
        .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == appUser.Id);
    if (member is null && !appUser.IsGlobalAdmin) return NotFound();

    if (member is not null)
    {
        member.IsMuted = request.IsMuted;
        await db.SaveChangesAsync();
    }

    return NoContent();
}
```

- [ ] **Step 3: Add `MuteChannel` endpoint**

```csharp
[HttpPut("{serverId:guid}/channels/{channelId:guid}/mute")]
public async Task<IActionResult> MuteChannel(Guid serverId, Guid channelId, [FromBody] MuteRequest request)
{
    var (appUser, _) = await userService.GetOrCreateUserAsync(User);
    var isMember = await db.ServerMembers
        .AnyAsync(m => m.ServerId == serverId && m.UserId == appUser.Id);
    if (!isMember && !appUser.IsGlobalAdmin) return NotFound();

    var channelExists = await db.Channels.AnyAsync(c => c.Id == channelId && c.ServerId == serverId);
    if (!channelExists) return NotFound();

    var existing = await db.ChannelNotificationOverrides
        .FirstOrDefaultAsync(o => o.UserId == appUser.Id && o.ChannelId == channelId);

    if (request.IsMuted)
    {
        if (existing is null)
        {
            db.ChannelNotificationOverrides.Add(new ChannelNotificationOverride
            {
                UserId = appUser.Id,
                ChannelId = channelId,
                IsMuted = true
            });
        }
        else
        {
            existing.IsMuted = true;
        }
    }
    else if (existing is not null)
    {
        db.ChannelNotificationOverrides.Remove(existing);
    }

    await db.SaveChangesAsync();
    return NoContent();
}
```

- [ ] **Step 4: Add `GetNotificationPreferences` endpoint**

```csharp
[HttpGet("{serverId:guid}/notification-preferences")]
public async Task<IActionResult> GetNotificationPreferences(Guid serverId)
{
    var (appUser, _) = await userService.GetOrCreateUserAsync(User);
    var member = await db.ServerMembers
        .AsNoTracking()
        .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == appUser.Id);
    if (member is null && !appUser.IsGlobalAdmin) return NotFound();

    var channelIds = await db.Channels
        .Where(c => c.ServerId == serverId)
        .Select(c => c.Id)
        .ToListAsync();

    var overrides = await db.ChannelNotificationOverrides
        .AsNoTracking()
        .Where(o => o.UserId == appUser.Id && channelIds.Contains(o.ChannelId))
        .Select(o => new { o.ChannelId, o.IsMuted })
        .ToListAsync();

    return Ok(new
    {
        serverMuted = member?.IsMuted ?? false,
        channelOverrides = overrides
    });
}
```

- [ ] **Step 5: Verify build and commit**

Run: `cd apps/api/Codec.Api && dotnet build`

```
git add apps/api/Codec.Api/Controllers/ServersController.cs
git commit -m "feat: add audit log, server mute, channel mute, and notification preferences endpoints"
```

---

## Task 8: Add audit logging to existing controller actions

**Files:**
- Modify: `apps/api/Codec.Api/Controllers/ServersController.cs`
- Modify: `apps/api/Codec.Api/Controllers/ChannelsController.cs`

- [ ] **Step 1: Add audit logging to existing `ServersController` actions**

For each action, add `[FromServices] AuditService audit` parameter and an `audit.LogAsync(...)` call after the action completes:

- `UploadServerIcon` → `AuditAction.ServerIconChanged`
- `DeleteServerIcon` → `AuditAction.ServerIconChanged`
- `CreateChannel` → `AuditAction.ChannelCreated`
- `UpdateChannel` (name change) → `AuditAction.ChannelRenamed`
- `DeleteChannel` → `AuditAction.ChannelDeleted`
- `DeleteServer` → `AuditAction.ServerDeleted`
- `KickMember` → `AuditAction.MemberKicked`
- `UpdateMemberRole` → `AuditAction.MemberRoleChanged`
- `CreateInvite` → `AuditAction.InviteCreated`
- `RevokeInvite` → `AuditAction.InviteRevoked`
- Custom emoji endpoints (upload/rename/delete) → `AuditAction.EmojiUploaded/Renamed/Deleted`

Each call includes meaningful `Details` string (e.g., `"Kicked @username"`, `"Created #channel-name"`, `"Promoted @user to Admin"`).

- [ ] **Step 2: Add audit logging to `ChannelsController` admin message delete**

In `ChannelsController.cs`, in the `DeleteMessage` method (around line 684), after the message is deleted, if the deleter is not the author (admin deletion), add:

```csharp
if (message.UserId != appUser.Id)
{
    var channel = await db.Channels.FindAsync(channelId);
    if (channel is not null)
    {
        await audit.LogAsync(channel.ServerId, appUser.Id, AuditAction.MessageDeletedByAdmin,
            "Message", messageId.ToString(), $"Deleted message by {message.User?.DisplayName} in #{channel.Name}");
    }
}
```

- [ ] **Step 3: Add audit logging to `PurgeChannelMessages`**

In `ChannelsController.cs`, in the `PurgeChannelMessages` method, add:

```csharp
await audit.LogAsync(channel.ServerId, appUser.Id, AuditAction.ChannelPurged,
    "Channel", channelId.ToString(), $"Purged all messages in #{channel.Name}");
```

- [ ] **Step 4: Verify build and commit**

Run: `cd apps/api/Codec.Api && dotnet build`

```
git add apps/api/Codec.Api/Controllers/
git commit -m "feat: add audit logging to all admin actions in ServersController and ChannelsController"
```

---

## Task 9: Frontend types and API client

**Files:**
- Modify: `apps/web/src/lib/types/models.ts`
- Modify: `apps/web/src/lib/api/client.ts`

- [ ] **Step 1: Add new types to `models.ts`**

Add to `apps/web/src/lib/types/models.ts`:

```typescript
export interface ChannelCategory {
    id: string;
    serverId: string;
    name: string;
    position: number;
}

export interface AuditLogEntry {
    id: string;
    action: string;
    targetType?: string;
    targetId?: string;
    details?: string;
    createdAt: string;
    actor: { userId?: string; displayName: string; avatarUrl?: string };
}

export interface PaginatedAuditLog {
    hasMore: boolean;
    entries: AuditLogEntry[];
}

export interface NotificationPreferences {
    serverMuted: boolean;
    channelOverrides: { channelId: string; isMuted: boolean }[];
}
```

Extend existing types:
- `MemberServer`: add `description?: string`
- `Channel`: add `description?: string`, `categoryId?: string`, `position: number`

- [ ] **Step 2: Add API client methods**

Add to `apps/web/src/lib/api/client.ts`:

```typescript
// Categories
async getCategories(serverId: string): Promise<ChannelCategory[]>
async createCategory(serverId: string, name: string): Promise<ChannelCategory>
async renameCategory(serverId: string, categoryId: string, name: string): Promise<void>
async deleteCategory(serverId: string, categoryId: string): Promise<void>
async updateChannelOrder(serverId: string, channels: { channelId: string; categoryId?: string; position: number }[]): Promise<void>
async updateCategoryOrder(serverId: string, categories: { categoryId: string; position: number }[]): Promise<void>

// Audit log
async getAuditLog(serverId: string, options?: { before?: string; limit?: number }): Promise<PaginatedAuditLog>

// Notification preferences
async muteServer(serverId: string, isMuted: boolean): Promise<void>
async muteChannel(serverId: string, channelId: string, isMuted: boolean): Promise<void>
async getNotificationPreferences(serverId: string): Promise<NotificationPreferences>

// Server description (existing updateServerName may need extending)
async updateServer(serverId: string, data: { name?: string; description?: string }): Promise<void>
async updateChannel(serverId: string, channelId: string, data: { name?: string; description?: string }): Promise<void>
```

Follow existing `ApiClient` patterns (error handling, auth header, base URL).

- [ ] **Step 3: Verify type-check**

Run: `cd apps/web && npm run check`

Expected: 0 errors.

- [ ] **Step 4: Commit**

```
git add apps/web/src/lib/types/ apps/web/src/lib/api/
git commit -m "feat: add frontend types and API client methods for server settings enhancements"
```

---

## Task 10: SignalR event handlers

**Files:**
- Modify: `apps/web/src/lib/services/chat-hub.ts`
- Modify: `apps/web/src/lib/state/app-state.svelte.ts`

- [ ] **Step 1: Add new event types and callbacks to `chat-hub.ts`**

Add event types:
```typescript
export type ChannelOrderChangedEvent = { serverId: string };
export type CategoryOrderChangedEvent = { serverId: string };
export type CategoryCreatedEvent = { serverId: string; categoryId: string; name: string; position: number };
export type CategoryRenamedEvent = { serverId: string; categoryId: string; name: string };
export type CategoryDeletedEvent = { serverId: string; categoryId: string };
export type ServerDescriptionChangedEvent = { serverId: string; description: string };
export type ChannelDescriptionChangedEvent = { serverId: string; channelId: string; description: string };
```

Add callbacks to `SignalRCallbacks`:
```typescript
onChannelOrderChanged?: (event: ChannelOrderChangedEvent) => void;
onCategoryOrderChanged?: (event: CategoryOrderChangedEvent) => void;
onCategoryCreated?: (event: CategoryCreatedEvent) => void;
onCategoryRenamed?: (event: CategoryRenamedEvent) => void;
onCategoryDeleted?: (event: CategoryDeletedEvent) => void;
onServerDescriptionChanged?: (event: ServerDescriptionChangedEvent) => void;
onChannelDescriptionChanged?: (event: ChannelDescriptionChangedEvent) => void;
```

Register handlers in the `start()` method, following existing pattern.

- [ ] **Step 2: Wire callbacks in `AppState.startSignalR()`**

In `app-state.svelte.ts`, wire each callback to update reactive state:

- `onServerDescriptionChanged`: update the matching server's description in `servers` array
- `onChannelDescriptionChanged`: update the matching channel's description in `channels` array
- `onCategoryCreated/Renamed/Deleted`: update `categories` array
- `onChannelOrderChanged/CategoryOrderChanged`: reload channels and categories from API

- [ ] **Step 3: Verify type-check and commit**

Run: `cd apps/web && npm run check`

```
git add apps/web/src/lib/services/chat-hub.ts apps/web/src/lib/state/app-state.svelte.ts
git commit -m "feat: add SignalR event handlers for server settings changes"
```

---

## Task 11: AppState — settings state and methods

**Files:**
- Modify: `apps/web/src/lib/state/app-state.svelte.ts`

- [ ] **Step 1: Add new state fields**

```typescript
// Category state
categories = $state<ChannelCategory[]>([]);

// Audit log state
auditLogEntries = $state<AuditLogEntry[]>([]);
hasMoreAuditLog = $state(false);
isLoadingAuditLog = $state(false);

// Notification preferences
notificationPreferences = $state<NotificationPreferences | null>(null);
```

- [ ] **Step 2: Extend `serverSettingsCategory` type**

Change:
```typescript
serverSettingsCategory = $state<'general' | 'emojis' | 'members'>('general');
```
To:
```typescript
serverSettingsCategory = $state<'general' | 'channels' | 'invites' | 'emojis' | 'members' | 'audit-log'>('general');
```

- [ ] **Step 3: Add category methods**

```typescript
async loadCategories()   // GET /servers/{id}/categories → set categories
async createCategory(name: string)  // POST → append to categories
async renameCategory(categoryId: string, name: string)  // PATCH → update in categories
async deleteCategory(categoryId: string)  // DELETE → filter from categories
async saveChannelOrder(channels: ChannelOrderItem[])  // PUT /servers/{id}/channel-order
async saveCategoryOrder(categories: CategoryOrderItem[])  // PUT /servers/{id}/category-order
```

- [ ] **Step 4: Add server/channel description methods**

```typescript
async updateServerDescription(description: string)  // PATCH /servers/{id} with description
async updateChannelDescription(channelId: string, description: string)  // PATCH /servers/{id}/channels/{id} with description
```

- [ ] **Step 5: Add audit log methods**

```typescript
async loadAuditLog()  // Initial load, clears existing entries
async loadOlderAuditLog()  // Pagination, uses last entry's createdAt as cursor
```

- [ ] **Step 6: Add notification preference methods**

```typescript
async loadNotificationPreferences()  // GET /servers/{id}/notification-preferences
async toggleServerMute()  // PUT /servers/{id}/mute
async toggleChannelMute(channelId: string)  // PUT /servers/{id}/channels/{id}/mute
```

Add derived helpers:
```typescript
get isServerMuted()  // derives from notificationPreferences
isChannelMuted(channelId: string)  // check channel overrides
```

- [ ] **Step 7: Remove `showInvitePanel` state**

Remove the `showInvitePanel = $state(false)` field and any toggle methods.

- [ ] **Step 8: Wire category/preference loading into server selection**

When a server is selected (`selectServer` method), also call `loadCategories()` and `loadNotificationPreferences()`.

- [ ] **Step 9: Clean up on sign-out/server switch**

Reset `categories`, `auditLogEntries`, `notificationPreferences` when switching servers or signing out.

- [ ] **Step 10: Verify type-check and commit**

Run: `cd apps/web && npm run check`

```
git add apps/web/src/lib/state/app-state.svelte.ts
git commit -m "feat: add AppState fields and methods for categories, audit log, descriptions, and notification preferences"
```

---

## Task 12: Settings modal — General tab (server description) and tab restructuring

**Files:**
- Modify: `apps/web/src/lib/components/server-settings/ServerSettingsSidebar.svelte`
- Modify: `apps/web/src/lib/components/server-settings/ServerSettingsModal.svelte`
- Modify: `apps/web/src/lib/components/server-settings/ServerSettings.svelte`

- [ ] **Step 1: Update `ServerSettingsSidebar` with new tabs**

Add "Channels", "Invites", and "Audit Log" categories. The tab list should be:
- General (always visible)
- Channels (admin+)
- Invites (admin+)
- Emojis (admin+)
- Members (admin+)
- Audit Log (admin+)

- [ ] **Step 2: Update `ServerSettingsModal` to render new tab components**

Import and conditionally render `ServerChannels`, `ServerInvites`, `ServerAuditLog` based on `app.serverSettingsCategory`.

- [ ] **Step 3: Simplify General tab — move channels section out, add description**

In `ServerSettings.svelte`:
- Remove the channels management section (it moves to the new Channels tab)
- Add a description textarea below the server name edit, with 256 char counter
- Save on blur or Enter, call `app.updateServerDescription()`

- [ ] **Step 4: Verify type-check and commit**

Run: `cd apps/web && npm run check`

```
git add apps/web/src/lib/components/server-settings/
git commit -m "feat: restructure settings tabs, add server description to General tab"
```

---

## Task 13: Settings modal — Channels tab with drag-and-drop

**Files:**
- Create: `apps/web/src/lib/components/server-settings/ServerChannels.svelte`

- [ ] **Step 1: Install `svelte-dnd-action`**

Run: `cd apps/web && npm install svelte-dnd-action`

- [ ] **Step 2: Create `ServerChannels.svelte`**

This component contains:
- **Category management section**: create new category (input + button), list categories with rename/delete
- **Channel list grouped by category**: uncategorized channels at top, then each category as a collapsible group
- **Drag-and-drop**: use `svelte-dnd-action`'s `dndzone` action on each category group and the uncategorized group to enable reordering within and across categories
- **Per-channel controls**: inline rename, description textarea (expandable), type indicator, delete/purge buttons (moved from the old General tab)
- **Save order**: after a drag-and-drop completes, call `app.saveChannelOrder()` with the new positions

Follow existing styling patterns from `ServerSettings.svelte` for consistency.

- [ ] **Step 3: Verify type-check and commit**

Run: `cd apps/web && npm run check`

```
git add apps/web/src/lib/components/server-settings/ServerChannels.svelte apps/web/package.json apps/web/package-lock.json
git commit -m "feat: add Channels settings tab with category management and drag-and-drop reordering"
```

---

## Task 14: Settings modal — Invites tab

**Files:**
- Create: `apps/web/src/lib/components/server-settings/ServerInvites.svelte`
- Delete: `apps/web/src/lib/components/channel-sidebar/InvitePanel.svelte`
- Modify: `apps/web/src/lib/components/channel-sidebar/ChannelSidebar.svelte`

- [ ] **Step 1: Create `ServerInvites.svelte`**

Port the invite management UI from `InvitePanel.svelte` into a settings tab component:
- Create invite form: expiration dropdown (1h, 6h, 12h, 24h, 7d, never), max uses input
- Active invites table: code, created by, uses/max, expires (relative time), copy link button, revoke button
- Call `app.loadInvites()` on mount
- Use existing `app.createInvite()` and `app.revokeInvite()` methods

Style consistently with other settings tabs.

- [ ] **Step 2: Delete `InvitePanel.svelte` and remove from sidebar**

Delete `apps/web/src/lib/components/channel-sidebar/InvitePanel.svelte`.

In `ChannelSidebar.svelte`:
- Remove the `InvitePanel` import
- Remove the invite toggle button from the header
- Remove the `{#if app.showInvitePanel}` conditional block

- [ ] **Step 3: Verify type-check and commit**

Run: `cd apps/web && npm run check`

```
git add apps/web/src/lib/components/server-settings/ServerInvites.svelte apps/web/src/lib/components/channel-sidebar/
git commit -m "feat: move invite management to settings Invites tab, remove sidebar panel"
```

---

## Task 15: Settings modal — Audit Log tab

**Files:**
- Create: `apps/web/src/lib/components/server-settings/ServerAuditLog.svelte`

- [ ] **Step 1: Create `ServerAuditLog.svelte`**

This component displays a paginated, scrollable list of audit log entries:
- Call `app.loadAuditLog()` on mount
- Each entry row: actor avatar (32px), actor display name, action description (formatted from `action` + `details`), relative timestamp
- Format action descriptions: e.g., `ServerRenamed` + details → "Renamed server from 'Old' to 'New'"
- Handle null actor (show "Deleted User" with placeholder avatar)
- Infinite scroll: detect scroll near bottom, call `app.loadOlderAuditLog()` when `hasMoreAuditLog` is true
- "Loading..." indicator at bottom during pagination
- Empty state: "No audit log entries yet" message

- [ ] **Step 2: Verify type-check and commit**

Run: `cd apps/web && npm run check`

```
git add apps/web/src/lib/components/server-settings/ServerAuditLog.svelte
git commit -m "feat: add Audit Log settings tab with paginated event history"
```

---

## Task 16: Channel sidebar — category grouping and collapsible headers

**Files:**
- Modify: `apps/web/src/lib/components/channel-sidebar/ChannelSidebar.svelte`

- [ ] **Step 1: Add category grouping logic**

Replace the current flat text/voice channel split with category-aware grouping:

```typescript
// Derive grouped channel structure
const uncategorizedChannels = $derived(
    app.channels.filter(c => !c.categoryId).sort((a, b) => a.position - b.position)
);

const categorizedGroups = $derived(
    app.categories
        .sort((a, b) => a.position - b.position)
        .map(cat => ({
            ...cat,
            channels: app.channels
                .filter(c => c.categoryId === cat.id)
                .sort((a, b) => a.position - b.position)
        }))
);
```

- [ ] **Step 2: Add collapse state management**

```typescript
let collapsedCategories = $state<Set<string>>(new Set());

function loadCollapsedState() {
    const key = `codec:category-collapse:${app.selectedServerId}`;
    const stored = localStorage.getItem(key);
    collapsedCategories = stored ? new Set(JSON.parse(stored)) : new Set();
}

function toggleCollapse(categoryId: string) {
    const next = new Set(collapsedCategories);
    if (next.has(categoryId)) next.delete(categoryId);
    else next.add(categoryId);
    collapsedCategories = next;
    const key = `codec:category-collapse:${app.selectedServerId}`;
    localStorage.setItem(key, JSON.stringify([...next]));
}
```

Call `loadCollapsedState()` when server changes.

- [ ] **Step 3: Update template**

Render uncategorized channels first, then each category with a collapsible header:

```svelte
<!-- Uncategorized channels -->
{#each uncategorizedChannels as channel}
    <!-- existing channel button markup -->
{/each}

<!-- Categorized groups -->
{#each categorizedGroups as group}
    <button class="category-header" onclick={() => toggleCollapse(group.id)}>
        <span class="collapse-arrow" class:collapsed={collapsedCategories.has(group.id)}>▸</span>
        {group.name}
    </button>
    {#if !collapsedCategories.has(group.id)}
        {#each group.channels as channel}
            <!-- existing channel button markup -->
        {/each}
    {/if}
{/each}
```

- [ ] **Step 4: Add CSS for category headers**

Style category headers with uppercase text, smaller font size, muted color, consistent with Discord's category styling.

- [ ] **Step 5: Verify type-check and commit**

Run: `cd apps/web && npm run check`

```
git add apps/web/src/lib/components/channel-sidebar/ChannelSidebar.svelte
git commit -m "feat: add category grouping with collapsible headers to channel sidebar"
```

---

## Task 17: Chat area header — server and channel descriptions

**Files:**
- Modify: `apps/web/src/lib/components/chat/ChatArea.svelte`

- [ ] **Step 1: Add server description to header**

Below the server name in the chat area header, add:

```svelte
{#if app.selectedServerDescription}
    <span class="server-description" title={app.selectedServerDescription}>
        {app.selectedServerDescription.length > 80
            ? app.selectedServerDescription.slice(0, 80) + '...'
            : app.selectedServerDescription}
    </span>
{/if}
```

Add derived property `selectedServerDescription` to `AppState` if not already present.

- [ ] **Step 2: Add channel description/topic to header**

After the channel name, add a vertical divider and the channel description:

```svelte
{#if selectedChannel?.description}
    <span class="header-divider">|</span>
    <span class="channel-description" title={selectedChannel.description}>
        {selectedChannel.description}
    </span>
{/if}
```

- [ ] **Step 3: Add inline edit for admins**

For both server and channel descriptions, if the user is Owner/Admin/GlobalAdmin, show a pencil icon on hover that opens an inline edit input. On Enter or blur, save via `app.updateServerDescription()` or `app.updateChannelDescription()`.

- [ ] **Step 4: Add CSS**

Style descriptions with muted color (`var(--text-muted)`), smaller font, truncation with ellipsis. Pencil icon appears on hover with subtle transition.

- [ ] **Step 5: Verify type-check and commit**

Run: `cd apps/web && npm run check`

```
git add apps/web/src/lib/components/chat/ChatArea.svelte
git commit -m "feat: display server and channel descriptions in chat area header with inline editing"
```

---

## Task 18: Notification mute — context menus and UI

**Files:**
- Create: `apps/web/src/lib/components/channel-sidebar/ContextMenu.svelte`
- Modify: `apps/web/src/lib/components/server-sidebar/ServerSidebar.svelte`
- Modify: `apps/web/src/lib/components/channel-sidebar/ChannelSidebar.svelte`

- [ ] **Step 1: Create reusable `ContextMenu` component**

Create `apps/web/src/lib/components/channel-sidebar/ContextMenu.svelte`:

```svelte
<script lang="ts">
    let { x, y, items, onClose }: {
        x: number;
        y: number;
        items: { label: string; onClick: () => void }[];
        onClose: () => void;
    } = $props();
</script>

<svelte:window onclick={onClose} onkeydown={(e) => e.key === 'Escape' && onClose()} />

<div class="context-menu" style="left: {x}px; top: {y}px;">
    {#each items as item}
        <button onclick={item.onClick}>{item.label}</button>
    {/each}
</div>
```

Style with dark background, rounded corners, shadow, matching existing design tokens.

- [ ] **Step 2: Add server mute context menu to `ServerSidebar`**

In `ServerSidebar.svelte`, add a `contextmenu` event handler on each server icon:

```svelte
<button
    oncontextmenu={(e) => { e.preventDefault(); openServerContextMenu(e, server); }}
    ...
>
```

Show context menu with "Mute Server" / "Unmute Server" toggle. On click, call `app.toggleServerMute()`.

Add a muted bell badge overlay on muted server icons.

- [ ] **Step 3: Add channel mute context menu to `ChannelSidebar`**

In `ChannelSidebar.svelte`, add a `contextmenu` event handler on each channel button:

```svelte
<button
    oncontextmenu={(e) => { e.preventDefault(); openChannelContextMenu(e, channel); }}
    ...
>
```

Show context menu with "Mute Channel" / "Unmute Channel" toggle. On click, call `app.toggleChannelMute(channel.id)`.

Muted channels render with dimmed text (`opacity: 0.5` or `var(--text-muted)`).

- [ ] **Step 4: Verify type-check and commit**

Run: `cd apps/web && npm run check`

```
git add apps/web/src/lib/components/channel-sidebar/ apps/web/src/lib/components/server-sidebar/
git commit -m "feat: add right-click context menus for server and channel mute toggles"
```

---

## Task 19: Final verification and documentation

**Files:**
- Modify: `docs/FEATURES.md`
- Modify: `docs/ARCHITECTURE.md`
- Modify: `docs/SERVER_SETTINGS.md`
- Modify: `PLAN.md`

- [ ] **Step 1: Full backend build and test**

Run: `cd apps/api/Codec.Api && dotnet build`

Expected: Build succeeded. 0 Errors.

Run: `cd apps/api && dotnet test Codec.sln`

Expected: All tests PASS.

- [ ] **Step 2: Full frontend build and type-check**

Run: `cd apps/web && npm run check`

Expected: 0 errors.

Run: `cd apps/web && npm run build`

Expected: Build succeeds.

- [ ] **Step 3: Update documentation**

Update `docs/FEATURES.md`:
- Move "Server settings/configuration" from Planned to Implemented
- Add entries for: invite management tab, server descriptions, channel descriptions, channel categories, audit log, notification preferences

Update `docs/SERVER_SETTINGS.md`:
- Add new tabs documentation (Channels, Invites, Audit Log)
- Document category management and channel ordering
- Document notification preferences

Update `docs/ARCHITECTURE.md`:
- Add new endpoints
- Add new SignalR events
- Add new entities to data model section

Update `PLAN.md`:
- Add task breakdown section for server settings enhancements
- Mark feature as implemented in current status

- [ ] **Step 4: Commit documentation**

```
git add docs/ PLAN.md
git commit -m "docs: update documentation for server settings enhancements"
```
