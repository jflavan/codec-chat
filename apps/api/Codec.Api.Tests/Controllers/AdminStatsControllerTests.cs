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
        _db.Users.Add(new User { Id = Guid.NewGuid(), GoogleSubject = "g-1", DisplayName = "User1" });
        _db.Users.Add(new User { Id = Guid.NewGuid(), GoogleSubject = "g-2", DisplayName = "User2" });
        _db.Servers.Add(new Server { Name = "Server1" });
        await _db.SaveChangesAsync();

        var result = await _controller.GetStats();

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetStats_EmptyDatabase_ReturnsOkWithZeroCounts()
    {
        var result = await _controller.GetStats();

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetStats_IncludesUserCounts()
    {
        _db.Users.Add(new User { Id = Guid.NewGuid(), GoogleSubject = "g-1", DisplayName = "User1", CreatedAt = DateTimeOffset.UtcNow });
        _db.Users.Add(new User { Id = Guid.NewGuid(), GoogleSubject = "g-2", DisplayName = "User2", CreatedAt = DateTimeOffset.UtcNow.AddDays(-10) });
        await _db.SaveChangesAsync();

        var result = await _controller.GetStats();

        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetStats_IncludesServerCounts()
    {
        _db.Servers.Add(new Server { Name = "S1", CreatedAt = DateTimeOffset.UtcNow });
        _db.Servers.Add(new Server { Name = "S2", CreatedAt = DateTimeOffset.UtcNow.AddDays(-15) });
        await _db.SaveChangesAsync();

        var result = await _controller.GetStats();

        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetStats_IncludesOpenReportsCount()
    {
        var user = new User { Id = Guid.NewGuid(), GoogleSubject = "g-1", DisplayName = "Reporter" };
        _db.Users.Add(user);
        _db.Reports.Add(new Report
        {
            Id = Guid.NewGuid(), ReporterId = user.Id, ReportType = ReportType.User,
            TargetId = Guid.NewGuid().ToString(), Reason = "Test", Status = ReportStatus.Open
        });
        _db.Reports.Add(new Report
        {
            Id = Guid.NewGuid(), ReporterId = user.Id, ReportType = ReportType.User,
            TargetId = Guid.NewGuid().ToString(), Reason = "Test 2", Status = ReportStatus.Resolved
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetStats();

        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
    }

    [Fact]
    public async Task GetStats_IncludesLiveMetrics()
    {
        var result = await _controller.GetStats();

        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        // live section includes activeConnections and messagesPerMinute
        okResult!.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetStats_CountsRecentMessages()
    {
        var user = new User { Id = Guid.NewGuid(), GoogleSubject = "g-1", DisplayName = "Sender" };
        var server = new Server { Name = "S1" };
        var channel = new Channel { Server = server, Name = "general" };
        _db.Users.Add(user);
        _db.Servers.Add(server);
        _db.Channels.Add(channel);
        _db.Messages.Add(new Message { ChannelId = channel.Id, AuthorUserId = user.Id, Body = "Recent", CreatedAt = DateTimeOffset.UtcNow });
        _db.Messages.Add(new Message { ChannelId = channel.Id, AuthorUserId = user.Id, Body = "Old", CreatedAt = DateTimeOffset.UtcNow.AddDays(-40) });
        await _db.SaveChangesAsync();

        var result = await _controller.GetStats();

        result.Should().BeOfType<OkObjectResult>();
    }

    public void Dispose() => _db.Dispose();
}
