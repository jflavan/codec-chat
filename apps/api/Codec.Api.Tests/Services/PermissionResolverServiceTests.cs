using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Tests.Services;

public class PermissionResolverServiceTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly PermissionResolverService _svc;

    // Shared test data
    private readonly Guid _serverId = Guid.NewGuid();
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _memberId = Guid.NewGuid();
    private readonly Guid _channelId = Guid.NewGuid();

    private readonly ServerRoleEntity _ownerRole;
    private readonly ServerRoleEntity _adminRole;
    private readonly ServerRoleEntity _memberRole;
    private readonly ServerRoleEntity _moderatorRole;

    public PermissionResolverServiceTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);
        _svc = new PermissionResolverService(_db);

        // Seed: roles
        _ownerRole = new ServerRoleEntity
        {
            Id = Guid.NewGuid(),
            ServerId = _serverId,
            Name = "Owner",
            Position = 0,
            Permissions = Permission.Administrator,
            IsSystemRole = true,
            IsHoisted = true,
        };
        _adminRole = new ServerRoleEntity
        {
            Id = Guid.NewGuid(),
            ServerId = _serverId,
            Name = "Admin",
            Position = 1,
            Permissions = PermissionExtensions.AdminDefaults,
            IsSystemRole = true,
            IsHoisted = true,
        };
        _memberRole = new ServerRoleEntity
        {
            Id = Guid.NewGuid(),
            ServerId = _serverId,
            Name = "Member",
            Position = 2,
            Permissions = PermissionExtensions.MemberDefaults,
            IsSystemRole = true,
        };
        _moderatorRole = new ServerRoleEntity
        {
            Id = Guid.NewGuid(),
            ServerId = _serverId,
            Name = "Moderator",
            Position = 1,
            Permissions = Permission.ManageMessages | Permission.KickMembers,
            IsSystemRole = false,
        };
        _db.ServerRoles.AddRange(_ownerRole, _adminRole, _memberRole, _moderatorRole);

        // Seed: server
        var server = new Server { Id = _serverId, Name = "Test Server" };
        _db.Servers.Add(server);

        // Seed: users
        var ownerUser = new User { Id = _ownerId, DisplayName = "Owner User" };
        var memberUser = new User { Id = _memberId, DisplayName = "Regular Member" };
        _db.Users.AddRange(ownerUser, memberUser);

        // Seed: server members
        var ownerMember = new ServerMember
        {
            ServerId = _serverId,
            UserId = _ownerId,
        };
        var regularMember = new ServerMember
        {
            ServerId = _serverId,
            UserId = _memberId,
        };
        _db.ServerMembers.AddRange(ownerMember, regularMember);

        // Seed: multi-role assignments via ServerMemberRoles
        var ownerMemberRole = new ServerMemberRole
        {
            UserId = _ownerId,
            RoleId = _ownerRole.Id,
        };
        var regularMemberRole = new ServerMemberRole
        {
            UserId = _memberId,
            RoleId = _memberRole.Id,
        };
        _db.ServerMemberRoles.AddRange(ownerMemberRole, regularMemberRole);

        // Seed: channel
        var channel = new Channel
        {
            Id = _channelId,
            ServerId = _serverId,
            Name = "general",
            Position = 0,
        };
        _db.Channels.Add(channel);

        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    // ------------------------------------------------------------------
    // ResolveServerPermissions tests
    // ------------------------------------------------------------------

    [Fact]
    public async Task ResolveServerPermissions_SingleRole_ReturnsRolePermissions()
    {
        var perms = await _svc.ResolveServerPermissionsAsync(_serverId, _memberId);
        perms.Should().Be(PermissionExtensions.MemberDefaults);
    }

    [Fact]
    public async Task ResolveServerPermissions_MultiRole_ORsCombined()
    {
        // Give member the moderator role as well
        _db.ServerMemberRoles.Add(new ServerMemberRole { UserId = _memberId, RoleId = _moderatorRole.Id });
        await _db.SaveChangesAsync();

        // Create a fresh service instance to bypass any in-memory caching
        var svc = new PermissionResolverService(_db);
        var perms = await svc.ResolveServerPermissionsAsync(_serverId, _memberId);

        var expected = PermissionExtensions.MemberDefaults | Permission.ManageMessages | Permission.KickMembers;
        perms.Should().Be(expected);
    }

    [Fact]
    public async Task ResolveServerPermissions_Owner_ReturnsAllPermissions()
    {
        var perms = await _svc.ResolveServerPermissionsAsync(_serverId, _ownerId);
        // Owner gets all bits set
        perms.Should().Be((Permission)~0L);
    }

    [Fact]
    public async Task ResolveServerPermissions_AdministratorRole_GrantsAll()
    {
        // Give regular member a role with Administrator flag
        var adminishRole = new ServerRoleEntity
        {
            Id = Guid.NewGuid(),
            ServerId = _serverId,
            Name = "SuperAdmin",
            Position = 3,
            Permissions = Permission.Administrator,
            IsSystemRole = false,
        };
        _db.ServerRoles.Add(adminishRole);
        _db.ServerMemberRoles.Add(new ServerMemberRole { UserId = _memberId, RoleId = adminishRole.Id });
        await _db.SaveChangesAsync();

        var svc = new PermissionResolverService(_db);
        var perms = await svc.ResolveServerPermissionsAsync(_serverId, _memberId);
        perms.Should().Be((Permission)~0L);
    }

    // ------------------------------------------------------------------
    // ResolveChannelPermissions tests
    // ------------------------------------------------------------------

    [Fact]
    public async Task ResolveChannelPermissions_NoOverrides_ReturnsServerPerms()
    {
        var perms = await _svc.ResolveChannelPermissionsAsync(_channelId, _memberId);
        perms.Should().Be(PermissionExtensions.MemberDefaults);
    }

    [Fact]
    public async Task ResolveChannelPermissions_AllowOverride_GrantsExtra()
    {
        _db.ChannelPermissionOverrides.Add(new ChannelPermissionOverride
        {
            Id = Guid.NewGuid(),
            ChannelId = _channelId,
            RoleId = _memberRole.Id,
            Allow = Permission.ManageMessages,
            Deny = Permission.None,
        });
        await _db.SaveChangesAsync();

        var svc = new PermissionResolverService(_db);
        var perms = await svc.ResolveChannelPermissionsAsync(_channelId, _memberId);
        (perms & Permission.ManageMessages).Should().Be(Permission.ManageMessages);
    }

    [Fact]
    public async Task ResolveChannelPermissions_DenyOverride_RevokesPermission()
    {
        // SendMessages is in MemberDefaults; deny it
        _db.ChannelPermissionOverrides.Add(new ChannelPermissionOverride
        {
            Id = Guid.NewGuid(),
            ChannelId = _channelId,
            RoleId = _memberRole.Id,
            Allow = Permission.None,
            Deny = Permission.SendMessages,
        });
        await _db.SaveChangesAsync();

        var svc = new PermissionResolverService(_db);
        var perms = await svc.ResolveChannelPermissionsAsync(_channelId, _memberId);
        (perms & Permission.SendMessages).Should().Be(Permission.None);
    }

    [Fact]
    public async Task ResolveChannelPermissions_DenyWinsOverAllow()
    {
        // Give member the moderator role
        _db.ServerMemberRoles.Add(new ServerMemberRole { UserId = _memberId, RoleId = _moderatorRole.Id });

        // One role allows ManageMessages, the other denies it
        _db.ChannelPermissionOverrides.AddRange(
            new ChannelPermissionOverride
            {
                Id = Guid.NewGuid(),
                ChannelId = _channelId,
                RoleId = _memberRole.Id,
                Allow = Permission.ManageMessages,
                Deny = Permission.None,
            },
            new ChannelPermissionOverride
            {
                Id = Guid.NewGuid(),
                ChannelId = _channelId,
                RoleId = _moderatorRole.Id,
                Allow = Permission.None,
                Deny = Permission.ManageMessages,
            });
        await _db.SaveChangesAsync();

        var svc = new PermissionResolverService(_db);
        var perms = await svc.ResolveChannelPermissionsAsync(_channelId, _memberId);
        (perms & Permission.ManageMessages).Should().Be(Permission.None);
    }

    [Fact]
    public async Task ResolveChannelPermissions_ViewChannelsGate_DeniesEverything()
    {
        // Deny ViewChannels
        _db.ChannelPermissionOverrides.Add(new ChannelPermissionOverride
        {
            Id = Guid.NewGuid(),
            ChannelId = _channelId,
            RoleId = _memberRole.Id,
            Allow = Permission.None,
            Deny = Permission.ViewChannels,
        });
        await _db.SaveChangesAsync();

        var svc = new PermissionResolverService(_db);
        var perms = await svc.ResolveChannelPermissionsAsync(_channelId, _memberId);
        perms.Should().Be(Permission.None);
    }

    [Fact]
    public async Task ResolveChannelPermissions_Administrator_BypassesOverrides()
    {
        // Even if ViewChannels is denied, owner (Administrator) sees everything
        _db.ChannelPermissionOverrides.Add(new ChannelPermissionOverride
        {
            Id = Guid.NewGuid(),
            ChannelId = _channelId,
            RoleId = _ownerRole.Id,
            Allow = Permission.None,
            Deny = Permission.ViewChannels | Permission.SendMessages,
        });
        await _db.SaveChangesAsync();

        var svc = new PermissionResolverService(_db);
        var perms = await svc.ResolveChannelPermissionsAsync(_channelId, _ownerId);
        perms.Should().Be((Permission)~0L);
    }

    // ------------------------------------------------------------------
    // IsOwner tests
    // ------------------------------------------------------------------

    [Fact]
    public async Task IsOwner_ServerOwner_ReturnsTrue()
    {
        var result = await _svc.IsOwnerAsync(_serverId, _ownerId);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsOwner_RegularMember_ReturnsFalse()
    {
        var result = await _svc.IsOwnerAsync(_serverId, _memberId);
        result.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // GetHighestRolePosition tests
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetHighestRolePosition_MultiRole_ReturnsLowestNumber()
    {
        // Give member moderator (position 1) in addition to member (position 2)
        _db.ServerMemberRoles.Add(new ServerMemberRole { UserId = _memberId, RoleId = _moderatorRole.Id });
        await _db.SaveChangesAsync();

        var svc = new PermissionResolverService(_db);
        var pos = await svc.GetHighestRolePositionAsync(_serverId, _memberId);
        pos.Should().Be(1); // lower number = higher rank
    }

    [Fact]
    public async Task GetHighestRolePosition_NoRoles_ReturnsMaxValue()
    {
        // User with no role assignments
        var noRoleUserId = Guid.NewGuid();
        _db.Users.Add(new User { Id = noRoleUserId, DisplayName = "NoRole" });
        await _db.SaveChangesAsync();

        var svc = new PermissionResolverService(_db);
        var pos = await svc.GetHighestRolePositionAsync(_serverId, noRoleUserId);
        pos.Should().Be(int.MaxValue);
    }
}
