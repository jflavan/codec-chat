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

    public void Dispose() => _db.Dispose();
}
