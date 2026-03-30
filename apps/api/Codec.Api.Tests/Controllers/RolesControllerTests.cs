using System.Security.Claims;
using Codec.Api.Controllers;
using Codec.Api.Data;
using Codec.Api.Hubs;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Codec.Api.Tests.Controllers;

public class RolesControllerTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IUserService> _userService = new();
    private readonly Mock<IHubContext<ChatHub>> _hub = new();
    private readonly AuditService _auditService;
    private readonly RolesController _controller;
    private readonly User _testUser;

    public RolesControllerTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        _testUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-1", DisplayName = "Test User" };
        _db.Users.Add(_testUser);
        _db.SaveChanges();

        _auditService = new AuditService(_db);

        var clients = new Mock<IHubClients>();
        var clientProxy = new Mock<IClientProxy>();
        _hub.Setup(h => h.Clients).Returns(clients.Object);
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxy.Object);

        _controller = new RolesController(_db, _userService.Object, _hub.Object, new PermissionResolverService(_db));
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim("sub", "g-1"), new Claim("name", "Test User")
                ], "Bearer"))
            }
        };

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_db.Users.First(u => u.Id == _testUser.Id), false));
    }

    public void Dispose() => _db.Dispose();

    private (ServerRoleEntity owner, ServerRoleEntity admin, ServerRoleEntity member) CreateDefaultRoles(Server server)
    {
        var ownerRole = new ServerRoleEntity { ServerId = server.Id, Name = "Owner", Position = 0, Permissions = Permission.Administrator, IsSystemRole = true, IsHoisted = true };
        var adminRole = new ServerRoleEntity { ServerId = server.Id, Name = "Admin", Position = 1, Permissions = PermissionExtensions.AdminDefaults, IsSystemRole = true, IsHoisted = true };
        var memberRole = new ServerRoleEntity { ServerId = server.Id, Name = "Member", Position = 2, Permissions = PermissionExtensions.MemberDefaults, IsSystemRole = true };
        _db.ServerRoles.AddRange(ownerRole, adminRole, memberRole);
        return (ownerRole, adminRole, memberRole);
    }

    private static ServerRoleEntity MakeAdminRole(Guid serverId = default) => new()
    {
        ServerId = serverId, Name = "Admin", Position = 1, Permissions = PermissionExtensions.AdminDefaults, IsSystemRole = true
    };

    private static ServerRoleEntity MakeOwnerRole(Guid serverId = default) => new()
    {
        ServerId = serverId, Name = "Owner", Position = 0, Permissions = Permission.Administrator, IsSystemRole = true
    };

    // --- GetRoles ---

    [Fact]
    public async Task GetRoles_ReturnsOrderedRoles()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (ownerRole, _, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = memberRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureMemberAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id });

        var result = await _controller.GetRoles(server.Id);

        result.Should().BeOfType<OkObjectResult>();
    }

    // --- CreateRole ---

    [Fact]
    public async Task CreateRole_ValidRequest_ReturnsCreated()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.CreateRole(server.Id, new CreateRoleRequest { Name = "Moderator" }, _auditService);

        result.Should().BeOfType<CreatedResult>();
        _db.ServerRoles.Should().Contain(r => r.Name == "Moderator" && r.ServerId == server.Id);
    }

    [Fact]
    public async Task CreateRole_EmptyName_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        var result = await _controller.CreateRole(server.Id, new CreateRoleRequest { Name = "" }, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateRole_NameTooLong_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        var result = await _controller.CreateRole(server.Id, new CreateRoleRequest { Name = new string('A', 101) }, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateRole_DuplicateName_ReturnsConflict()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.CreateRole(server.Id, new CreateRoleRequest { Name = "Admin" }, _auditService);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task CreateRole_WithPermissions_SetsPermissions()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var perms = (long)(Permission.ViewChannels | Permission.SendMessages);
        var result = await _controller.CreateRole(server.Id, new CreateRoleRequest { Name = "Custom", Permissions = perms }, _auditService);

        result.Should().BeOfType<CreatedResult>();
        var created = _db.ServerRoles.First(r => r.Name == "Custom" && r.ServerId == server.Id);
        created.Permissions.Should().Be(Permission.ViewChannels | Permission.SendMessages);
    }

    [Fact]
    public async Task CreateRole_ShiftsMemberRoleDown()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, adminRole, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        await _controller.CreateRole(server.Id, new CreateRoleRequest { Name = "Custom" }, _auditService);

        var updatedMemberRole = _db.ServerRoles.First(r => r.Id == memberRole.Id);
        updatedMemberRole.Position.Should().Be(3); // shifted from 2 to 3
    }

    [Fact]
    public async Task CreateRole_CannotGrantPermissionsCallerLacks()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, _, memberRole) = CreateDefaultRoles(server);
        // Give caller a role with limited permissions (no ManageServer)
        var limitedRole = new ServerRoleEntity
        {
            ServerId = server.Id, Name = "Limited", Position = 1,
            Permissions = Permission.ManageRoles | Permission.ViewChannels, IsSystemRole = false
        };
        _db.ServerRoles.Add(limitedRole);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = limitedRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = limitedRole });

        // Try to create a role with ManageServer permission which the caller doesn't have
        var result = await _controller.CreateRole(server.Id,
            new CreateRoleRequest { Name = "Elevated", Permissions = (long)Permission.ManageServer }, _auditService);

        result.Should().BeOfType<ForbidResult>();
    }

    // --- UpdateRole ---

    [Fact]
    public async Task UpdateRole_ChangesName()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server);
        var customRole = new ServerRoleEntity { ServerId = server.Id, Name = "Custom", Position = 3, IsSystemRole = false, Permissions = PermissionExtensions.MemberDefaults };
        _db.ServerRoles.Add(customRole);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateRole(server.Id, customRole.Id, new UpdateRoleRequest { Name = "Renamed" }, _auditService);

        result.Should().BeOfType<OkObjectResult>();
        var updated = _db.ServerRoles.First(r => r.Id == customRole.Id);
        updated.Name.Should().Be("Renamed");
    }

    [Fact]
    public async Task UpdateRole_NotFound_Returns404()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateRole(server.Id, Guid.NewGuid(), new UpdateRoleRequest { Name = "X" }, _auditService);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task UpdateRole_CannotRenameSystemRole()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, adminRole, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateRole(server.Id, memberRole.Id, new UpdateRoleRequest { Name = "NotMember" }, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateRole_DuplicateName_ReturnsConflict()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server);
        var customRole = new ServerRoleEntity { ServerId = server.Id, Name = "Custom", Position = 3, IsSystemRole = false, Permissions = PermissionExtensions.MemberDefaults };
        _db.ServerRoles.Add(customRole);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        // Try to rename custom role to an existing name
        var result = await _controller.UpdateRole(server.Id, customRole.Id, new UpdateRoleRequest { Name = "Admin" }, _auditService);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task UpdateRole_CannotEditHigherRole_ReturnsForbid()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (ownerRole, adminRole, _) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        // Caller is admin (position 1) trying to edit owner role (position 0)
        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });
        _userService.Setup(u => u.IsOwnerAsync(server.Id, _testUser.Id)).ReturnsAsync(false);

        var result = await _controller.UpdateRole(server.Id, ownerRole.Id, new UpdateRoleRequest { Color = "#FF0000" }, _auditService);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task UpdateRole_UpdatesColor()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server);
        var customRole = new ServerRoleEntity { ServerId = server.Id, Name = "Custom", Position = 3, IsSystemRole = false, Permissions = PermissionExtensions.MemberDefaults };
        _db.ServerRoles.Add(customRole);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateRole(server.Id, customRole.Id, new UpdateRoleRequest { Color = "#00FF00" }, _auditService);

        result.Should().BeOfType<OkObjectResult>();
        var updated = _db.ServerRoles.First(r => r.Id == customRole.Id);
        updated.Color.Should().Be("#00FF00");
    }

    [Fact]
    public async Task UpdateRole_UpdatesPermissions()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server);
        var customRole = new ServerRoleEntity { ServerId = server.Id, Name = "Custom", Position = 3, IsSystemRole = false, Permissions = PermissionExtensions.MemberDefaults };
        _db.ServerRoles.Add(customRole);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var newPerms = (long)(Permission.ViewChannels | Permission.ManageChannels);
        var result = await _controller.UpdateRole(server.Id, customRole.Id, new UpdateRoleRequest { Permissions = newPerms }, _auditService);

        result.Should().BeOfType<OkObjectResult>();
        var updated = _db.ServerRoles.First(r => r.Id == customRole.Id);
        updated.Permissions.Should().Be(Permission.ViewChannels | Permission.ManageChannels);
    }

    [Fact]
    public async Task UpdateRole_UpdatesHoistedAndMentionable()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server);
        var customRole = new ServerRoleEntity { ServerId = server.Id, Name = "Custom", Position = 3, IsSystemRole = false, Permissions = PermissionExtensions.MemberDefaults };
        _db.ServerRoles.Add(customRole);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateRole(server.Id, customRole.Id, new UpdateRoleRequest { IsHoisted = true, IsMentionable = true }, _auditService);

        result.Should().BeOfType<OkObjectResult>();
        var updated = _db.ServerRoles.First(r => r.Id == customRole.Id);
        updated.IsHoisted.Should().BeTrue();
        updated.IsMentionable.Should().BeTrue();
    }

    // --- DeleteRole ---

    [Fact]
    public async Task DeleteRole_ValidCustomRole_ReturnsNoContent()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, adminRole, memberRole) = CreateDefaultRoles(server);
        var customRole = new ServerRoleEntity { ServerId = server.Id, Name = "Custom", Position = 3, IsSystemRole = false, Permissions = PermissionExtensions.MemberDefaults };
        _db.ServerRoles.Add(customRole);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.DeleteRole(server.Id, customRole.Id, _auditService);

        result.Should().BeOfType<NoContentResult>();
        _db.ServerRoles.Any(r => r.Id == customRole.Id).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteRole_NotFound_Returns404()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.DeleteRole(server.Id, Guid.NewGuid(), _auditService);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DeleteRole_SystemRole_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, adminRole, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.DeleteRole(server.Id, memberRole.Id, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task DeleteRole_CannotDeleteHigherRole_ReturnsForbid()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server);
        // Custom role at position 0 (higher than admin at position 1)
        var higherRole = new ServerRoleEntity { ServerId = server.Id, Name = "Custom", Position = 0, IsSystemRole = false, Permissions = PermissionExtensions.MemberDefaults };
        _db.ServerRoles.Add(higherRole);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.DeleteRole(server.Id, higherRole.Id, _auditService);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task DeleteRole_ReassignsMembersToMemberRole()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, adminRole, memberRole) = CreateDefaultRoles(server);
        var customRole = new ServerRoleEntity { ServerId = server.Id, Name = "Custom", Position = 3, IsSystemRole = false, Permissions = PermissionExtensions.MemberDefaults };
        _db.ServerRoles.Add(customRole);
        await _db.SaveChangesAsync();

        // Another user on the custom role
        var otherUser = new User { GoogleSubject = "g-other", DisplayName = "Other" };
        _db.Users.Add(otherUser);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = otherUser.Id, RoleId = customRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.DeleteRole(server.Id, customRole.Id, _auditService);

        result.Should().BeOfType<NoContentResult>();
        var otherMembership = _db.ServerMembers.First(m => m.UserId == otherUser.Id && m.ServerId == server.Id);
        otherMembership.RoleId.Should().Be(memberRole.Id);
    }

    // --- ReorderRoles ---

    [Fact]
    public async Task ReorderRoles_EmptyList_ReturnsBadRequest()
    {
        var result = await _controller.ReorderRoles(Guid.NewGuid(), new ReorderRolesRequest { RoleIds = [] }, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ReorderRoles_OwnerNotFirst_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (ownerRole, adminRole, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        // Put admin first instead of owner
        var result = await _controller.ReorderRoles(server.Id,
            new ReorderRolesRequest { RoleIds = [adminRole.Id, ownerRole.Id, memberRole.Id] }, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ReorderRoles_ValidReorder_ReturnsNoContent()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (ownerRole, adminRole, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        // Swap admin and member but keep owner first
        var result = await _controller.ReorderRoles(server.Id,
            new ReorderRolesRequest { RoleIds = [ownerRole.Id, memberRole.Id, adminRole.Id] }, _auditService);

        result.Should().BeOfType<NoContentResult>();
        var updatedAdmin = _db.ServerRoles.First(r => r.Id == adminRole.Id);
        var updatedMember = _db.ServerRoles.First(r => r.Id == memberRole.Id);
        updatedAdmin.Position.Should().Be(2);
        updatedMember.Position.Should().Be(1);
    }
}
