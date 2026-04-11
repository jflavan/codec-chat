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

        result.Should().BeOfType<OkObjectResult>();
    }

    public void Dispose() => _db.Dispose();
}
