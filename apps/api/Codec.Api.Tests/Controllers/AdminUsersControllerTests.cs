using System.Security.Claims;
using Codec.Api.Controllers.Admin;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Codec.Api.Tests.Controllers;

public class AdminUsersControllerTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IUserService> _userService = new();
    private readonly AdminActionService _adminActions;
    private readonly AdminUsersController _controller;
    private readonly User _adminUser;
    private readonly User _targetUser;

    public AdminUsersControllerTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new CodecDbContext(options);

        _adminUser = new User { Id = Guid.NewGuid(), GoogleSubject = "admin-1", DisplayName = "Admin", IsGlobalAdmin = true };
        _targetUser = new User { Id = Guid.NewGuid(), GoogleSubject = "user-1", DisplayName = "Target User", Email = "target@test.com" };

        _db.Users.Add(_adminUser);
        _db.Users.Add(_targetUser);
        _db.SaveChanges();

        _adminActions = new AdminActionService(_db);

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_adminUser, false));

        _controller = new AdminUsersController(_db, _userService.Object, _adminActions);
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
    public async Task GetUsers_ReturnsOkWithUsers()
    {
        var result = await _controller.GetUsers(new PaginationParams());
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetUser_ExistingId_ReturnsOk()
    {
        var result = await _controller.GetUser(_targetUser.Id);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetUser_NonExistentId_ReturnsNotFound()
    {
        var result = await _controller.GetUser(Guid.NewGuid());
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task DisableUser_ExistingUser_ReturnsOk()
    {
        var request = new AdminUsersController.DisableRequest { Reason = "Test disable" };
        var result = await _controller.DisableUser(_targetUser.Id, request);
        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task DisableUser_NonExistentUser_ReturnsNotFound()
    {
        var request = new AdminUsersController.DisableRequest { Reason = "Test" };
        var result = await _controller.DisableUser(Guid.NewGuid(), request);
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task EnableUser_ExistingUser_ReturnsOk()
    {
        _targetUser.IsDisabled = true;
        await _db.SaveChangesAsync();

        var result = await _controller.EnableUser(_targetUser.Id);
        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task EnableUser_NonExistentUser_ReturnsNotFound()
    {
        var result = await _controller.EnableUser(Guid.NewGuid());
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task ForceLogout_ExistingUser_ReturnsOk()
    {
        var result = await _controller.ForceLogout(_targetUser.Id);
        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task ForceLogout_NonExistentUser_ReturnsNotFound()
    {
        var result = await _controller.ForceLogout(Guid.NewGuid());
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task ResetPassword_ExistingUser_ReturnsOk()
    {
        var result = await _controller.ResetPassword(_targetUser.Id);
        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task ResetPassword_NonExistentUser_ReturnsNotFound()
    {
        var result = await _controller.ResetPassword(Guid.NewGuid());
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task SetGlobalAdmin_ExistingUser_ReturnsOk()
    {
        var request = new AdminUsersController.GlobalAdminRequest { IsGlobalAdmin = true };
        var result = await _controller.SetGlobalAdmin(_targetUser.Id, request);
        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task SetGlobalAdmin_NonExistentUser_ReturnsNotFound()
    {
        var request = new AdminUsersController.GlobalAdminRequest { IsGlobalAdmin = true };
        var result = await _controller.SetGlobalAdmin(Guid.NewGuid(), request);
        result.Should().BeOfType<NotFoundResult>();
    }

    public void Dispose() => _db.Dispose();
}
