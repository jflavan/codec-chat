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
        _testServer = new Server { Name = "Test Server", Description = "A test server" };

        _db.Users.Add(_testUser);
        _db.Users.Add(_targetUser);
        _db.Servers.Add(_testServer);
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

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateReport_UserReport_PersistsReportInDatabase()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.User,
            TargetId = _targetUser.Id.ToString(),
            Reason = "Spam"
        };

        await _controller.CreateReport(request);

        var report = await _db.Reports.FirstOrDefaultAsync();
        report.Should().NotBeNull();
        report!.ReporterId.Should().Be(_testUser.Id);
        report.ReportType.Should().Be(ReportType.User);
        report.TargetId.Should().Be(_targetUser.Id.ToString());
        report.Reason.Should().Be("Spam");
        report.Status.Should().Be(ReportStatus.Open);
        report.TargetSnapshot.Should().NotBeNullOrEmpty();
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

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateReport_ServerReport_PersistsSnapshotWithServerInfo()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.Server,
            TargetId = _testServer.Id.ToString(),
            Reason = "Inappropriate content"
        };

        await _controller.CreateReport(request);

        var report = await _db.Reports.FirstOrDefaultAsync();
        report.Should().NotBeNull();
        report!.TargetSnapshot.Should().Contain("Test Server");
    }

    [Fact]
    public async Task CreateReport_MessageReport_ReturnsOkWithId()
    {
        var channel = new Channel { Name = "general", ServerId = _testServer.Id };
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();

        var message = new Message
        {
            Id = Guid.NewGuid(),
            ChannelId = channel.Id,
            AuthorUserId = _targetUser.Id,
            AuthorName = _targetUser.DisplayName,
            Body = "Bad message content"
        };
        _db.Messages.Add(message);
        await _db.SaveChangesAsync();

        var request = new CreateReportRequest
        {
            ReportType = ReportType.Message,
            TargetId = message.Id.ToString(),
            Reason = "Offensive"
        };

        var result = await _controller.CreateReport(request);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task CreateReport_MessageReport_PersistsSnapshotWithMessageBody()
    {
        var channel = new Channel { Name = "general", ServerId = _testServer.Id };
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();

        var message = new Message
        {
            Id = Guid.NewGuid(),
            ChannelId = channel.Id,
            AuthorUserId = _targetUser.Id,
            AuthorName = _targetUser.DisplayName,
            Body = "Bad message content"
        };
        _db.Messages.Add(message);
        await _db.SaveChangesAsync();

        var request = new CreateReportRequest
        {
            ReportType = ReportType.Message,
            TargetId = message.Id.ToString(),
            Reason = "Offensive"
        };

        await _controller.CreateReport(request);

        var report = await _db.Reports.FirstOrDefaultAsync();
        report.Should().NotBeNull();
        report!.TargetSnapshot.Should().Contain("Bad message content");
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

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value.Should().NotBeNull();
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

    [Fact]
    public async Task CreateReport_SetsCreatedAtToUtcNow()
    {
        var before = DateTimeOffset.UtcNow;

        var request = new CreateReportRequest
        {
            ReportType = ReportType.User,
            TargetId = _targetUser.Id.ToString(),
            Reason = "Test timing"
        };

        await _controller.CreateReport(request);

        var after = DateTimeOffset.UtcNow;
        var report = await _db.Reports.FirstOrDefaultAsync();
        report.Should().NotBeNull();
        report!.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    public void Dispose() => _db.Dispose();
}
