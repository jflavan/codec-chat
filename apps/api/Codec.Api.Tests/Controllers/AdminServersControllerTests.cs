using System.Security.Claims;
using Codec.Api.Controllers.Admin;
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

public class AdminServersControllerTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IUserService> _userService = new();
    private readonly Mock<IHubContext<ChatHub>> _hub = new();
    private readonly AdminActionService _adminActions;
    private readonly AdminServersController _controller;
    private readonly User _adminUser;
    private readonly User _ownerUser;
    private readonly Server _testServer;

    public AdminServersControllerTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new CodecDbContext(options);

        _adminUser = new User { Id = Guid.NewGuid(), GoogleSubject = "admin-1", DisplayName = "Admin", IsGlobalAdmin = true };
        _ownerUser = new User { Id = Guid.NewGuid(), GoogleSubject = "owner-1", DisplayName = "Owner" };
        _testServer = new Server { Name = "Test Server" };

        _db.Users.Add(_adminUser);
        _db.Users.Add(_ownerUser);
        _db.Servers.Add(_testServer);
        _db.ServerMembers.Add(new ServerMember { ServerId = _testServer.Id, UserId = _ownerUser.Id });
        _db.ServerRoles.Add(new ServerRoleEntity { ServerId = _testServer.Id, Name = "Owner", Position = 0, IsSystemRole = true, Permissions = 0 });
        _db.SaveChanges();

        _adminActions = new AdminActionService(_db);

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_adminUser, false));

        var clients = new Mock<IHubClients>();
        var clientProxy = new Mock<IClientProxy>();
        _hub.Setup(h => h.Clients).Returns(clients.Object);
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxy.Object);

        _controller = new AdminServersController(_db, _userService.Object, _adminActions, _hub.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim("sub", "admin-1"), new Claim("name", "Admin")
                ], "Bearer"))
            }
        };
    }

    [Fact]
    public async Task GetServers_ReturnsOkWithServers()
    {
        var result = await _controller.GetServers(new PaginationParams());
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetServer_ExistingId_ReturnsOk()
    {
        var result = await _controller.GetServer(_testServer.Id);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetServer_NonExistentId_ReturnsNotFound()
    {
        var result = await _controller.GetServer(Guid.NewGuid());
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task QuarantineServer_ExistingServer_ReturnsOk()
    {
        var request = new AdminServersController.ReasonRequest { Reason = "Violating TOS" };
        var result = await _controller.QuarantineServer(_testServer.Id, request);
        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task QuarantineServer_NonExistentServer_ReturnsNotFound()
    {
        var request = new AdminServersController.ReasonRequest { Reason = "Test" };
        var result = await _controller.QuarantineServer(Guid.NewGuid(), request);
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task UnquarantineServer_ExistingServer_ReturnsOk()
    {
        _testServer.IsQuarantined = true;
        await _db.SaveChangesAsync();

        var result = await _controller.UnquarantineServer(_testServer.Id);
        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task UnquarantineServer_NonExistentServer_ReturnsNotFound()
    {
        var result = await _controller.UnquarantineServer(Guid.NewGuid());
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task DeleteServer_ExistingServer_ReturnsOk()
    {
        var request = new AdminServersController.ReasonRequest { Reason = "Removed" };
        var result = await _controller.DeleteServer(_testServer.Id, request);
        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task DeleteServer_NonExistentServer_ReturnsNotFound()
    {
        var request = new AdminServersController.ReasonRequest { Reason = "Test" };
        var result = await _controller.DeleteServer(Guid.NewGuid(), request);
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task TransferOwnership_ValidTransfer_ReturnsOk()
    {
        var newOwner = new User { Id = Guid.NewGuid(), GoogleSubject = "new-1", DisplayName = "New Owner" };
        _db.Users.Add(newOwner);
        _db.ServerMembers.Add(new ServerMember { ServerId = _testServer.Id, UserId = newOwner.Id });
        await _db.SaveChangesAsync();

        var request = new AdminServersController.TransferRequest { NewOwnerUserId = newOwner.Id };
        var result = await _controller.TransferOwnership(_testServer.Id, request);
        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task TransferOwnership_NonExistentServer_ReturnsNotFound()
    {
        var request = new AdminServersController.TransferRequest { NewOwnerUserId = Guid.NewGuid() };
        var result = await _controller.TransferOwnership(Guid.NewGuid(), request);
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task TransferOwnership_UserNotMember_ReturnsBadRequest()
    {
        var nonMember = new User { Id = Guid.NewGuid(), GoogleSubject = "non-1", DisplayName = "Non Member" };
        _db.Users.Add(nonMember);
        await _db.SaveChangesAsync();

        var request = new AdminServersController.TransferRequest { NewOwnerUserId = nonMember.Id };
        var result = await _controller.TransferOwnership(_testServer.Id, request);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetServers_WithSearch_ReturnsFilteredResults()
    {
        _db.Servers.Add(new Server { Name = "Unique Name XYZ" });
        await _db.SaveChangesAsync();

        var result = await _controller.GetServers(new PaginationParams { Search = "Unique" });
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task QuarantineServer_SetsQuarantineFields()
    {
        var request = new AdminServersController.ReasonRequest { Reason = "Violating TOS" };
        await _controller.QuarantineServer(_testServer.Id, request);

        var updated = await _db.Servers.FindAsync(_testServer.Id);
        updated!.IsQuarantined.Should().BeTrue();
        updated.QuarantinedReason.Should().Be("Violating TOS");
        updated.QuarantinedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UnquarantineServer_ClearsQuarantineFields()
    {
        _testServer.IsQuarantined = true;
        _testServer.QuarantinedReason = "Test";
        _testServer.QuarantinedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        await _controller.UnquarantineServer(_testServer.Id);

        var updated = await _db.Servers.FindAsync(_testServer.Id);
        updated!.IsQuarantined.Should().BeFalse();
        updated.QuarantinedReason.Should().BeNull();
        updated.QuarantinedAt.Should().BeNull();
    }

    [Fact]
    public async Task DeleteServer_RemovesServerFromDb()
    {
        var serverToDelete = new Server { Name = "Delete Me" };
        _db.Servers.Add(serverToDelete);
        await _db.SaveChangesAsync();

        var request = new AdminServersController.ReasonRequest { Reason = "Removed" };
        await _controller.DeleteServer(serverToDelete.Id, request);

        var found = await _db.Servers.FindAsync(serverToDelete.Id);
        found.Should().BeNull();
    }

    [Fact]
    public async Task QuarantineServer_LogsAdminAction()
    {
        var request = new AdminServersController.ReasonRequest { Reason = "Bad content" };
        await _controller.QuarantineServer(_testServer.Id, request);

        var action = await _db.AdminActions.FirstOrDefaultAsync(a => a.TargetId == _testServer.Id.ToString());
        action.Should().NotBeNull();
        action!.ActionType.Should().Be(AdminActionType.ServerQuarantined);
    }

    [Fact]
    public async Task TransferOwnership_NoOwnerRole_ReturnsBadRequest()
    {
        // Create a server with no owner role
        var server = new Server { Name = "No Owner Role Server" };
        _db.Servers.Add(server);
        var member = new User { Id = Guid.NewGuid(), GoogleSubject = "m-1", DisplayName = "Member" };
        _db.Users.Add(member);
        _db.ServerMembers.Add(new ServerMember { ServerId = server.Id, UserId = member.Id });
        await _db.SaveChangesAsync();

        var request = new AdminServersController.TransferRequest { NewOwnerUserId = member.Id };
        var result = await _controller.TransferOwnership(server.Id, request);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    public void Dispose() => _db.Dispose();
}
