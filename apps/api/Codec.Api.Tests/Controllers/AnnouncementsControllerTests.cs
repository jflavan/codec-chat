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
        items.Should().NotBeNull();
        items.Should().BeEmpty();
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
        items.Should().NotBeNull();
        items.Should().HaveCount(1);
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
        items.Should().NotBeNull();
        items.Should().BeEmpty();
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
        items.Should().NotBeNull();
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActive_FutureExpiryAnnouncement_IncludesIt()
    {
        _db.SystemAnnouncements.Add(new SystemAnnouncement
        {
            Id = Guid.NewGuid(),
            Title = "Still Valid",
            Body = "Expires later",
            IsActive = true,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = ok.Value as IEnumerable<object>;
        items.Should().NotBeNull();
        items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetActive_NullExpiresAt_IncludesIt()
    {
        _db.SystemAnnouncements.Add(new SystemAnnouncement
        {
            Id = Guid.NewGuid(),
            Title = "No Expiry",
            Body = "Forever active",
            IsActive = true,
            ExpiresAt = null,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = ok.Value as IEnumerable<object>;
        items.Should().NotBeNull();
        items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetActive_MixedAnnouncements_ReturnsOnlyActiveNonExpired()
    {
        _db.SystemAnnouncements.AddRange(
            new SystemAnnouncement
            {
                Id = Guid.NewGuid(),
                Title = "Active",
                Body = "Show me",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new SystemAnnouncement
            {
                Id = Guid.NewGuid(),
                Title = "Inactive",
                Body = "Hidden",
                IsActive = false,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new SystemAnnouncement
            {
                Id = Guid.NewGuid(),
                Title = "Expired",
                Body = "Too late",
                IsActive = true,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1),
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            }
        );
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = ok.Value as IEnumerable<object>;
        items.Should().NotBeNull();
        items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetActive_MultipleActive_OrderedByCreatedAtDescending()
    {
        var older = DateTimeOffset.UtcNow.AddDays(-2);
        var newer = DateTimeOffset.UtcNow.AddDays(-1);

        _db.SystemAnnouncements.AddRange(
            new SystemAnnouncement
            {
                Id = Guid.NewGuid(),
                Title = "Older",
                Body = "First created",
                IsActive = true,
                CreatedAt = older
            },
            new SystemAnnouncement
            {
                Id = Guid.NewGuid(),
                Title = "Newer",
                Body = "Second created",
                IsActive = true,
                CreatedAt = newer
            }
        );
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = (ok.Value as IEnumerable<object>)!.ToList();
        items.Should().HaveCount(2);

        // Verify descending order: newer item should be first
        var firstTitle = items[0].GetType().GetProperty("Title")!.GetValue(items[0]) as string;
        var secondTitle = items[1].GetType().GetProperty("Title")!.GetValue(items[1]) as string;
        firstTitle.Should().Be("Newer");
        secondTitle.Should().Be("Older");
    }

    public void Dispose() => _db.Dispose();
}
