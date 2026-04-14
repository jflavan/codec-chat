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
    private readonly Message _testMessage;

    public ReportsControllerTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        _testUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-1", DisplayName = "Reporter" };
        _targetUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-2", DisplayName = "Target", Email = "target@test.com" };
        _testServer = new Server { Name = "Test Server", Description = "A test server" };

        _db.Users.AddRange(_testUser, _targetUser);
        _db.Servers.Add(_testServer);
        _db.ServerMembers.Add(new ServerMember { ServerId = _testServer.Id, UserId = _testUser.Id });

        _testChannel = new Channel { Id = Guid.NewGuid(), ServerId = _testServer.Id, Name = "general" };
        _db.Channels.Add(_testChannel);

        _testMessage = new Message
        {
            Id = Guid.NewGuid(),
            ChannelId = _testChannel.Id,
            AuthorUserId = _targetUser.Id,
            AuthorName = "Target",
            Body = "Some offensive content"
        };
        _db.Messages.Add(_testMessage);
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

    public void Dispose() => _db.Dispose();

    // --- CreateReport: Invalid target ID ---

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

    // --- CreateReport: User reports ---

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
    }

    [Fact]
    public async Task CreateReport_UserReport_CapturesSnapshot()
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
        report!.TargetSnapshot.Should().NotBeNullOrEmpty();
        report.TargetSnapshot.Should().Contain("Target");
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

    // --- CreateReport: Server reports ---

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
    public async Task CreateReport_ServerReport_CapturesSnapshot()
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
        report!.TargetSnapshot.Should().NotBeNullOrEmpty();
        report.TargetSnapshot.Should().Contain("Test Server");
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
    public async Task CreateReport_ServerReport_NonMember_ReturnsNotFound()
    {
        var otherServer = new Server { Name = "Other Server" };
        _db.Servers.Add(otherServer);
        await _db.SaveChangesAsync();

        var request = new CreateReportRequest
        {
            ReportType = ReportType.Server,
            TargetId = otherServer.Id.ToString(),
            Reason = "Bad server"
        };

        var result = await _controller.CreateReport(request);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task CreateReport_ServerReport_GlobalAdmin_CanReportWithoutMembership()
    {
        var adminUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-admin", DisplayName = "Admin", IsGlobalAdmin = true };
        _db.Users.Add(adminUser);
        var otherServer = new Server { Name = "Other Server", Description = "Desc" };
        _db.Servers.Add(otherServer);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((adminUser, false));

        var request = new CreateReportRequest
        {
            ReportType = ReportType.Server,
            TargetId = otherServer.Id.ToString(),
            Reason = "Admin report"
        };

        var result = await _controller.CreateReport(request);

        result.Should().BeOfType<OkObjectResult>();
    }

    // --- CreateReport: Message reports ---

    [Fact]
    public async Task CreateReport_MessageReport_ReturnsOkWithId()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.Message,
            TargetId = _testMessage.Id.ToString(),
            Reason = "Offensive"
        };

        var result = await _controller.CreateReport(request);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task CreateReport_MessageReport_CapturesSnapshot()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.Message,
            TargetId = _testMessage.Id.ToString(),
            Reason = "Offensive"
        };

        await _controller.CreateReport(request);

        var report = await _db.Reports.FirstOrDefaultAsync();
        report.Should().NotBeNull();
        report!.TargetSnapshot.Should().NotBeNullOrEmpty();
        report.TargetSnapshot.Should().Contain("Some offensive content");
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
    public async Task CreateReport_MessageReport_NonMember_ReturnsNotFound()
    {
        var nonMemberUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-non", DisplayName = "NonMember" };
        _db.Users.Add(nonMemberUser);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((nonMemberUser, false));

        var request = new CreateReportRequest
        {
            ReportType = ReportType.Message,
            TargetId = _testMessage.Id.ToString(),
            Reason = "Offensive"
        };

        var result = await _controller.CreateReport(request);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task CreateReport_MessageReport_GlobalAdmin_CanReportWithoutMembership()
    {
        var adminUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-admin", DisplayName = "Admin", IsGlobalAdmin = true };
        _db.Users.Add(adminUser);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((adminUser, false));

        var request = new CreateReportRequest
        {
            ReportType = ReportType.Message,
            TargetId = _testMessage.Id.ToString(),
            Reason = "Admin report"
        };

        var result = await _controller.CreateReport(request);

        result.Should().BeOfType<OkObjectResult>();
    }

    // --- CreateReport: General behavior ---

    [Fact]
    public async Task CreateReport_SetsStatusToOpen()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.User,
            TargetId = _targetUser.Id.ToString(),
            Reason = "Test"
        };

        await _controller.CreateReport(request);

        var report = await _db.Reports.FirstOrDefaultAsync();
        report.Should().NotBeNull();
        report!.Status.Should().Be(ReportStatus.Open);
    }

    [Fact]
    public async Task CreateReport_SetsCreatedAtToUtcNow()
    {
        var before = DateTimeOffset.UtcNow;

        var request = new CreateReportRequest
        {
            ReportType = ReportType.User,
            TargetId = _targetUser.Id.ToString(),
            Reason = "Test"
        };

        await _controller.CreateReport(request);

        var after = DateTimeOffset.UtcNow;
        var report = await _db.Reports.FirstOrDefaultAsync();
        report.Should().NotBeNull();
        report!.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public async Task CreateReport_ReturnsUniqueReportId()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.User,
            TargetId = _targetUser.Id.ToString(),
            Reason = "Test"
        };

        var result1 = await _controller.CreateReport(request);
        var result2 = await _controller.CreateReport(request);

        var ok1 = result1.Should().BeOfType<OkObjectResult>().Subject;
        var ok2 = result2.Should().BeOfType<OkObjectResult>().Subject;
        ok1.Value.Should().NotBeEquivalentTo(ok2.Value);
    }
}
