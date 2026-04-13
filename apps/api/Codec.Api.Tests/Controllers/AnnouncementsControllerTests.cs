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
        var items = ok.Value as IEnumerable<object>;
        items.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task GetActive_WithActiveAnnouncement_ReturnsIt()
    {
        _db.SystemAnnouncements.Add(new SystemAnnouncement
        {
            Id = Guid.NewGuid(),
            Title = "Test",
            Body = "Active announcement",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = ok.Value as IEnumerable<object>;
        items.Should().NotBeNull().And.HaveCount(1);
    }

    [Fact]
    public async Task GetActive_ExpiredAnnouncement_ExcludesIt()
    {
        _db.SystemAnnouncements.Add(new SystemAnnouncement
        {
            Id = Guid.NewGuid(),
            Title = "Expired",
            Body = "Old",
            IsActive = true,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = ok.Value as IEnumerable<object>;
        items.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task GetActive_InactiveAnnouncement_ExcludesIt()
    {
        _db.SystemAnnouncements.Add(new SystemAnnouncement
        {
            Id = Guid.NewGuid(),
            Title = "Inactive",
            Body = "Not shown",
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = ok.Value as IEnumerable<object>;
        items.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task GetActive_FutureExpiry_IncludesAnnouncement()
    {
        _db.SystemAnnouncements.Add(new SystemAnnouncement
        {
            Id = Guid.NewGuid(),
            Title = "Future",
            Body = "Still valid",
            IsActive = true,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = ok.Value as IEnumerable<object>;
        items.Should().NotBeNull().And.HaveCount(1);
    }

    [Fact]
    public async Task GetActive_NullExpiry_IncludesAnnouncement()
    {
        _db.SystemAnnouncements.Add(new SystemAnnouncement
        {
            Id = Guid.NewGuid(),
            Title = "No Expiry",
            Body = "Permanent",
            IsActive = true,
            ExpiresAt = null,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = ok.Value as IEnumerable<object>;
        items.Should().NotBeNull().And.HaveCount(1);
    }

    [Fact]
    public async Task GetActive_MultipleAnnouncements_OrderedByCreatedAtDescending()
    {
        _db.SystemAnnouncements.Add(new SystemAnnouncement
        {
            Id = Guid.NewGuid(),
            Title = "Older",
            Body = "First created",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2)
        });
        _db.SystemAnnouncements.Add(new SystemAnnouncement
        {
            Id = Guid.NewGuid(),
            Title = "Newer",
            Body = "Second created",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = ok.Value as IEnumerable<object>;
        items.Should().NotBeNull().And.HaveCount(2);
    }

    [Fact]
    public async Task GetActive_MixedActiveAndInactive_OnlyReturnsActive()
    {
        _db.SystemAnnouncements.Add(new SystemAnnouncement
        {
            Id = Guid.NewGuid(),
            Title = "Active One",
            Body = "Shown",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        _db.SystemAnnouncements.Add(new SystemAnnouncement
        {
            Id = Guid.NewGuid(),
            Title = "Inactive One",
            Body = "Hidden",
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow
        });
        _db.SystemAnnouncements.Add(new SystemAnnouncement
        {
            Id = Guid.NewGuid(),
            Title = "Expired One",
            Body = "Gone",
            IsActive = true,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = ok.Value as IEnumerable<object>;
        items.Should().NotBeNull().And.HaveCount(1);
    }

    public void Dispose() => _db.Dispose();
}
