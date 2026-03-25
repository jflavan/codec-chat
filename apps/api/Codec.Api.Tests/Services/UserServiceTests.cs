using System.Security.Claims;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using Codec.Api.Services.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Tests.Services;

public class UserServiceTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly UserService _svc;

    public UserServiceTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);
        _svc = new UserService(_db);
    }

    public void Dispose() => _db.Dispose();

    private static ClaimsPrincipal CreatePrincipal(string sub, string name = "Test", string? email = "test@test.com", string? picture = null, string? issuer = null)
    {
        var claims = new List<Claim> { new("sub", sub), new("name", name) };
        if (email is not null) claims.Add(new("email", email));
        if (picture is not null) claims.Add(new("picture", picture));
        if (issuer is not null) claims.Add(new("iss", issuer));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    /// <summary>Helper to create the three default system roles for a server.</summary>
    private (ServerRoleEntity owner, ServerRoleEntity admin, ServerRoleEntity member) CreateDefaultRoles(Guid serverId)
    {
        var ownerRole = new ServerRoleEntity { ServerId = serverId, Name = "Owner", Position = 0, Permissions = Permission.Administrator, IsSystemRole = true, IsHoisted = true };
        var adminRole = new ServerRoleEntity { ServerId = serverId, Name = "Admin", Position = 1, Permissions = PermissionExtensions.AdminDefaults, IsSystemRole = true, IsHoisted = true };
        var memberRole = new ServerRoleEntity { ServerId = serverId, Name = "Member", Position = 2, Permissions = PermissionExtensions.MemberDefaults, IsSystemRole = true };
        _db.ServerRoles.AddRange(ownerRole, adminRole, memberRole);
        return (ownerRole, adminRole, memberRole);
    }

    // --- GetOrCreateUserAsync (Google JWT) ---

    [Fact]
    public async Task GetOrCreateUserAsync_CreatesNewUser()
    {
        var principal = CreatePrincipal("google-1", "Alice", "alice@test.com");
        var (user, isNew) = await _svc.GetOrCreateUserAsync(principal);

        user.GoogleSubject.Should().Be("google-1");
        user.DisplayName.Should().Be("Alice");
        user.Email.Should().Be("alice@test.com");
        isNew.Should().BeTrue();
    }

    [Fact]
    public async Task GetOrCreateUserAsync_ReturnsExistingUser()
    {
        _db.Users.Add(new User { GoogleSubject = "google-2", DisplayName = "Bob", Email = "bob@test.com" });
        await _db.SaveChangesAsync();

        var principal = CreatePrincipal("google-2", "Bob", "bob@test.com");
        var (user, isNew) = await _svc.GetOrCreateUserAsync(principal);

        user.DisplayName.Should().Be("Bob");
        isNew.Should().BeFalse();
        _db.Users.Count(u => u.GoogleSubject == "google-2").Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateUserAsync_UpdatesChangedProfile()
    {
        _db.Users.Add(new User { GoogleSubject = "google-3", DisplayName = "OldName", Email = "old@test.com" });
        await _db.SaveChangesAsync();

        var principal = CreatePrincipal("google-3", "NewName", "new@test.com");
        var (user, _) = await _svc.GetOrCreateUserAsync(principal);

        user.DisplayName.Should().Be("NewName");
        user.Email.Should().Be("new@test.com");
    }

    [Fact]
    public async Task GetOrCreateUserAsync_MissingSubClaim_Throws()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        await _svc.Invoking(s => s.GetOrCreateUserAsync(principal))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*subject*");
    }

    [Fact]
    public async Task GetOrCreateUserAsync_JoinsDefaultServer()
    {
        // Create default server
        var defaultServer = new Server { Id = Server.DefaultServerId, Name = "Codec HQ" };
        _db.Servers.Add(defaultServer);
        var (_, _, memberRole) = CreateDefaultRoles(defaultServer.Id);
        await _db.SaveChangesAsync();

        var principal = CreatePrincipal("google-4", "NewUser");
        var (user, _) = await _svc.GetOrCreateUserAsync(principal);

        var membership = await _db.ServerMembers
            .Include(m => m.Role)
            .FirstOrDefaultAsync(m => m.UserId == user.Id && m.ServerId == Server.DefaultServerId);
        membership.Should().NotBeNull();
        membership!.Role.Should().NotBeNull();
        membership.Role!.Name.Should().Be("Member");
    }

    // --- GetOrCreateUserAsync (Local JWT --- issuer "codec-api") ---

    [Fact]
    public async Task GetOrCreateUserAsync_LocalJwt_ReturnsExistingUserById()
    {
        var existingUser = new User { DisplayName = "Local User", Email = "local@test.com" };
        _db.Users.Add(existingUser);
        await _db.SaveChangesAsync();

        var principal = CreatePrincipal(existingUser.Id.ToString(), "Local User", "local@test.com", issuer: "codec-api");
        var (user, isNew) = await _svc.GetOrCreateUserAsync(principal);

        user.Id.Should().Be(existingUser.Id);
        isNew.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrCreateUserAsync_LocalJwt_ThrowsForNonExistentUser()
    {
        var nonExistentId = Guid.NewGuid();
        var principal = CreatePrincipal(nonExistentId.ToString(), "Ghost", issuer: "codec-api");

        await _svc.Invoking(s => s.GetOrCreateUserAsync(principal))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{nonExistentId}*");
    }

    [Fact]
    public async Task GetOrCreateUserAsync_LocalJwt_InvalidGuid_Throws()
    {
        var principal = CreatePrincipal("not-a-guid", "Bad", issuer: "codec-api");

        await _svc.Invoking(s => s.GetOrCreateUserAsync(principal))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*sub*");
    }

    // --- ResolveUserAsync ---

    [Fact]
    public async Task ResolveUserAsync_LocalJwt_ReturnsUserById()
    {
        var existingUser = new User { DisplayName = "Resolve Me", Email = "resolve@test.com" };
        _db.Users.Add(existingUser);
        await _db.SaveChangesAsync();

        var principal = CreatePrincipal(existingUser.Id.ToString(), issuer: "codec-api");
        var user = await _svc.ResolveUserAsync(principal);

        user.Should().NotBeNull();
        user!.Id.Should().Be(existingUser.Id);
    }

    [Fact]
    public async Task ResolveUserAsync_GoogleJwt_ReturnsUserByGoogleSubject()
    {
        _db.Users.Add(new User { GoogleSubject = "google-resolve", DisplayName = "Google User" });
        await _db.SaveChangesAsync();

        var principal = CreatePrincipal("google-resolve", "Google User");
        var user = await _svc.ResolveUserAsync(principal);

        user.Should().NotBeNull();
        user!.GoogleSubject.Should().Be("google-resolve");
    }

    [Fact]
    public async Task ResolveUserAsync_ReturnsNullForUnknownUser()
    {
        var principal = CreatePrincipal(Guid.NewGuid().ToString(), issuer: "codec-api");
        var user = await _svc.ResolveUserAsync(principal);
        user.Should().BeNull();
    }

    [Fact]
    public async Task ResolveUserAsync_ReturnsNullForUnknownGoogleSubject()
    {
        var principal = CreatePrincipal("unknown-google-sub");
        var user = await _svc.ResolveUserAsync(principal);
        user.Should().BeNull();
    }

    // --- GetEffectiveDisplayName ---

    [Fact]
    public void GetEffectiveDisplayName_NoNickname_ReturnsDisplayName()
    {
        var user = new User { DisplayName = "Alice", Nickname = null };
        _svc.GetEffectiveDisplayName(user).Should().Be("Alice");
    }

    [Fact]
    public void GetEffectiveDisplayName_WithNickname_ReturnsNickname()
    {
        var user = new User { DisplayName = "Alice", Nickname = "Ali" };
        _svc.GetEffectiveDisplayName(user).Should().Be("Ali");
    }

    [Fact]
    public void GetEffectiveDisplayName_EmptyNickname_ReturnsDisplayName()
    {
        var user = new User { DisplayName = "Alice", Nickname = "  " };
        _svc.GetEffectiveDisplayName(user).Should().Be("Alice");
    }

    // --- IsMemberAsync ---

    [Fact]
    public async Task IsMemberAsync_IsMember_ReturnsTrue()
    {
        var server = new Server { Name = "Test" };
        var user = new User { GoogleSubject = "g-5", DisplayName = "X" };
        _db.Servers.Add(server);
        _db.Users.Add(user);
        var (_, _, memberRole) = CreateDefaultRoles(server.Id);
        _db.ServerMembers.Add(new ServerMember { Server = server, User = user, RoleId = memberRole.Id });
        await _db.SaveChangesAsync();

        (await _svc.IsMemberAsync(server.Id, user.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task IsMemberAsync_NotMember_ReturnsFalse()
    {
        var server = new Server { Name = "Test" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        (await _svc.IsMemberAsync(server.Id, Guid.NewGuid())).Should().BeFalse();
    }

    // --- EnsureMemberAsync ---

    [Fact]
    public async Task EnsureMemberAsync_MemberExists_ReturnsMember()
    {
        var server = new Server { Name = "S" };
        var user = new User { GoogleSubject = "g-6", DisplayName = "X" };
        _db.Servers.Add(server);
        _db.Users.Add(user);
        var (_, adminRole, _) = CreateDefaultRoles(server.Id);
        _db.ServerMembers.Add(new ServerMember { Server = server, User = user, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        var member = await _svc.EnsureMemberAsync(server.Id, user.Id);
        member.Role.Should().NotBeNull();
        member.Role!.Name.Should().Be("Admin");
    }

    [Fact]
    public async Task EnsureMemberAsync_NotMember_ThrowsForbidden()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        await _svc.Invoking(s => s.EnsureMemberAsync(server.Id, Guid.NewGuid()))
            .Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task EnsureMemberAsync_ServerNotFound_ThrowsNotFound()
    {
        await _svc.Invoking(s => s.EnsureMemberAsync(Guid.NewGuid(), Guid.NewGuid()))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task EnsureMemberAsync_GlobalAdmin_BypassesMembership()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        var member = await _svc.EnsureMemberAsync(server.Id, Guid.NewGuid(), isGlobalAdmin: true);
        member.Should().NotBeNull();
    }

    // --- EnsureAdminAsync ---

    [Fact]
    public async Task EnsureAdminAsync_AdminRole_Succeeds()
    {
        var server = new Server { Name = "S" };
        var user = new User { GoogleSubject = "g-7", DisplayName = "X" };
        _db.Servers.Add(server);
        _db.Users.Add(user);
        var (_, adminRole, _) = CreateDefaultRoles(server.Id);
        _db.ServerMembers.Add(new ServerMember { Server = server, User = user, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        var member = await _svc.EnsureAdminAsync(server.Id, user.Id);
        member.Role.Should().NotBeNull();
        member.Role!.Name.Should().Be("Admin");
    }

    [Fact]
    public async Task EnsureAdminAsync_MemberRole_ThrowsForbidden()
    {
        var server = new Server { Name = "S" };
        var user = new User { GoogleSubject = "g-8", DisplayName = "X" };
        _db.Servers.Add(server);
        _db.Users.Add(user);
        var (_, _, memberRole) = CreateDefaultRoles(server.Id);
        _db.ServerMembers.Add(new ServerMember { Server = server, User = user, RoleId = memberRole.Id });
        await _db.SaveChangesAsync();

        await _svc.Invoking(s => s.EnsureAdminAsync(server.Id, user.Id))
            .Should().ThrowAsync<ForbiddenException>();
    }

    // --- EnsureOwnerAsync ---

    [Fact]
    public async Task EnsureOwnerAsync_OwnerRole_Succeeds()
    {
        var server = new Server { Name = "S" };
        var user = new User { GoogleSubject = "g-9", DisplayName = "X" };
        _db.Servers.Add(server);
        _db.Users.Add(user);
        var (ownerRole, _, _) = CreateDefaultRoles(server.Id);
        _db.ServerMembers.Add(new ServerMember { Server = server, User = user, RoleId = ownerRole.Id });
        await _db.SaveChangesAsync();

        var member = await _svc.EnsureOwnerAsync(server.Id, user.Id);
        member.Role.Should().NotBeNull();
        member.Role!.Name.Should().Be("Owner");
        member.Role.Position.Should().Be(0);
    }

    [Fact]
    public async Task EnsureOwnerAsync_AdminRole_ThrowsForbidden()
    {
        var server = new Server { Name = "S" };
        var user = new User { GoogleSubject = "g-10", DisplayName = "X" };
        _db.Servers.Add(server);
        _db.Users.Add(user);
        var (_, adminRole, _) = CreateDefaultRoles(server.Id);
        _db.ServerMembers.Add(new ServerMember { Server = server, User = user, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        await _svc.Invoking(s => s.EnsureOwnerAsync(server.Id, user.Id))
            .Should().ThrowAsync<ForbiddenException>();
    }

    // --- EnsureDmParticipantAsync ---

    [Fact]
    public async Task EnsureDmParticipantAsync_IsParticipant_Succeeds()
    {
        var user = new User { GoogleSubject = "g-11", DisplayName = "X" };
        var channel = new DmChannel();
        _db.Users.Add(user);
        _db.DmChannels.Add(channel);
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = channel, User = user });
        await _db.SaveChangesAsync();

        await _svc.Invoking(s => s.EnsureDmParticipantAsync(channel.Id, user.Id))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureDmParticipantAsync_NotParticipant_ThrowsForbidden()
    {
        var channel = new DmChannel();
        _db.DmChannels.Add(channel);
        await _db.SaveChangesAsync();

        await _svc.Invoking(s => s.EnsureDmParticipantAsync(channel.Id, Guid.NewGuid()))
            .Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task EnsureDmParticipantAsync_ChannelNotFound_ThrowsNotFound()
    {
        await _svc.Invoking(s => s.EnsureDmParticipantAsync(Guid.NewGuid(), Guid.NewGuid()))
            .Should().ThrowAsync<NotFoundException>();
    }

    // --- GetPermissionsAsync ---

    [Fact]
    public async Task GetPermissionsAsync_MemberExists_ReturnsPermissions()
    {
        var server = new Server { Name = "PermServer" };
        _db.Servers.Add(server);
        var (_, _, memberRole) = CreateDefaultRoles(server.Id);
        var user = new User { GoogleSubject = "perm-user", DisplayName = "Perm" };
        _db.Users.Add(user);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = user.Id, RoleId = memberRole.Id });
        await _db.SaveChangesAsync();

        var permissions = await _svc.GetPermissionsAsync(server.Id, user.Id);

        permissions.Should().NotBe(Permission.None);
    }

    [Fact]
    public async Task GetPermissionsAsync_NotMember_ReturnsNone()
    {
        var server = new Server { Name = "NoPermServer" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        var permissions = await _svc.GetPermissionsAsync(server.Id, Guid.NewGuid());

        permissions.Should().Be(Permission.None);
    }

    // --- IsOwnerAsync ---

    [Fact]
    public async Task IsOwnerAsync_OwnerRole_ReturnsTrue()
    {
        var server = new Server { Name = "OwnerTestServer" };
        _db.Servers.Add(server);
        var (ownerRole, _, _) = CreateDefaultRoles(server.Id);
        var user = new User { GoogleSubject = "owner-check", DisplayName = "Owner" };
        _db.Users.Add(user);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = user.Id, RoleId = ownerRole.Id });
        await _db.SaveChangesAsync();

        var isOwner = await _svc.IsOwnerAsync(server.Id, user.Id);

        isOwner.Should().BeTrue();
    }

    [Fact]
    public async Task IsOwnerAsync_AdminRole_ReturnsFalse()
    {
        var server = new Server { Name = "AdminNotOwnerServer" };
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server.Id);
        var user = new User { GoogleSubject = "admin-check", DisplayName = "Admin" };
        _db.Users.Add(user);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = user.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        var isOwner = await _svc.IsOwnerAsync(server.Id, user.Id);

        isOwner.Should().BeFalse();
    }

    [Fact]
    public async Task IsOwnerAsync_NotMember_ReturnsFalse()
    {
        var server = new Server { Name = "NotMemberServer" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        var isOwner = await _svc.IsOwnerAsync(server.Id, Guid.NewGuid());

        isOwner.Should().BeFalse();
    }

    // --- CreateDefaultRolesAsync ---

    [Fact]
    public async Task CreateDefaultRolesAsync_CreatesThreeRoles()
    {
        var server = new Server { Name = "DefaultRolesServer" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        var (owner, admin, member) = await _svc.CreateDefaultRolesAsync(server.Id);

        owner.Name.Should().Be("Owner");
        owner.Position.Should().Be(0);
        owner.IsSystemRole.Should().BeTrue();
        owner.Permissions.Should().Be(Permission.Administrator);

        admin.Name.Should().Be("Admin");
        admin.Position.Should().Be(1);

        member.Name.Should().Be("Member");
        member.Position.Should().Be(2);

        var roles = await _db.ServerRoles.Where(r => r.ServerId == server.Id).ToListAsync();
        roles.Should().HaveCount(3);
    }

    // --- EnsurePermissionAsync ---

    [Fact]
    public async Task EnsurePermissionAsync_HasPermission_ReturnsMember()
    {
        var server = new Server { Name = "PermCheckServer" };
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server.Id);
        var user = new User { GoogleSubject = "perm-check", DisplayName = "PermUser" };
        _db.Users.Add(user);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = user.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        var result = await _svc.EnsurePermissionAsync(server.Id, user.Id, Permission.ManageServer);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task EnsurePermissionAsync_LacksPermission_ThrowsForbidden()
    {
        var server = new Server { Name = "NoPermServer2" };
        _db.Servers.Add(server);
        var (_, _, memberRole) = CreateDefaultRoles(server.Id);
        var user = new User { GoogleSubject = "no-perm", DisplayName = "NoPerm" };
        _db.Users.Add(user);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = user.Id, RoleId = memberRole.Id });
        await _db.SaveChangesAsync();

        await _svc.Invoking(s => s.EnsurePermissionAsync(server.Id, user.Id, Permission.ManageServer))
            .Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task EnsurePermissionAsync_ServerNotFound_ThrowsNotFound()
    {
        await _svc.Invoking(s => s.EnsurePermissionAsync(Guid.NewGuid(), Guid.NewGuid(), Permission.ManageServer))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task EnsurePermissionAsync_GlobalAdmin_Bypasses()
    {
        var server = new Server { Name = "GlobalAdminPermServer" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        var result = await _svc.EnsurePermissionAsync(server.Id, Guid.NewGuid(), Permission.ManageServer, isGlobalAdmin: true);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task EnsurePermissionAsync_NullRole_ThrowsForbidden()
    {
        var server = new Server { Name = "NullRoleServer" };
        _db.Servers.Add(server);
        var user = new User { GoogleSubject = "null-role", DisplayName = "NullRole" };
        _db.Users.Add(user);
        // Member with no role assigned
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = user.Id });
        await _db.SaveChangesAsync();

        await _svc.Invoking(s => s.EnsurePermissionAsync(server.Id, user.Id, Permission.ManageServer))
            .Should().ThrowAsync<ForbiddenException>();
    }

    // --- EnsureOwnerAsync ---

    [Fact]
    public async Task EnsureOwnerAsync_Owner_Succeeds()
    {
        var server = new Server { Name = "OwnerTestServer2" };
        _db.Servers.Add(server);
        var (ownerRole, _, _) = CreateDefaultRoles(server.Id);
        var user = new User { GoogleSubject = "owner-test2", DisplayName = "Owner2" };
        _db.Users.Add(user);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = user.Id, RoleId = ownerRole.Id });
        await _db.SaveChangesAsync();

        var result = await _svc.EnsureOwnerAsync(server.Id, user.Id);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task EnsureOwnerAsync_NotMember_ThrowsForbidden()
    {
        var server = new Server { Name = "NotMemberOwner" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        await _svc.Invoking(s => s.EnsureOwnerAsync(server.Id, Guid.NewGuid()))
            .Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task EnsureOwnerAsync_ServerNotFound_ThrowsNotFound()
    {
        await _svc.Invoking(s => s.EnsureOwnerAsync(Guid.NewGuid(), Guid.NewGuid()))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task EnsureOwnerAsync_GlobalAdmin_Bypasses()
    {
        var server = new Server { Name = "GlobalAdminOwnerServer" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        var result = await _svc.EnsureOwnerAsync(server.Id, Guid.NewGuid(), isGlobalAdmin: true);

        result.Should().NotBeNull();
    }

    // --- GetOrCreateUserAsync (edge cases) ---

    [Fact]
    public async Task GetOrCreateUserAsync_NoProfileChanges_DoesNotSave()
    {
        // Create user first
        var principal = CreatePrincipal("no-change", "Test", "test@test.com", "https://pic.example.com");
        var (user, isNew) = await _svc.GetOrCreateUserAsync(principal);
        isNew.Should().BeTrue();

        // Call again with same data
        var (user2, isNew2) = await _svc.GetOrCreateUserAsync(principal);
        isNew2.Should().BeFalse();
        user2.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task GetOrCreateUserAsync_ProfileChanged_Updates()
    {
        var principal = CreatePrincipal("change-test", "OldName", "old@test.com");
        var (user, _) = await _svc.GetOrCreateUserAsync(principal);

        // Call with changed profile
        var newPrincipal = CreatePrincipal("change-test", "NewName", "new@test.com");
        var (updated, isNew) = await _svc.GetOrCreateUserAsync(newPrincipal);

        isNew.Should().BeFalse();
        updated.DisplayName.Should().Be("NewName");
        updated.Email.Should().Be("new@test.com");
    }

    // --- ResolveUserAsync edge cases ---

    [Fact]
    public async Task ResolveUserAsync_LocalJwt_InvalidGuid_ReturnsNull()
    {
        var principal = CreatePrincipal("not-a-guid", issuer: "codec-api");
        var result = await _svc.ResolveUserAsync(principal);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveUserAsync_LocalJwt_MissingSub_ReturnsNull()
    {
        var claims = new List<Claim> { new("iss", "codec-api") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));

        var result = await _svc.ResolveUserAsync(principal);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveUserAsync_EmptyGoogleSubject_ReturnsNull()
    {
        var principal = CreatePrincipal("  ");
        var result = await _svc.ResolveUserAsync(principal);
        result.Should().BeNull();
    }

    // ═══════════════════ EnsureMemberAsync — global admin non-member ═══════════════════

    [Fact]
    public async Task EnsureMemberAsync_GlobalAdmin_NonMember_ReturnsStub()
    {
        var server = new Server { Id = Guid.NewGuid(), Name = "TestServer" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        var userId = Guid.NewGuid();

        var result = await _svc.EnsureMemberAsync(server.Id, userId, isGlobalAdmin: true);

        result.Should().NotBeNull();
        result.ServerId.Should().Be(server.Id);
        result.UserId.Should().Be(userId);
    }

    // ═══════════════════ EnsurePermissionAsync — role has no permission ═══════════════════

    [Fact]
    public async Task EnsurePermissionAsync_NoPermission_ThrowsForbidden()
    {
        var server = new Server { Id = Guid.NewGuid(), Name = "TestServer" };
        _db.Servers.Add(server);
        var (_, _, memberRole) = CreateDefaultRoles(server.Id);
        await _db.SaveChangesAsync();

        var user = new User { Id = Guid.NewGuid(), GoogleSubject = "perm-test", DisplayName = "Test" };
        _db.Users.Add(user);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = user.Id, RoleId = memberRole.Id });
        await _db.SaveChangesAsync();

        // Member role does not have ManageServer permission
        await _svc.Invoking(s => s.EnsurePermissionAsync(server.Id, user.Id, Permission.ManageServer))
            .Should().ThrowAsync<ForbiddenException>();
    }

    // ═══════════════════ EnsureOwnerAsync — non-owner position ═══════════════════

    [Fact]
    public async Task EnsureOwnerAsync_NonOwnerRole_ThrowsForbidden()
    {
        var server = new Server { Id = Guid.NewGuid(), Name = "TestServer" };
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server.Id);
        await _db.SaveChangesAsync();

        var user = new User { Id = Guid.NewGuid(), GoogleSubject = "admin-test", DisplayName = "Admin" };
        _db.Users.Add(user);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = user.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        await _svc.Invoking(s => s.EnsureOwnerAsync(server.Id, user.Id))
            .Should().ThrowAsync<ForbiddenException>();
    }

    // ═══════════════════ EnsureAdminAsync — delegates to EnsurePermissionAsync ═══════════════════

    [Fact]
    public async Task EnsureAdminAsync_WithAdminPermission_Succeeds()
    {
        var server = new Server { Id = Guid.NewGuid(), Name = "TestServer" };
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server.Id);
        await _db.SaveChangesAsync();

        var user = new User { Id = Guid.NewGuid(), GoogleSubject = "admin-perm", DisplayName = "Admin" };
        _db.Users.Add(user);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = user.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        var result = await _svc.EnsureAdminAsync(server.Id, user.Id);

        result.Should().NotBeNull();
    }

    // ═══════════════════ EnsureDmParticipantAsync — channel not found ═══════════════════

    [Fact]
    public async Task EnsureDmParticipantAsync_NonexistentChannel_ThrowsNotFound()
    {
        var userId = Guid.NewGuid();

        await _svc.Invoking(s => s.EnsureDmParticipantAsync(Guid.NewGuid(), userId))
            .Should().ThrowAsync<NotFoundException>();
    }

    // ═══════════════════ GetOrCreateUserAsync — picture claim ═══════════════════

    [Fact]
    public async Task GetOrCreateUserAsync_WithPicture_SetsAvatarUrl()
    {
        var principal = CreatePrincipal("google-pic", "Alice", "alice@test.com", picture: "https://photo.url/pic.jpg");
        var (user, isNew) = await _svc.GetOrCreateUserAsync(principal);

        user.AvatarUrl.Should().Be("https://photo.url/pic.jpg");
        isNew.Should().BeTrue();
    }

    // ═══════════════════ GetPermissionsAsync — various states ═══════════════════

    [Fact]
    public async Task GetPermissionsAsync_NonMember_ReturnsNone()
    {
        var server = new Server { Id = Guid.NewGuid(), Name = "TestServer" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        var result = await _svc.GetPermissionsAsync(server.Id, Guid.NewGuid());

        result.Should().Be(Permission.None);
    }

    // ═══════════════════ IsOwnerAsync — non-member ═══════════════════

    [Fact]
    public async Task IsOwnerAsync_NonMember_ReturnsFalse()
    {
        var server = new Server { Id = Guid.NewGuid(), Name = "TestServer" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        var result = await _svc.IsOwnerAsync(server.Id, Guid.NewGuid());

        result.Should().BeFalse();
    }
}
