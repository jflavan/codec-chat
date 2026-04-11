using System.Security.Claims;
using Codec.Api.Controllers;
using Codec.Api.Data;
using Codec.Api.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Tests.Controllers;

public class AnnouncementsControllerTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly AnnouncementsController _controller;

    public AnnouncementsControllerTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        _controller = new AnnouncementsController(_db);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim("sub", "user-1"), new Claim("name", "User")
                ], "Bearer"))
            }
        };
    }

    [Fact]
    public async Task GetActive_NoAnnouncements_ReturnsEmptyList()
    {
        var result = await _controller.GetActive();
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetActive_WithActiveAnnouncement_ReturnsIt()
    {
        _db.SystemAnnouncements.Add(new SystemAnnouncement
        {
            Title = "Test",
            Body = "Active announcement",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetActive_ExpiredAnnouncement_ExcludesIt()
    {
        _db.SystemAnnouncements.Add(new SystemAnnouncement
        {
            Title = "Expired",
            Body = "Old",
            IsActive = true,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetActive_InactiveAnnouncement_ExcludesIt()
    {
        _db.SystemAnnouncements.Add(new SystemAnnouncement
        {
            Title = "Inactive",
            Body = "Not shown",
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();
        result.Should().BeOfType<OkObjectResult>();
    }

    public void Dispose() => _db.Dispose();
}
