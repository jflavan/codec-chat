# Roles and Permissions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement Discord-style multi-role assignment, granular permission editing, and per-channel permission overrides.

**Architecture:** New `ServerMemberRole` join table replaces single `RoleId` on `ServerMember`. A `ChannelPermissionOverride` table stores per-channel Allow/Deny bitmasks. A new `PermissionResolverService` computes effective permissions by OR'ing all role grants, then applying channel overrides (deny wins). All existing `EnsureAdminAsync`/`EnsurePermissionAsync` calls migrate to the new resolver.

**Tech Stack:** ASP.NET Core 10 / EF Core / PostgreSQL (backend), SvelteKit 5 / TypeScript (frontend), xUnit + Testcontainers (tests)

**Spec:** `docs/superpowers/specs/2026-03-30-roles-and-permissions-design.md`

---

## Task 1: New Data Model Entities

**Files:**
- Create: `apps/api/Codec.Api/Models/ServerMemberRole.cs`
- Create: `apps/api/Codec.Api/Models/ChannelPermissionOverride.cs`
- Modify: `apps/api/Codec.Api/Models/ServerMember.cs`
- Modify: `apps/api/Codec.Api/Models/ServerRoleEntity.cs`

- [ ] **Step 1: Create `ServerMemberRole` join entity**

```csharp
// apps/api/Codec.Api/Models/ServerMemberRole.cs
namespace Codec.Api.Models;

/// <summary>
/// Join entity linking a server member to one of their assigned roles.
/// A member can have multiple roles; permissions are OR'd across all of them.
/// </summary>
public class ServerMemberRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public ServerMember? Member { get; set; }
    public ServerRoleEntity? Role { get; set; }
}
```

- [ ] **Step 2: Create `ChannelPermissionOverride` entity**

```csharp
// apps/api/Codec.Api/Models/ChannelPermissionOverride.cs
namespace Codec.Api.Models;

/// <summary>
/// Per-channel permission override for a specific role.
/// Allow bits grant permissions beyond the server-level role grants.
/// Deny bits revoke permissions even if granted by the role.
/// Deny is applied last and always wins.
/// </summary>
public class ChannelPermissionOverride
{
    public Guid Id { get; set; }
    public Guid ChannelId { get; set; }
    public Guid RoleId { get; set; }

    /// <summary>Permission bits explicitly granted in this channel.</summary>
    public Permission Allow { get; set; } = Permission.None;

    /// <summary>Permission bits explicitly denied in this channel. Deny always wins over Allow.</summary>
    public Permission Deny { get; set; } = Permission.None;

    // Navigation properties
    public Channel? Channel { get; set; }
    public ServerRoleEntity? Role { get; set; }
}
```

- [ ] **Step 3: Update `ServerMember` — add `MemberRoles` collection**

In `apps/api/Codec.Api/Models/ServerMember.cs`, add after the existing navigation properties:

```csharp
public List<ServerMemberRole> MemberRoles { get; set; } = [];
```

Do NOT remove `RoleId` or `Role` yet — that happens in the migration task. Both old and new must coexist until migration runs.

- [ ] **Step 4: Update `ServerRoleEntity` — add `MemberRoles` collection**

In `apps/api/Codec.Api/Models/ServerRoleEntity.cs`, add alongside the existing `Members` property:

```csharp
public List<ServerMemberRole> MemberRoles { get; set; } = [];
```

Keep the old `Members` nav property for now — it will be removed after migration.

- [ ] **Step 5: Commit**

```bash
git add apps/api/Codec.Api/Models/ServerMemberRole.cs apps/api/Codec.Api/Models/ChannelPermissionOverride.cs apps/api/Codec.Api/Models/ServerMember.cs apps/api/Codec.Api/Models/ServerRoleEntity.cs
git commit -m "feat: add ServerMemberRole and ChannelPermissionOverride entities"
```

---

## Task 2: DbContext Configuration and Migration

**Files:**
- Modify: `apps/api/Codec.Api/Data/CodecDbContext.cs`
- Create: `apps/api/Codec.Api/Migrations/<timestamp>_MultiRoleAndChannelOverrides.cs`

- [ ] **Step 1: Add DbSets and configure relationships in `CodecDbContext.cs`**

Add DbSet properties near the existing ones:

```csharp
public DbSet<ServerMemberRole> ServerMemberRoles => Set<ServerMemberRole>();
public DbSet<ChannelPermissionOverride> ChannelPermissionOverrides => Set<ChannelPermissionOverride>();
```

In `OnModelCreating`, add after the existing `ServerRoleEntity` config block (after line ~147):

```csharp
modelBuilder.Entity<ServerMemberRole>(e =>
{
    e.HasKey(mr => new { mr.UserId, mr.RoleId });

    e.HasOne(mr => mr.Member)
        .WithMany(m => m.MemberRoles)
        .HasForeignKey(mr => mr.UserId)
        .HasPrincipalKey(m => m.UserId)
        .OnDelete(DeleteBehavior.Cascade);

    e.HasOne(mr => mr.Role)
        .WithMany(r => r.MemberRoles)
        .HasForeignKey(mr => mr.RoleId)
        .OnDelete(DeleteBehavior.Cascade);
});

modelBuilder.Entity<ChannelPermissionOverride>(e =>
{
    e.HasOne(o => o.Channel)
        .WithMany()
        .HasForeignKey(o => o.ChannelId)
        .OnDelete(DeleteBehavior.Cascade);

    e.HasOne(o => o.Role)
        .WithMany()
        .HasForeignKey(o => o.RoleId)
        .OnDelete(DeleteBehavior.Cascade);

    e.HasIndex(o => new { o.ChannelId, o.RoleId }).IsUnique();
});
```

**Note on `ServerMemberRole.Member` FK:** `ServerMember` has a composite PK `(ServerId, UserId)`, but `ServerMemberRole` only stores `UserId`. The `HasPrincipalKey(m => m.UserId)` creates an alternate key on `ServerMember.UserId`. This works because a user can only be in one `ServerMember` row per server, and the role's `ServerId` (via `ServerRoleEntity`) implicitly scopes it. However, if this causes issues with EF Core's alternate key handling, fall back to adding `ServerId` to `ServerMemberRole` and using `HasForeignKey(mr => new { mr.ServerId, mr.UserId })` instead.

- [ ] **Step 2: Create the migration**

```bash
cd apps/api/Codec.Api
dotnet ef migrations add MultiRoleAndChannelOverrides
```

If `dotnet ef` is unavailable in the shell, manually create the migration file. The migration must:

1. Create `ServerMemberRoles` table with columns: `UserId` (Guid), `RoleId` (Guid), `AssignedAt` (DateTimeOffset), PK on `(UserId, RoleId)`, FKs to `ServerMembers(UserId)` and `ServerRoles(Id)`.
2. Create `ChannelPermissionOverrides` table with columns: `Id` (Guid), `ChannelId` (Guid), `RoleId` (Guid), `Allow` (bigint), `Deny` (bigint), unique index on `(ChannelId, RoleId)`, FKs to `Channels(Id)` and `ServerRoles(Id)`.
3. Migrate data: `INSERT INTO ServerMemberRoles (UserId, RoleId, AssignedAt) SELECT UserId, RoleId, JoinedAt FROM ServerMembers WHERE RoleId IS NOT NULL`.
4. Drop `RoleId` column and its FK from `ServerMembers`.

- [ ] **Step 3: Verify migration compiles**

```bash
cd apps/api/Codec.Api
dotnet build
```

Expected: Build succeeds. If there are compile errors from code still referencing `ServerMember.RoleId` or `ServerMember.Role`, do NOT fix them yet — those will be addressed in subsequent tasks. The migration file itself must compile, but the rest of the project may have temporary compile errors.

- [ ] **Step 4: Commit**

```bash
git add apps/api/Codec.Api/Data/CodecDbContext.cs apps/api/Codec.Api/Migrations/
git commit -m "feat: add migration for multi-role join table and channel overrides"
```

---

## Task 3: `PermissionResolverService` (TDD)

**Files:**
- Create: `apps/api/Codec.Api/Services/IPermissionResolverService.cs`
- Create: `apps/api/Codec.Api/Services/PermissionResolverService.cs`
- Create: `apps/api/Codec.Api.Tests/Services/PermissionResolverServiceTests.cs`

- [ ] **Step 1: Create the interface**

```csharp
// apps/api/Codec.Api/Services/IPermissionResolverService.cs
namespace Codec.Api.Services;

using Codec.Api.Models;

public interface IPermissionResolverService
{
    /// <summary>Compute aggregated server-level permissions across all of a member's roles.</summary>
    Task<Permission> ResolveServerPermissionsAsync(Guid serverId, Guid userId);

    /// <summary>Compute effective permissions for a user in a specific channel (includes overrides).</summary>
    Task<Permission> ResolveChannelPermissionsAsync(Guid channelId, Guid userId);

    /// <summary>Check if a user has a specific permission at the server level.</summary>
    Task<bool> HasServerPermissionAsync(Guid serverId, Guid userId, Permission permission);

    /// <summary>Check if a user has a specific permission in a specific channel.</summary>
    Task<bool> HasChannelPermissionAsync(Guid channelId, Guid userId, Permission permission);

    /// <summary>Get the highest role position (lowest number) for a user in a server. Returns int.MaxValue if no roles.</summary>
    Task<int> GetHighestRolePositionAsync(Guid serverId, Guid userId);

    /// <summary>Check if a user is the server owner.</summary>
    Task<bool> IsOwnerAsync(Guid serverId, Guid userId);
}
```

- [ ] **Step 2: Write failing tests for the resolver**

```csharp
// apps/api/Codec.Api.Tests/Services/PermissionResolverServiceTests.cs
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Tests.Services;

public class PermissionResolverServiceTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly PermissionResolverService _sut;

    // Shared test data
    private readonly Guid _serverId = Guid.NewGuid();
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _memberId = Guid.NewGuid();
    private readonly Guid _channelId = Guid.NewGuid();
    private readonly Guid _ownerRoleId = Guid.NewGuid();
    private readonly Guid _adminRoleId = Guid.NewGuid();
    private readonly Guid _memberRoleId = Guid.NewGuid();
    private readonly Guid _customRoleId = Guid.NewGuid();

    public PermissionResolverServiceTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);
        _sut = new PermissionResolverService(_db);

        SeedTestData();
    }

    private void SeedTestData()
    {
        var server = new Server { Id = _serverId, Name = "Test", OwnerId = _ownerId };
        _db.Servers.Add(server);

        // Roles
        var ownerRole = new ServerRoleEntity
        {
            Id = _ownerRoleId, ServerId = _serverId, Name = "Owner",
            Position = 0, Permissions = Permission.Administrator, IsSystemRole = true
        };
        var adminRole = new ServerRoleEntity
        {
            Id = _adminRoleId, ServerId = _serverId, Name = "Admin",
            Position = 1, Permissions = PermissionExtensions.AdminDefaults, IsSystemRole = true
        };
        var memberRole = new ServerRoleEntity
        {
            Id = _memberRoleId, ServerId = _serverId, Name = "Member",
            Position = 2, Permissions = PermissionExtensions.MemberDefaults, IsSystemRole = true
        };
        var customRole = new ServerRoleEntity
        {
            Id = _customRoleId, ServerId = _serverId, Name = "Moderator",
            Position = 1, Permissions = Permission.ManageMessages | Permission.KickMembers
        };
        _db.ServerRoles.AddRange(ownerRole, adminRole, memberRole, customRole);

        // Members
        _db.ServerMembers.Add(new ServerMember { ServerId = _serverId, UserId = _ownerId });
        _db.ServerMembers.Add(new ServerMember { ServerId = _serverId, UserId = _memberId });

        // Role assignments via join table
        _db.ServerMemberRoles.Add(new ServerMemberRole { UserId = _ownerId, RoleId = _ownerRoleId });
        _db.ServerMemberRoles.Add(new ServerMemberRole { UserId = _memberId, RoleId = _memberRoleId });

        // Channel
        _db.Channels.Add(new Channel { Id = _channelId, ServerId = _serverId, Name = "general", Position = 0 });

        _db.SaveChanges();
    }

    [Fact]
    public async Task ResolveServerPermissions_SingleRole_ReturnsRolePermissions()
    {
        var perms = await _sut.ResolveServerPermissionsAsync(_serverId, _memberId);
        Assert.Equal(PermissionExtensions.MemberDefaults, perms);
    }

    [Fact]
    public async Task ResolveServerPermissions_MultiRole_ORsCombined()
    {
        // Give member the custom Moderator role too
        _db.ServerMemberRoles.Add(new ServerMemberRole { UserId = _memberId, RoleId = _customRoleId });
        await _db.SaveChangesAsync();

        var perms = await _sut.ResolveServerPermissionsAsync(_serverId, _memberId);
        var expected = PermissionExtensions.MemberDefaults | Permission.ManageMessages | Permission.KickMembers;
        Assert.Equal(expected, perms);
    }

    [Fact]
    public async Task ResolveServerPermissions_Owner_ReturnsAllPermissions()
    {
        var perms = await _sut.ResolveServerPermissionsAsync(_serverId, _ownerId);
        // Owner bypasses everything — should have all bits set
        Assert.True(perms.Has(Permission.Administrator));
        Assert.True(perms.Has(Permission.BanMembers));
        Assert.True(perms.Has(Permission.ManageServer));
    }

    [Fact]
    public async Task ResolveServerPermissions_AdministratorRole_GrantsAll()
    {
        // Give member the owner role (which has Administrator)
        _db.ServerMemberRoles.Add(new ServerMemberRole { UserId = _memberId, RoleId = _ownerRoleId });
        await _db.SaveChangesAsync();

        var perms = await _sut.ResolveServerPermissionsAsync(_serverId, _memberId);
        Assert.True(perms.Has(Permission.Administrator));
    }

    [Fact]
    public async Task ResolveChannelPermissions_NoOverrides_ReturnsServerPerms()
    {
        var perms = await _sut.ResolveChannelPermissionsAsync(_channelId, _memberId);
        Assert.Equal(PermissionExtensions.MemberDefaults, perms);
    }

    [Fact]
    public async Task ResolveChannelPermissions_AllowOverride_GrantsExtra()
    {
        _db.ChannelPermissionOverrides.Add(new ChannelPermissionOverride
        {
            Id = Guid.NewGuid(), ChannelId = _channelId, RoleId = _memberRoleId,
            Allow = Permission.ManageMessages, Deny = Permission.None
        });
        await _db.SaveChangesAsync();

        var perms = await _sut.ResolveChannelPermissionsAsync(_channelId, _memberId);
        Assert.True(perms.Has(Permission.ManageMessages));
        Assert.True(perms.Has(Permission.SendMessages)); // from server role
    }

    [Fact]
    public async Task ResolveChannelPermissions_DenyOverride_RevokesPermission()
    {
        _db.ChannelPermissionOverrides.Add(new ChannelPermissionOverride
        {
            Id = Guid.NewGuid(), ChannelId = _channelId, RoleId = _memberRoleId,
            Allow = Permission.None, Deny = Permission.SendMessages
        });
        await _db.SaveChangesAsync();

        var perms = await _sut.ResolveChannelPermissionsAsync(_channelId, _memberId);
        Assert.False((perms & Permission.SendMessages) == Permission.SendMessages);
    }

    [Fact]
    public async Task ResolveChannelPermissions_DenyWinsOverAllow()
    {
        // Custom role allows SendMessages, but override denies it
        _db.ServerMemberRoles.Add(new ServerMemberRole { UserId = _memberId, RoleId = _customRoleId });
        _db.ChannelPermissionOverrides.Add(new ChannelPermissionOverride
        {
            Id = Guid.NewGuid(), ChannelId = _channelId, RoleId = _customRoleId,
            Allow = Permission.SendMessages, Deny = Permission.None
        });
        _db.ChannelPermissionOverrides.Add(new ChannelPermissionOverride
        {
            Id = Guid.NewGuid(), ChannelId = _channelId, RoleId = _memberRoleId,
            Allow = Permission.None, Deny = Permission.SendMessages
        });
        await _db.SaveChangesAsync();

        var perms = await _sut.ResolveChannelPermissionsAsync(_channelId, _memberId);
        // Deny applied last: (serverPerms | channelAllow) & ~channelDeny
        Assert.False((perms & Permission.SendMessages) == Permission.SendMessages);
    }

    [Fact]
    public async Task ResolveChannelPermissions_ViewChannelsGate_DeniesEverything()
    {
        _db.ChannelPermissionOverrides.Add(new ChannelPermissionOverride
        {
            Id = Guid.NewGuid(), ChannelId = _channelId, RoleId = _memberRoleId,
            Allow = Permission.None, Deny = Permission.ViewChannels
        });
        await _db.SaveChangesAsync();

        var perms = await _sut.ResolveChannelPermissionsAsync(_channelId, _memberId);
        Assert.Equal(Permission.None, perms);
    }

    [Fact]
    public async Task ResolveChannelPermissions_Administrator_BypassesOverrides()
    {
        _db.ChannelPermissionOverrides.Add(new ChannelPermissionOverride
        {
            Id = Guid.NewGuid(), ChannelId = _channelId, RoleId = _ownerRoleId,
            Allow = Permission.None, Deny = Permission.SendMessages
        });
        await _db.SaveChangesAsync();

        // Owner has Administrator role — should bypass channel overrides
        var perms = await _sut.ResolveChannelPermissionsAsync(_channelId, _ownerId);
        Assert.True(perms.Has(Permission.SendMessages));
    }

    [Fact]
    public async Task IsOwner_ServerOwner_ReturnsTrue()
    {
        Assert.True(await _sut.IsOwnerAsync(_serverId, _ownerId));
    }

    [Fact]
    public async Task IsOwner_RegularMember_ReturnsFalse()
    {
        Assert.False(await _sut.IsOwnerAsync(_serverId, _memberId));
    }

    [Fact]
    public async Task GetHighestRolePosition_MultiRole_ReturnsLowestNumber()
    {
        // Member has memberRole (position 2). Add customRole (position 1).
        _db.ServerMemberRoles.Add(new ServerMemberRole { UserId = _memberId, RoleId = _customRoleId });
        await _db.SaveChangesAsync();

        var pos = await _sut.GetHighestRolePositionAsync(_serverId, _memberId);
        Assert.Equal(1, pos);
    }

    [Fact]
    public async Task GetHighestRolePosition_NoRoles_ReturnsMaxValue()
    {
        var unknownUser = Guid.NewGuid();
        var pos = await _sut.GetHighestRolePositionAsync(_serverId, unknownUser);
        Assert.Equal(int.MaxValue, pos);
    }

    public void Dispose() => _db.Dispose();
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
cd apps/api
dotnet test Codec.Api.Tests/Codec.Api.Tests.csproj --filter "FullyQualifiedName~PermissionResolverServiceTests" --no-restore
```

Expected: Build error — `PermissionResolverService` class does not exist.

- [ ] **Step 4: Implement `PermissionResolverService`**

```csharp
// apps/api/Codec.Api/Services/PermissionResolverService.cs
using Codec.Api.Data;
using Codec.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Services;

public class PermissionResolverService(CodecDbContext db) : IPermissionResolverService
{
    // Per-request cache: serverId+userId → list of role entities
    private readonly Dictionary<(Guid, Guid), List<ServerRoleEntity>> _roleCache = new();

    public async Task<Permission> ResolveServerPermissionsAsync(Guid serverId, Guid userId)
    {
        // Server owner bypasses everything
        if (await IsOwnerAsync(serverId, userId))
            return (Permission)~0L; // all bits set

        var roles = await GetRolesAsync(serverId, userId);
        var perms = Permission.None;
        foreach (var role in roles)
            perms |= role.Permissions;

        // Administrator bypasses everything
        if ((perms & Permission.Administrator) != 0)
            return (Permission)~0L;

        return perms;
    }

    public async Task<Permission> ResolveChannelPermissionsAsync(Guid channelId, Guid userId)
    {
        var channel = await db.Channels.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == channelId);
        if (channel is null) return Permission.None;

        var serverId = channel.ServerId;

        // Server owner bypasses everything
        if (await IsOwnerAsync(serverId, userId))
            return (Permission)~0L;

        var roles = await GetRolesAsync(serverId, userId);
        var serverPerms = Permission.None;
        foreach (var role in roles)
            serverPerms |= role.Permissions;

        // Administrator bypasses channel overrides
        if ((serverPerms & Permission.Administrator) != 0)
            return (Permission)~0L;

        // Load channel overrides for the user's roles
        var roleIds = roles.Select(r => r.Id).ToList();
        var overrides = await db.ChannelPermissionOverrides.AsNoTracking()
            .Where(o => o.ChannelId == channelId && roleIds.Contains(o.RoleId))
            .ToListAsync();

        var channelAllow = Permission.None;
        var channelDeny = Permission.None;
        foreach (var o in overrides)
        {
            channelAllow |= o.Allow;
            channelDeny |= o.Deny;
        }

        // Deny wins: apply allow first, then deny last
        var effective = (serverPerms | channelAllow) & ~channelDeny;

        // ViewChannels gate
        if ((effective & Permission.ViewChannels) == 0)
            return Permission.None;

        return effective;
    }

    public async Task<bool> HasServerPermissionAsync(Guid serverId, Guid userId, Permission permission)
    {
        var perms = await ResolveServerPermissionsAsync(serverId, userId);
        return perms.Has(permission);
    }

    public async Task<bool> HasChannelPermissionAsync(Guid channelId, Guid userId, Permission permission)
    {
        var perms = await ResolveChannelPermissionsAsync(channelId, userId);
        return perms.Has(permission);
    }

    public async Task<int> GetHighestRolePositionAsync(Guid serverId, Guid userId)
    {
        var roles = await GetRolesAsync(serverId, userId);
        return roles.Count > 0 ? roles.Min(r => r.Position) : int.MaxValue;
    }

    public async Task<bool> IsOwnerAsync(Guid serverId, Guid userId)
    {
        var server = await db.Servers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == serverId);
        return server?.OwnerId == userId;
    }

    private async Task<List<ServerRoleEntity>> GetRolesAsync(Guid serverId, Guid userId)
    {
        if (_roleCache.TryGetValue((serverId, userId), out var cached))
            return cached;

        var roles = await db.ServerMemberRoles.AsNoTracking()
            .Where(mr => mr.Role!.ServerId == serverId && mr.UserId == userId)
            .Select(mr => mr.Role!)
            .ToListAsync();

        _roleCache[(serverId, userId)] = roles;
        return roles;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
cd apps/api
dotnet test Codec.Api.Tests/Codec.Api.Tests.csproj --filter "FullyQualifiedName~PermissionResolverServiceTests" --no-restore -v n
```

Expected: All 12 tests pass.

- [ ] **Step 6: Register the service in `Program.cs`**

In `apps/api/Codec.Api/Program.cs`, add alongside the existing service registrations:

```csharp
builder.Services.AddScoped<IPermissionResolverService, PermissionResolverService>();
```

- [ ] **Step 7: Commit**

```bash
git add apps/api/Codec.Api/Services/IPermissionResolverService.cs apps/api/Codec.Api/Services/PermissionResolverService.cs apps/api/Codec.Api.Tests/Services/PermissionResolverServiceTests.cs apps/api/Codec.Api/Program.cs
git commit -m "feat: add PermissionResolverService with multi-role and channel override resolution"
```

---

## Task 4: Migrate `UserService` to Multi-Role

**Files:**
- Modify: `apps/api/Codec.Api/Services/UserService.cs`
- Modify: `apps/api/Codec.Api/Services/IUserService.cs`
- Modify: `apps/api/Codec.Api.Tests/Services/UserServiceTests.cs`

- [ ] **Step 1: Update `EnsureMemberAsync` — remove `.Include(m => m.Role)`**

In `apps/api/Codec.Api/Services/UserService.cs`, replace lines 143-172 with:

```csharp
public async Task<ServerMember> EnsureMemberAsync(Guid serverId, Guid userId, bool isGlobalAdmin = false)
{
    if (isGlobalAdmin)
    {
        var member = await db.ServerMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == userId);
        if (member is not null) return member;

        var serverExists = await db.Servers.AsNoTracking().AnyAsync(s => s.Id == serverId);
        if (!serverExists) throw new Exceptions.NotFoundException("Server not found.");

        return new ServerMember { ServerId = serverId, UserId = userId };
    }

    var membership = await db.ServerMembers
        .AsNoTracking()
        .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == userId);

    if (membership is null)
    {
        var serverExists = await db.Servers.AsNoTracking().AnyAsync(s => s.Id == serverId);
        if (!serverExists) throw new Exceptions.NotFoundException("Server not found.");
        throw new Exceptions.ForbiddenException();
    }

    return membership;
}
```

- [ ] **Step 2: Rewrite `EnsurePermissionAsync` to use `PermissionResolverService`**

Inject `IPermissionResolverService` into `UserService` constructor. Replace lines 175-208:

```csharp
public async Task<ServerMember> EnsurePermissionAsync(Guid serverId, Guid userId, Permission permission, bool isGlobalAdmin = false)
{
    if (isGlobalAdmin)
    {
        var serverExists = await db.Servers.AsNoTracking().AnyAsync(s => s.Id == serverId);
        if (!serverExists) throw new Exceptions.NotFoundException("Server not found.");

        var member = await db.ServerMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == userId);
        return member ?? new ServerMember { ServerId = serverId, UserId = userId };
    }

    var membership = await db.ServerMembers.AsNoTracking()
        .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == userId);

    if (membership is null)
    {
        var serverExists = await db.Servers.AsNoTracking().AnyAsync(s => s.Id == serverId);
        if (!serverExists) throw new Exceptions.NotFoundException("Server not found.");
        throw new Exceptions.ForbiddenException();
    }

    var hasPermission = await permissionResolver.HasServerPermissionAsync(serverId, userId, permission);
    if (!hasPermission)
        throw new Exceptions.ForbiddenException();

    return membership;
}
```

- [ ] **Step 3: Rewrite `EnsureOwnerAsync` to use `Server.OwnerId`**

Replace lines 218-248:

```csharp
public async Task<ServerMember> EnsureOwnerAsync(Guid serverId, Guid userId, bool isGlobalAdmin = false)
{
    if (isGlobalAdmin)
    {
        var serverExists = await db.Servers.AsNoTracking().AnyAsync(s => s.Id == serverId);
        if (!serverExists) throw new Exceptions.NotFoundException("Server not found.");

        var member = await db.ServerMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == userId);
        return member ?? new ServerMember { ServerId = serverId, UserId = userId };
    }

    var membership = await db.ServerMembers.AsNoTracking()
        .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == userId);

    if (membership is null)
    {
        var serverExists = await db.Servers.AsNoTracking().AnyAsync(s => s.Id == serverId);
        if (!serverExists) throw new Exceptions.NotFoundException("Server not found.");
        throw new Exceptions.ForbiddenException();
    }

    var isOwner = await permissionResolver.IsOwnerAsync(serverId, userId);
    if (!isOwner)
        throw new Exceptions.ForbiddenException();

    return membership;
}
```

- [ ] **Step 4: Rewrite `GetPermissionsAsync` and `IsOwnerAsync`**

```csharp
public async Task<Permission> GetPermissionsAsync(Guid serverId, Guid userId)
{
    return await permissionResolver.ResolveServerPermissionsAsync(serverId, userId);
}

public async Task<bool> IsOwnerAsync(Guid serverId, Guid userId)
{
    return await permissionResolver.IsOwnerAsync(serverId, userId);
}
```

- [ ] **Step 5: Update `CreateDefaultRolesAsync` — assign roles via join table**

Find where new members are assigned the Member role (in `CreateDefaultRolesAsync` or wherever members are added to servers). Add the `ServerMemberRole` join entry instead of setting `member.RoleId`.

Where the code does `member.RoleId = memberRole.Id`, replace with:

```csharp
db.ServerMemberRoles.Add(new ServerMemberRole
{
    UserId = member.UserId,
    RoleId = memberRole.Id
});
```

- [ ] **Step 6: Update `UserServiceTests` to work with multi-role**

In `apps/api/Codec.Api.Tests/Services/UserServiceTests.cs`, update test setup to use `ServerMemberRoles` instead of `member.RoleId`. Mock or inject `IPermissionResolverService` as needed.

- [ ] **Step 7: Run tests**

```bash
cd apps/api
dotnet test Codec.Api.Tests/Codec.Api.Tests.csproj --filter "FullyQualifiedName~UserServiceTests" -v n
```

Expected: All tests pass.

- [ ] **Step 8: Commit**

```bash
git add apps/api/Codec.Api/Services/UserService.cs apps/api/Codec.Api/Services/IUserService.cs apps/api/Codec.Api.Tests/Services/UserServiceTests.cs
git commit -m "refactor: migrate UserService to multi-role via PermissionResolverService"
```

---

## Task 5: Update `ServersController` — Member Roles and Response Shapes

**Files:**
- Modify: `apps/api/Codec.Api/Controllers/ServersController.cs`
- Create: `apps/api/Codec.Api/Models/UpdateMemberRolesRequest.cs`

- [ ] **Step 1: Create `UpdateMemberRolesRequest` DTO**

```csharp
// apps/api/Codec.Api/Models/UpdateMemberRolesRequest.cs
using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class UpdateMemberRolesRequest
{
    [Required]
    public List<Guid> RoleIds { get; set; } = [];
}
```

- [ ] **Step 2: Update `GetMyServers` response to include multi-role data**

In `ServersController.GetMyServers` (around line 42), change the response projection. Where the code projects `Role = member.Role!.Name` and `Permissions = (long)member.Role.Permissions`, replace with a query that joins through `ServerMemberRoles`:

```csharp
var memberRoles = await db.ServerMemberRoles.AsNoTracking()
    .Where(mr => mr.UserId == userId && mr.Role!.ServerId == member.ServerId)
    .Select(mr => new
    {
        mr.Role!.Id,
        mr.Role.Name,
        mr.Role.Color,
        mr.Role.Position,
        mr.Role.IsSystemRole
    })
    .OrderBy(r => r.Position)
    .ToListAsync();

// In the response object:
// roles = memberRoles,
// permissions = (long)await permissionResolver.ResolveServerPermissionsAsync(member.ServerId, userId),
// isOwner = server.OwnerId == userId,
```

- [ ] **Step 3: Update `GetMembers` response for multi-role**

In `ServersController.GetMembers` (around line 243), replace the single-role projection with:

```csharp
// For each member, load their roles from the join table
var memberRoleMap = await db.ServerMemberRoles.AsNoTracking()
    .Where(mr => mr.Role!.ServerId == serverId)
    .Select(mr => new { mr.UserId, mr.RoleId, mr.Role!.Name, mr.Role.Color, mr.Role.Position, mr.Role.IsSystemRole })
    .ToListAsync();

// In the member projection:
// roles = memberRoleMap.Where(r => r.UserId == m.UserId).OrderBy(r => r.Position).Select(r => new { ... }),
// permissions = aggregated permissions,
// displayRole = first role with color (by position),
// highestPosition = min position
```

- [ ] **Step 4: Add multi-role assignment endpoints**

Add these endpoints to `ServersController`:

```csharp
/// <summary>Set the full list of roles for a member.</summary>
[HttpPut("{serverId:guid}/members/{userId:guid}/roles")]
public async Task<IActionResult> SetMemberRoles(Guid serverId, Guid userId, [FromBody] UpdateMemberRolesRequest request)
{
    var callerId = GetUserId();
    var isGlobalAdmin = IsGlobalAdmin();
    await userService.EnsurePermissionAsync(serverId, callerId, Permission.ManageRoles, isGlobalAdmin);

    // Hierarchy check: caller can only assign roles below their own highest position
    var callerPosition = await permissionResolver.GetHighestRolePositionAsync(serverId, callerId);
    var server = await db.Servers.FindAsync(serverId);
    if (server is null) return NotFound();

    // Validate all requested roles exist and belong to this server
    var roles = await db.ServerRoles.AsNoTracking()
        .Where(r => r.ServerId == serverId && request.RoleIds.Contains(r.Id))
        .ToListAsync();

    if (roles.Count != request.RoleIds.Count)
        return BadRequest(new { error = "One or more role IDs are invalid." });

    // Cannot assign Owner role or roles above caller's position (unless global admin)
    foreach (var role in roles)
    {
        if (role.Name == "Owner" && server.OwnerId != callerId && !isGlobalAdmin)
            return BadRequest(new { error = "Cannot assign the Owner role." });
        if (role.Position <= callerPosition && !isGlobalAdmin && server.OwnerId != callerId)
            return Forbid();
    }

    // Replace all existing role assignments
    var existing = await db.ServerMemberRoles
        .Where(mr => mr.UserId == userId && mr.Role!.ServerId == serverId)
        .ToListAsync();
    db.ServerMemberRoles.RemoveRange(existing);

    foreach (var roleId in request.RoleIds)
    {
        db.ServerMemberRoles.Add(new ServerMemberRole { UserId = userId, RoleId = roleId });
    }

    await db.SaveChangesAsync();

    // Broadcast real-time update
    await hubContext.Clients.Group($"server-{serverId}")
        .SendAsync("MemberRolesUpdated", new { userId, roles = roles.Select(r => new { r.Id, r.Name, r.Color, r.Position, r.IsSystemRole }) });

    return Ok();
}

/// <summary>Add a single role to a member.</summary>
[HttpPost("{serverId:guid}/members/{userId:guid}/roles/{roleId:guid}")]
public async Task<IActionResult> AddMemberRole(Guid serverId, Guid userId, Guid roleId)
{
    var callerId = GetUserId();
    var isGlobalAdmin = IsGlobalAdmin();
    await userService.EnsurePermissionAsync(serverId, callerId, Permission.ManageRoles, isGlobalAdmin);

    var role = await db.ServerRoles.AsNoTracking()
        .FirstOrDefaultAsync(r => r.Id == roleId && r.ServerId == serverId);
    if (role is null) return NotFound();

    var server = await db.Servers.FindAsync(serverId);
    if (role.Name == "Owner" && server?.OwnerId != callerId && !isGlobalAdmin)
        return BadRequest(new { error = "Cannot assign the Owner role." });

    var callerPosition = await permissionResolver.GetHighestRolePositionAsync(serverId, callerId);
    if (role.Position <= callerPosition && !isGlobalAdmin && server?.OwnerId != callerId)
        return Forbid();

    var exists = await db.ServerMemberRoles.AnyAsync(mr => mr.UserId == userId && mr.RoleId == roleId);
    if (exists) return Ok(); // idempotent

    db.ServerMemberRoles.Add(new ServerMemberRole { UserId = userId, RoleId = roleId });
    await db.SaveChangesAsync();

    await hubContext.Clients.Group($"server-{serverId}")
        .SendAsync("MemberRolesUpdated", new { userId, roleId, action = "added" });

    return Ok();
}

/// <summary>Remove a single role from a member.</summary>
[HttpDelete("{serverId:guid}/members/{userId:guid}/roles/{roleId:guid}")]
public async Task<IActionResult> RemoveMemberRole(Guid serverId, Guid userId, Guid roleId)
{
    var callerId = GetUserId();
    var isGlobalAdmin = IsGlobalAdmin();
    await userService.EnsurePermissionAsync(serverId, callerId, Permission.ManageRoles, isGlobalAdmin);

    var role = await db.ServerRoles.AsNoTracking()
        .FirstOrDefaultAsync(r => r.Id == roleId && r.ServerId == serverId);
    if (role is null) return NotFound();

    if (role.Name == "Owner") return BadRequest(new { error = "Cannot remove the Owner role." });

    var callerPosition = await permissionResolver.GetHighestRolePositionAsync(serverId, callerId);
    if (role.Position <= callerPosition && !isGlobalAdmin)
        return Forbid();

    var entry = await db.ServerMemberRoles
        .FirstOrDefaultAsync(mr => mr.UserId == userId && mr.RoleId == roleId);
    if (entry is null) return NotFound();

    db.ServerMemberRoles.Remove(entry);

    // If member now has no roles, auto-assign the Member role
    var remainingCount = await db.ServerMemberRoles
        .CountAsync(mr => mr.UserId == userId && mr.Role!.ServerId == serverId);
    if (remainingCount == 0)
    {
        var memberRole = await db.ServerRoles.FirstOrDefaultAsync(r => r.ServerId == serverId && r.Name == "Member" && r.IsSystemRole);
        if (memberRole is not null)
            db.ServerMemberRoles.Add(new ServerMemberRole { UserId = userId, RoleId = memberRole.Id });
    }

    await db.SaveChangesAsync();

    await hubContext.Clients.Group($"server-{serverId}")
        .SendAsync("MemberRolesUpdated", new { userId, roleId, action = "removed" });

    return Ok();
}
```

- [ ] **Step 5: Inject `IPermissionResolverService` into `ServersController` constructor**

Add `IPermissionResolverService permissionResolver` to the constructor parameters.

- [ ] **Step 6: Build and verify**

```bash
cd apps/api/Codec.Api
dotnet build
```

Expected: Build succeeds (some warnings about unused old member role endpoint are OK — remove the old `UpdateMemberRole` endpoint if it still exists).

- [ ] **Step 7: Commit**

```bash
git add apps/api/Codec.Api/Controllers/ServersController.cs apps/api/Codec.Api/Models/UpdateMemberRolesRequest.cs
git commit -m "feat: add multi-role assignment endpoints and update response shapes"
```

---

## Task 6: Update `RolesController` — Permission Editing and Multi-Role Delete

**Files:**
- Modify: `apps/api/Codec.Api/Controllers/RolesController.cs`

- [ ] **Step 1: Update `DeleteRole` to use `ServerMemberRoles` join table**

In `RolesController.DeleteRole` (around line 273+), replace the code that does `member.RoleId = memberRole.Id` with:

```csharp
// Remove all assignments for the deleted role
var assignments = await db.ServerMemberRoles
    .Where(mr => mr.RoleId == roleId)
    .ToListAsync();
db.ServerMemberRoles.RemoveRange(assignments);

// For each affected user, if they now have no roles in this server, assign Member role
var memberRole = await db.ServerRoles
    .FirstOrDefaultAsync(r => r.ServerId == serverId && r.Name == "Member" && r.IsSystemRole);

if (memberRole is not null)
{
    var affectedUserIds = assignments.Select(a => a.UserId).Distinct();
    foreach (var uid in affectedUserIds)
    {
        var hasOtherRoles = await db.ServerMemberRoles
            .AnyAsync(mr => mr.UserId == uid && mr.Role!.ServerId == serverId && mr.RoleId != roleId);
        if (!hasOtherRoles)
        {
            db.ServerMemberRoles.Add(new ServerMemberRole { UserId = uid, RoleId = memberRole.Id });
        }
    }
}
```

- [ ] **Step 2: Update role hierarchy checks to use `PermissionResolverService`**

Inject `IPermissionResolverService` into the controller. Replace `callerMembership.Role.Position` comparisons with `await permissionResolver.GetHighestRolePositionAsync(serverId, callerId)`.

- [ ] **Step 3: Verify permission editing already works on `UpdateRole`**

The existing `PATCH /servers/{serverId}/roles/{roleId}` endpoint should already accept `permissions` in the request body. Check that the `UpdateRoleRequest` DTO includes `Permissions` as a `long?` field. If not, add it:

```csharp
public long? Permissions { get; set; }
```

And in the handler, apply it:

```csharp
if (request.Permissions is not null)
{
    // Cannot grant permissions the caller doesn't have (escalation protection)
    var callerPerms = await permissionResolver.ResolveServerPermissionsAsync(serverId, callerId);
    var requested = (Permission)request.Permissions.Value;
    if (!isGlobalAdmin && (requested & ~callerPerms) != Permission.None)
        return BadRequest(new { error = "Cannot grant permissions you don't have." });

    role.Permissions = requested;
}
```

- [ ] **Step 4: Build and verify**

```bash
cd apps/api/Codec.Api
dotnet build
```

- [ ] **Step 5: Commit**

```bash
git add apps/api/Codec.Api/Controllers/RolesController.cs
git commit -m "refactor: update RolesController for multi-role delete and permission editing"
```

---

## Task 7: Channel Override Endpoints

**Files:**
- Modify: `apps/api/Codec.Api/Controllers/ChannelsController.cs`

- [ ] **Step 1: Add channel override endpoints to `ChannelsController`**

```csharp
/// <summary>List all permission overrides for a channel.</summary>
[HttpGet("{channelId:guid}/overrides")]
public async Task<IActionResult> GetChannelOverrides(Guid channelId)
{
    var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == channelId);
    if (channel is null) return NotFound();

    var callerId = GetUserId();
    var isGlobalAdmin = IsGlobalAdmin();
    await userService.EnsurePermissionAsync(channel.ServerId, callerId, Permission.ManageChannels, isGlobalAdmin);

    var overrides = await db.ChannelPermissionOverrides.AsNoTracking()
        .Where(o => o.ChannelId == channelId)
        .Include(o => o.Role)
        .Select(o => new
        {
            o.ChannelId,
            o.RoleId,
            RoleName = o.Role!.Name,
            Allow = (long)o.Allow,
            Deny = (long)o.Deny
        })
        .ToListAsync();

    return Ok(overrides);
}

/// <summary>Set or update a permission override for a role on a channel.</summary>
[HttpPut("{channelId:guid}/overrides/{roleId:guid}")]
public async Task<IActionResult> SetChannelOverride(Guid channelId, Guid roleId, [FromBody] ChannelOverrideRequest request)
{
    var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == channelId);
    if (channel is null) return NotFound();

    var callerId = GetUserId();
    var isGlobalAdmin = IsGlobalAdmin();
    await userService.EnsurePermissionAsync(channel.ServerId, callerId, Permission.ManageChannels, isGlobalAdmin);

    var role = await db.ServerRoles.AsNoTracking()
        .FirstOrDefaultAsync(r => r.Id == roleId && r.ServerId == channel.ServerId);
    if (role is null) return NotFound();

    var existing = await db.ChannelPermissionOverrides
        .FirstOrDefaultAsync(o => o.ChannelId == channelId && o.RoleId == roleId);

    if (existing is not null)
    {
        existing.Allow = (Permission)request.Allow;
        existing.Deny = (Permission)request.Deny;
    }
    else
    {
        db.ChannelPermissionOverrides.Add(new ChannelPermissionOverride
        {
            Id = Guid.NewGuid(),
            ChannelId = channelId,
            RoleId = roleId,
            Allow = (Permission)request.Allow,
            Deny = (Permission)request.Deny
        });
    }

    await db.SaveChangesAsync();

    await hubContext.Clients.Group($"server-{channel.ServerId}")
        .SendAsync("ChannelOverrideUpdated", new { channelId, roleId, request.Allow, request.Deny });

    return Ok();
}

/// <summary>Remove a permission override for a role on a channel.</summary>
[HttpDelete("{channelId:guid}/overrides/{roleId:guid}")]
public async Task<IActionResult> DeleteChannelOverride(Guid channelId, Guid roleId)
{
    var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == channelId);
    if (channel is null) return NotFound();

    var callerId = GetUserId();
    var isGlobalAdmin = IsGlobalAdmin();
    await userService.EnsurePermissionAsync(channel.ServerId, callerId, Permission.ManageChannels, isGlobalAdmin);

    var existing = await db.ChannelPermissionOverrides
        .FirstOrDefaultAsync(o => o.ChannelId == channelId && o.RoleId == roleId);
    if (existing is null) return NotFound();

    db.ChannelPermissionOverrides.Remove(existing);
    await db.SaveChangesAsync();

    await hubContext.Clients.Group($"server-{channel.ServerId}")
        .SendAsync("ChannelOverrideUpdated", new { channelId, roleId, allow = 0, deny = 0 });

    return Ok();
}
```

- [ ] **Step 2: Create the `ChannelOverrideRequest` DTO**

```csharp
// apps/api/Codec.Api/Models/ChannelOverrideRequest.cs
namespace Codec.Api.Models;

public class ChannelOverrideRequest
{
    public long Allow { get; set; }
    public long Deny { get; set; }
}
```

- [ ] **Step 3: Build and verify**

```bash
cd apps/api/Codec.Api
dotnet build
```

- [ ] **Step 4: Commit**

```bash
git add apps/api/Codec.Api/Controllers/ChannelsController.cs apps/api/Codec.Api/Models/ChannelOverrideRequest.cs
git commit -m "feat: add channel permission override endpoints"
```

---

## Task 8: Update All `EnsureAdminAsync` Call Sites

**Files:**
- Modify: `apps/api/Codec.Api/Controllers/ServersController.cs` (27 call sites)
- Modify: `apps/api/Codec.Api/Controllers/ChannelsController.cs` (2 call sites)

This task is mechanical: each `EnsureAdminAsync` call is replaced with `EnsurePermissionAsync` using the **specific permission** that action actually requires, rather than the blanket `ManageServer`.

- [ ] **Step 1: Audit and replace each call site in `ServersController`**

For each `await userService.EnsureAdminAsync(serverId, callerId, isGlobalAdmin)` call, determine the correct specific permission based on the action:

| Action context | Replace with |
|---------------|-------------|
| Channel CRUD (create, update, delete, reorder) | `Permission.ManageChannels` |
| Emoji management | `Permission.ManageEmojis` |
| Invite management (delete, manage) | `Permission.ManageInvites` |
| Webhook management | `Permission.ManageServer` |
| Ban member | `Permission.BanMembers` |
| Kick member | `Permission.KickMembers` |
| Server settings (update name, icon, description) | `Permission.ManageServer` |
| Audit log access | `Permission.ViewAuditLog` |
| Role management | `Permission.ManageRoles` |

Example replacement:

```csharp
// Before:
await userService.EnsureAdminAsync(serverId, callerId, isGlobalAdmin);

// After (for a channel create action):
await userService.EnsurePermissionAsync(serverId, callerId, Permission.ManageChannels, isGlobalAdmin);
```

Go through each of the 27 call sites and apply the correct permission.

- [ ] **Step 2: Replace the 2 call sites in `ChannelsController`**

Same pattern — determine the specific permission for each action in `ChannelsController`.

- [ ] **Step 3: Build and verify**

```bash
cd apps/api/Codec.Api
dotnet build
```

- [ ] **Step 4: Run existing integration tests**

```bash
cd apps/api
dotnet test Codec.Api.IntegrationTests/Codec.Api.IntegrationTests.csproj -v n
```

Expected: Tests pass (the integration test users are server owners, so all permissions are granted via the owner bypass).

- [ ] **Step 5: Commit**

```bash
git add apps/api/Codec.Api/Controllers/ServersController.cs apps/api/Codec.Api/Controllers/ChannelsController.cs
git commit -m "refactor: replace EnsureAdminAsync with specific permission checks"
```

---

## Task 9: Update `ChatHub` for Channel-Resolved Permissions

**Files:**
- Modify: `apps/api/Codec.Api/Hubs/ChatHub.cs`

- [ ] **Step 1: Inject `IPermissionResolverService` into `ChatHub`**

Add to the constructor alongside existing dependencies.

- [ ] **Step 2: Add channel permission checks to message and voice actions**

For each hub method that operates on a channel (SendMessage, JoinChannel, etc.), add a permission check:

```csharp
// Example: before sending a message in a channel
var canSend = await permissionResolver.HasChannelPermissionAsync(channelId, userId, Permission.SendMessages);
if (!canSend) throw new HubException("Missing permission: SendMessages");
```

Apply to:
- Join channel group → `Permission.ViewChannels`
- Send message → `Permission.SendMessages`
- Voice join → `Permission.Connect`
- Voice speak → `Permission.Speak`

- [ ] **Step 3: Update `MemberRoleChanged` event to `MemberRolesUpdated`**

Find where `ChatHub` sends `MemberRoleChanged` events and rename to `MemberRolesUpdated`. Update the payload to include the full role list.

- [ ] **Step 3b: Update `WebhookEventType` enum**

In `apps/api/Codec.Api/Models/Webhook.cs`, add `MemberRolesUpdated` to the `WebhookEventType` enum. Keep `MemberRoleChanged` as an alias for backward compatibility.

- [ ] **Step 4: Build and verify**

```bash
cd apps/api/Codec.Api
dotnet build
```

- [ ] **Step 5: Commit**

```bash
git add apps/api/Codec.Api/Hubs/ChatHub.cs
git commit -m "feat: add channel-resolved permission checks in ChatHub"
```

---

## Task 10: Remove Legacy `ServerMember.RoleId` and Clean Up

**Files:**
- Modify: `apps/api/Codec.Api/Models/ServerMember.cs`
- Modify: `apps/api/Codec.Api/Models/ServerRoleEntity.cs`
- Modify: `apps/api/Codec.Api/Data/CodecDbContext.cs`
- Delete: `apps/api/Codec.Api/Models/UpdateMemberRoleRequest.cs` (old single-role DTO)

- [ ] **Step 1: Remove `RoleId` and `Role` from `ServerMember`**

In `apps/api/Codec.Api/Models/ServerMember.cs`, remove:

```csharp
public Guid RoleId { get; set; }       // DELETE
public ServerRoleEntity? Role { get; set; }  // DELETE
```

- [ ] **Step 2: Remove old `Members` nav from `ServerRoleEntity`**

In `apps/api/Codec.Api/Models/ServerRoleEntity.cs`, remove:

```csharp
public List<ServerMember> Members { get; set; } = [];  // DELETE
```

Keep `MemberRoles` which points to the join table.

- [ ] **Step 3: Update `CodecDbContext` — remove old relationship config**

In `CodecDbContext.OnModelCreating`, remove the `ServerMember → Role` relationship config (lines ~133-135):

```csharp
// DELETE these lines:
modelBuilder.Entity<ServerMember>()
    .HasOne(member => member.Role)
    .WithMany(role => role.Members)
    .HasForeignKey(member => member.RoleId);
```

- [ ] **Step 4: Delete `UpdateMemberRoleRequest.cs`**

```bash
rm apps/api/Codec.Api/Models/UpdateMemberRoleRequest.cs
```

- [ ] **Step 5: Fix any remaining compile errors**

Search for any remaining references to `member.Role`, `member.RoleId`, `m.Role`, `.Include(m => m.Role)` and fix them. These should have been addressed in earlier tasks, but verify.

```bash
cd apps/api/Codec.Api
dotnet build
```

Expected: Build succeeds with no errors.

- [ ] **Step 6: Run all backend tests**

```bash
cd apps/api
dotnet test Codec.sln -v n
```

Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add -A apps/api/
git commit -m "refactor: remove legacy single-role RoleId from ServerMember"
```

---

## Task 11: Frontend Types and API Client

**Files:**
- Modify: `apps/web/src/lib/types/models.ts`
- Modify: `apps/web/src/lib/api/client.ts`

- [ ] **Step 1: Update `MemberServer` type**

In `apps/web/src/lib/types/models.ts`, replace the `MemberServer` type (lines 11-19):

```typescript
export type MemberServer = {
	serverId: string;
	name: string;
	description?: string;
	iconUrl?: string | null;
	roles: MemberRole[];
	sortOrder: number;
	permissions: number;
	isOwner: boolean;
};
```

- [ ] **Step 2: Add `MemberRole` type**

Add after `ServerRole` type:

```typescript
/** A role assigned to a member (subset of ServerRole for API responses). */
export type MemberRole = {
	id: string;
	name: string;
	color?: string | null;
	position: number;
	isSystemRole: boolean;
};
```

- [ ] **Step 3: Update `Member` type**

Replace the `Member` type (lines 174-187):

```typescript
export type Member = {
	userId: string;
	displayName: string;
	email?: string | null;
	avatarUrl?: string | null;
	roles: MemberRole[];
	permissions: number;
	displayRole?: MemberRole | null;
	highestPosition: number;
	joinedAt: string;
	statusText?: string | null;
	statusEmoji?: string | null;
};
```

- [ ] **Step 4: Add `ChannelPermissionOverride` type**

```typescript
/** Per-channel permission override for a role. */
export type ChannelPermissionOverride = {
	channelId: string;
	roleId: string;
	roleName: string;
	allow: number;
	deny: number;
};
```

- [ ] **Step 5: Fix `Permission` constants for voice permissions**

Add the missing voice permissions that exceed 32-bit range:

```typescript
export const Permission = {
	// ... existing entries ...
	Connect: 1 << 30,
	Speak: 2 ** 31,
	MuteMembers: 2 ** 32,
	DeafenMembers: 2 ** 33,
	Administrator: 2 ** 40,
} as const;
```

- [ ] **Step 6: Add multi-role API methods to `client.ts`**

In `apps/web/src/lib/api/client.ts`, add:

```typescript
async setMemberRoles(token: string, serverId: string, userId: string, roleIds: string[]): Promise<void> {
	await this.fetchApi(`/servers/${serverId}/members/${userId}/roles`, {
		method: 'PUT',
		headers: this.authHeaders(token),
		body: JSON.stringify({ roleIds })
	});
}

async addMemberRole(token: string, serverId: string, userId: string, roleId: string): Promise<void> {
	await this.fetchApi(`/servers/${serverId}/members/${userId}/roles/${roleId}`, {
		method: 'POST',
		headers: this.authHeaders(token)
	});
}

async removeMemberRole(token: string, serverId: string, userId: string, roleId: string): Promise<void> {
	await this.fetchApi(`/servers/${serverId}/members/${userId}/roles/${roleId}`, {
		method: 'DELETE',
		headers: this.authHeaders(token)
	});
}

async getChannelOverrides(token: string, channelId: string): Promise<ChannelPermissionOverride[]> {
	return this.fetchApi(`/channels/${channelId}/overrides`, {
		headers: this.authHeaders(token)
	});
}

async setChannelOverride(token: string, channelId: string, roleId: string, allow: number, deny: number): Promise<void> {
	await this.fetchApi(`/channels/${channelId}/overrides/${roleId}`, {
		method: 'PUT',
		headers: this.authHeaders(token),
		body: JSON.stringify({ allow, deny })
	});
}

async deleteChannelOverride(token: string, channelId: string, roleId: string): Promise<void> {
	await this.fetchApi(`/channels/${channelId}/overrides/${roleId}`, {
		method: 'DELETE',
		headers: this.authHeaders(token)
	});
}
```

- [ ] **Step 7: Commit**

```bash
git add apps/web/src/lib/types/models.ts apps/web/src/lib/api/client.ts
git commit -m "feat: update frontend types and API client for multi-role"
```

---

## Task 12: Update Frontend State (`AppState`)

**Files:**
- Modify: `apps/web/src/lib/state/app-state.svelte.ts`
- Modify: `apps/web/src/lib/services/chat-hub.ts`

- [ ] **Step 1: Replace `currentServerRole` with `isServerOwner`**

In `apps/web/src/lib/state/app-state.svelte.ts`, replace the `currentServerRole` derived (line ~296):

```typescript
// DELETE:
// readonly currentServerRole = $derived(
//     this.servers.find((s) => s.serverId === this.selectedServerId)?.role ?? null
// );

// ADD:
readonly isServerOwner = $derived(
    this.servers.find((s) => s.serverId === this.selectedServerId)?.isOwner ?? false
);
```

- [ ] **Step 2: Fix `canBanMembers` and `canDeleteServer`**

Replace lines ~318-328:

```typescript
readonly canBanMembers = $derived(
    this.isGlobalAdmin || hasPermission(this.currentServerPermissions, Permission.BanMembers)
);

readonly canDeleteServer = $derived(
    this.isGlobalAdmin || this.isServerOwner
);
```

- [ ] **Step 3: Update role management methods for multi-role**

Replace the existing `updateMemberRole` method with:

```typescript
async addMemberRole(userId: string, roleId: string): Promise<void> {
    if (!this.idToken || !this.selectedServerId) return;
    await this.api.addMemberRole(this.idToken, this.selectedServerId, userId, roleId);
}

async removeMemberRole(userId: string, roleId: string): Promise<void> {
    if (!this.idToken || !this.selectedServerId) return;
    await this.api.removeMemberRole(this.idToken, this.selectedServerId, userId, roleId);
}

async setMemberRoles(userId: string, roleIds: string[]): Promise<void> {
    if (!this.idToken || !this.selectedServerId) return;
    await this.api.setMemberRoles(this.idToken, this.selectedServerId, userId, roleIds);
}
```

- [ ] **Step 4: Update SignalR event handler**

In `apps/web/src/lib/services/chat-hub.ts`, rename the `MemberRoleChanged` callback to `MemberRolesUpdated` and update the payload type to handle multi-role data:

```typescript
// In the callback registration:
this.connection.on('MemberRolesUpdated', (data: { userId: string; roles?: MemberRole[]; roleId?: string; action?: string }) => {
    callbacks.onMemberRolesUpdated?.(data);
});
```

Update `AppState` to handle this new event shape — reload member data when roles change.

- [ ] **Step 5: Build and verify**

```bash
cd apps/web
npm run check
```

Expected: Type checking passes.

- [ ] **Step 6: Commit**

```bash
git add apps/web/src/lib/state/app-state.svelte.ts apps/web/src/lib/services/chat-hub.ts
git commit -m "refactor: update AppState for multi-role and remove currentServerRole"
```

---

## Task 13: Fix Frontend Components for Multi-Role

**Files:**
- Modify: `apps/web/src/lib/components/server-settings/ServerMembers.svelte`
- Modify: `apps/web/src/lib/components/members/MemberItem.svelte`

- [ ] **Step 1: Update `ServerMembers.svelte` — replace string role checks**

Replace all `member.role === 'Owner'` / `member.role !== 'Owner'` checks (lines 18, 24, 30, 144) with:

```typescript
// Before:
member.role !== 'Owner'
// After:
member.userId !== server.ownerId
```

Replace the single role dropdown with a role tag list showing all assigned roles as colored pills. For each member, show `member.roles` as badges. Add a dropdown to add roles, and an X button on each badge to remove.

- [ ] **Step 2: Update `MemberItem.svelte` — role display**

Replace the single role color display (line ~8, 28-31) with the `displayRole` from the member data:

```typescript
// Before:
member.role !== 'Owner'
// After:
member.userId !== server?.ownerId

// For display color:
const displayColor = member.displayRole?.color;
```

- [ ] **Step 3: Update hierarchy checks to use `highestPosition`**

Replace all `member.rolePosition` comparisons with `member.highestPosition`:

```typescript
// Before:
const canActOn = member.rolePosition > myRolePosition;
// After:
const canActOn = member.highestPosition > myHighestPosition;
```

- [ ] **Step 4: Build and verify**

```bash
cd apps/web
npm run check
```

- [ ] **Step 5: Commit**

```bash
git add apps/web/src/lib/components/server-settings/ServerMembers.svelte apps/web/src/lib/components/members/MemberItem.svelte
git commit -m "refactor: update member components for multi-role display"
```

---

## Task 14: Permission Editing UI in `ServerRoles.svelte`

**Files:**
- Modify: `apps/web/src/lib/components/server-settings/ServerRoles.svelte`

- [ ] **Step 1: Add permission toggle grid to the role editor**

When editing a role, add a section below the existing name/color fields that shows all permissions grouped by category with on/off toggles:

```typescript
const permissionCategories = [
    {
        name: 'General',
        permissions: [
            { flag: Permission.ViewChannels, label: 'View Channels' },
            { flag: Permission.ManageChannels, label: 'Manage Channels' },
            { flag: Permission.ManageServer, label: 'Manage Server' },
            { flag: Permission.ManageRoles, label: 'Manage Roles' },
            { flag: Permission.ManageEmojis, label: 'Manage Emojis' },
            { flag: Permission.ViewAuditLog, label: 'View Audit Log' },
            { flag: Permission.CreateInvites, label: 'Create Invites' },
            { flag: Permission.ManageInvites, label: 'Manage Invites' },
        ]
    },
    {
        name: 'Membership',
        permissions: [
            { flag: Permission.KickMembers, label: 'Kick Members' },
            { flag: Permission.BanMembers, label: 'Ban Members' },
        ]
    },
    {
        name: 'Messages',
        permissions: [
            { flag: Permission.SendMessages, label: 'Send Messages' },
            { flag: Permission.EmbedLinks, label: 'Embed Links' },
            { flag: Permission.AttachFiles, label: 'Attach Files' },
            { flag: Permission.AddReactions, label: 'Add Reactions' },
            { flag: Permission.MentionEveryone, label: 'Mention Everyone' },
            { flag: Permission.ManageMessages, label: 'Manage Messages' },
            { flag: Permission.PinMessages, label: 'Pin Messages' },
        ]
    },
    {
        name: 'Voice',
        permissions: [
            { flag: Permission.Connect, label: 'Connect' },
            { flag: Permission.Speak, label: 'Speak' },
            { flag: Permission.MuteMembers, label: 'Mute Members' },
            { flag: Permission.DeafenMembers, label: 'Deafen Members' },
        ]
    },
    {
        name: 'Dangerous',
        permissions: [
            { flag: Permission.Administrator, label: 'Administrator' },
        ]
    }
];
```

For each permission, render a toggle switch. The toggle is disabled (read-only) for:
- System Owner role (all permissions shown as granted, not editable)
- Permissions the current user doesn't have (can't grant what you don't have)

When a toggle changes, update the local bitmask and save via `app.updateRole(roleId, { permissions: newBitmask })`.

- [ ] **Step 2: Handle the 32-bit overflow for voice permissions**

Use the `hasPermission` helper (which handles the float math for >32-bit flags) to check the toggle state. When toggling, use `2 ** N` for the high-bit flags:

```typescript
function togglePermission(current: number, flag: number): number {
    if (hasPermission(current, flag)) {
        // Remove: for flags > 2^30, use float subtraction
        return flag > (1 << 30) ? current - flag : current & ~flag;
    } else {
        // Add: for flags > 2^30, use float addition
        return flag > (1 << 30) ? current + flag : current | flag;
    }
}
```

- [ ] **Step 3: Build and verify**

```bash
cd apps/web
npm run check
```

- [ ] **Step 4: Commit**

```bash
git add apps/web/src/lib/components/server-settings/ServerRoles.svelte
git commit -m "feat: add permission editing UI to ServerRoles"
```

---

## Task 15: Channel Permissions Component

**Files:**
- Create: `apps/web/src/lib/components/channel-settings/ChannelPermissions.svelte`

- [ ] **Step 1: Create the `ChannelPermissions` component**

This component is accessed from channel settings. It shows all server roles and allows setting three-state overrides (Allow / Neutral / Deny) per permission per role.

```svelte
<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte';
	import { Permission, hasPermission, type ChannelPermissionOverride, type ServerRole } from '$lib/types/models';

	const app = getAppState();

	let { channelId }: { channelId: string } = $props();

	let overrides = $state<ChannelPermissionOverride[]>([]);
	let selectedRoleId = $state<string | null>(null);
	let isLoading = $state(false);

	const permissionList = [
		{ flag: Permission.ViewChannels, label: 'View Channel' },
		{ flag: Permission.SendMessages, label: 'Send Messages' },
		{ flag: Permission.EmbedLinks, label: 'Embed Links' },
		{ flag: Permission.AttachFiles, label: 'Attach Files' },
		{ flag: Permission.AddReactions, label: 'Add Reactions' },
		{ flag: Permission.MentionEveryone, label: 'Mention Everyone' },
		{ flag: Permission.ManageMessages, label: 'Manage Messages' },
		{ flag: Permission.PinMessages, label: 'Pin Messages' },
		{ flag: Permission.Connect, label: 'Connect' },
		{ flag: Permission.Speak, label: 'Speak' },
	];

	async function loadOverrides() {
		if (!app.idToken) return;
		isLoading = true;
		overrides = await app.api.getChannelOverrides(app.idToken, channelId);
		isLoading = false;
	}

	function getState(roleId: string, flag: number): 'allow' | 'neutral' | 'deny' {
		const override = overrides.find((o) => o.roleId === roleId);
		if (!override) return 'neutral';
		if (hasPermission(override.allow, flag)) return 'allow';
		if (hasPermission(override.deny, flag)) return 'deny';
		return 'neutral';
	}

	async function cycleState(roleId: string, flag: number) {
		const current = getState(roleId, flag);
		const override = overrides.find((o) => o.roleId === roleId);
		let allow = override?.allow ?? 0;
		let deny = override?.deny ?? 0;

		// Cycle: neutral → allow → deny → neutral
		if (current === 'neutral') {
			allow = allow | flag;
		} else if (current === 'allow') {
			allow = allow & ~flag;
			deny = deny | flag;
		} else {
			deny = deny & ~flag;
		}

		if (!app.idToken) return;
		if (allow === 0 && deny === 0) {
			await app.api.deleteChannelOverride(app.idToken, channelId, roleId);
		} else {
			await app.api.setChannelOverride(app.idToken, channelId, roleId, allow, deny);
		}
		await loadOverrides();
	}

	$effect(() => {
		loadOverrides();
	});
</script>

<!-- Role list on the left, permission grid on the right when a role is selected -->
<div class="channel-permissions">
	<div class="role-list">
		{#each app.serverRoles as role}
			<button
				class="role-item"
				class:selected={selectedRoleId === role.id}
				onclick={() => (selectedRoleId = role.id)}
			>
				{#if role.color}
					<span class="color-dot" style="background: {role.color}"></span>
				{/if}
				{role.name}
			</button>
		{/each}
	</div>

	{#if selectedRoleId}
		<div class="permission-grid">
			{#each permissionList as { flag, label }}
				{@const state = getState(selectedRoleId, flag)}
				<div class="permission-row">
					<span class="permission-label">{label}</span>
					<button
						class="state-toggle {state}"
						onclick={() => cycleState(selectedRoleId!, flag)}
					>
						{#if state === 'allow'}
							<span class="allow-icon">&#10003;</span>
						{:else if state === 'deny'}
							<span class="deny-icon">&#10007;</span>
						{:else}
							<span class="neutral-icon">&#8212;</span>
						{/if}
					</button>
				</div>
			{/each}
		</div>
	{/if}
</div>
```

- [ ] **Step 2: Build and verify**

```bash
cd apps/web
npm run check
```

- [ ] **Step 3: Commit**

```bash
git add apps/web/src/lib/components/channel-settings/ChannelPermissions.svelte
git commit -m "feat: add ChannelPermissions component for three-state overrides"
```

---

## Task 16: Channel Visibility Filtering

**Files:**
- Modify: `apps/web/src/lib/components/channel-sidebar/ChannelSidebar.svelte`

- [ ] **Step 1: Filter channels by `ViewChannels` permission**

The API should already filter channels where the user lacks `ViewChannels` after the backend changes. On the frontend, if the API returns the channel, it's visible. However, for real-time updates (when a channel override changes), the frontend should re-fetch the channel list.

In `ChannelSidebar.svelte`, add a handler for the `ChannelOverrideUpdated` SignalR event that triggers a channel list refresh:

```typescript
// When channel overrides change, reload the channel list
// This is handled via the existing SignalR callback mechanism
```

No explicit filtering needed on the frontend — the API handles it. Just ensure the channel list refreshes when overrides change.

- [ ] **Step 2: Build and verify**

```bash
cd apps/web
npm run check
```

- [ ] **Step 3: Commit**

```bash
git add apps/web/src/lib/components/channel-sidebar/ChannelSidebar.svelte
git commit -m "feat: refresh channel list on permission override changes"
```

---

## Task 17: Integration Tests

**Files:**
- Modify: `apps/api/Codec.Api.IntegrationTests/Tests/CustomRoleTests.cs`
- Create: `apps/api/Codec.Api.IntegrationTests/Tests/MultiRoleTests.cs`
- Create: `apps/api/Codec.Api.IntegrationTests/Tests/ChannelOverrideTests.cs`

- [ ] **Step 1: Add multi-role integration tests**

```csharp
// apps/api/Codec.Api.IntegrationTests/Tests/MultiRoleTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;

namespace Codec.Api.IntegrationTests.Tests;

public class MultiRoleTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task AddMemberRole_AssignsRole()
    {
        var owner = CreateClient();
        var member = CreateSecondClient();
        var (serverId, _) = await CreateServerAsync(owner);

        // Join the member to the server via invite
        var inviteResponse = await owner.PostAsJsonAsync($"/servers/{serverId}/invites", new { });
        inviteResponse.EnsureSuccessStatusCode();
        var invite = await inviteResponse.Content.ReadFromJsonAsync<JsonElement>();
        var code = invite.GetProperty("code").GetString();
        await member.PostAsJsonAsync($"/invites/{code}/accept", new { });

        // Create a custom role
        var roleResponse = await owner.PostAsJsonAsync($"/servers/{serverId}/roles", new { name = "Moderator" });
        roleResponse.EnsureSuccessStatusCode();
        var role = await roleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var roleId = role.GetProperty("id").GetString();

        // Add the role to the member
        var memberId = await GetUserIdAsync(member);
        var addResponse = await owner.PostAsync($"/servers/{serverId}/members/{memberId}/roles/{roleId}", null);
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        // Verify member now has the role
        var membersResponse = await owner.GetFromJsonAsync<JsonElement>($"/servers/{serverId}/members");
        var members = membersResponse.GetProperty("members").EnumerateArray();
        var targetMember = members.FirstOrDefault(m => m.GetProperty("userId").GetGuid() == memberId);
        var roles = targetMember.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("name").GetString());
        Assert.Contains("Moderator", roles);
    }

    [Fact]
    public async Task RemoveMemberRole_AutoAssignsMemberWhenEmpty()
    {
        var owner = CreateClient();
        var member = CreateSecondClient();
        var (serverId, _) = await CreateServerAsync(owner);

        // Join the member, get their ID
        var inviteResponse = await owner.PostAsJsonAsync($"/servers/{serverId}/invites", new { });
        var invite = await inviteResponse.Content.ReadFromJsonAsync<JsonElement>();
        var code = invite.GetProperty("code").GetString();
        await member.PostAsJsonAsync($"/invites/{code}/accept", new { });
        var memberId = await GetUserIdAsync(member);

        // Get the Member role ID
        var rolesResponse = await owner.GetFromJsonAsync<JsonElement>($"/servers/{serverId}/roles");
        var memberRoleId = rolesResponse.EnumerateArray()
            .FirstOrDefault(r => r.GetProperty("name").GetString() == "Member")
            .GetProperty("id").GetString();

        // Remove the Member role
        var removeResponse = await owner.DeleteAsync($"/servers/{serverId}/members/{memberId}/roles/{memberRoleId}");
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);

        // Member should still have the Member role (auto-reassigned when last role removed)
        var membersResponse = await owner.GetFromJsonAsync<JsonElement>($"/servers/{serverId}/members");
        var members = membersResponse.GetProperty("members").EnumerateArray();
        var targetMember = members.FirstOrDefault(m => m.GetProperty("userId").GetGuid() == memberId);
        var roles = targetMember.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("name").GetString());
        Assert.Contains("Member", roles);
    }

    [Fact]
    public async Task MultiRole_PermissionsCombine()
    {
        var owner = CreateClient();
        var member = CreateSecondClient();
        var (serverId, _) = await CreateServerAsync(owner);

        // Join member
        var inviteResponse = await owner.PostAsJsonAsync($"/servers/{serverId}/invites", new { });
        var invite = await inviteResponse.Content.ReadFromJsonAsync<JsonElement>();
        await member.PostAsJsonAsync($"/invites/{invite.GetProperty("code").GetString()}/accept", new { });
        var memberId = await GetUserIdAsync(member);

        // Create a Moderator role with ManageMessages
        var roleResponse = await owner.PostAsJsonAsync($"/servers/{serverId}/roles", new
        {
            name = "Moderator",
            permissions = (long)(Models.Permission.ManageMessages | Models.Permission.KickMembers)
        });
        var role = await roleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var roleId = role.GetProperty("id").GetString();

        // Add role to member
        await owner.PostAsync($"/servers/{serverId}/members/{memberId}/roles/{roleId}", null);

        // Check aggregated permissions include both Member defaults and Moderator perms
        var membersResponse = await owner.GetFromJsonAsync<JsonElement>($"/servers/{serverId}/members");
        var targetMember = membersResponse.GetProperty("members").EnumerateArray()
            .FirstOrDefault(m => m.GetProperty("userId").GetGuid() == memberId);
        var perms = targetMember.GetProperty("permissions").GetInt64();

        // Should have SendMessages (from Member) AND ManageMessages (from Moderator)
        Assert.True((perms & (long)Models.Permission.SendMessages) != 0);
        Assert.True((perms & (long)Models.Permission.ManageMessages) != 0);
        Assert.True((perms & (long)Models.Permission.KickMembers) != 0);
    }
}
```

- [ ] **Step 2: Add channel override integration tests**

```csharp
// apps/api/Codec.Api.IntegrationTests/Tests/ChannelOverrideTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;

namespace Codec.Api.IntegrationTests.Tests;

public class ChannelOverrideTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task SetChannelOverride_DenyBlocksPermission()
    {
        var owner = CreateClient();
        var (serverId, _) = await CreateServerAsync(owner);
        var channelId = await CreateChannelAsync(owner, serverId);

        // Get the Member role
        var rolesResponse = await owner.GetFromJsonAsync<JsonElement>($"/servers/{serverId}/roles");
        var memberRoleId = rolesResponse.EnumerateArray()
            .FirstOrDefault(r => r.GetProperty("name").GetString() == "Member")
            .GetProperty("id").GetString();

        // Set a deny override for SendMessages on the Member role
        var overrideResponse = await owner.PutAsJsonAsync(
            $"/channels/{channelId}/overrides/{memberRoleId}",
            new { allow = 0L, deny = (long)Models.Permission.SendMessages }
        );
        Assert.Equal(HttpStatusCode.OK, overrideResponse.StatusCode);

        // Verify the override was saved
        var overrides = await owner.GetFromJsonAsync<JsonElement>($"/channels/{channelId}/overrides");
        Assert.Single(overrides.EnumerateArray());
        var o = overrides.EnumerateArray().First();
        Assert.Equal((long)Models.Permission.SendMessages, o.GetProperty("deny").GetInt64());
    }

    [Fact]
    public async Task DeleteChannelOverride_RemovesOverride()
    {
        var owner = CreateClient();
        var (serverId, _) = await CreateServerAsync(owner);
        var channelId = await CreateChannelAsync(owner, serverId);

        var rolesResponse = await owner.GetFromJsonAsync<JsonElement>($"/servers/{serverId}/roles");
        var memberRoleId = rolesResponse.EnumerateArray()
            .FirstOrDefault(r => r.GetProperty("name").GetString() == "Member")
            .GetProperty("id").GetString();

        // Set then delete
        await owner.PutAsJsonAsync($"/channels/{channelId}/overrides/{memberRoleId}",
            new { allow = 0L, deny = (long)Models.Permission.SendMessages });

        var deleteResponse = await owner.DeleteAsync($"/channels/{channelId}/overrides/{memberRoleId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var overrides = await owner.GetFromJsonAsync<JsonElement>($"/channels/{channelId}/overrides");
        Assert.Empty(overrides.EnumerateArray());
    }
}
```

- [ ] **Step 3: Run all integration tests**

```bash
cd apps/api
dotnet test Codec.Api.IntegrationTests/Codec.Api.IntegrationTests.csproj -v n
```

Expected: All tests pass including the new ones.

- [ ] **Step 4: Commit**

```bash
git add apps/api/Codec.Api.IntegrationTests/Tests/MultiRoleTests.cs apps/api/Codec.Api.IntegrationTests/Tests/ChannelOverrideTests.cs
git commit -m "test: add integration tests for multi-role and channel overrides"
```

---

## Task 18: Final Build and Verification

**Files:** None (verification only)

- [ ] **Step 1: Full backend build**

```bash
cd apps/api/Codec.Api
dotnet build
```

Expected: No errors.

- [ ] **Step 2: All backend tests**

```bash
cd apps/api
dotnet test Codec.sln -v n
```

Expected: All tests pass.

- [ ] **Step 3: Frontend type check**

```bash
cd apps/web
npm run check
```

Expected: No errors.

- [ ] **Step 4: Frontend build**

```bash
cd apps/web
npm run build
```

Expected: Build succeeds.

- [ ] **Step 5: Commit any remaining fixes**

If any verification steps revealed issues, fix and commit them.

```bash
git add -A
git commit -m "fix: resolve final build issues for roles and permissions"
```
