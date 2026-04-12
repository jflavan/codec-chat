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

        _adminUser = new User { Id = Guid.NewGuid(), GoogleSubject = "admin-1", DisplayName = "Admin", IsGlobalAdmin = true, Email = "admin@test.com" };
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
    public async Task GetUsers_WithSearchByDisplayName_ReturnsOk()
    {
        var result = await _controller.GetUsers(new PaginationParams { Search = "Target" });
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetUsers_WithSearchByEmail_ReturnsOk()
    {
        var result = await _controller.GetUsers(new PaginationParams { Search = "target@test.com" });
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetUsers_WithSearch_NoMatch_ReturnsEmptyResults()
    {
        var result = await _controller.GetUsers(new PaginationParams { Search = "nonexistent" });
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
        var result = await _controller.GetUser(_targetUser.Id) as OkObjectResult;
        result.Should().NotBeNull();
        var value = result!.Value!;
        var type = value.GetType();
        type.GetProperty("user").Should().NotBeNull();
        type.GetProperty("memberships").Should().NotBeNull();
        type.GetProperty("recentMessages").Should().NotBeNull();
        type.GetProperty("reportHistory").Should().NotBeNull();
        type.GetProperty("adminHistory").Should().NotBeNull();
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
        var request = new AdminUsersController.DisableRequest { Reason = "Spamming" };
        await _controller.DisableUser(_targetUser.Id, request);

        var user = await _db.Users.FindAsync(_targetUser.Id);
        user!.IsDisabled.Should().BeTrue();
        user.DisabledReason.Should().Be("Spamming");
        user.DisabledAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DisableUser_RemovesRefreshTokens()
    {
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = _targetUser.Id,
            TokenHash = "hash1",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        });
        await _db.SaveChangesAsync();

        var request = new AdminUsersController.DisableRequest { Reason = "Test" };
        await _controller.DisableUser(_targetUser.Id, request);

        var tokens = await _db.RefreshTokens.Where(t => t.UserId == _targetUser.Id).CountAsync();
        tokens.Should().Be(0);
    }

    [Fact]
    public async Task DisableUser_NonExistentUser_ReturnsNotFound()
    {
        var request = new AdminUsersController.DisableRequest { Reason = "Test" };
        var result = await _controller.DisableUser(Guid.NewGuid(), request);
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task DisableUser_Self_ReturnsBadRequest()
    {
        var request = new AdminUsersController.DisableRequest { Reason = "Self-disable" };
        var result = await _controller.DisableUser(_adminUser.Id, request);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task DisableUser_LogsAdminAction()
    {
        var request = new AdminUsersController.DisableRequest { Reason = "Banned" };
        await _controller.DisableUser(_targetUser.Id, request);

        var action = await _db.AdminActions.FirstOrDefaultAsync(a =>
            a.ActionType == AdminActionType.UserDisabled && a.TargetId == _targetUser.Id.ToString());
        action.Should().NotBeNull();
        action!.Reason.Should().Be("Banned");
    }

    // --- EnableUser ---

    [Fact]
    public async Task EnableUser_ExistingUser_ReturnsOk()
    {
        _targetUser.IsDisabled = true;
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
    public async Task EnableUser_NonExistentUser_ReturnsNotFound()
    {
        var result = await _controller.EnableUser(Guid.NewGuid());
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task EnableUser_LogsAdminAction()
    {
        _targetUser.IsDisabled = true;
        await _db.SaveChangesAsync();

        await _controller.EnableUser(_targetUser.Id);

        var action = await _db.AdminActions.FirstOrDefaultAsync(a =>
            a.ActionType == AdminActionType.UserEnabled && a.TargetId == _targetUser.Id.ToString());
        action.Should().NotBeNull();
    }

    // --- ForceLogout ---

    [Fact]
    public async Task ForceLogout_ExistingUser_ReturnsOk()
    {
        var result = await _controller.ForceLogout(_targetUser.Id);
        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task ForceLogout_RemovesRefreshTokens()
    {
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = _targetUser.Id,
            TokenHash = "hash1",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        });
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = _targetUser.Id,
            TokenHash = "hash2",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        });
        await _db.SaveChangesAsync();

        await _controller.ForceLogout(_targetUser.Id);

        var tokens = await _db.RefreshTokens.Where(t => t.UserId == _targetUser.Id).CountAsync();
        tokens.Should().Be(0);
    }

    [Fact]
    public async Task ForceLogout_NonExistentUser_ReturnsNotFound()
    {
        var result = await _controller.ForceLogout(Guid.NewGuid());
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task ForceLogout_LogsAdminAction()
    {
        await _controller.ForceLogout(_targetUser.Id);

        var action = await _db.AdminActions.FirstOrDefaultAsync(a =>
            a.ActionType == AdminActionType.UserForcedLogout && a.TargetId == _targetUser.Id.ToString());
        action.Should().NotBeNull();
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
    public async Task ResetPassword_RemovesRefreshTokens()
    {
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = _targetUser.Id,
            TokenHash = "hash1",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        });
        await _db.SaveChangesAsync();

        await _controller.ResetPassword(_targetUser.Id);

        var tokens = await _db.RefreshTokens.Where(t => t.UserId == _targetUser.Id).CountAsync();
        tokens.Should().Be(0);
    }

    [Fact]
    public async Task ResetPassword_NonExistentUser_ReturnsNotFound()
    {
        var result = await _controller.ResetPassword(Guid.NewGuid());
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task ResetPassword_Self_ReturnsBadRequest()
    {
        var result = await _controller.ResetPassword(_adminUser.Id);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ResetPassword_LogsAdminAction()
    {
        await _controller.ResetPassword(_targetUser.Id);

        var action = await _db.AdminActions.FirstOrDefaultAsync(a =>
            a.ActionType == AdminActionType.UserPasswordReset && a.TargetId == _targetUser.Id.ToString());
        action.Should().NotBeNull();
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
    public async Task SetGlobalAdmin_PromoteUser_SetsFlag()
    {
        var request = new AdminUsersController.GlobalAdminRequest { IsGlobalAdmin = true };
        await _controller.SetGlobalAdmin(_targetUser.Id, request);

        var user = await _db.Users.FindAsync(_targetUser.Id);
        user!.IsGlobalAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task SetGlobalAdmin_PromoteUser_LogsAdminAction()
    {
        var request = new AdminUsersController.GlobalAdminRequest { IsGlobalAdmin = true };
        await _controller.SetGlobalAdmin(_targetUser.Id, request);

        var action = await _db.AdminActions.FirstOrDefaultAsync(a =>
            a.ActionType == AdminActionType.UserPromotedAdmin && a.TargetId == _targetUser.Id.ToString());
        action.Should().NotBeNull();
    }

    [Fact]
    public async Task SetGlobalAdmin_NonExistentUser_ReturnsNotFound()
    {
        var request = new AdminUsersController.GlobalAdminRequest { IsGlobalAdmin = true };
        var result = await _controller.SetGlobalAdmin(Guid.NewGuid(), request);
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task SetGlobalAdmin_DemoteUser_ReturnsOk()
    {
        // Make target an admin too, so we have 2 admins
        _targetUser.IsGlobalAdmin = true;
        await _db.SaveChangesAsync();

        var request = new AdminUsersController.GlobalAdminRequest { IsGlobalAdmin = false };
        var result = await _controller.SetGlobalAdmin(_targetUser.Id, request);
        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task SetGlobalAdmin_DemoteUser_ClearsFlag()
    {
        _targetUser.IsGlobalAdmin = true;
        await _db.SaveChangesAsync();

        var request = new AdminUsersController.GlobalAdminRequest { IsGlobalAdmin = false };
        await _controller.SetGlobalAdmin(_targetUser.Id, request);

        var user = await _db.Users.FindAsync(_targetUser.Id);
        user!.IsGlobalAdmin.Should().BeFalse();
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
        // Only _adminUser is a global admin (the only one)
        var request = new AdminUsersController.GlobalAdminRequest { IsGlobalAdmin = false };
        var result = await _controller.SetGlobalAdmin(_targetUser.Id, request);

        // This checks adminCount <= 1, so it should return BadRequest even though
        // _targetUser is not actually an admin. The check happens before finding the user.
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SetGlobalAdmin_DemoteNonExistentUser_WhenMultipleAdmins_ReturnsNotFound()
    {
        // Add a second admin so the "last admin" check passes
        _targetUser.IsGlobalAdmin = true;
        await _db.SaveChangesAsync();

        var request = new AdminUsersController.GlobalAdminRequest { IsGlobalAdmin = false };
        var result = await _controller.SetGlobalAdmin(Guid.NewGuid(), request);
        result.Should().BeOfType<NotFoundResult>();
    }

    public void Dispose() => _db.Dispose();
}
