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

    // --- GetActive ---

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
            Title = "Live Update",
            Body = "Active announcement body",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = ok.Value as IEnumerable<object>;
        items.Should().ContainSingle();
    }

    [Fact]
    public async Task GetActive_ActiveAnnouncementWithNullExpiry_ReturnsIt()
    {
        _db.SystemAnnouncements.Add(new SystemAnnouncement
        {
            Title = "No Expiry",
            Body = "Should appear",
            IsActive = true,
            ExpiresAt = null,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = ok.Value as IEnumerable<object>;
        items.Should().ContainSingle();
    }

    [Fact]
    public async Task GetActive_ActiveAnnouncementWithFutureExpiry_ReturnsIt()
    {
        _db.SystemAnnouncements.Add(new SystemAnnouncement
        {
            Title = "Future Expiry",
            Body = "Still valid",
            IsActive = true,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = ok.Value as IEnumerable<object>;
        items.Should().ContainSingle();
    }

    [Fact]
    public async Task GetActive_ExpiredAnnouncement_ExcludesIt()
    {
        _db.SystemAnnouncements.Add(new SystemAnnouncement
        {
            Title = "Expired",
            Body = "Old news",
            IsActive = true,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = ok.Value as IEnumerable<object>;
        items.Should().BeEmpty();
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

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = ok.Value as IEnumerable<object>;
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActive_InactiveAnnouncementWithFutureExpiry_ExcludesIt()
    {
        _db.SystemAnnouncements.Add(new SystemAnnouncement
        {
            Title = "Inactive Future",
            Body = "Inactive even though not expired",
            IsActive = false,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = ok.Value as IEnumerable<object>;
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActive_MixedAnnouncements_ReturnsOnlyActiveNonExpired()
    {
        _db.SystemAnnouncements.AddRange(
            new SystemAnnouncement
            {
                Title = "Active Valid",
                Body = "Show me",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new SystemAnnouncement
            {
                Title = "Expired",
                Body = "Hide me",
                IsActive = true,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1),
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            },
            new SystemAnnouncement
            {
                Title = "Inactive",
                Body = "Hide me too",
                IsActive = false,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new SystemAnnouncement
            {
                Title = "Active Future",
                Body = "Show me too",
                IsActive = true,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(5),
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
            }
        );
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = ok.Value as IEnumerable<object>;
        items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetActive_MultipleActive_ReturnsOrderedByCreatedAtDescending()
    {
        var older = DateTimeOffset.UtcNow.AddHours(-2);
        var newer = DateTimeOffset.UtcNow.AddHours(-1);
        var newest = DateTimeOffset.UtcNow;

        _db.SystemAnnouncements.AddRange(
            new SystemAnnouncement { Title = "Older", Body = "1", IsActive = true, CreatedAt = older },
            new SystemAnnouncement { Title = "Newest", Body = "3", IsActive = true, CreatedAt = newest },
            new SystemAnnouncement { Title = "Newer", Body = "2", IsActive = true, CreatedAt = newer }
        );
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = (ok.Value as IEnumerable<object>)!.ToList();
        items.Should().HaveCount(3);

        // Verify ordering: newest first
        var titles = items.Select(i => i.GetType().GetProperty("Title")!.GetValue(i)!.ToString()).ToList();
        titles.Should().ContainInOrder("Newest", "Newer", "Older");
    }

    [Fact]
    public async Task GetActive_ProjectsOnlyExpectedFields()
    {
        _db.SystemAnnouncements.Add(new SystemAnnouncement
        {
            Title = "Projected",
            Body = "Check fields",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            CreatedByUserId = Guid.NewGuid()
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = (ok.Value as IEnumerable<object>)!.ToList();
        items.Should().ContainSingle();

        var item = items[0];
        var properties = item.GetType().GetProperties().Select(p => p.Name).ToList();

        // The Select projection should include exactly these fields
        properties.Should().Contain("Id");
        properties.Should().Contain("Title");
        properties.Should().Contain("Body");
        properties.Should().Contain("CreatedAt");
        properties.Should().Contain("ExpiresAt");

        // Should NOT include fields not in the Select projection
        properties.Should().NotContain("IsActive");
        properties.Should().NotContain("CreatedByUserId");
        properties.Should().NotContain("CreatedByUser");
    }

    public void Dispose() => _db.Dispose();
}
