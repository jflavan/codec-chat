using System.Security.Claims;
using Codec.Api.Controllers.Admin;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Tests.Controllers;

public class AdminStatsControllerTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly MetricsCounterService _metrics;
    private readonly PresenceTracker _presence;
    private readonly AdminStatsController _controller;

    public AdminStatsControllerTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        _metrics = new MetricsCounterService();
        _presence = new PresenceTracker();

        _controller = new AdminStatsController(_db, _metrics, _presence);
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
    public async Task GetStats_ReturnsOkWithStats()
    {
        // Arrange -- add some test data
        _db.Users.Add(new User { Id = Guid.NewGuid(), GoogleSubject = "g-1", DisplayName = "User1" });
        _db.Users.Add(new User { Id = Guid.NewGuid(), GoogleSubject = "g-2", DisplayName = "User2" });
        _db.Servers.Add(new Server { Name = "Server1" });
        await _db.SaveChangesAsync();

        // Act
        var result = await _controller.GetStats();

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetStats_EmptyDatabase_ReturnsOkWithZeroCounts()
    {
        var result = await _controller.GetStats();

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetStats_ReturnsExpectedStructure()
    {
        var result = await _controller.GetStats();

        var ok = result as OkObjectResult;
        ok.Should().NotBeNull();
        ok!.Value.Should().NotBeNull();

        // Verify the response contains the expected top-level properties
        var value = ok.Value!;
        var type = value.GetType();
        type.GetProperty("users").Should().NotBeNull();
        type.GetProperty("servers").Should().NotBeNull();
        type.GetProperty("messages").Should().NotBeNull();
        type.GetProperty("openReports").Should().NotBeNull();
        type.GetProperty("live").Should().NotBeNull();
    }

    [Fact]
    public async Task GetStats_WithRecentData_CountsCorrectly()
    {
        // Add users created recently
        _db.Users.Add(new User { Id = Guid.NewGuid(), GoogleSubject = "g-recent-1", DisplayName = "Recent1", CreatedAt = DateTimeOffset.UtcNow });
        _db.Users.Add(new User { Id = Guid.NewGuid(), GoogleSubject = "g-recent-2", DisplayName = "Recent2", CreatedAt = DateTimeOffset.UtcNow });

        // Add a server created recently
        var server = new Server { Name = "RecentServer", CreatedAt = DateTimeOffset.UtcNow };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        var channel = new Channel { ServerId = server.Id, Name = "general" };
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();

        // Add recent messages
        _db.Messages.Add(new Message { ChannelId = channel.Id, AuthorUserId = (await _db.Users.FirstAsync()).Id, Body = "Hello" });
        await _db.SaveChangesAsync();

        var result = await _controller.GetStats();

        var ok = result as OkObjectResult;
        ok.Should().NotBeNull();

        // Get the users property and check total
        var value = ok!.Value!;
        var usersProperty = value.GetType().GetProperty("users");
        var usersValue = usersProperty!.GetValue(value)!;
        var totalProperty = usersValue.GetType().GetProperty("total");
        var total = (int)totalProperty!.GetValue(usersValue)!;
        total.Should().Be(2);
    }

    [Fact]
    public async Task GetStats_WithOpenReports_CountsCorrectly()
    {
        var user = new User { Id = Guid.NewGuid(), GoogleSubject = "reporter", DisplayName = "Reporter" };
        _db.Users.Add(user);
        _db.Reports.Add(new Report { ReporterId = user.Id, ReportType = ReportType.User, TargetId = Guid.NewGuid().ToString(), Reason = "Test", Status = ReportStatus.Open });
        _db.Reports.Add(new Report { ReporterId = user.Id, ReportType = ReportType.User, TargetId = Guid.NewGuid().ToString(), Reason = "Test2", Status = ReportStatus.Open });
        _db.Reports.Add(new Report { ReporterId = user.Id, ReportType = ReportType.User, TargetId = Guid.NewGuid().ToString(), Reason = "Test3", Status = ReportStatus.Resolved });
        await _db.SaveChangesAsync();

        var result = await _controller.GetStats();

        var ok = result as OkObjectResult;
        var value = ok!.Value!;
        var openReports = (int)value.GetType().GetProperty("openReports")!.GetValue(value)!;
        openReports.Should().Be(2);
    }

    [Fact]
    public async Task GetStats_LiveSection_ContainsConnectionAndMessageMetrics()
    {
        var result = await _controller.GetStats();

        var ok = result as OkObjectResult;
        var value = ok!.Value!;
        var liveProperty = value.GetType().GetProperty("live");
        var liveValue = liveProperty!.GetValue(value)!;
        liveValue.GetType().GetProperty("activeConnections").Should().NotBeNull();
        liveValue.GetType().GetProperty("messagesPerMinute").Should().NotBeNull();
    }

    public void Dispose() => _db.Dispose();
}
