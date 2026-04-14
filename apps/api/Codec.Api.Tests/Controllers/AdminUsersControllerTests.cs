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

    // --- GetUsers ---

    [Fact]
    public async Task GetUsers_ReturnsOkWithUsers()
    {
        var result = await _controller.GetUsers(new PaginationParams());
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetUsers_WithSearch_ReturnsFilteredResults()
    {
        var result = await _controller.GetUsers(new PaginationParams { Search = "Target" });
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetUsers_SearchByEmail_ReturnsFilteredResults()
    {
        var result = await _controller.GetUsers(new PaginationParams { Search = "target@test" });
        result.Should().BeOfType<OkObjectResult>();
    }

    // --- GetUser ---

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
    public async Task GetUser_ReturnsDetailedUserInfo()
    {
        var result = await _controller.GetUser(_targetUser.Id);
        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.Value.Should().NotBeNull();
    }

    // --- DisableUser ---

    [Fact]
    public async Task DisableUser_ExistingUser_ReturnsOk()
    {
        var request = new AdminUsersController.DisableRequest { Reason = "Test disable" };
        var result = await _controller.DisableUser(_targetUser.Id, request);
        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task DisableUser_SetsDisabledFields()
    {
        var request = new AdminUsersController.DisableRequest { Reason = "Abuse" };
        await _controller.DisableUser(_targetUser.Id, request);

        var user = await _db.Users.FindAsync(_targetUser.Id);
        user!.IsDisabled.Should().BeTrue();
        user.DisabledReason.Should().Be("Abuse");
        user.DisabledAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DisableUser_RevokesRefreshTokens()
    {
        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = _targetUser.Id,
            TokenHash = "hash1", ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        });
        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = _targetUser.Id,
            TokenHash = "hash2", ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        });
        await _db.SaveChangesAsync();

        var request = new AdminUsersController.DisableRequest { Reason = "Banned" };
        await _controller.DisableUser(_targetUser.Id, request);

        var remainingTokens = await _db.RefreshTokens.Where(t => t.UserId == _targetUser.Id).CountAsync();
        remainingTokens.Should().Be(0);
    }

    [Fact]
    public async Task DisableUser_LogsAdminAction()
    {
        var request = new AdminUsersController.DisableRequest { Reason = "Policy violation" };
        await _controller.DisableUser(_targetUser.Id, request);

        var action = await _db.AdminActions.FirstOrDefaultAsync(a => a.ActionType == AdminActionType.UserDisabled);
        action.Should().NotBeNull();
        action!.Reason.Should().Be("Policy violation");
    }

    [Fact]
    public async Task DisableUser_Self_ReturnsBadRequest()
    {
        var request = new AdminUsersController.DisableRequest { Reason = "Self disable" };
        var result = await _controller.DisableUser(_adminUser.Id, request);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task DisableUser_NonExistentUser_ReturnsNotFound()
    {
        var request = new AdminUsersController.DisableRequest { Reason = "Test" };
        var result = await _controller.DisableUser(Guid.NewGuid(), request);
        result.Should().BeOfType<NotFoundResult>();
    }

    // --- EnableUser ---

    [Fact]
    public async Task EnableUser_ExistingUser_ReturnsOk()
    {
        _targetUser.IsDisabled = true;
        _targetUser.DisabledReason = "Was disabled";
        _targetUser.DisabledAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        var result = await _controller.EnableUser(_targetUser.Id);
        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task EnableUser_ClearsDisabledFields()
    {
        _targetUser.IsDisabled = true;
        _targetUser.DisabledReason = "Was disabled";
        _targetUser.DisabledAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        await _controller.EnableUser(_targetUser.Id);

        var user = await _db.Users.FindAsync(_targetUser.Id);
        user!.IsDisabled.Should().BeFalse();
        user.DisabledReason.Should().BeNull();
        user.DisabledAt.Should().BeNull();
    }

    [Fact]
    public async Task EnableUser_LogsAdminAction()
    {
        _targetUser.IsDisabled = true;
        await _db.SaveChangesAsync();

        await _controller.EnableUser(_targetUser.Id);

        var action = await _db.AdminActions.FirstOrDefaultAsync(a => a.ActionType == AdminActionType.UserEnabled);
        action.Should().NotBeNull();
    }

    [Fact]
    public async Task EnableUser_NonExistentUser_ReturnsNotFound()
    {
        var result = await _controller.EnableUser(Guid.NewGuid());
        result.Should().BeOfType<NotFoundResult>();
    }

    // --- ForceLogout ---

    [Fact]
    public async Task ForceLogout_ExistingUser_ReturnsOk()
    {
        var result = await _controller.ForceLogout(_targetUser.Id);
        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task ForceLogout_RevokesAllRefreshTokens()
    {
        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = _targetUser.Id,
            TokenHash = "hash1", ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        });
        await _db.SaveChangesAsync();

        await _controller.ForceLogout(_targetUser.Id);

        var remainingTokens = await _db.RefreshTokens.Where(t => t.UserId == _targetUser.Id).CountAsync();
        remainingTokens.Should().Be(0);
    }

    [Fact]
    public async Task ForceLogout_LogsAdminAction()
    {
        await _controller.ForceLogout(_targetUser.Id);

        var action = await _db.AdminActions.FirstOrDefaultAsync(a => a.ActionType == AdminActionType.UserForcedLogout);
        action.Should().NotBeNull();
    }

    [Fact]
    public async Task ForceLogout_NonExistentUser_ReturnsNotFound()
    {
        var result = await _controller.ForceLogout(Guid.NewGuid());
        result.Should().BeOfType<NotFoundResult>();
    }

    // --- ResetPassword ---

    [Fact]
    public async Task ResetPassword_ExistingUser_ReturnsOk()
    {
        var result = await _controller.ResetPassword(_targetUser.Id);
        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task ResetPassword_ClearsPasswordHash()
    {
        _targetUser.PasswordHash = "some-hash";
        await _db.SaveChangesAsync();

        await _controller.ResetPassword(_targetUser.Id);

        var user = await _db.Users.FindAsync(_targetUser.Id);
        user!.PasswordHash.Should().BeNull();
    }

    [Fact]
    public async Task ResetPassword_RevokesRefreshTokens()
    {
        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = _targetUser.Id,
            TokenHash = "hash1", ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        });
        await _db.SaveChangesAsync();

        await _controller.ResetPassword(_targetUser.Id);

        var remainingTokens = await _db.RefreshTokens.Where(t => t.UserId == _targetUser.Id).CountAsync();
        remainingTokens.Should().Be(0);
    }

    [Fact]
    public async Task ResetPassword_LogsAdminAction()
    {
        await _controller.ResetPassword(_targetUser.Id);

        var action = await _db.AdminActions.FirstOrDefaultAsync(a => a.ActionType == AdminActionType.UserPasswordReset);
        action.Should().NotBeNull();
    }

    [Fact]
    public async Task ResetPassword_Self_ReturnsBadRequest()
    {
        var result = await _controller.ResetPassword(_adminUser.Id);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ResetPassword_NonExistentUser_ReturnsNotFound()
    {
        var result = await _controller.ResetPassword(Guid.NewGuid());
        result.Should().BeOfType<NotFoundResult>();
    }

    // --- SetGlobalAdmin ---

    [Fact]
    public async Task SetGlobalAdmin_PromoteUser_ReturnsOk()
    {
        var request = new AdminUsersController.GlobalAdminRequest { IsGlobalAdmin = true };
        var result = await _controller.SetGlobalAdmin(_targetUser.Id, request);
        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task SetGlobalAdmin_PromoteUser_SetsIsGlobalAdmin()
    {
        var request = new AdminUsersController.GlobalAdminRequest { IsGlobalAdmin = true };
        await _controller.SetGlobalAdmin(_targetUser.Id, request);

        var user = await _db.Users.FindAsync(_targetUser.Id);
        user!.IsGlobalAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task SetGlobalAdmin_PromoteUser_LogsAction()
    {
        var request = new AdminUsersController.GlobalAdminRequest { IsGlobalAdmin = true };
        await _controller.SetGlobalAdmin(_targetUser.Id, request);

        var action = await _db.AdminActions.FirstOrDefaultAsync(a => a.ActionType == AdminActionType.UserPromotedAdmin);
        action.Should().NotBeNull();
    }

    [Fact]
    public async Task SetGlobalAdmin_DemoteUser_ReturnsOk()
    {
        // Make target an admin too, so there are 2 admins
        _targetUser.IsGlobalAdmin = true;
        await _db.SaveChangesAsync();

        var request = new AdminUsersController.GlobalAdminRequest { IsGlobalAdmin = false };
        var result = await _controller.SetGlobalAdmin(_targetUser.Id, request);
        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task SetGlobalAdmin_DemoteUser_SetsIsGlobalAdminFalse()
    {
        _targetUser.IsGlobalAdmin = true;
        await _db.SaveChangesAsync();

        var request = new AdminUsersController.GlobalAdminRequest { IsGlobalAdmin = false };
        await _controller.SetGlobalAdmin(_targetUser.Id, request);

        var user = await _db.Users.FindAsync(_targetUser.Id);
        user!.IsGlobalAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task SetGlobalAdmin_DemoteUser_LogsAction()
    {
        _targetUser.IsGlobalAdmin = true;
        await _db.SaveChangesAsync();

        var request = new AdminUsersController.GlobalAdminRequest { IsGlobalAdmin = false };
        await _controller.SetGlobalAdmin(_targetUser.Id, request);

        var action = await _db.AdminActions.FirstOrDefaultAsync(a => a.ActionType == AdminActionType.UserDemotedAdmin);
        action.Should().NotBeNull();
    }

    [Fact]
    public async Task SetGlobalAdmin_DemoteSelf_ReturnsBadRequest()
    {
        var request = new AdminUsersController.GlobalAdminRequest { IsGlobalAdmin = false };
        var result = await _controller.SetGlobalAdmin(_adminUser.Id, request);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SetGlobalAdmin_DemoteLastAdmin_ReturnsBadRequest()
    {
        // _adminUser is the only admin. Try to demote _targetUser who isn't admin yet,
        // but that won't trigger the "last admin" check because we need to demote an actual admin.
        // So let's make target an admin, then demote admin user (which is blocked by self-check).
        // Instead: only _adminUser is admin, try to demote a third user who IS admin when they're the last.
        // Actually, the check is: if adminCount <= 1, block. So with only _adminUser being admin,
        // and we try to demote some other user who IS admin but that still leaves only _adminUser...
        // The real scenario: there's only 1 admin total. We can't demote them.
        // But demoting self is blocked first. So we need a user who IS the only other admin.
        // Let me make _adminUser the only admin and try to demote them via a different approach.

        // Setup: only _targetUser is admin (not _adminUser for this test scenario)
        _adminUser.IsGlobalAdmin = false;
        _targetUser.IsGlobalAdmin = true;
        await _db.SaveChangesAsync();

        // The admin performing the action is still _adminUser (via mock),
        // but the request is to demote _targetUser who is the last admin.
        var request = new AdminUsersController.GlobalAdminRequest { IsGlobalAdmin = false };
        var result = await _controller.SetGlobalAdmin(_targetUser.Id, request);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SetGlobalAdmin_PromoteNonExistentUser_ReturnsNotFound()
    {
        var request = new AdminUsersController.GlobalAdminRequest { IsGlobalAdmin = true };
        var result = await _controller.SetGlobalAdmin(Guid.NewGuid(), request);
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task SetGlobalAdmin_DemoteNonExistentUser_ReturnsNotFound()
    {
        // Need at least 2 admins so it doesn't hit the "last admin" check
        _targetUser.IsGlobalAdmin = true;
        await _db.SaveChangesAsync();

        var request = new AdminUsersController.GlobalAdminRequest { IsGlobalAdmin = false };
        var result = await _controller.SetGlobalAdmin(Guid.NewGuid(), request);
        result.Should().BeOfType<NotFoundResult>();
    }

    public void Dispose() => _db.Dispose();
}
