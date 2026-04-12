using System.Security.Claims;
using System.Text.Json;
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
        _testServer = new Server { Id = Guid.NewGuid(), Name = "Test Server", Description = "A test server" };
        _testChannel = new Channel { Id = Guid.NewGuid(), ServerId = _testServer.Id, Name = "general" };
        _testMessage = new Message
        {
            Id = Guid.NewGuid(),
            ChannelId = _testChannel.Id,
            AuthorUserId = _targetUser.Id,
            AuthorName = _targetUser.DisplayName,
            Body = "This is a test message"
        };

        _db.Users.Add(_testUser);
        _db.Users.Add(_targetUser);
        _db.Servers.Add(_testServer);
        _db.Channels.Add(_testChannel);
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

    // --- Happy path tests ---

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
    public async Task CreateReport_MessageReport_ReturnsOkWithId()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.Message,
            TargetId = _testMessage.Id.ToString(),
            Reason = "Offensive content"
        };

        var result = await _controller.CreateReport(request);

        result.Should().BeOfType<OkObjectResult>();
    }

    // --- Persistence verification tests ---

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

        var saved = await _db.Reports.FirstOrDefaultAsync(r => r.Reason == "Harassment");
        saved.Should().NotBeNull();
        saved!.ReporterId.Should().Be(_testUser.Id);
        saved.ReportType.Should().Be(ReportType.User);
        saved.TargetId.Should().Be(_targetUser.Id.ToString());
        saved.Status.Should().Be(ReportStatus.Open);
    }

    [Fact]
    public async Task CreateReport_ServerReport_SavesReportToDatabase()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.Server,
            TargetId = _testServer.Id.ToString(),
            Reason = "TOS violation"
        };

        await _controller.CreateReport(request);

        var saved = await _db.Reports.FirstOrDefaultAsync(r => r.Reason == "TOS violation");
        saved.Should().NotBeNull();
        saved!.ReporterId.Should().Be(_testUser.Id);
        saved.ReportType.Should().Be(ReportType.Server);
        saved.TargetId.Should().Be(_testServer.Id.ToString());
        saved.Status.Should().Be(ReportStatus.Open);
    }

    [Fact]
    public async Task CreateReport_MessageReport_SavesReportToDatabase()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.Message,
            TargetId = _testMessage.Id.ToString(),
            Reason = "Hate speech"
        };

        await _controller.CreateReport(request);

        var saved = await _db.Reports.FirstOrDefaultAsync(r => r.Reason == "Hate speech");
        saved.Should().NotBeNull();
        saved!.ReporterId.Should().Be(_testUser.Id);
        saved.ReportType.Should().Be(ReportType.Message);
        saved.TargetId.Should().Be(_testMessage.Id.ToString());
        saved.Status.Should().Be(ReportStatus.Open);
    }

    // --- Snapshot verification tests ---

    [Fact]
    public async Task CreateReport_UserReport_CapturesUserSnapshot()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.User,
            TargetId = _targetUser.Id.ToString(),
            Reason = "Snapshot test"
        };

        await _controller.CreateReport(request);

        var saved = await _db.Reports.FirstOrDefaultAsync(r => r.Reason == "Snapshot test");
        saved.Should().NotBeNull();
        saved!.TargetSnapshot.Should().NotBeNullOrEmpty();

        using var doc = JsonDocument.Parse(saved.TargetSnapshot!);
        var root = doc.RootElement;
        root.GetProperty("DisplayName").GetString().Should().Be("Target");
        root.GetProperty("Email").GetString().Should().Be("target@test.com");
    }

    [Fact]
    public async Task CreateReport_ServerReport_CapturesServerSnapshot()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.Server,
            TargetId = _testServer.Id.ToString(),
            Reason = "Server snapshot test"
        };

        await _controller.CreateReport(request);

        var saved = await _db.Reports.FirstOrDefaultAsync(r => r.Reason == "Server snapshot test");
        saved.Should().NotBeNull();
        saved!.TargetSnapshot.Should().NotBeNullOrEmpty();

        using var doc = JsonDocument.Parse(saved.TargetSnapshot!);
        var root = doc.RootElement;
        root.GetProperty("Name").GetString().Should().Be("Test Server");
        root.GetProperty("Description").GetString().Should().Be("A test server");
    }

    [Fact]
    public async Task CreateReport_MessageReport_CapturesMessageSnapshot()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.Message,
            TargetId = _testMessage.Id.ToString(),
            Reason = "Message snapshot test"
        };

        await _controller.CreateReport(request);

        var saved = await _db.Reports.FirstOrDefaultAsync(r => r.Reason == "Message snapshot test");
        saved.Should().NotBeNull();
        saved!.TargetSnapshot.Should().NotBeNullOrEmpty();

        using var doc = JsonDocument.Parse(saved.TargetSnapshot!);
        var root = doc.RootElement;
        root.GetProperty("Body").GetString().Should().Be("This is a test message");
        root.GetProperty("Author").GetString().Should().Be("Target");
    }

    // --- Returned ID verification ---

    [Fact]
    public async Task CreateReport_ReturnsIdMatchingSavedReport()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.User,
            TargetId = _targetUser.Id.ToString(),
            Reason = "ID check"
        };

        var result = await _controller.CreateReport(request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var id = ok.Value!.GetType().GetProperty("Id")!.GetValue(ok.Value) as Guid?;
        id.Should().NotBeNull();

        var saved = await _db.Reports.FindAsync(id!.Value);
        saved.Should().NotBeNull();
        saved!.Reason.Should().Be("ID check");
    }

    // --- Error path tests ---

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
    public async Task CreateReport_EmptyTargetId_ReturnsBadRequest()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.User,
            TargetId = "",
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

    [Fact]
    public async Task CreateReport_InvalidTargetId_DoesNotSaveToDatabase()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.User,
            TargetId = "not-a-guid",
            Reason = "Should not be saved"
        };

        await _controller.CreateReport(request);

        var count = await _db.Reports.CountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task CreateReport_NonExistentTarget_DoesNotSaveToDatabase()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.User,
            TargetId = Guid.NewGuid().ToString(),
            Reason = "Should not be saved either"
        };

        await _controller.CreateReport(request);

        var count = await _db.Reports.CountAsync();
        count.Should().Be(0);
    }

    // --- Error message verification ---

    [Fact]
    public async Task CreateReport_InvalidTargetId_ReturnsCorrectErrorMessage()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.User,
            TargetId = "bad-id",
            Reason = "Spam"
        };

        var result = await _controller.CreateReport(request);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = bad.Value!.GetType().GetProperty("error")!.GetValue(bad.Value) as string;
        error.Should().Be("Invalid target ID.");
    }

    [Fact]
    public async Task CreateReport_NonExistentMessage_ReturnsCorrectErrorMessage()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.Message,
            TargetId = Guid.NewGuid().ToString(),
            Reason = "Offensive"
        };

        var result = await _controller.CreateReport(request);

        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var error = notFound.Value!.GetType().GetProperty("error")!.GetValue(notFound.Value) as string;
        error.Should().Be("Target message not found.");
    }

    [Fact]
    public async Task CreateReport_NonExistentUser_ReturnsCorrectErrorMessage()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.User,
            TargetId = Guid.NewGuid().ToString(),
            Reason = "Spam"
        };

        var result = await _controller.CreateReport(request);

        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var error = notFound.Value!.GetType().GetProperty("error")!.GetValue(notFound.Value) as string;
        error.Should().Be("Target user not found.");
    }

    [Fact]
    public async Task CreateReport_NonExistentServer_ReturnsCorrectErrorMessage()
    {
        var request = new CreateReportRequest
        {
            ReportType = ReportType.Server,
            TargetId = Guid.NewGuid().ToString(),
            Reason = "Bad"
        };

        var result = await _controller.CreateReport(request);

        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var error = notFound.Value!.GetType().GetProperty("error")!.GetValue(notFound.Value) as string;
        error.Should().Be("Target server not found.");
    }

    // --- CreatedAt verification ---

    [Fact]
    public async Task CreateReport_SetsCreatedAtToCurrentUtcTime()
    {
        var before = DateTimeOffset.UtcNow;

        var request = new CreateReportRequest
        {
            ReportType = ReportType.User,
            TargetId = _targetUser.Id.ToString(),
            Reason = "Timing test"
        };

        await _controller.CreateReport(request);

        var after = DateTimeOffset.UtcNow;
        var saved = await _db.Reports.FirstOrDefaultAsync(r => r.Reason == "Timing test");
        saved.Should().NotBeNull();
        saved!.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    public void Dispose() => _db.Dispose();
}
