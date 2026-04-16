using System.Security.Claims;
using System.Text.Json;
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
        // Arrange — add some test data
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

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.SerializeToDocument(okResult.Value);
        json.RootElement.GetProperty("users").GetProperty("total").GetInt32().Should().Be(0);
        json.RootElement.GetProperty("servers").GetProperty("total").GetInt32().Should().Be(0);
        json.RootElement.GetProperty("messages").GetProperty("last24h").GetInt32().Should().Be(0);
        json.RootElement.GetProperty("openReports").GetInt32().Should().Be(0);
        json.RootElement.GetProperty("live").GetProperty("activeConnections").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task GetStats_WithRecentData_ReturnsCorrectCounts()
    {
        _db.Users.Add(new User { Id = Guid.NewGuid(), GoogleSubject = "g-3", DisplayName = "Recent" });
        _db.Servers.Add(new Server { Name = "Recent Server" });
        _db.Reports.Add(new Report
        {
            Id = Guid.NewGuid(),
            ReporterId = Guid.NewGuid(),
            ReportType = ReportType.User,
            TargetId = "target",
            Reason = "test",
            Status = ReportStatus.Open
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetStats();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.SerializeToDocument(okResult.Value);
        json.RootElement.GetProperty("users").GetProperty("total").GetInt32().Should().Be(1);
        json.RootElement.GetProperty("servers").GetProperty("total").GetInt32().Should().Be(1);
        json.RootElement.GetProperty("openReports").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GetStats_LiveMetrics_ReturnsConnectionAndMessageData()
    {
        var result = await _controller.GetStats();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.SerializeToDocument(okResult.Value);
        var live = json.RootElement.GetProperty("live");
        live.GetProperty("activeConnections").GetInt32().Should().Be(0);
        live.GetProperty("messagesPerMinute").GetDouble().Should().Be(0.0);
    }

    public void Dispose() => _db.Dispose();
}
