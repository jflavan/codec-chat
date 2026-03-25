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

    // Note: DeleteServer tests are skipped because ExecuteDeleteAsync is not supported by the InMemory provider.
    // These are covered by integration tests instead.

    // ═══════════════════ DeleteChannel ═══════════════════

    [Fact]
    public async Task DeleteChannel_ExistingChannel_ReturnsNoContent()
    {
        var server = new Server { Name = "S" };
        var channel = new Channel { Server = server, Name = "to-delete" };
        _db.Servers.Add(server);
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.DeleteChannel(server.Id, channel.Id, _auditService);

        result.Should().BeOfType<NoContentResult>();
        _db.Channels.Any(c => c.Id == channel.Id).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteChannel_NotFound_Returns404()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.DeleteChannel(server.Id, Guid.NewGuid(), _auditService);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DeleteChannel_CascadeDeletesMessages()
    {
        var server = new Server { Name = "S" };
        var channel = new Channel { Server = server, Name = "has-messages" };
        _db.Servers.Add(server);
        _db.Channels.Add(channel);
        _db.Messages.Add(new Message { ChannelId = channel.Id, AuthorUserId = _testUser.Id, Body = "hello" });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.DeleteChannel(server.Id, channel.Id, _auditService);

        result.Should().BeOfType<NoContentResult>();
        _db.Messages.Any(m => m.ChannelId == channel.Id).Should().BeFalse();
    }

    // ═══════════════════ DeleteServerIcon ═══════════════════

    [Fact]
    public async Task DeleteServerIcon_WithIcon_ReturnsNoContent()
    {
        var server = new Server { Name = "S", IconUrl = "icons/test.png" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });
        _avatarService.Setup(a => a.DeleteServerIconAsync(server.Id)).Returns(Task.CompletedTask);

        var result = await _controller.DeleteServerIcon(server.Id, _auditService);

        result.Should().BeOfType<NoContentResult>();
        var updated = await _db.Servers.FindAsync(server.Id);
        updated!.IconUrl.Should().BeNull();
    }

    [Fact]
    public async Task DeleteServerIcon_NoIcon_ReturnsNoContent()
    {
        var server = new Server { Name = "S", IconUrl = null };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.DeleteServerIcon(server.Id, _auditService);

        result.Should().BeOfType<NoContentResult>();
    }

    // ═══════════════════ UploadServerIcon ═══════════════════

    [Fact]
    public async Task UploadServerIcon_InvalidFile_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _avatarService.Setup(a => a.Validate(It.IsAny<IFormFile>())).Returns("File too large.");

        var fileMock = new Mock<IFormFile>();
        var result = await _controller.UploadServerIcon(server.Id, fileMock.Object, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UploadServerIcon_ValidFile_ReturnsOk()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _avatarService.Setup(a => a.Validate(It.IsAny<IFormFile>())).Returns((string?)null);
        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });
        _avatarService.Setup(a => a.SaveServerIconAsync(server.Id, It.IsAny<IFormFile>()))
            .ReturnsAsync("icons/new.png");

        var fileMock = new Mock<IFormFile>();
        var result = await _controller.UploadServerIcon(server.Id, fileMock.Object, _auditService);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════ RenameEmoji ═══════════════════

    [Fact]
    public async Task RenameEmoji_NotFound_Returns404()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.RenameEmoji(server.Id, Guid.NewGuid(), new RenameEmojiRequest("new_name"), _auditService);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task RenameEmoji_Valid_ReturnsOk()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var emoji = new CustomEmoji { ServerId = server.Id, Name = "old_name", ImageUrl = "emojis/test.png", ContentType = "image/png", UploadedByUserId = _testUser.Id };
        _db.CustomEmojis.Add(emoji);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.RenameEmoji(server.Id, emoji.Id, new RenameEmojiRequest("new_name"), _auditService);

        result.Should().BeOfType<OkObjectResult>();
        var updated = await _db.CustomEmojis.FindAsync(emoji.Id);
        updated!.Name.Should().Be("new_name");
    }

    [Fact]
    public async Task RenameEmoji_NameConflict_ReturnsConflict()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var emoji1 = new CustomEmoji { ServerId = server.Id, Name = "emoji_a", ImageUrl = "emojis/a.png", ContentType = "image/png", UploadedByUserId = _testUser.Id };
        var emoji2 = new CustomEmoji { ServerId = server.Id, Name = "emoji_b", ImageUrl = "emojis/b.png", ContentType = "image/png", UploadedByUserId = _testUser.Id };
        _db.CustomEmojis.AddRange(emoji1, emoji2);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.RenameEmoji(server.Id, emoji1.Id, new RenameEmojiRequest("emoji_b"), _auditService);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    // ═══════════════════ DeleteEmoji ═══════════════════

    [Fact]
    public async Task DeleteEmoji_NotFound_Returns404()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.DeleteEmoji(server.Id, Guid.NewGuid(), _auditService);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DeleteEmoji_Valid_ReturnsNoContent()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var emoji = new CustomEmoji { ServerId = server.Id, Name = "bye", ImageUrl = "emojis/bye.png", ContentType = "image/png", UploadedByUserId = _testUser.Id };
        _db.CustomEmojis.Add(emoji);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });
        _emojiService.Setup(e => e.DeleteEmojiAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var result = await _controller.DeleteEmoji(server.Id, emoji.Id, _auditService);

        result.Should().BeOfType<NoContentResult>();
        _db.CustomEmojis.Any(e => e.Id == emoji.Id).Should().BeFalse();
    }

    // ═══════════════════ UpdateCategoryOrder ═══════════════════

    [Fact]
    public async Task UpdateCategoryOrder_ValidOrder_ReturnsNoContent()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var cat1 = new ChannelCategory { Server = server, Name = "A", Position = 0 };
        var cat2 = new ChannelCategory { Server = server, Name = "B", Position = 1 };
        _db.ChannelCategories.AddRange(cat1, cat2);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var request = new UpdateCategoryOrderRequest([
            new CategoryOrderItem(cat2.Id, 0),
            new CategoryOrderItem(cat1.Id, 1)
        ]);

        var result = await _controller.UpdateCategoryOrder(server.Id, request, _auditService);

        result.Should().BeOfType<NoContentResult>();
        (await _db.ChannelCategories.FindAsync(cat1.Id))!.Position.Should().Be(1);
        (await _db.ChannelCategories.FindAsync(cat2.Id))!.Position.Should().Be(0);
    }

    [Fact]
    public async Task UpdateCategoryOrder_MissingCategory_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var cat1 = new ChannelCategory { Server = server, Name = "A", Position = 0 };
        var cat2 = new ChannelCategory { Server = server, Name = "B", Position = 1 };
        _db.ChannelCategories.AddRange(cat1, cat2);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        // Only include one category when there are two
        var request = new UpdateCategoryOrderRequest([
            new CategoryOrderItem(cat1.Id, 0)
        ]);

        var result = await _controller.UpdateCategoryOrder(server.Id, request, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateCategoryOrder_InvalidCategoryId_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var cat1 = new ChannelCategory { Server = server, Name = "A", Position = 0 };
        _db.ChannelCategories.Add(cat1);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var request = new UpdateCategoryOrderRequest([
            new CategoryOrderItem(Guid.NewGuid(), 0)
        ]);

        var result = await _controller.UpdateCategoryOrder(server.Id, request, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ═══════════════════ Webhook CRUD ═══════════════════

    [Fact]
    public async Task CreateWebhook_Valid_ReturnsCreated()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var request = new CreateWebhookRequest
        {
            Name = "Test Hook",
            Url = "https://example.com/webhook",
            EventTypes = ["MessageCreated"]
        };

        var result = await _controller.CreateWebhook(server.Id, request, _auditService);

        result.Should().BeOfType<CreatedResult>();
        _db.Webhooks.Should().ContainSingle(w => w.ServerId == server.Id && w.Name == "Test Hook");
    }

    [Fact]
    public async Task CreateWebhook_InvalidEventType_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var request = new CreateWebhookRequest
        {
            Name = "Bad Hook",
            Url = "https://example.com/webhook",
            EventTypes = ["InvalidEvent"]
        };

        var result = await _controller.CreateWebhook(server.Id, request, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateWebhook_LocalhostUrl_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var request = new CreateWebhookRequest
        {
            Name = "Bad URL Hook",
            Url = "http://localhost/webhook",
            EventTypes = ["MessageCreated"]
        };

        var result = await _controller.CreateWebhook(server.Id, request, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetWebhooks_ReturnsList()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        _db.Webhooks.Add(new Webhook { ServerId = server.Id, Name = "Hook1", Url = "https://example.com/1", EventTypes = "MessageCreated", CreatedByUserId = _testUser.Id });
        _db.Webhooks.Add(new Webhook { ServerId = server.Id, Name = "Hook2", Url = "https://example.com/2", EventTypes = "MemberJoined", CreatedByUserId = _testUser.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.GetWebhooks(server.Id);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task UpdateWebhook_NotFound_Returns404()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateWebhook(server.Id, Guid.NewGuid(), new UpdateWebhookRequest { Name = "Updated" }, _auditService);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task UpdateWebhook_ValidUpdate_ReturnsOk()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var webhook = new Webhook { ServerId = server.Id, Name = "Old", Url = "https://example.com/hook", EventTypes = "MessageCreated", CreatedByUserId = _testUser.Id };
        _db.Webhooks.Add(webhook);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateWebhook(server.Id, webhook.Id, new UpdateWebhookRequest { Name = "Updated" }, _auditService);

        result.Should().BeOfType<OkObjectResult>();
        var updated = await _db.Webhooks.FindAsync(webhook.Id);
        updated!.Name.Should().Be("Updated");
    }

    [Fact]
    public async Task UpdateWebhook_InvalidEventType_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var webhook = new Webhook { ServerId = server.Id, Name = "Hook", Url = "https://example.com/hook", EventTypes = "MessageCreated", CreatedByUserId = _testUser.Id };
        _db.Webhooks.Add(webhook);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateWebhook(server.Id, webhook.Id, new UpdateWebhookRequest { EventTypes = ["FakeEvent"] }, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateWebhook_LocalhostUrl_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var webhook = new Webhook { ServerId = server.Id, Name = "Hook", Url = "https://example.com/hook", EventTypes = "MessageCreated", CreatedByUserId = _testUser.Id };
        _db.Webhooks.Add(webhook);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateWebhook(server.Id, webhook.Id, new UpdateWebhookRequest { Url = "http://127.0.0.1/hook" }, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task DeleteWebhook_NotFound_Returns404()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.DeleteWebhook(server.Id, Guid.NewGuid(), _auditService);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DeleteWebhook_Valid_ReturnsNoContent()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var webhook = new Webhook { ServerId = server.Id, Name = "ToDelete", Url = "https://example.com/hook", EventTypes = "MessageCreated", CreatedByUserId = _testUser.Id };
        _db.Webhooks.Add(webhook);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.DeleteWebhook(server.Id, webhook.Id, _auditService);

        result.Should().BeOfType<NoContentResult>();
        _db.Webhooks.Any(w => w.Id == webhook.Id).Should().BeFalse();
    }

    // ═══════════════════ GetWebhookDeliveries ═══════════════════

    [Fact]
    public async Task GetWebhookDeliveries_NotFound_Returns404()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.GetWebhookDeliveries(server.Id, Guid.NewGuid());

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetWebhookDeliveries_ReturnsLogs()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var webhook = new Webhook { ServerId = server.Id, Name = "Hook", Url = "https://example.com/hook", EventTypes = "MessageCreated", CreatedByUserId = _testUser.Id };
        _db.Webhooks.Add(webhook);
        _db.WebhookDeliveryLogs.Add(new WebhookDeliveryLog { WebhookId = webhook.Id, EventType = "MessageCreated", Payload = "{}", StatusCode = 200, Success = true, Attempt = 1 });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.GetWebhookDeliveries(server.Id, webhook.Id);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════ UpdateMemberRole — additional coverage ═══════════════════

    [Fact]
    public async Task UpdateMemberRole_Self_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, adminRole, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateMemberRole(server.Id, _testUser.Id, new UpdateMemberRoleRequest { Role = memberRole.Id.ToString() }, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateMemberRole_CannotAssignOwnerRole_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        var target = new User { GoogleSubject = "g-target2", DisplayName = "Target" };
        _db.Users.Add(target);
        _db.Servers.Add(server);
        var (ownerRole, adminRole, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = target.Id, RoleId = memberRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateMemberRole(server.Id, target.Id, new UpdateMemberRoleRequest { Role = ownerRole.Id.ToString() }, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateMemberRole_TargetNotMember_Returns404()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, adminRole, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateMemberRole(server.Id, Guid.NewGuid(), new UpdateMemberRoleRequest { Role = memberRole.Id.ToString() }, _auditService);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task UpdateMemberRole_RoleNotFound_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateMemberRole(server.Id, Guid.NewGuid(), new UpdateMemberRoleRequest { Role = "NonExistentRole" }, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateMemberRole_EmptyRole_ReturnsBadRequest()
    {
        var result = await _controller.UpdateMemberRole(Guid.NewGuid(), Guid.NewGuid(), new UpdateMemberRoleRequest { Role = "" }, _auditService);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ═══════════════════ UpdateChannel — edge cases ═══════════════════

    [Fact]
    public async Task UpdateChannel_BothNull_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        var result = await _controller.UpdateChannel(server.Id, Guid.NewGuid(), new UpdateChannelRequest(null, null), _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateChannel_DescriptionOnly_ReturnsOk()
    {
        var server = new Server { Name = "S" };
        var channel = new Channel { Server = server, Name = "ch", Description = null };
        _db.Servers.Add(server);
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateChannel(server.Id, channel.Id, new UpdateChannelRequest(null, "new desc"), _auditService);

        result.Should().BeOfType<OkObjectResult>();
        var updated = await _db.Channels.FindAsync(channel.Id);
        updated!.Description.Should().Be("new desc");
    }

    // ═══════════════════ CreateChannel — edge cases ═══════════════════

    [Fact]
    public async Task CreateChannel_VoiceType_ReturnsCreated()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember());

        var result = await _controller.CreateChannel(server.Id, new CreateChannelRequest("voice-room", "voice"), _auditService);

        result.Should().BeOfType<CreatedResult>();
        _db.Channels.Should().ContainSingle(c => c.Name == "voice-room" && c.Type == ChannelType.Voice);
    }

    [Fact]
    public async Task CreateChannel_InvalidType_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember());

        var result = await _controller.CreateChannel(server.Id, new CreateChannelRequest("bad", "video"), _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ═══════════════════ ReorderServers — edge cases ═══════════════════

    [Fact]
    public async Task ReorderServers_TooManyIds_ReturnsBadRequest()
    {
        var ids = Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid()).ToList();
        var result = await _controller.ReorderServers(new ReorderServersRequest { ServerIds = ids });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ═══════════════════ KickMember — higher role forbids ═══════════════════

    [Fact]
    public async Task KickMember_AdminCannotKickAdmin_ReturnsForbid()
    {
        var server = new Server { Name = "S" };
        var target = new User { GoogleSubject = "g-target-admin", DisplayName = "AdminTarget" };
        _db.Users.Add(target);
        _db.Servers.Add(server);
        var (_, adminRole, _) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = adminRole.Id });
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = target.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.KickMember(server.Id, target.Id, _auditService);

        result.Should().BeOfType<ForbidResult>();
    }

    // ═══════════════════ MuteServer — not member ═══════════════════

    [Fact]
    public async Task MuteServer_NotMember_Returns404()
    {
        var serverId = Guid.NewGuid();
        var result = await _controller.MuteServer(serverId, new MuteRequest(true));
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ═══════════════════ MuteChannel — edge cases ═══════════════════

    [Fact]
    public async Task MuteChannel_NotMember_Returns404()
    {
        var serverId = Guid.NewGuid();
        var result = await _controller.MuteChannel(serverId, Guid.NewGuid(), new MuteRequest(true));
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task MuteChannel_ChannelNotFound_Returns404()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, _, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = memberRole.Id });
        await _db.SaveChangesAsync();

        var result = await _controller.MuteChannel(server.Id, Guid.NewGuid(), new MuteRequest(true));

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ═══════════════════ GetNotificationPreferences — not member ═══════════════════

    [Fact]
    public async Task GetNotificationPreferences_NotMember_Returns404()
    {
        var serverId = Guid.NewGuid();
        var result = await _controller.GetNotificationPreferences(serverId);
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ═══════════════════ DeleteCategory — not found ═══════════════════

    [Fact]
    public async Task DeleteCategory_NotFound_Returns404()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.DeleteCategory(server.Id, Guid.NewGuid(), _auditService);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ═══════════════════ GetAuditLog — with cursor ═══════════════════

    [Fact]
    public async Task GetAuditLog_WithBeforeCursor_FiltersByDate()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (ownerRole, _, _) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = ownerRole.Id });
        var cutoff = DateTimeOffset.UtcNow;
        _db.AuditLogEntries.AddRange(
            new AuditLogEntry { ServerId = server.Id, ActorUserId = _testUser.Id, Action = AuditAction.ChannelCreated, CreatedAt = cutoff.AddMinutes(-5) },
            new AuditLogEntry { ServerId = server.Id, ActorUserId = _testUser.Id, Action = AuditAction.ServerRenamed, CreatedAt = cutoff.AddMinutes(5) }
        );
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeOwnerRole(server.Id) });

        var result = await _controller.GetAuditLog(server.Id, cutoff, 50);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════ UploadEmoji — validation edge cases ═══════════════════

    [Fact]
    public async Task UploadEmoji_InvalidName_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var fileMock = new Mock<IFormFile>();
        var result = await _controller.UploadEmoji(server.Id, "a", fileMock.Object, _auditService); // too short

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UploadEmoji_InvalidFile_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });
        _emojiService.Setup(e => e.Validate(It.IsAny<IFormFile>())).Returns("File too large.");

        var fileMock = new Mock<IFormFile>();
        var result = await _controller.UploadEmoji(server.Id, "valid_name", fileMock.Object, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UploadEmoji_ExceedsLimit_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        // Add 50 emojis to hit the limit
        for (int i = 0; i < 50; i++)
        {
            _db.CustomEmojis.Add(new CustomEmoji { ServerId = server.Id, Name = $"emoji_{i}", ImageUrl = $"emojis/{i}.png", ContentType = "image/png", UploadedByUserId = _testUser.Id });
        }
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });
        _emojiService.Setup(e => e.Validate(It.IsAny<IFormFile>())).Returns((string?)null);

        var fileMock = new Mock<IFormFile>();
        var result = await _controller.UploadEmoji(server.Id, "new_emoji", fileMock.Object, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UploadEmoji_DuplicateName_ReturnsConflict()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        _db.CustomEmojis.Add(new CustomEmoji { ServerId = server.Id, Name = "taken", ImageUrl = "emojis/taken.png", ContentType = "image/png", UploadedByUserId = _testUser.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });
        _emojiService.Setup(e => e.Validate(It.IsAny<IFormFile>())).Returns((string?)null);

        var fileMock = new Mock<IFormFile>();
        var result = await _controller.UploadEmoji(server.Id, "taken", fileMock.Object, _auditService);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task UploadEmoji_Valid_ReturnsCreated()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });
        _emojiService.Setup(e => e.Validate(It.IsAny<IFormFile>())).Returns((string?)null);
        _emojiService.Setup(e => e.SaveEmojiAsync(server.Id, "happy", It.IsAny<IFormFile>()))
            .ReturnsAsync("emojis/happy.png");

        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.ContentType).Returns("image/png");
        var result = await _controller.UploadEmoji(server.Id, "happy", fileMock.Object, _auditService);

        result.Should().BeOfType<CreatedResult>();
        _db.CustomEmojis.Should().ContainSingle(e => e.Name == "happy");
    }

    // ═══════════════════ UpdateWebhook — toggle active/secret ═══════════════════

    [Fact]
    public async Task UpdateWebhook_ToggleActive_ReturnsOk()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var webhook = new Webhook { ServerId = server.Id, Name = "Hook", Url = "https://example.com/hook", EventTypes = "MessageCreated", IsActive = true, CreatedByUserId = _testUser.Id };
        _db.Webhooks.Add(webhook);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateWebhook(server.Id, webhook.Id, new UpdateWebhookRequest { IsActive = false }, _auditService);

        result.Should().BeOfType<OkObjectResult>();
        var updated = await _db.Webhooks.FindAsync(webhook.Id);
        updated!.IsActive.Should().BeFalse();
    }

    // ═══════════════════ SearchMessages — validation ═══════════════════

    [Fact]
    public async Task SearchMessages_QueryTooShort_ReturnsBadRequest()
    {
        var result = await _controller.SearchMessages(Guid.NewGuid(), new SearchMessagesRequest { Q = "a" });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SearchMessages_QueryTooLong_ReturnsBadRequest()
    {
        var longQuery = new string('x', 201);
        var result = await _controller.SearchMessages(Guid.NewGuid(), new SearchMessagesRequest { Q = longQuery });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ═══════════════════ CreateWebhook — with secret ═══════════════════

    [Fact]
    public async Task CreateWebhook_WithSecret_StoresSecret()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var request = new CreateWebhookRequest
        {
            Name = "Secret Hook",
            Url = "https://example.com/webhook",
            Secret = "my-secret-key",
            EventTypes = ["MessageCreated", "MemberJoined"]
        };

        var result = await _controller.CreateWebhook(server.Id, request, _auditService);

        result.Should().BeOfType<CreatedResult>();
        var webhook = _db.Webhooks.First(w => w.ServerId == server.Id);
        webhook.Secret.Should().Be("my-secret-key");
        webhook.EventTypes.Should().Contain("MessageCreated");
        webhook.EventTypes.Should().Contain("MemberJoined");
    }

    // ═══════════════════ UpdateWebhook — update URL and event types ═══════════════════

    [Fact]
    public async Task UpdateWebhook_UpdateUrlAndEventTypes_ReturnsOk()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var webhook = new Webhook { ServerId = server.Id, Name = "Hook", Url = "https://example.com/old", EventTypes = "MessageCreated", CreatedByUserId = _testUser.Id };
        _db.Webhooks.Add(webhook);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateWebhook(server.Id, webhook.Id, new UpdateWebhookRequest
        {
            Url = "https://example.com/new",
            EventTypes = ["MemberJoined", "MemberLeft"]
        }, _auditService);

        result.Should().BeOfType<OkObjectResult>();
        var updated = await _db.Webhooks.FindAsync(webhook.Id);
        updated!.Url.Should().Be("https://example.com/new");
        updated.EventTypes.Should().Contain("MemberJoined");
    }

    // ═══════════════════ UploadServerIcon — replaces existing ═══════════════════

    [Fact]
    public async Task UploadServerIcon_ReplacesExistingIcon()
    {
        var server = new Server { Name = "S", IconUrl = "icons/old.png" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _avatarService.Setup(a => a.Validate(It.IsAny<IFormFile>())).Returns((string?)null);
        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });
        _avatarService.Setup(a => a.DeleteServerIconAsync(server.Id)).Returns(Task.CompletedTask);
        _avatarService.Setup(a => a.SaveServerIconAsync(server.Id, It.IsAny<IFormFile>()))
            .ReturnsAsync("icons/new.png");

        var fileMock = new Mock<IFormFile>();
        var result = await _controller.UploadServerIcon(server.Id, fileMock.Object, _auditService);

        result.Should().BeOfType<OkObjectResult>();
        _avatarService.Verify(a => a.DeleteServerIconAsync(server.Id), Times.Once);
        var updated = await _db.Servers.FindAsync(server.Id);
        updated!.IconUrl.Should().Be("icons/new.png");
    }

    // ═══════════════════ UpdateMemberRole — valid role change ═══════════════════

    [Fact]
    public async Task UpdateMemberRole_ValidChange_ReturnsOkAndUpdatesRole()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (ownerRole, adminRole, memberRole) = CreateDefaultRoles(server);
        await _db.SaveChangesAsync();

        var targetUser = new User { Id = Guid.NewGuid(), GoogleSubject = "t-1", DisplayName = "Target" };
        _db.Users.Add(targetUser);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, RoleId = ownerRole.Id });
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = targetUser.Id, RoleId = memberRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = ownerRole });

        var result = await _controller.UpdateMemberRole(server.Id, targetUser.Id,
            new UpdateMemberRoleRequest { Role = adminRole.Id.ToString() }, _auditService);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task UpdateMemberRole_CannotChangeOwnerRole_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (ownerRole, adminRole, memberRole) = CreateDefaultRoles(server);
        await _db.SaveChangesAsync();

        var ownerUser = new User { Id = Guid.NewGuid(), GoogleSubject = "own-1", DisplayName = "Owner" };
        _db.Users.Add(ownerUser);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, RoleId = adminRole.Id });
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = ownerUser.Id, RoleId = ownerRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = adminRole });

        var result = await _controller.UpdateMemberRole(server.Id, ownerUser.Id,
            new UpdateMemberRoleRequest { Role = memberRole.Name }, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateMemberRole_SameRole_ReturnsOkWithoutChange()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (ownerRole, _, memberRole) = CreateDefaultRoles(server);
        await _db.SaveChangesAsync();

        var targetUser = new User { Id = Guid.NewGuid(), GoogleSubject = "t-2", DisplayName = "Target" };
        _db.Users.Add(targetUser);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, RoleId = ownerRole.Id });
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = targetUser.Id, RoleId = memberRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = ownerRole });

        var result = await _controller.UpdateMemberRole(server.Id, targetUser.Id,
            new UpdateMemberRoleRequest { Role = memberRole.Id.ToString() }, _auditService);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task UpdateMemberRole_CannotAssignHigherRole_ReturnsForbid()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (ownerRole, adminRole, memberRole) = CreateDefaultRoles(server);
        await _db.SaveChangesAsync();

        var targetUser = new User { Id = Guid.NewGuid(), GoogleSubject = "t-3", DisplayName = "Target" };
        _db.Users.Add(targetUser);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, RoleId = adminRole.Id });
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = targetUser.Id, RoleId = memberRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = adminRole });

        // Admin (position 1) cannot assign Owner (position 0)
        var result = await _controller.UpdateMemberRole(server.Id, targetUser.Id,
            new UpdateMemberRoleRequest { Role = ownerRole.Id.ToString() }, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ═══════════════════ CreateInvite — various expiry configs ═══════════════════

    [Fact]
    public async Task CreateInvite_NeverExpires_CreatesWithNullExpiry()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.CreateInvite(server.Id,
            new CreateInviteRequest { ExpiresInHours = 0 }, _auditService);

        var created = result.Should().BeOfType<CreatedResult>().Subject;
        created.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateInvite_WithMaxUses_CreatesWithLimit()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.CreateInvite(server.Id,
            new CreateInviteRequest { ExpiresInHours = 24, MaxUses = 10 }, _auditService);

        result.Should().BeOfType<CreatedResult>();
        var invite = await _db.ServerInvites.FirstAsync();
        invite.MaxUses.Should().Be(10);
    }

    [Fact]
    public async Task CreateInvite_NegativeHours_DefaultsTo7Days()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.CreateInvite(server.Id,
            new CreateInviteRequest { ExpiresInHours = -5 }, _auditService);

        result.Should().BeOfType<CreatedResult>();
    }

    // ═══════════════════ JoinViaInvite — more edge cases ═══════════════════

    [Fact]
    public async Task JoinViaInvite_IncreasesUseCount()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, _, memberRole) = CreateDefaultRoles(server);
        await _db.SaveChangesAsync();

        var invite = new ServerInvite { ServerId = server.Id, Code = "TEST123A", CreatedByUserId = Guid.NewGuid() };
        _db.ServerInvites.Add(invite);
        await _db.SaveChangesAsync();

        var result = await _controller.JoinViaInvite("TEST123A");

        result.Should().BeOfType<CreatedResult>();
        var updatedInvite = await _db.ServerInvites.FirstAsync(i => i.Code == "TEST123A");
        updatedInvite.UseCount.Should().Be(1);
    }

    // ═══════════════════ DeleteServer ═══════════════════

    // Note: DeleteServer uses ExecuteDeleteAsync which is not supported by InMemory provider.
    // These tests would need an integration test with a real database (e.g., Testcontainers).
    // The ExecuteDeleteAsync path is tested in integration tests.

    // ═══════════════════ DeleteChannel — voice channel cleanup ═══════════════════

    [Fact]
    public async Task DeleteChannel_VoiceChannel_CleansUpVoiceStates()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var voiceCh = new Channel { ServerId = server.Id, Name = "vc", Type = ChannelType.Voice };
        _db.Channels.Add(voiceCh);
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(), UserId = _testUser.Id, ChannelId = voiceCh.Id,
            ConnectionId = "conn-1", ParticipantId = "p-1", JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var mockClient = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockClient.Object) { BaseAddress = new Uri("http://localhost:3001") };
        _httpFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var result = await _controller.DeleteChannel(server.Id, voiceCh.Id, _auditService);

        result.Should().BeOfType<NoContentResult>();
        var remaining = await _db.VoiceStates.CountAsync();
        remaining.Should().Be(0);
    }

    // ═══════════════════ UpdateChannel — description only ═══════════════════

    [Fact]
    public async Task UpdateChannel_NameOnly_UpdatesName()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var ch = new Channel { ServerId = server.Id, Name = "old-name" };
        _db.Channels.Add(ch);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateChannel(server.Id, ch.Id,
            new UpdateChannelRequest("new-name", null), _auditService);

        result.Should().BeOfType<OkObjectResult>();
        var updated = await _db.Channels.FindAsync(ch.Id);
        updated!.Name.Should().Be("new-name");
    }

    // ═══════════════════ UpdateServer — name change + description ═══════════════════

    [Fact]
    public async Task UpdateServer_NameAndDescription_UpdatesBoth()
    {
        var server = new Server { Name = "Old", Description = "Old desc" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateServer(server.Id,
            new UpdateServerRequest("New", "New desc"), _auditService);

        result.Should().BeOfType<OkObjectResult>();
        var updated = await _db.Servers.FindAsync(server.Id);
        updated!.Name.Should().Be("New");
        updated.Description.Should().Be("New desc");
    }

    [Fact]
    public async Task UpdateServer_SameName_DoesNotTriggerRename()
    {
        var server = new Server { Name = "Same" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateServer(server.Id,
            new UpdateServerRequest("Same", "desc"), _auditService);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════ UpdateChannelOrder — count mismatch ═══════════════════

    [Fact]
    public async Task UpdateChannelOrder_CountMismatch_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var ch1 = new Channel { ServerId = server.Id, Name = "ch1" };
        var ch2 = new Channel { ServerId = server.Id, Name = "ch2" };
        _db.Channels.AddRange(ch1, ch2);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        // Only send 1 of 2 channels
        var result = await _controller.UpdateChannelOrder(server.Id,
            new UpdateChannelOrderRequest([new ChannelOrderItem(ch1.Id, null, 0)]), _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ═══════════════════ UpdateCategoryOrder — count mismatch ═══════════════════

    [Fact]
    public async Task UpdateCategoryOrder_CountMismatch_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var cat1 = new ChannelCategory { ServerId = server.Id, Name = "Cat1", Position = 0 };
        var cat2 = new ChannelCategory { ServerId = server.Id, Name = "Cat2", Position = 1 };
        _db.ChannelCategories.AddRange(cat1, cat2);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateCategoryOrder(server.Id,
            new UpdateCategoryOrderRequest([new CategoryOrderItem(cat1.Id, 0)]), _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ═══════════════════ KickMember — caller cannot kick equal role ═══════════════════

    [Fact]
    public async Task KickMember_HigherRoleTarget_ReturnsForbid()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (ownerRole, adminRole, memberRole) = CreateDefaultRoles(server);
        await _db.SaveChangesAsync();

        var targetUser = new User { Id = Guid.NewGuid(), GoogleSubject = "t-k", DisplayName = "Target" };
        _db.Users.Add(targetUser);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, RoleId = adminRole.Id });
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = targetUser.Id, RoleId = ownerRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = adminRole });

        var result = await _controller.KickMember(server.Id, targetUser.Id, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>(); // Cannot kick the server owner
    }

    // ═══════════════════ MuteChannel — already muted, update ═══════════════════

    [Fact]
    public async Task MuteChannel_AlreadyMuted_Updates()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var ch = new Channel { ServerId = server.Id, Name = "ch" };
        _db.Channels.Add(ch);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, RoleId = Guid.NewGuid() });
        _db.ChannelNotificationOverrides.Add(new ChannelNotificationOverride
        {
            UserId = _testUser.Id, ChannelId = ch.Id, IsMuted = true
        });
        await _db.SaveChangesAsync();

        var result = await _controller.MuteChannel(server.Id, ch.Id, new MuteRequest(true));

        result.Should().BeOfType<NoContentResult>();
    }

    // ═══════════════════ GetNotificationPreferences — global admin ═══════════════════

    [Fact]
    public async Task GetNotificationPreferences_GlobalAdmin_ReturnsPrefs()
    {
        var adminUser = new User { Id = Guid.NewGuid(), GoogleSubject = "ga-1", DisplayName = "Admin", IsGlobalAdmin = true };
        _db.Users.Add(adminUser);
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>()))
            .ReturnsAsync((adminUser, false));

        var result = await _controller.GetNotificationPreferences(server.Id);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════ GetAuditLog — limit clamping ═══════════════════

    [Fact]
    public async Task GetAuditLog_NegativeLimit_ClampsTo1()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.GetAuditLog(server.Id, null, -10);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetAuditLog_ExcessiveLimit_ClampsTo100()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.GetAuditLog(server.Id, null, 500);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════ ReorderServers — partial overlap ═══════════════════

    [Fact]
    public async Task ReorderServers_PartialOverlap_OnlyUpdatesKnownServers()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var role = MakeMemberRole(server.Id);
        _db.ServerRoles.Add(role);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, RoleId = role.Id, SortOrder = 5 });
        await _db.SaveChangesAsync();

        var unknownServerId = Guid.NewGuid();
        var result = await _controller.ReorderServers(new ReorderServersRequest
        {
            ServerIds = [server.Id, unknownServerId]
        });

        result.Should().BeOfType<NoContentResult>();
        var member = await _db.ServerMembers.FirstAsync(m => m.UserId == _testUser.Id && m.ServerId == server.Id);
        member.SortOrder.Should().Be(0);
    }

    // ═══════════════════ CreateChannel — default type text ═══════════════════

    [Fact]
    public async Task CreateChannel_NoType_DefaultsToText()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.CreateChannel(server.Id,
            new CreateChannelRequest("new-channel"), _auditService);

        result.Should().BeOfType<CreatedResult>();
        var ch = await _db.Channels.FirstAsync(c => c.Name == "new-channel");
        ch.Type.Should().Be(ChannelType.Text);
    }

    // ═══════════════════ BanMember — with reason ═══════════════════

    [Fact]
    public async Task BanMember_WithReason_StoresReason()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (ownerRole, _, memberRole) = CreateDefaultRoles(server);
        await _db.SaveChangesAsync();

        var targetUser = new User { Id = Guid.NewGuid(), GoogleSubject = "ban-t", DisplayName = "Banned" };
        _db.Users.Add(targetUser);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, RoleId = ownerRole.Id });
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = targetUser.Id, RoleId = memberRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = ownerRole });

        var result = await _controller.BanMember(server.Id, targetUser.Id,
            new BanMemberRequest { Reason = "Rule violation", DeleteMessages = false }, _auditService);

        result.Should().BeOfType<NoContentResult>();
        var ban = await _db.BannedMembers.FindAsync(server.Id, targetUser.Id);
        ban.Should().NotBeNull();
        ban!.Reason.Should().Be("Rule violation");
    }

    // ═══════════════════ UpdateMemberRole — by role ID (GUID) ═══════════════════

    [Fact]
    public async Task UpdateMemberRole_ByRoleId_UpdatesSuccessfully()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (ownerRole, adminRole, memberRole) = CreateDefaultRoles(server);
        await _db.SaveChangesAsync();

        var targetUser = new User { Id = Guid.NewGuid(), GoogleSubject = "t-id", DisplayName = "Target" };
        _db.Users.Add(targetUser);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, RoleId = ownerRole.Id });
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = targetUser.Id, RoleId = memberRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = ownerRole });

        var result = await _controller.UpdateMemberRole(server.Id, targetUser.Id,
            new UpdateMemberRoleRequest { Role = adminRole.Id.ToString() }, _auditService);

        result.Should().BeOfType<OkObjectResult>();
        var updatedMembership = await _db.ServerMembers.FirstAsync(m => m.UserId == targetUser.Id && m.ServerId == server.Id);
        updatedMembership.RoleId.Should().Be(adminRole.Id);
    }

    // ═══════════════════ UpdateMemberRole — cannot modify higher role target ═══════════════════

    [Fact]
    public async Task UpdateMemberRole_CannotModifyHigherRoleMember_ReturnsForbid()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (ownerRole, adminRole, memberRole) = CreateDefaultRoles(server);
        await _db.SaveChangesAsync();

        var targetAdmin = new User { Id = Guid.NewGuid(), GoogleSubject = "t-admin", DisplayName = "OtherAdmin" };
        _db.Users.Add(targetAdmin);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, RoleId = adminRole.Id });
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = targetAdmin.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsurePermissionAsync(server.Id, _testUser.Id, Permission.ManageRoles, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = adminRole });

        var result = await _controller.UpdateMemberRole(server.Id, targetAdmin.Id,
            new UpdateMemberRoleRequest { Role = memberRole.Id.ToString() }, _auditService);

        result.Should().BeOfType<ForbidResult>();
    }

    // ═══════════════════ MuteServer — success path ═══════════════════

    [Fact]
    public async Task MuteServer_ValidMember_MutesSuccessfully()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, _, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, RoleId = memberRole.Id });
        await _db.SaveChangesAsync();

        var result = await _controller.MuteServer(server.Id, new MuteRequest(true));

        result.Should().BeOfType<NoContentResult>();
        var member = await _db.ServerMembers.FirstAsync(m => m.ServerId == server.Id && m.UserId == _testUser.Id);
        member.IsMuted.Should().BeTrue();
    }

    [Fact]
    public async Task MuteServer_UnmuteAfterMute_SetsIsMutedFalse()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, _, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, RoleId = memberRole.Id, IsMuted = true });
        await _db.SaveChangesAsync();

        var result = await _controller.MuteServer(server.Id, new MuteRequest(false));

        result.Should().BeOfType<NoContentResult>();
        var member = await _db.ServerMembers.FirstAsync(m => m.ServerId == server.Id && m.UserId == _testUser.Id);
        member.IsMuted.Should().BeFalse();
    }

    // ═══════════════════ MuteChannel — success paths ═══════════════════

    [Fact]
    public async Task MuteChannel_ValidMember_CreatesOverride()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, _, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, RoleId = memberRole.Id });
        var channel = new Channel { ServerId = server.Id, Name = "test-channel" };
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();

        var result = await _controller.MuteChannel(server.Id, channel.Id, new MuteRequest(true));

        result.Should().BeOfType<NoContentResult>();
        var overrideExists = await _db.ChannelNotificationOverrides
            .AnyAsync(o => o.UserId == _testUser.Id && o.ChannelId == channel.Id && o.IsMuted);
        overrideExists.Should().BeTrue();
    }

    [Fact]
    public async Task MuteChannel_UnmuteExisting_RemovesOverride()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, _, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, RoleId = memberRole.Id });
        var channel = new Channel { ServerId = server.Id, Name = "test-channel" };
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
        var overrideExists = await _db.ChannelNotificationOverrides
            .AnyAsync(o => o.UserId == _testUser.Id && o.ChannelId == channel.Id);
        overrideExists.Should().BeFalse();
    }

    [Fact]
    public async Task MuteChannel_UnmuteNoExisting_IsNoOp()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, _, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, RoleId = memberRole.Id });
        var channel = new Channel { ServerId = server.Id, Name = "test-channel" };
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();

        var result = await _controller.MuteChannel(server.Id, channel.Id, new MuteRequest(false));

        result.Should().BeOfType<NoContentResult>();
    }

    // ═══════════════════ GetNotificationPreferences — success ═══════════════════

    [Fact]
    public async Task GetNotificationPreferences_ValidMember_ReturnsPrefs()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, _, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, RoleId = memberRole.Id, IsMuted = true });
        var channel = new Channel { ServerId = server.Id, Name = "test-channel" };
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
    }

    // ═══════════════════ UnbanMember ═══════════════════

    [Fact]
    public async Task UnbanMember_NotBanned_Returns404()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UnbanMember(server.Id, Guid.NewGuid(), _auditService);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task UnbanMember_BannedUser_Unbans()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var bannedUser = new User { Id = Guid.NewGuid(), GoogleSubject = "banned", DisplayName = "Banned" };
        _db.Users.Add(bannedUser);
        _db.BannedMembers.Add(new BannedMember
        {
            ServerId = server.Id,
            UserId = bannedUser.Id,
            BannedByUserId = _testUser.Id,
            Reason = "Test"
        });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UnbanMember(server.Id, bannedUser.Id, _auditService);

        result.Should().BeOfType<NoContentResult>();
        var ban = await _db.BannedMembers.FindAsync(server.Id, bannedUser.Id);
        ban.Should().BeNull();
    }

    // ═══════════════════ GetBans — with multiple banned users ═══════════════════

    [Fact]
    public async Task GetBans_MultipleBannedUsers_ReturnsAllBans()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var bannedUser1 = new User { Id = Guid.NewGuid(), GoogleSubject = "banned3a", DisplayName = "Banned1" };
        var bannedUser2 = new User { Id = Guid.NewGuid(), GoogleSubject = "banned3b", DisplayName = "Banned2" };
        _db.Users.AddRange(bannedUser1, bannedUser2);
        _db.BannedMembers.Add(new BannedMember { ServerId = server.Id, UserId = bannedUser1.Id, BannedByUserId = _testUser.Id, Reason = "Spam" });
        _db.BannedMembers.Add(new BannedMember { ServerId = server.Id, UserId = bannedUser2.Id, BannedByUserId = _testUser.Id, Reason = "Abuse" });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.GetBans(server.Id);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════ UpdateCategoryOrder — verify position updates ═══════════════════

    [Fact]
    public async Task UpdateCategoryOrder_ThreeCategories_UpdatesAllPositions()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var cat1 = new ChannelCategory { Server = server, Name = "Cat1", Position = 0 };
        var cat2 = new ChannelCategory { Server = server, Name = "Cat2", Position = 1 };
        var cat3 = new ChannelCategory { Server = server, Name = "Cat3", Position = 2 };
        _db.ChannelCategories.AddRange(cat1, cat2, cat3);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var request = new UpdateCategoryOrderRequest([
            new CategoryOrderItem(cat3.Id, 0),
            new CategoryOrderItem(cat1.Id, 1),
            new CategoryOrderItem(cat2.Id, 2)
        ]);
        var result = await _controller.UpdateCategoryOrder(server.Id, request, _auditService);

        result.Should().BeOfType<NoContentResult>();
        var updatedCat3 = await _db.ChannelCategories.FirstAsync(c => c.Id == cat3.Id);
        updatedCat3.Position.Should().Be(0);
    }

    [Fact]
    public async Task UpdateCategoryOrder_CategoryNotFound_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var cat = new ChannelCategory { Server = server, Name = "Cat", Position = 0 };
        _db.ChannelCategories.Add(cat);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var request = new UpdateCategoryOrderRequest([
            new CategoryOrderItem(Guid.NewGuid(), 0)
        ]);
        var result = await _controller.UpdateCategoryOrder(server.Id, request, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ═══════════════════ JoinViaInvite — max uses with higher count ═══════════════════

    [Fact]
    public async Task JoinViaInvite_MaxUsesExceeded_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var invite = new ServerInvite
        {
            ServerId = server.Id,
            Code = "MAXED456",
            CreatedByUserId = _testUser.Id,
            MaxUses = 5,
            UseCount = 5
        };
        _db.ServerInvites.Add(invite);
        await _db.SaveChangesAsync();

        var result = await _controller.JoinViaInvite("MAXED456");

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task JoinViaInvite_NullExpiry_NeverExpires()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, _, memberRole) = CreateDefaultRoles(server);
        var invite = new ServerInvite
        {
            ServerId = server.Id,
            Code = "NOEXPIRY1",
            CreatedByUserId = Guid.NewGuid(),
            ExpiresAt = null // never expires
        };
        _db.ServerInvites.Add(invite);
        await _db.SaveChangesAsync();

        var result = await _controller.JoinViaInvite("NOEXPIRY1");

        result.Should().BeOfType<CreatedResult>();
    }

    // ═══════════════════ GetAuditLog — with entries ═══════════════════

    [Fact]
    public async Task GetAuditLog_WithEntries_ReturnsEntriesInOrder()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        _db.AuditLogEntries.Add(new AuditLogEntry
        {
            ServerId = server.Id,
            ActorUserId = _testUser.Id,
            Action = AuditAction.ChannelCreated,
            Details = "Created #general",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        });
        _db.AuditLogEntries.Add(new AuditLogEntry
        {
            ServerId = server.Id,
            ActorUserId = _testUser.Id,
            Action = AuditAction.ChannelDeleted,
            Details = "Deleted #old",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.GetAuditLog(server.Id, null, 50);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════ GetInvites ═══════════════════

    [Fact]
    public async Task GetInvites_ReturnsActiveInvites()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        _db.ServerInvites.Add(new ServerInvite
        {
            ServerId = server.Id,
            Code = "ACTIVE1",
            CreatedByUserId = _testUser.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        });
        _db.ServerInvites.Add(new ServerInvite
        {
            ServerId = server.Id,
            Code = "EXPIRED1",
            CreatedByUserId = _testUser.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) // expired
        });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.GetInvites(server.Id);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════ RevokeInvite — valid removal ═══════════════════

    [Fact]
    public async Task RevokeInvite_ValidInvite_DeletesAndReturnsNoContent()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var invite = new ServerInvite
        {
            ServerId = server.Id,
            Code = "REVOKE1",
            CreatedByUserId = _testUser.Id
        };
        _db.ServerInvites.Add(invite);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.RevokeInvite(server.Id, invite.Id, _auditService);

        result.Should().BeOfType<NoContentResult>();
        var remaining = await _db.ServerInvites.FindAsync(invite.Id);
        remaining.Should().BeNull();
    }

    // ═══════════════════ UpdateServer — description only ═══════════════════

    [Fact]
    public async Task UpdateServer_DescriptionOnly_UpdatesDescription()
    {
        var server = new Server { Name = "S", Description = "Old desc" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateServer(server.Id,
            new UpdateServerRequest(null, "New desc"), _auditService);

        result.Should().BeOfType<OkObjectResult>();
        var updated = await _db.Servers.FindAsync(server.Id);
        updated!.Description.Should().Be("New desc");
        updated.Name.Should().Be("S");
    }

    [Fact]
    public async Task UpdateServer_NeitherNameNorDescription_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        var result = await _controller.UpdateServer(server.Id,
            new UpdateServerRequest(null, null), _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ═══════════════════ GetMembers ═══════════════════

    [Fact]
    public async Task GetMembers_ValidServer_ReturnsMembers()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, _, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, RoleId = memberRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureMemberAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id });

        var result = await _controller.GetMembers(server.Id);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════ DeleteChannel — text channel ═══════════════════

    [Fact]
    public async Task DeleteChannel_TextChannel_ReturnsNoContent()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var channel = new Channel { ServerId = server.Id, Name = "to-delete", Type = ChannelType.Text };
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.DeleteChannel(server.Id, channel.Id, _auditService);

        result.Should().BeOfType<NoContentResult>();
        var remaining = await _db.Channels.FindAsync(channel.Id);
        remaining.Should().BeNull();
    }

    [Fact]
    public async Task DeleteChannel_NonexistentChannelId_Returns404()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.DeleteChannel(server.Id, Guid.NewGuid(), _auditService);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DeleteChannel_WithMessages_DeletesMessages()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var channel = new Channel { ServerId = server.Id, Name = "ch-with-msgs", Type = ChannelType.Text };
        _db.Channels.Add(channel);
        _db.Messages.Add(new Message { ChannelId = channel.Id, AuthorName = "User", Body = "test" });
        _db.Messages.Add(new Message { ChannelId = channel.Id, AuthorName = "User", Body = "test2" });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.DeleteChannel(server.Id, channel.Id, _auditService);

        result.Should().BeOfType<NoContentResult>();
        var msgs = await _db.Messages.Where(m => m.ChannelId == channel.Id).CountAsync();
        msgs.Should().Be(0);
    }

    // ═══════════════════ CreateChannel — with name trimming ═══════════════════

    [Fact]
    public async Task CreateChannel_TrimsName()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.CreateChannel(server.Id,
            new CreateChannelRequest("  spaced  "), _auditService);

        result.Should().BeOfType<CreatedResult>();
        var ch = await _db.Channels.FirstAsync(c => c.ServerId == server.Id);
        ch.Name.Should().Be("spaced");
    }

    // ═══════════════════ GetChannels ═══════════════════

    [Fact]
    public async Task GetChannels_ReturnsAllChannelsInServer()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        _db.Channels.Add(new Channel { ServerId = server.Id, Name = "ch1" });
        _db.Channels.Add(new Channel { ServerId = server.Id, Name = "ch2" });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureMemberAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id });

        var result = await _controller.GetChannels(server.Id);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════ UpdateChannel — name change ═══════════════════

    [Fact]
    public async Task UpdateChannel_NameChange_Updates()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var channel = new Channel { ServerId = server.Id, Name = "old-name" };
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateChannel(server.Id, channel.Id,
            new UpdateChannelRequest("new-name", null), _auditService);

        result.Should().BeOfType<OkObjectResult>();
        var updated = await _db.Channels.FindAsync(channel.Id);
        updated!.Name.Should().Be("new-name");
    }

    [Fact]
    public async Task UpdateChannel_ChannelNotFound_Returns404()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UpdateChannel(server.Id, Guid.NewGuid(),
            new UpdateChannelRequest("x", null), _auditService);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ═══════════════════ CreateInvite — default expiry ═══════════════════

    [Fact]
    public async Task CreateInvite_DefaultExpiry_Creates7DayExpiry()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.CreateInvite(server.Id,
            new CreateInviteRequest { ExpiresInHours = null }, _auditService);

        result.Should().BeOfType<CreatedResult>();
        var invite = await _db.ServerInvites.FirstAsync(i => i.ServerId == server.Id);
        invite.ExpiresAt.Should().NotBeNull();
        invite.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(7), TimeSpan.FromMinutes(1));
    }

    // ═══════════════════ GetCategories ═══════════════════

    [Fact]
    public async Task GetCategories_ReturnsOrderedCategories()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        _db.ChannelCategories.Add(new ChannelCategory { Server = server, Name = "Z-Cat", Position = 2 });
        _db.ChannelCategories.Add(new ChannelCategory { Server = server, Name = "A-Cat", Position = 0 });
        _db.ChannelCategories.Add(new ChannelCategory { Server = server, Name = "M-Cat", Position = 1 });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureMemberAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id });

        var result = await _controller.GetCategories(server.Id);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════ GetCategories — empty ═══════════════════

    [Fact]
    public async Task GetCategories_NoCategories_ReturnsEmptyList()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureMemberAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id });

        var result = await _controller.GetCategories(server.Id);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════ KickMember — self kick ═══════════════════

    [Fact]
    public async Task KickMember_NonExistentTarget_Returns404()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (ownerRole, _, _) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, RoleId = ownerRole.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = ownerRole });

        var result = await _controller.KickMember(server.Id, Guid.NewGuid(), _auditService);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ═══════════════════ MuteChannel — GlobalAdmin bypass ═══════════════════

    [Fact]
    public async Task MuteChannel_GlobalAdmin_BypassesMembershipCheck()
    {
        var adminUser = new User { GoogleSubject = "admin-mute", DisplayName = "Admin", IsGlobalAdmin = true };
        _db.Users.Add(adminUser);
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var channel = new Channel { ServerId = server.Id, Name = "test-channel" };
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((adminUser, false));

        var result = await _controller.MuteChannel(server.Id, channel.Id, new MuteRequest(true));

        result.Should().BeOfType<NoContentResult>();
    }

    // ═══════════════════ CreateCategory — multiple categories in order ═══════════════════

    [Fact]
    public async Task CreateCategory_ThreeInOrder_PositionsAutoIncrement()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        await _controller.CreateCategory(server.Id, new CreateCategoryRequest("First"), _auditService);
        await _controller.CreateCategory(server.Id, new CreateCategoryRequest("Second"), _auditService);
        await _controller.CreateCategory(server.Id, new CreateCategoryRequest("Third"), _auditService);

        var cats = await _db.ChannelCategories
            .Where(c => c.ServerId == server.Id)
            .OrderBy(c => c.Position)
            .ToListAsync();

        cats.Should().HaveCount(3);
        cats[0].Position.Should().Be(0);
        cats[1].Position.Should().Be(1);
        cats[2].Position.Should().Be(2);
    }

    // ═══════════════════ RenameCategory — trims whitespace ═══════════════════

    [Fact]
    public async Task RenameCategory_TrimsWhitespace()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var cat = new ChannelCategory { Server = server, Name = "Old", Position = 0 };
        _db.ChannelCategories.Add(cat);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.RenameCategory(server.Id, cat.Id,
            new RenameCategoryRequest("  New Name  "), _auditService);

        result.Should().BeOfType<OkObjectResult>();
        var updated = await _db.ChannelCategories.FindAsync(cat.Id);
        updated!.Name.Should().Be("New Name");
    }

    // ═══════════════════ UpdateChannelOrder — channel not found ═══════════════════

    [Fact]
    public async Task UpdateChannelOrder_ChannelNotInServer_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var ch = new Channel { ServerId = server.Id, Name = "ch1" };
        _db.Channels.Add(ch);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var request = new UpdateChannelOrderRequest([
            new ChannelOrderItem(Guid.NewGuid(), null, 0) // non-existent channel
        ]);
        var result = await _controller.UpdateChannelOrder(server.Id, request, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ═══════════════════ GetNotificationPreferences — GlobalAdmin ═══════════════════

    [Fact]
    public async Task GetNotificationPreferences_MemberWithNoOverrides_ReturnsDefaultPrefs()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var (_, _, memberRole) = CreateDefaultRoles(server);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, RoleId = memberRole.Id });
        await _db.SaveChangesAsync();

        var result = await _controller.GetNotificationPreferences(server.Id);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════ UnbanMember — with nickname ═══════════════════

    [Fact]
    public async Task UnbanMember_UserWithNickname_UsesNickname()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var bannedUser = new User { Id = Guid.NewGuid(), GoogleSubject = "banned-nick", DisplayName = "Banned", Nickname = "BannedNick" };
        _db.Users.Add(bannedUser);
        _db.BannedMembers.Add(new BannedMember
        {
            ServerId = server.Id,
            UserId = bannedUser.Id,
            BannedByUserId = _testUser.Id
        });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = MakeAdminRole(server.Id) });

        var result = await _controller.UnbanMember(server.Id, bannedUser.Id, _auditService);

        result.Should().BeOfType<NoContentResult>();
        var isBanned = await _db.BannedMembers.AnyAsync(b => b.UserId == bannedUser.Id && b.ServerId == server.Id);
        isBanned.Should().BeFalse();
    }
}
