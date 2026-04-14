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

    public void Dispose() => _db.Dispose();

    // --- GetActive ---

    [Fact]
    public async Task GetActive_NoAnnouncements_ReturnsEmptyList()
    {
        var result = await _controller.GetActive();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as System.Collections.IList;
        list.Should().NotBeNull();
        list!.Count.Should().Be(0);
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
        var list = ok.Value as System.Collections.IList;
        list.Should().NotBeNull();
        list!.Count.Should().Be(1);
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

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as System.Collections.IList;
        list.Should().NotBeNull();
        list!.Count.Should().Be(0);
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
        var list = ok.Value as System.Collections.IList;
        list.Should().NotBeNull();
        list!.Count.Should().Be(0);
    }

    [Fact]
    public async Task GetActive_ActiveWithNullExpiry_IncludesIt()
    {
        _db.SystemAnnouncements.Add(new SystemAnnouncement
        {
            Title = "No expiry",
            Body = "Permanent announcement",
            IsActive = true,
            ExpiresAt = null,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as System.Collections.IList;
        list.Should().NotBeNull();
        list!.Count.Should().Be(1);
    }

    [Fact]
    public async Task GetActive_ActiveWithFutureExpiry_IncludesIt()
    {
        _db.SystemAnnouncements.Add(new SystemAnnouncement
        {
            Title = "Future expiry",
            Body = "Still valid",
            IsActive = true,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as System.Collections.IList;
        list.Should().NotBeNull();
        list!.Count.Should().Be(1);
    }

    [Fact]
    public async Task GetActive_MixedAnnouncements_ReturnsOnlyActiveNonExpired()
    {
        var now = DateTimeOffset.UtcNow;
        _db.SystemAnnouncements.AddRange(
            new SystemAnnouncement
            {
                Title = "Active permanent",
                Body = "Yes",
                IsActive = true,
                ExpiresAt = null,
                CreatedAt = now
            },
            new SystemAnnouncement
            {
                Title = "Active future",
                Body = "Yes",
                IsActive = true,
                ExpiresAt = now.AddDays(3),
                CreatedAt = now.AddMinutes(-5)
            },
            new SystemAnnouncement
            {
                Title = "Active expired",
                Body = "No",
                IsActive = true,
                ExpiresAt = now.AddDays(-1),
                CreatedAt = now.AddDays(-3)
            },
            new SystemAnnouncement
            {
                Title = "Inactive",
                Body = "No",
                IsActive = false,
                CreatedAt = now.AddMinutes(-10)
            },
            new SystemAnnouncement
            {
                Title = "Inactive expired",
                Body = "No",
                IsActive = false,
                ExpiresAt = now.AddDays(-2),
                CreatedAt = now.AddDays(-5)
            }
        );
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as System.Collections.IList;
        list.Should().NotBeNull();
        list!.Count.Should().Be(2);
    }

    [Fact]
    public async Task GetActive_MultipleActive_OrderedByCreatedAtDescending()
    {
        var now = DateTimeOffset.UtcNow;
        _db.SystemAnnouncements.AddRange(
            new SystemAnnouncement
            {
                Title = "Oldest",
                Body = "Created first",
                IsActive = true,
                CreatedAt = now.AddHours(-3)
            },
            new SystemAnnouncement
            {
                Title = "Newest",
                Body = "Created last",
                IsActive = true,
                CreatedAt = now
            },
            new SystemAnnouncement
            {
                Title = "Middle",
                Body = "Created second",
                IsActive = true,
                CreatedAt = now.AddHours(-1)
            }
        );
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as System.Collections.IList;
        list.Should().NotBeNull();
        list!.Count.Should().Be(3);

        // Verify descending order by checking the first item is the newest
        var items = (list as System.Collections.IEnumerable)!.Cast<object>().ToList();
        var firstTitle = items[0].GetType().GetProperty("Title")!.GetValue(items[0]) as string;
        var lastTitle = items[2].GetType().GetProperty("Title")!.GetValue(items[2]) as string;
        firstTitle.Should().Be("Newest");
        lastTitle.Should().Be("Oldest");
    }

    [Fact]
    public async Task GetActive_ReturnsExpectedProjectedFields()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var expiresAt = createdAt.AddDays(7);
        var announcement = new SystemAnnouncement
        {
            Title = "Field Test",
            Body = "Check all fields",
            IsActive = true,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt
        };
        _db.SystemAnnouncements.Add(announcement);
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as System.Collections.IList;
        list.Should().NotBeNull();
        list!.Count.Should().Be(1);

        var item = list[0]!;
        var type = item.GetType();
        type.GetProperty("Id").Should().NotBeNull();
        type.GetProperty("Title").Should().NotBeNull();
        type.GetProperty("Body").Should().NotBeNull();
        type.GetProperty("CreatedAt").Should().NotBeNull();
        type.GetProperty("ExpiresAt").Should().NotBeNull();

        var title = type.GetProperty("Title")!.GetValue(item) as string;
        var body = type.GetProperty("Body")!.GetValue(item) as string;
        title.Should().Be("Field Test");
        body.Should().Be("Check all fields");
    }

    [Fact]
    public async Task GetActive_DoesNotExposeCreatedByUserId()
    {
        var user = new User { Id = Guid.NewGuid(), GoogleSubject = "g-admin", DisplayName = "Admin" };
        _db.Users.Add(user);
        _db.SystemAnnouncements.Add(new SystemAnnouncement
        {
            Title = "Admin Post",
            Body = "Secret admin",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = user.Id
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetActive();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as System.Collections.IList;
        list!.Count.Should().Be(1);

        // The projection should NOT include CreatedByUserId or IsActive
        var item = list[0]!;
        var type = item.GetType();
        type.GetProperty("CreatedByUserId").Should().BeNull();
        type.GetProperty("IsActive").Should().BeNull();
        type.GetProperty("CreatedByUser").Should().BeNull();
    }
}
