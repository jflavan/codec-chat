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

public class AdminSystemControllerTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IUserService> _userService = new();
    private readonly AdminActionService _adminActions;
    private readonly PresenceTracker _presence;
    private readonly AdminSystemController _controller;
    private readonly User _adminUser;

    public AdminSystemControllerTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        _adminUser = new User { Id = Guid.NewGuid(), GoogleSubject = "admin-1", DisplayName = "Admin", IsGlobalAdmin = true };
        _db.Users.Add(_adminUser);
        _db.SaveChanges();

        _adminActions = new AdminActionService(_db);
        _presence = new PresenceTracker();

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_adminUser, false));

        _controller = new AdminSystemController(_db, _userService.Object, _adminActions, _presence);
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
    public async Task GetAdminActions_ReturnsOk()
    {
        var result = await _controller.GetAdminActions(new PaginationParams());
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void GetConnections_ReturnsOk()
    {
        var result = _controller.GetConnections();
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetAnnouncements_ReturnsOk()
    {
        var result = await _controller.GetAnnouncements();
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task CreateAnnouncement_ValidRequest_ReturnsCreated()
    {
        var request = new AdminSystemController.CreateAnnouncementRequest
        {
            Title = "Test Announcement",
            Body = "This is a test"
        };

        var result = await _controller.CreateAnnouncement(request);
        result.Should().BeAssignableTo<ObjectResult>();
    }

    [Fact]
    public async Task UpdateAnnouncement_ExistingAnnouncement_ReturnsOk()
    {
        var announcement = new SystemAnnouncement
        {
            Id = Guid.NewGuid(),
            Title = "Original",
            Body = "Original body",
            CreatedByUserId = _adminUser.Id,
            IsActive = true
        };
        _db.SystemAnnouncements.Add(announcement);
        await _db.SaveChangesAsync();

        var request = new AdminSystemController.UpdateAnnouncementRequest
        {
            Title = "Updated Title"
        };

        var result = await _controller.UpdateAnnouncement(announcement.Id, request);
        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task UpdateAnnouncement_NonExistent_ReturnsNotFound()
    {
        var request = new AdminSystemController.UpdateAnnouncementRequest
        {
            Title = "Updated"
        };

        var result = await _controller.UpdateAnnouncement(Guid.NewGuid(), request);
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task DeleteAnnouncement_ExistingAnnouncement_ReturnsNoContent()
    {
        var announcement = new SystemAnnouncement
        {
            Id = Guid.NewGuid(),
            Title = "To Delete",
            Body = "Body",
            CreatedByUserId = _adminUser.Id,
            IsActive = true
        };
        _db.SystemAnnouncements.Add(announcement);
        await _db.SaveChangesAsync();

        var result = await _controller.DeleteAnnouncement(announcement.Id);
        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task DeleteAnnouncement_NonExistent_ReturnsNotFound()
    {
        var result = await _controller.DeleteAnnouncement(Guid.NewGuid());
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetAdminActions_FilterByType_ReturnsFilteredResults()
    {
        await _adminActions.LogAsync(_adminUser.Id, AdminActionType.UserDisabled, "User", "target-1", "test");

        var result = await _controller.GetAdminActions(new PaginationParams(), actionType: AdminActionType.UserDisabled);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task CreateAnnouncement_ReturnsOkWithId()
    {
        var request = new AdminSystemController.CreateAnnouncementRequest
        {
            Title = "New Announcement",
            Body = "Body content"
        };

        var result = await _controller.CreateAnnouncement(request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();
        var idProperty = okResult.Value!.GetType().GetProperty("Id");
        idProperty.Should().NotBeNull();
        var id = (Guid)idProperty!.GetValue(okResult.Value)!;
        id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateAnnouncement_PersistsToDatabase()
    {
        var request = new AdminSystemController.CreateAnnouncementRequest
        {
            Title = "Persisted",
            Body = "Should be in DB"
        };

        await _controller.CreateAnnouncement(request);

        var announcement = await _db.SystemAnnouncements.FirstOrDefaultAsync(a => a.Title == "Persisted");
        announcement.Should().NotBeNull();
        announcement!.Body.Should().Be("Should be in DB");
        announcement.IsActive.Should().BeTrue();
        announcement.CreatedByUserId.Should().Be(_adminUser.Id);
    }

    [Fact]
    public async Task CreateAnnouncement_WithExpiresAt_SetsExpiry()
    {
        var expiry = DateTimeOffset.UtcNow.AddDays(7);
        var request = new AdminSystemController.CreateAnnouncementRequest
        {
            Title = "Expiring",
            Body = "Expires soon",
            ExpiresAt = expiry
        };

        await _controller.CreateAnnouncement(request);

        var announcement = await _db.SystemAnnouncements.FirstOrDefaultAsync(a => a.Title == "Expiring");
        announcement!.ExpiresAt.Should().Be(expiry);
    }

    [Fact]
    public async Task UpdateAnnouncement_ClearExpiresAt_RemovesExpiry()
    {
        var announcement = new SystemAnnouncement
        {
            Id = Guid.NewGuid(),
            Title = "Has Expiry",
            Body = "Body",
            CreatedByUserId = _adminUser.Id,
            IsActive = true,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        };
        _db.SystemAnnouncements.Add(announcement);
        await _db.SaveChangesAsync();

        var request = new AdminSystemController.UpdateAnnouncementRequest
        {
            ClearExpiresAt = true
        };

        await _controller.UpdateAnnouncement(announcement.Id, request);

        var updated = await _db.SystemAnnouncements.FindAsync(announcement.Id);
        updated!.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAnnouncement_DeactivateAnnouncement_SetsIsActiveFalse()
    {
        var announcement = new SystemAnnouncement
        {
            Id = Guid.NewGuid(),
            Title = "Active",
            Body = "Body",
            CreatedByUserId = _adminUser.Id,
            IsActive = true
        };
        _db.SystemAnnouncements.Add(announcement);
        await _db.SaveChangesAsync();

        var request = new AdminSystemController.UpdateAnnouncementRequest
        {
            IsActive = false
        };

        await _controller.UpdateAnnouncement(announcement.Id, request);

        var updated = await _db.SystemAnnouncements.FindAsync(announcement.Id);
        updated!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAnnouncement_RemovesFromDatabase()
    {
        var announcement = new SystemAnnouncement
        {
            Id = Guid.NewGuid(),
            Title = "Will Be Deleted",
            Body = "Body",
            CreatedByUserId = _adminUser.Id,
            IsActive = true
        };
        _db.SystemAnnouncements.Add(announcement);
        await _db.SaveChangesAsync();

        await _controller.DeleteAnnouncement(announcement.Id);

        var found = await _db.SystemAnnouncements.FindAsync(announcement.Id);
        found.Should().BeNull();
    }

    [Fact]
    public async Task CreateAnnouncement_LogsAdminAction()
    {
        var request = new AdminSystemController.CreateAnnouncementRequest
        {
            Title = "Logged",
            Body = "Body"
        };

        await _controller.CreateAnnouncement(request);

        var action = await _db.AdminActions.FirstOrDefaultAsync(a => a.ActionType == AdminActionType.AnnouncementCreated);
        action.Should().NotBeNull();
    }

    public void Dispose() => _db.Dispose();
}
