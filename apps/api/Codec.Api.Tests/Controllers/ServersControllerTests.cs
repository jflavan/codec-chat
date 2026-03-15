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

        var clients = new Mock<IHubClients>();
        var clientProxy = new Mock<IClientProxy>();
        _hub.Setup(h => h.Clients).Returns(clients.Object);
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxy.Object);
        clients.Setup(c => c.All).Returns(clientProxy.Object);

        _controller = new ServersController(_db, _userService.Object, _avatarService.Object, _emojiService.Object, _hub.Object, _httpFactory.Object, _config.Object, _messageCache);
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
        _avatarService.Setup(a => a.ResolveUrl(It.IsAny<string?>())).Returns((string?)null);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetMyServers_ReturnsUserServers()
    {
        var server = new Server { Name = "Test Server" };
        _db.Servers.Add(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, Role = ServerRole.Member });
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
        _db.ServerMembers.Add(new ServerMember { Server = s1, UserId = _testUser.Id, Role = ServerRole.Member });
        _db.ServerMembers.Add(new ServerMember { Server = s2, UserId = _testUser.Id, Role = ServerRole.Member });
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
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = ServerRole.Admin });

        var result = await _controller.UpdateServer(server.Id, new UpdateServerRequest("New"));
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMembers_ReturnsList()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, Role = ServerRole.Owner });
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

        var result = await _controller.CreateChannel(server.Id, new CreateChannelRequest("new-channel"));
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

        var result = await _controller.CreateInvite(server.Id, new CreateInviteRequest());
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

        var result = await _controller.RevokeInvite(server.Id, invite.Id);
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

        var result = await _controller.RevokeInvite(server.Id, Guid.NewGuid());
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // --- KickMember ---

    [Fact]
    public async Task KickMember_Self_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, Role = ServerRole.Admin });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = ServerRole.Admin });

        var result = await _controller.KickMember(server.Id, _testUser.Id);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task KickMember_TargetNotMember_Returns404()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = ServerRole.Admin });

        var result = await _controller.KickMember(server.Id, Guid.NewGuid());
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task KickMember_CannotKickOwner_ReturnsBadRequest()
    {
        var server = new Server { Name = "S" };
        var owner = new User { GoogleSubject = "g-owner", DisplayName = "Owner" };
        _db.Users.Add(owner);
        _db.Servers.Add(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, Role = ServerRole.Admin });
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = owner.Id, Role = ServerRole.Owner });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = ServerRole.Admin });

        var result = await _controller.KickMember(server.Id, owner.Id);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task KickMember_ValidKick_ReturnsNoContent()
    {
        var server = new Server { Name = "S" };
        var target = new User { GoogleSubject = "g-target", DisplayName = "Target" };
        _db.Users.Add(target);
        _db.Servers.Add(server);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, Role = ServerRole.Admin });
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = target.Id, Role = ServerRole.Member });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureAdminAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id, Role = ServerRole.Admin });

        var result = await _controller.KickMember(server.Id, target.Id);
        result.Should().BeOfType<NoContentResult>();
    }

    // --- UpdateMemberRole ---

    [Fact]
    public async Task UpdateMemberRole_InvalidRole_ReturnsBadRequest()
    {
        var result = await _controller.UpdateMemberRole(Guid.NewGuid(), Guid.NewGuid(), new UpdateMemberRoleRequest { Role = "Owner" });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateMemberRole_InvalidRoleName_ReturnsBadRequest()
    {
        var result = await _controller.UpdateMemberRole(Guid.NewGuid(), Guid.NewGuid(), new UpdateMemberRoleRequest { Role = "SuperAdmin" });
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

        var result = await _controller.UpdateChannel(server.Id, channel.Id, new UpdateChannelRequest("new-name"));
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

        var result = await _controller.UpdateChannel(server.Id, Guid.NewGuid(), new UpdateChannelRequest("renamed"));
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
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, Role = ServerRole.Member });
        _db.ServerInvites.Add(new ServerInvite { Server = server, Code = "rejoiner", CreatedByUserId = _testUser.Id });
        await _db.SaveChangesAsync();

        var result = await _controller.JoinViaInvite("rejoiner");
        result.Should().BeOfType<OkObjectResult>();
    }
}
