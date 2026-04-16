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
    private readonly Channel _testChannel;

    public ReportsControllerTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        _testUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-1", DisplayName = "Reporter" };
        _targetUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-2", DisplayName = "Target", Email = "target@test.com" };
        _testServer = new Server { Id = Guid.NewGuid(), Name = "Test Server", Description = "A test server" };
        _testChannel = new Channel { Id = Guid.NewGuid(), ServerId = _testServer.Id, Name = "general" };

        _db.Users.Add(_testUser);
        _db.Users.Add(_targetUser);
        _db.Servers.Add(_testServer);
        _db.Channels.Add(_testChannel);
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
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateReport_UserReport_SavesReportToDatabase()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.User,
            TargetId = _targetUser.Id.ToString(),
            Reason = "Harassment"
        };

        await _controller.CreateReport(request);

        var report = await _db.Reports.FirstOrDefaultAsync();
        report.Should().NotBeNull();
        report!.ReporterId.Should().Be(_testUser.Id);
        report.ReportType.Should().Be(ReportType.User);
        report.TargetId.Should().Be(_targetUser.Id.ToString());
        report.Reason.Should().Be("Harassment");
        report.Status.Should().Be(ReportStatus.Open);
        report.TargetSnapshot.Should().NotBeNull();
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
    public async Task CreateReport_ServerReport_SavesSnapshotWithNameAndDescription()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.Server,
            TargetId = _testServer.Id.ToString(),
            Reason = "Bad content"
        };

        await _controller.CreateReport(request);

        var report = await _db.Reports.FirstOrDefaultAsync();
        report.Should().NotBeNull();
        report!.TargetSnapshot.Should().Contain("Test Server");
        report.TargetSnapshot.Should().Contain("A test server");
    }

    [Fact]
    public async Task CreateReport_MessageReport_ReturnsOkWithId()
    {
        var msg = new Message
        {
            Id = Guid.NewGuid(),
            ChannelId = _testChannel.Id,
            AuthorUserId = _targetUser.Id,
            AuthorName = "Target",
            Body = "Bad message content"
        };
        _db.Messages.Add(msg);
        await _db.SaveChangesAsync();

        var request = new CreateReportRequest
        {
            ReportType = ReportType.Message,
            TargetId = msg.Id.ToString(),
            Reason = "Offensive"
        };

        var result = await _controller.CreateReport(request);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task CreateReport_MessageReport_SavesSnapshotWithBodyAndAuthor()
    {
        var msg = new Message
        {
            Id = Guid.NewGuid(),
            ChannelId = _testChannel.Id,
            AuthorUserId = _targetUser.Id,
            AuthorName = "Target",
            Body = "Offensive content here"
        };
        _db.Messages.Add(msg);
        await _db.SaveChangesAsync();

        var request = new CreateReportRequest
        {
            ReportType = ReportType.Message,
            TargetId = msg.Id.ToString(),
            Reason = "Offensive"
        };

        await _controller.CreateReport(request);

        var report = await _db.Reports.FirstOrDefaultAsync();
        report.Should().NotBeNull();
        report!.TargetSnapshot.Should().Contain("Offensive content here");
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
    public async Task CreateReport_ServerReport_NonMemberNonAdmin_ReturnsNotFound()
    {
        var nonMember = new User { Id = Guid.NewGuid(), GoogleSubject = "g-3", DisplayName = "Outsider", IsGlobalAdmin = false };
        _db.Users.Add(nonMember);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((nonMember, false));

        var request = new CreateReportRequest
        {
            ReportType = ReportType.Server,
            TargetId = _testServer.Id.ToString(),
            Reason = "Bad"
        };

        var result = await _controller.CreateReport(request);
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task CreateReport_ServerReport_GlobalAdmin_CanReportEvenIfNotMember()
    {
        var admin = new User { Id = Guid.NewGuid(), GoogleSubject = "g-admin", DisplayName = "Admin", IsGlobalAdmin = true };
        _db.Users.Add(admin);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((admin, false));

        var request = new CreateReportRequest
        {
            ReportType = ReportType.Server,
            TargetId = _testServer.Id.ToString(),
            Reason = "Bad"
        };

        var result = await _controller.CreateReport(request);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task CreateReport_MessageReport_NonMemberNonAdmin_ReturnsNotFound()
    {
        var msg = new Message
        {
            Id = Guid.NewGuid(),
            ChannelId = _testChannel.Id,
            AuthorUserId = _targetUser.Id,
            AuthorName = "Target",
            Body = "Some message"
        };
        _db.Messages.Add(msg);
        await _db.SaveChangesAsync();

        var nonMember = new User { Id = Guid.NewGuid(), GoogleSubject = "g-outsider", DisplayName = "Outsider", IsGlobalAdmin = false };
        _db.Users.Add(nonMember);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((nonMember, false));

        var request = new CreateReportRequest
        {
            ReportType = ReportType.Message,
            TargetId = msg.Id.ToString(),
            Reason = "Offensive"
        };

        var result = await _controller.CreateReport(request);
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task CreateReport_MessageReport_GlobalAdmin_CanReportEvenIfNotMember()
    {
        var msg = new Message
        {
            Id = Guid.NewGuid(),
            ChannelId = _testChannel.Id,
            AuthorUserId = _targetUser.Id,
            AuthorName = "Target",
            Body = "Some message"
        };
        _db.Messages.Add(msg);
        await _db.SaveChangesAsync();

        var admin = new User { Id = Guid.NewGuid(), GoogleSubject = "g-admin2", DisplayName = "Admin", IsGlobalAdmin = true };
        _db.Users.Add(admin);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((admin, false));

        var request = new CreateReportRequest
        {
            ReportType = ReportType.Message,
            TargetId = msg.Id.ToString(),
            Reason = "Offensive"
        };

        var result = await _controller.CreateReport(request);
        result.Should().BeOfType<OkObjectResult>();
    }

    public void Dispose() => _db.Dispose();
}
