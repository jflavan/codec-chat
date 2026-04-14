using System.Security.Claims;
using Codec.Api.Controllers;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Codec.Api.Tests.Controllers;

public class ReportsControllerTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IUserService> _userService = new();
    private readonly ReportsController _controller;
    private readonly User _testUser;
    private readonly User _targetUser;
    private readonly Server _testServer;

    public ReportsControllerTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        _testUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-1", DisplayName = "Reporter" };
        _targetUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-2", DisplayName = "Target", Email = "target@test.com" };
        _testServer = new Server { Name = "Test Server" };

        _db.Users.Add(_testUser);
        _db.Users.Add(_targetUser);
        _db.Servers.Add(_testServer);
        _db.ServerMembers.Add(new ServerMember { ServerId = _testServer.Id, UserId = _testUser.Id });
        _db.SaveChanges();

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_testUser, false));

        _controller = new ReportsController(_db, _userService.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim("sub", "g-1"), new Claim("name", "Reporter")
                ], "Bearer"))
            }
        };
    }

    [Fact]
    public async Task CreateReport_UserReport_ReturnsOkWithId()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.User,
            TargetId = _targetUser.Id.ToString(),
            Reason = "Spam"
        };

        var result = await _controller.CreateReport(request);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task CreateReport_ServerReport_ReturnsOkWithId()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.Server,
            TargetId = _testServer.Id.ToString(),
            Reason = "Inappropriate content"
        };

        var result = await _controller.CreateReport(request);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task CreateReport_InvalidTargetId_ReturnsBadRequest()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.User,
            TargetId = "not-a-guid",
            Reason = "Spam"
        };

        var result = await _controller.CreateReport(request);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateReport_NonExistentUser_ReturnsNotFound()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.User,
            TargetId = Guid.NewGuid().ToString(),
            Reason = "Spam"
        };

        var result = await _controller.CreateReport(request);
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task CreateReport_NonExistentServer_ReturnsNotFound()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.Server,
            TargetId = Guid.NewGuid().ToString(),
            Reason = "Bad"
        };

        var result = await _controller.CreateReport(request);
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task CreateReport_NonExistentMessage_ReturnsNotFound()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.Message,
            TargetId = Guid.NewGuid().ToString(),
            Reason = "Offensive"
        };

        var result = await _controller.CreateReport(request);
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    public void Dispose() => _db.Dispose();
}
