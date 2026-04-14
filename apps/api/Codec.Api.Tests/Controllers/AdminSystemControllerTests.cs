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

    // --- GetAdminActions ---

    [Fact]
    public async Task GetAdminActions_ReturnsOk()
    {
        var result = await _controller.GetAdminActions(new PaginationParams());
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetAdminActions_FilterByActionType_ReturnsOk()
    {
        await _adminActions.LogAsync(_adminUser.Id, AdminActionType.UserDisabled, "User", Guid.NewGuid().ToString(), "test");
        await _adminActions.LogAsync(_adminUser.Id, AdminActionType.ServerQuarantined, "Server", Guid.NewGuid().ToString(), "test");

        var result = await _controller.GetAdminActions(new PaginationParams(), actionType: AdminActionType.UserDisabled);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetAdminActions_EmptyResults_ReturnsOk()
    {
        var result = await _controller.GetAdminActions(new PaginationParams(), actionType: AdminActionType.ServerDeleted);
        result.Should().BeOfType<OkObjectResult>();
    }

    // --- GetConnections ---

    [Fact]
    public void GetConnections_ReturnsOk()
    {
        var result = _controller.GetConnections();
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void GetConnections_ReturnsActiveUserCount()
    {
        var result = _controller.GetConnections();
        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.Value.Should().NotBeNull();
    }

    // --- GetAnnouncements ---

    [Fact]
    public async Task GetAnnouncements_ReturnsOk()
    {
        var result = await _controller.GetAnnouncements();
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetAnnouncements_ReturnsAllAnnouncements()
    {
        _db.SystemAnnouncements.Add(new SystemAnnouncement { Id = Guid.NewGuid(), Title = "A1", Body = "Body1", CreatedByUserId = _adminUser.Id, IsActive = true });
        _db.SystemAnnouncements.Add(new SystemAnnouncement { Id = Guid.NewGuid(), Title = "A2", Body = "Body2", CreatedByUserId = _adminUser.Id, IsActive = false });
        await _db.SaveChangesAsync();

        var result = await _controller.GetAnnouncements();
        result.Should().BeOfType<OkObjectResult>();
    }

    // --- CreateAnnouncement ---

    [Fact]
    public async Task CreateAnnouncement_ValidRequest_ReturnsOkWithId()
    {
        var request = new AdminSystemController.CreateAnnouncementRequest
        {
            Title = "Test Announcement",
            Body = "This is a test"
        };

        var result = await _controller.CreateAnnouncement(request);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task CreateAnnouncement_SavesAnnouncementToDatabase()
    {
        var request = new AdminSystemController.CreateAnnouncementRequest
        {
            Title = "Saved Announcement",
            Body = "Should be persisted"
        };

        await _controller.CreateAnnouncement(request);

        var announcement = await _db.SystemAnnouncements.FirstOrDefaultAsync(a => a.Title == "Saved Announcement");
        announcement.Should().NotBeNull();
        announcement!.Body.Should().Be("Should be persisted");
        announcement.IsActive.Should().BeTrue();
        announcement.CreatedByUserId.Should().Be(_adminUser.Id);
    }

    [Fact]
    public async Task CreateAnnouncement_WithExpiry_SetsExpiresAt()
    {
        var expiry = DateTimeOffset.UtcNow.AddDays(7);
        var request = new AdminSystemController.CreateAnnouncementRequest
        {
            Title = "Expiring",
            Body = "Will expire",
            ExpiresAt = expiry
        };

        await _controller.CreateAnnouncement(request);

        var announcement = await _db.SystemAnnouncements.FirstOrDefaultAsync(a => a.Title == "Expiring");
        announcement.Should().NotBeNull();
        announcement!.ExpiresAt.Should().BeCloseTo(expiry, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CreateAnnouncement_LogsAdminAction()
    {
        var request = new AdminSystemController.CreateAnnouncementRequest
        {
            Title = "Logged",
            Body = "Should log action"
        };

        await _controller.CreateAnnouncement(request);

        var action = await _db.AdminActions.FirstOrDefaultAsync(a => a.ActionType == AdminActionType.AnnouncementCreated);
        action.Should().NotBeNull();
        action!.ActorUserId.Should().Be(_adminUser.Id);
    }

    // --- UpdateAnnouncement ---

    [Fact]
    public async Task UpdateAnnouncement_ExistingAnnouncement_ReturnsOk()
    {
        var announcement = new SystemAnnouncement
        {
            Id = Guid.NewGuid(), Title = "Original", Body = "Original body",
            CreatedByUserId = _adminUser.Id, IsActive = true
        };
        _db.SystemAnnouncements.Add(announcement);
        await _db.SaveChangesAsync();

        var request = new AdminSystemController.UpdateAnnouncementRequest { Title = "Updated Title" };
        var result = await _controller.UpdateAnnouncement(announcement.Id, request);
        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task UpdateAnnouncement_UpdatesTitle()
    {
        var announcement = new SystemAnnouncement
        {
            Id = Guid.NewGuid(), Title = "Old Title", Body = "Body",
            CreatedByUserId = _adminUser.Id, IsActive = true
        };
        _db.SystemAnnouncements.Add(announcement);
        await _db.SaveChangesAsync();

        await _controller.UpdateAnnouncement(announcement.Id, new AdminSystemController.UpdateAnnouncementRequest { Title = "New Title" });

        var updated = await _db.SystemAnnouncements.FindAsync(announcement.Id);
        updated!.Title.Should().Be("New Title");
        updated.Body.Should().Be("Body"); // body unchanged
    }

    [Fact]
    public async Task UpdateAnnouncement_UpdatesBody()
    {
        var announcement = new SystemAnnouncement
        {
            Id = Guid.NewGuid(), Title = "Title", Body = "Old Body",
            CreatedByUserId = _adminUser.Id, IsActive = true
        };
        _db.SystemAnnouncements.Add(announcement);
        await _db.SaveChangesAsync();

        await _controller.UpdateAnnouncement(announcement.Id, new AdminSystemController.UpdateAnnouncementRequest { Body = "New Body" });

        var updated = await _db.SystemAnnouncements.FindAsync(announcement.Id);
        updated!.Body.Should().Be("New Body");
    }

    [Fact]
    public async Task UpdateAnnouncement_SetsExpiresAt()
    {
        var announcement = new SystemAnnouncement
        {
            Id = Guid.NewGuid(), Title = "Title", Body = "Body",
            CreatedByUserId = _adminUser.Id, IsActive = true
        };
        _db.SystemAnnouncements.Add(announcement);
        await _db.SaveChangesAsync();

        var expiry = DateTimeOffset.UtcNow.AddDays(3);
        await _controller.UpdateAnnouncement(announcement.Id, new AdminSystemController.UpdateAnnouncementRequest { ExpiresAt = expiry });

        var updated = await _db.SystemAnnouncements.FindAsync(announcement.Id);
        updated!.ExpiresAt.Should().BeCloseTo(expiry, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task UpdateAnnouncement_ClearExpiresAt_RemovesExpiry()
    {
        var announcement = new SystemAnnouncement
        {
            Id = Guid.NewGuid(), Title = "Title", Body = "Body",
            CreatedByUserId = _adminUser.Id, IsActive = true,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(5)
        };
        _db.SystemAnnouncements.Add(announcement);
        await _db.SaveChangesAsync();

        await _controller.UpdateAnnouncement(announcement.Id, new AdminSystemController.UpdateAnnouncementRequest { ClearExpiresAt = true });

        var updated = await _db.SystemAnnouncements.FindAsync(announcement.Id);
        updated!.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAnnouncement_SetIsActive_UpdatesActiveStatus()
    {
        var announcement = new SystemAnnouncement
        {
            Id = Guid.NewGuid(), Title = "Title", Body = "Body",
            CreatedByUserId = _adminUser.Id, IsActive = true
        };
        _db.SystemAnnouncements.Add(announcement);
        await _db.SaveChangesAsync();

        await _controller.UpdateAnnouncement(announcement.Id, new AdminSystemController.UpdateAnnouncementRequest { IsActive = false });

        var updated = await _db.SystemAnnouncements.FindAsync(announcement.Id);
        updated!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAnnouncement_LogsAdminAction()
    {
        var announcement = new SystemAnnouncement
        {
            Id = Guid.NewGuid(), Title = "Title", Body = "Body",
            CreatedByUserId = _adminUser.Id, IsActive = true
        };
        _db.SystemAnnouncements.Add(announcement);
        await _db.SaveChangesAsync();

        await _controller.UpdateAnnouncement(announcement.Id, new AdminSystemController.UpdateAnnouncementRequest { Title = "Updated" });

        var action = await _db.AdminActions.FirstOrDefaultAsync(a => a.ActionType == AdminActionType.AnnouncementUpdated);
        action.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateAnnouncement_NonExistent_ReturnsNotFound()
    {
        var request = new AdminSystemController.UpdateAnnouncementRequest { Title = "Updated" };
        var result = await _controller.UpdateAnnouncement(Guid.NewGuid(), request);
        result.Should().BeOfType<NotFoundResult>();
    }

    // --- DeleteAnnouncement ---

    [Fact]
    public async Task DeleteAnnouncement_ExistingAnnouncement_ReturnsOk()
    {
        var announcement = new SystemAnnouncement
        {
            Id = Guid.NewGuid(), Title = "To Delete", Body = "Body",
            CreatedByUserId = _adminUser.Id, IsActive = true
        };
        _db.SystemAnnouncements.Add(announcement);
        await _db.SaveChangesAsync();

        var result = await _controller.DeleteAnnouncement(announcement.Id);
        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task DeleteAnnouncement_RemovesFromDatabase()
    {
        var announcement = new SystemAnnouncement
        {
            Id = Guid.NewGuid(), Title = "Will Be Removed", Body = "Body",
            CreatedByUserId = _adminUser.Id, IsActive = true
        };
        _db.SystemAnnouncements.Add(announcement);
        await _db.SaveChangesAsync();

        await _controller.DeleteAnnouncement(announcement.Id);

        var deleted = await _db.SystemAnnouncements.FindAsync(announcement.Id);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAnnouncement_LogsAdminAction()
    {
        var announcement = new SystemAnnouncement
        {
            Id = Guid.NewGuid(), Title = "Log Delete", Body = "Body",
            CreatedByUserId = _adminUser.Id, IsActive = true
        };
        _db.SystemAnnouncements.Add(announcement);
        await _db.SaveChangesAsync();

        await _controller.DeleteAnnouncement(announcement.Id);

        var action = await _db.AdminActions.FirstOrDefaultAsync(a => a.ActionType == AdminActionType.AnnouncementDeleted);
        action.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteAnnouncement_NonExistent_ReturnsNotFound()
    {
        var result = await _controller.DeleteAnnouncement(Guid.NewGuid());
        result.Should().BeOfType<NotFoundResult>();
    }

    public void Dispose() => _db.Dispose();
}
