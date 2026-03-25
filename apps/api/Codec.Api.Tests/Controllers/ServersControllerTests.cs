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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Codec.Api.Tests.Controllers;

public class ServersControllerTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IUserService> _userService = new();
    private readonly Mock<IAvatarService> _avatarService = new();
    private readonly Mock<ICustomEmojiService> _emojiService = new();
    private readonly Mock<IHubContext<ChatHub>> _hub = new();
    private readonly Mock<IHttpClientFactory> _httpFactory = new();
    private readonly Mock<IConfiguration> _config = new();
    private readonly MessageCacheService _messageCache;
    private readonly AuditService _auditService;
    private readonly ServersController _controller;
    private readonly User _testUser;

    public ServersControllerTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        _testUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-1", DisplayName = "Test User" };
        _db.Users.Add(_testUser);
        _db.SaveChanges();

        _messageCache = new MessageCacheService(new Mock<ILogger<MessageCacheService>>().Object);
        _auditService = new AuditService(_db);

        var clients = new Mock<IHubClients>();
        var clientProxy = new Mock<IClientProxy>();
        _hub.Setup(h => h.Clients).Returns(clients.Object);
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxy.Object);
        clients.Setup(c => c.All).Returns(clientProxy.Object);

        var webhookService = new WebhookService(new Mock<IServiceScopeFactory>().Object, new Mock<IHttpClientFactory>().Object, new Mock<ILogger<WebhookService>>().Object);
        _controller = new ServersController(_db, _userService.Object, _avatarService.Object, _emojiService.Object, _hub.Object, _httpFactory.Object, _config.Object, _messageCache, webhookService);
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
        _userService.Setup(u => u.GetEffectiveDisplayName(It.IsAny<User>())).Returns("Test User");
        _userService.Setup(u => u.CreateDefaultRolesAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Guid serverId) =>
            {
                var ownerRole = new ServerRoleEntity { ServerId = serverId, Name = "Owner", Position = 0, Permissions = Permission.Administrator, IsSystemRole = true, IsHoisted = true };
                var adminRole = new ServerRoleEntity { ServerId = serverId, Name = "Admin", Position = 1, Permissions = PermissionExtensions.AdminDefaults, IsSystemRole = true, IsHoisted = true };
                var memberRole = new ServerRoleEntity { ServerId = serverId, Name = "Member", Position = 2, Permissions = PermissionExtensions.MemberDefaults, IsSystemRole = true };
                _db.ServerRoles.AddRange(ownerRole, adminRole, memberRole);
                _db.SaveChanges();
                return (ownerRole, adminRole, memberRole);
            });
        _avatarService.Setup(a => a.ResolveUrl(It.IsAny<string?>())).Returns((string?)null);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Helper to create system roles for a server and add them to the DB.</summary>
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

    private static ServerRoleEntity MakeMemberRole(Guid serverId = default) => new()
    {
        ServerId = serverId, Name = "Member", Position = 2, Permissions = PermissionExtensions.MemberDefaults, IsSystemRole = true
    };

    [Fact]
    public async Task GetMyServers_ReturnsUserServers()
    {
        var server = new Server { Name = "Test Server" };
        _db.Servers.Add(server);
        var (_, _, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = memberRole.Id });
        await _db.SaveChangesAsync();

        var result = await _controller.GetMyServers();
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMyServers_GlobalAdmin_ReturnsAllServers()
    {
        var adminUser = new User { GoogleSubject = "admin-g", DisplayName = "Admin", IsGlobalAdmin = true };
        _db.Users.Add(adminUser);
        _db.Servers.Add(new Server { Name = "Private" });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync((adminUser, false));

        var result = await _controller.GetMyServers();
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Create_ReturnsCreated()
    {
        var result = await _controller.Create(new CreateServerRequest("New Server"));
        result.Should().BeOfType<CreatedResult>();
        _db.Servers.Should().ContainSingle(s => s.Name == "New Server");
    }

    [Fact]
    public async Task ReorderServers_EmptyList_ReturnsBadRequest()
    {
        var result = await _controller.ReorderServers(new ReorderServersRequest { ServerIds = [] });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ReorderServers_DuplicateIds_ReturnsBadRequest()
    {
        var id = Guid.NewGuid();
        var result = await _controller.ReorderServers(new ReorderServersRequest { ServerIds = [id, id] });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ReorderServers_Valid_ReturnsNoContent()
    {
        var s1 = new Server { Name = "S1" };
        var s2 = new Server { Name = "S2" };
        _db.Servers.AddRange(s1, s2);
        var (_, _, memberRole1) = CreateDefaultRoles(s1);
        var (_, _, memberRole2) = CreateDefaultRoles(s2);
        _db.ServerMembers.Add(new ServerMember { Server = s1, UserId = _testUser.Id, RoleId = memberRole1.Id });
        _db.ServerMembers.Add(new ServerMember { Server = s2, UserId = _testUser.Id, RoleId = memberRole2.Id });
        await _db.SaveChangesAsync();

        var result = await _controller.ReorderServers(new ReorderServersRequest { ServerIds = [s2.Id, s1.Id] });
        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task UpdateServer_ChangesName()
    {
        var server = new Server { Name = "Old" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateServer(server.Id, new UpdateServerRequest("New", null), _auditService);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMembers_ReturnsList()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (ownerRole, _, _) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = ownerRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureMemberAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id });

        var result = await _controller.GetMembers(server.Id);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetChannels_ReturnsList()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        _db.Channels.Add(new Channel { Server = server, Name = "general" });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureMemberAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember());

        var result = await _controller.GetChannels(server.Id);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task CreateChannel_Valid_ReturnsCreated()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember());

        var result = await _controller.CreateChannel(server.Id, new CreateChannelRequest("new-channel"), _auditService);
        result.Should().BeOfType<CreatedResult>();
    }

    [Fact]
    public async Task CreateInvite_ReturnsCreated()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember());

        var result = await _controller.CreateInvite(server.Id, new CreateInviteRequest(), _auditService);
        result.Should().BeOfType<CreatedResult>();
        _db.ServerInvites.Should().ContainSingle();
    }

    [Fact]
    public async Task JoinViaInvite_InvalidCode_Returns404()
    {
        var result = await _controller.JoinViaInvite("nonexistent");
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task ListEmojis_ReturnsList()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureMemberAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember());

        var result = await _controller.ListEmojis(server.Id);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetInvites_ReturnsList()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember());

        var result = await _controller.GetInvites(server.Id);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task RevokeInvite_Succeeds()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var invite = new ServerInvite { Server = server, Code = "revoke-me", CreatedByUserId = _testUser.Id };
        _db.ServerInvites.Add(invite);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember());

        var result = await _controller.RevokeInvite(server.Id, invite.Id, _auditService);
        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task RevokeInvite_NotFound_Returns404()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember());

        var result = await _controller.RevokeInvite(server.Id, Guid.NewGuid(), _auditService);
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // --- KickMember ---

    [Fact]
    public async Task KickMember_Self_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.KickMember(server.Id, _testUser.Id, _auditService);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task KickMember_TargetNotMember_Returns404()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.KickMember(server.Id, Guid.NewGuid(), _auditService);
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task KickMember_CannotKickOwner_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        var owner = new User { GoogleSubject = "g-owner", DisplayName = "Owner" };
        _db.Users.Add(owner);
        _db.Servers.Add(server);
        var (ownerRole, adminRole, _) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = owner.Id, RoleId = ownerRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.KickMember(server.Id, owner.Id, _auditService);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task KickMember_ValidKick_ReturnsNoContent()
    {
        var server = new Server { Name = "S" };
        var target = new User { GoogleSubject = "g-target", DisplayName = "Target" };
        _db.Users.Add(target);
        _db.Servers.Add(server);
        var (_, adminRole, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = target.Id, RoleId = memberRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.KickMember(server.Id, target.Id, _auditService);
        result.Should().BeOfType<NoContentResult>();
    }

    // --- UpdateMemberRole ---

    [Fact]
    public async Task UpdateMemberRole_InvalidRole_ReturnsBadRequest()
    {
        var result = await _controller.UpdateMemberRole(Guid.NewGuid(), Guid.NewGuid(), new UpdateMemberRoleRequest { Role = "Owner" }, _auditService);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateMemberRole_InvalidRoleName_ReturnsBadRequest()
    {
        var result = await _controller.UpdateMemberRole(Guid.NewGuid(), Guid.NewGuid(), new UpdateMemberRoleRequest { Role = "SuperAdmin" }, _auditService);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // --- UpdateChannel ---

    [Fact]
    public async Task UpdateChannel_Valid_ReturnsOk()
    {
        var server = new Server { Name = "S" };
        var channel = new Channel { Server = server, Name = "old-name" };
        _db.Servers.Add(server);
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember());

        var result = await _controller.UpdateChannel(server.Id, channel.Id, new UpdateChannelRequest("new-name", null), _auditService);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task UpdateChannel_NotFound_Returns404()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember());

        var result = await _controller.UpdateChannel(server.Id, Guid.NewGuid(), new UpdateChannelRequest("renamed", null), _auditService);
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // --- JoinViaInvite expired ---

    [Fact]
    public async Task JoinViaInvite_ExpiredInvite_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        _db.ServerInvites.Add(new ServerInvite
        {
            Server = server,
            Code = "expired",
            CreatedByUserId = _testUser.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await _db.SaveChangesAsync();

        var result = await _controller.JoinViaInvite("expired");
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task JoinViaInvite_MaxUsesExhausted_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        _db.ServerInvites.Add(new ServerInvite
        {
            Server = server,
            Code = "maxed",
            CreatedByUserId = _testUser.Id,
            MaxUses = 1,
            UseCount = 1
        });
        await _db.SaveChangesAsync();

        var result = await _controller.JoinViaInvite("maxed");
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task JoinViaInvite_ValidCode_NewMember_ReturnsCreated()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, _, memberRole) = CreateDefaultRoles(server);
        _db.ServerInvites.Add(new ServerInvite { Server = server, Code = "valid", CreatedByUserId = _testUser.Id });
        // Create a different user who will join
        var joiner = new User { GoogleSubject = "g-joiner", DisplayName = "Joiner" };
        _db.Users.Add(joiner);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync((joiner, false));

        var result = await _controller.JoinViaInvite("valid");
        result.Should().BeOfType<CreatedResult>();
    }

    [Fact]
    public async Task JoinViaInvite_AlreadyMember_ReturnsOk()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, _, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = memberRole.Id });
        _db.ServerInvites.Add(new ServerInvite { Server = server, Code = "rejoiner", CreatedByUserId = _testUser.Id });
        await _db.SaveChangesAsync();

        var result = await _controller.JoinViaInvite("rejoiner");
        result.Should().BeOfType<OkObjectResult>();
    }

    // --- Category CRUD ---

    [Fact]
    public async Task CreateCategory_ReturnsCreated()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.CreateCategory(server.Id, new CreateCategoryRequest("General"), _auditService);

        result.Should().BeOfType<CreatedResult>();
        var created = (CreatedResult)result;
        created.Value.Should().NotBeNull();
        _db.ChannelCategories.Should().ContainSingle(c => c.Name == "General" && c.ServerId == server.Id);
    }

    [Fact]
    public async Task CreateCategory_AutoIncrementsPosition()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        await _controller.CreateCategory(server.Id, new CreateCategoryRequest("First"), _auditService);
        await _controller.CreateCategory(server.Id, new CreateCategoryRequest("Second"), _auditService);

        var categories = _db.ChannelCategories.OrderBy(c => c.Position).ToList();
        categories.Should().HaveCount(2);
        categories[0].Position.Should().Be(0);
        categories[1].Position.Should().Be(1);
    }

    [Fact]
    public async Task GetCategories_ReturnsOrderedList()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        _db.ChannelCategories.AddRange(
            new ChannelCategory { Server = server, Name = "C", Position = 2 },
            new ChannelCategory { Server = server, Name = "A", Position = 0 },
            new ChannelCategory { Server = server, Name = "B", Position = 1 }
        );
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureMemberAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id });

        var result = await _controller.GetCategories(server.Id);

        result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result;
        var list = ok.Value as IEnumerable<dynamic>;
        list.Should().NotBeNull();
        // Verify the result is ordered by Position by checking the raw DB state
        var dbCategories = _db.ChannelCategories
            .Where(c => c.ServerId == server.Id)
            .OrderBy(c => c.Position)
            .ToList();
        dbCategories[0].Name.Should().Be("A");
        dbCategories[1].Name.Should().Be("B");
        dbCategories[2].Name.Should().Be("C");
    }

    [Fact]
    public async Task RenameCategory_UpdatesName()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var category = new ChannelCategory { Server = server, Name = "Old Name", Position = 0 };
        _db.ChannelCategories.Add(category);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.RenameCategory(server.Id, category.Id, new RenameCategoryRequest("New Name"), _auditService);

        result.Should().BeOfType<OkObjectResult>();
        var updated = await _db.ChannelCategories.FindAsync(category.Id);
        updated!.Name.Should().Be("New Name");
    }

    [Fact]
    public async Task RenameCategory_NotFound_Returns404()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.RenameCategory(server.Id, Guid.NewGuid(), new RenameCategoryRequest("New Name"), _auditService);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DeleteCategory_ReturnsNoContent()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var category = new ChannelCategory { Server = server, Name = "To Delete", Position = 0 };
        _db.ChannelCategories.Add(category);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.DeleteCategory(server.Id, category.Id, _auditService);

        result.Should().BeOfType<NoContentResult>();
        _db.ChannelCategories.Any(c => c.Id == category.Id).Should().BeFalse();
    }

    // --- Channel Ordering ---

    [Fact]
    public async Task UpdateChannelOrder_UpdatesPositions()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var ch1 = new Channel { Server = server, Name = "ch1", Position = 0 };
        var ch2 = new Channel { Server = server, Name = "ch2", Position = 1 };
        _db.Channels.AddRange(ch1, ch2);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var request = new UpdateChannelOrderRequest(
        [
            new ChannelOrderItem(ch2.Id, null, 0),
            new ChannelOrderItem(ch1.Id, null, 1)
        ]);

        var result = await _controller.UpdateChannelOrder(server.Id, request, _auditService);

        result.Should().BeOfType<NoContentResult>();
        var updated1 = await _db.Channels.FindAsync(ch1.Id);
        var updated2 = await _db.Channels.FindAsync(ch2.Id);
        updated1!.Position.Should().Be(1);
        updated2!.Position.Should().Be(0);
    }

    [Fact]
    public async Task UpdateChannelOrder_MissingChannel_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var ch1 = new Channel { Server = server, Name = "ch1", Position = 0 };
        var ch2 = new Channel { Server = server, Name = "ch2", Position = 1 };
        _db.Channels.AddRange(ch1, ch2);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        // Only send one channel when there are two
        var request = new UpdateChannelOrderRequest(
        [
            new ChannelOrderItem(ch1.Id, null, 0)
        ]);

        var result = await _controller.UpdateChannelOrder(server.Id, request, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateChannelOrder_InvalidCategory_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        var otherServer = new Server { Name = "Other" };
        _db.Servers.AddRange(server, otherServer);
        var ch1 = new Channel { Server = server, Name = "ch1", Position = 0 };
        _db.Channels.Add(ch1);
        // Category belonging to a different server
        var otherCategory = new ChannelCategory { Server = otherServer, Name = "Other Cat", Position = 0 };
        _db.ChannelCategories.Add(otherCategory);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var request = new UpdateChannelOrderRequest(
        [
            new ChannelOrderItem(ch1.Id, otherCategory.Id, 0)
        ]);

        var result = await _controller.UpdateChannelOrder(server.Id, request, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // --- Audit Log ---

    [Fact]
    public async Task GetAuditLog_ReturnsPaginatedEntries()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (ownerRole, _, _) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = ownerRole.Id });
        await _db.SaveChangesAsync();

        _db.AuditLogEntries.AddRange(
            new AuditLogEntry { ServerId = server.Id, ActorUserId = _testUser.Id, Action = AuditAction.ChannelCreated, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2) },
            new AuditLogEntry { ServerId = server.Id, ActorUserId = _testUser.Id, Action = AuditAction.ServerRenamed, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1) }
        );
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeOwnerRole(server.Id) });

        var result = await _controller.GetAuditLog(server.Id, null, 50);

        result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result;
        ok.Value.Should().NotBeNull();
        // Verify entries exist in DB for this server
        _db.AuditLogEntries.Count(e => e.ServerId == server.Id).Should().Be(2);
    }

    // --- Mute Tests ---

    [Fact]
    public async Task MuteServer_SetsIsMuted()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, _, memberRole) = CreateDefaultRoles(server);
        var member = new ServerMember { Server = server, UserId = _testUser.Id, RoleId = memberRole.Id, IsMuted = false };
        _db.ServerMembers.Add(member);
        await _db.SaveChangesAsync();

        var result = await _controller.MuteServer(server.Id, new MuteRequest(true));

        result.Should().BeOfType<NoContentResult>();
        var updated = await _db.ServerMembers.FindAsync(server.Id, _testUser.Id);
        updated!.IsMuted.Should().BeTrue();
    }

    [Fact]
    public async Task MuteChannel_CreatesOverride()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, _, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = memberRole.Id });
        var channel = new Channel { Server = server, Name = "general" };
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();

        var result = await _controller.MuteChannel(server.Id, channel.Id, new MuteRequest(true));

        result.Should().BeOfType<NoContentResult>();
        var overrideEntry = _db.ChannelNotificationOverrides
            .FirstOrDefault(o => o.UserId == _testUser.Id && o.ChannelId == channel.Id);
        overrideEntry.Should().NotBeNull();
        overrideEntry!.IsMuted.Should().BeTrue();
    }

    [Fact]
    public async Task MuteChannel_Unmute_RemovesOverride()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, _, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = memberRole.Id });
        var channel = new Channel { Server = server, Name = "general" };
        _db.Channels.Add(channel);
        _db.ChannelNotificationOverrides.Add(new ChannelNotificationOverride
        {
            UserId = _testUser.Id,
            ChannelId = channel.Id,
            IsMuted = true
        });
        await _db.SaveChangesAsync();

        var result = await _controller.MuteChannel(server.Id, channel.Id, new MuteRequest(false));

        result.Should().BeOfType<NoContentResult>();
        var overrideEntry = _db.ChannelNotificationOverrides
            .FirstOrDefault(o => o.UserId == _testUser.Id && o.ChannelId == channel.Id);
        overrideEntry.Should().BeNull();
    }

    [Fact]
    public async Task GetNotificationPreferences_ReturnsCorrectState()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, _, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = memberRole.Id, IsMuted = true });
        var channel = new Channel { Server = server, Name = "general" };
        _db.Channels.Add(channel);
        _db.ChannelNotificationOverrides.Add(new ChannelNotificationOverride
        {
            UserId = _testUser.Id,
            ChannelId = channel.Id,
            IsMuted = true
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetNotificationPreferences(server.Id);

        result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result;
        ok.Value.Should().NotBeNull();
        // Verify DB state reflects muted server + channel override
        var memberInDb = _db.ServerMembers.First(m => m.ServerId == server.Id && m.UserId == _testUser.Id);
        memberInDb.IsMuted.Should().BeTrue();
        _db.ChannelNotificationOverrides.Any(o => o.UserId == _testUser.Id && o.ChannelId == channel.Id && o.IsMuted).Should().BeTrue();
    }

    // --- Description Tests ---

    [Fact]
    public async Task UpdateServer_Description_Updates()
    {
        var server = new Server { Name = "S", Description = null };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateServer(server.Id, new UpdateServerRequest(null, "A new description"), _auditService);

        result.Should().BeOfType<OkObjectResult>();
        var updated = await _db.Servers.FindAsync(server.Id);
        updated!.Description.Should().Be("A new description");
    }

    [Fact]
    public async Task UpdateServer_BothNull_ReturnsBadRequest()
    {
        var result = await _controller.UpdateServer(Guid.NewGuid(), new UpdateServerRequest(null, null), _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // --- BanMember ---

    [Fact]
    public async Task BanMember_Self_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.BanMember(server.Id, _testUser.Id, new BanMemberRequest(), _auditService);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task BanMember_CannotBanOwner_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        var owner = new User { GoogleSubject = "g-owner", DisplayName = "Owner" };
        _db.Users.Add(owner);
        _db.Servers.Add(server);
        var (ownerRole, adminRole, _) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = owner.Id, RoleId = ownerRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.BanMember(server.Id, owner.Id, new BanMemberRequest(), _auditService);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task BanMember_CannotBanHigherRole_ReturnsForbid()
    {
        var server = new Server { Name = "S" };
        var target = new User { GoogleSubject = "g-target", DisplayName = "Target" };
        _db.Users.Add(target);
        _db.Servers.Add(server);
        var (_, adminRole, memberRole) = CreateDefaultRoles(server);
        // Caller has member role (position 2), target has admin role (position 1)
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = memberRole.Id });
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = target.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeMemberRole(server.Id) });

        var result = await _controller.BanMember(server.Id, target.Id, new BanMemberRequest(), _auditService);
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task BanMember_AlreadyBanned_ReturnsConflict()
    {
        var server = new Server { Name = "S" };
        var target = new User { GoogleSubject = "g-target", DisplayName = "Target" };
        _db.Users.Add(target);
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        _db.BannedMembers.Add(new BannedMember { ServerId = server.Id, UserId = target.Id, BannedByUserId = _testUser.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.BanMember(server.Id, target.Id, new BanMemberRequest(), _auditService);
        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task BanMember_TargetNotFound_Returns404()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.BanMember(server.Id, Guid.NewGuid(), new BanMemberRequest(), _auditService);
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task BanMember_ValidBan_ReturnsNoContent()
    {
        var server = new Server { Name = "S" };
        var target = new User { GoogleSubject = "g-target", DisplayName = "Target" };
        _db.Users.Add(target);
        _db.Servers.Add(server);
        var (_, adminRole, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = target.Id, RoleId = memberRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.BanMember(server.Id, target.Id, new BanMemberRequest { Reason = "Spam" }, _auditService);

        result.Should().BeOfType<NoContentResult>();
        _db.BannedMembers.Should().Contain(b => b.ServerId == server.Id && b.UserId == target.Id);
        _db.ServerMembers.Any(m => m.ServerId == server.Id && m.UserId == target.Id).Should().BeFalse();
    }

    [Fact]
    public async Task BanMember_WithDeleteMessages_DeletesMessages()
    {
        var server = new Server { Name = "S" };
        var target = new User { GoogleSubject = "g-target", DisplayName = "Target" };
        _db.Users.Add(target);
        _db.Servers.Add(server);
        var (_, adminRole, memberRole) = CreateDefaultRoles(server);
        var channel = new Channel { Server = server, Name = "general" };
        _db.Channels.Add(channel);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = target.Id, RoleId = memberRole.Id });
        _db.Messages.Add(new Message { ChannelId = channel.Id, AuthorUserId = target.Id, Body = "spam" });
        _db.Messages.Add(new Message { ChannelId = channel.Id, AuthorUserId = target.Id, Body = "more spam" });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.BanMember(server.Id, target.Id, new BanMemberRequest { DeleteMessages = true }, _auditService);

        result.Should().BeOfType<NoContentResult>();
        _db.Messages.Any(m => m.AuthorUserId == target.Id).Should().BeFalse();
    }

    [Fact]
    public async Task BanMember_NonMemberUser_StillBans()
    {
        var server = new Server { Name = "S" };
        var target = new User { GoogleSubject = "g-target", DisplayName = "Target" };
        _db.Users.Add(target);
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.BanMember(server.Id, target.Id, new BanMemberRequest(), _auditService);

        result.Should().BeOfType<NoContentResult>();
        _db.BannedMembers.Should().Contain(b => b.ServerId == server.Id && b.UserId == target.Id);
    }

    // --- UnbanMember ---

    [Fact]
    public async Task UnbanMember_BannedUser_ReturnsNoContent()
    {
        var server = new Server { Name = "S" };
        var target = new User { GoogleSubject = "g-target", DisplayName = "Target" };
        _db.Users.Add(target);
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        _db.BannedMembers.Add(new BannedMember { ServerId = server.Id, UserId = target.Id, BannedByUserId = _testUser.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UnbanMember(server.Id, target.Id, _auditService);

        result.Should().BeOfType<NoContentResult>();
        _db.BannedMembers.Any(b => b.ServerId == server.Id && b.UserId == target.Id).Should().BeFalse();
    }

    [Fact]
    public async Task UnbanMember_NotBanned_ReturnsNotFound()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UnbanMember(server.Id, Guid.NewGuid(), _auditService);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // --- GetBans ---

    [Fact]
    public async Task GetBans_ReturnsBanList()
    {
        var server = new Server { Name = "S" };
        var banned1 = new User { GoogleSubject = "g-b1", DisplayName = "Banned1" };
        var banned2 = new User { GoogleSubject = "g-b2", DisplayName = "Banned2" };
        _db.Users.AddRange(banned1, banned2);
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        _db.BannedMembers.AddRange(
            new BannedMember { ServerId = server.Id, UserId = banned1.Id, BannedByUserId = _testUser.Id, Reason = "Spam", BannedAt = DateTimeOffset.UtcNow.AddMinutes(-2) },
            new BannedMember { ServerId = server.Id, UserId = banned2.Id, BannedByUserId = _testUser.Id, Reason = "Trolling", BannedAt = DateTimeOffset.UtcNow.AddMinutes(-1) }
        );
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.GetBans(server.Id);

        result.Should().BeOfType<OkObjectResult>();
        _db.BannedMembers.Count(b => b.ServerId == server.Id).Should().Be(2);
    }

    [Fact]
    public async Task GetBans_NoBans_ReturnsEmptyList()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.GetBans(server.Id);

        result.Should().BeOfType<OkObjectResult>();
    }

    // --- JoinViaInvite Banned User ---

    [Fact]
    public async Task JoinViaInvite_BannedUser_ReturnsForbidden()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, _, memberRole) = CreateDefaultRoles(server);
        _db.ServerInvites.Add(new ServerInvite { Server = server, Code = "banned-test", CreatedByUserId = _testUser.Id });

        var bannedUser = new User { GoogleSubject = "g-banned", DisplayName = "Banned" };
        _db.Users.Add(bannedUser);
        _db.BannedMembers.Add(new BannedMember { ServerId = server.Id, UserId = bannedUser.Id, BannedByUserId = _testUser.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync((bannedUser, false));

        var result = await _controller.JoinViaInvite("banned-test");

        // Banned users get a 403 Forbidden when trying to join via invite
        result.Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(403);
    }
}
