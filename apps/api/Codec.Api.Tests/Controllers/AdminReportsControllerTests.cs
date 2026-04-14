using System.Security.Claims;
using Codec.Api.Controllers.Admin;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Codec.Api.Tests.Controllers;

public class AdminReportsControllerTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IUserService> _userService = new();
    private readonly AdminActionService _adminActions;
    private readonly AdminReportsController _controller;
    private readonly User _adminUser;
    private readonly User _reportedUser;
    private readonly Report _testReport;

    public AdminReportsControllerTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        _adminUser = new User { Id = Guid.NewGuid(), GoogleSubject = "admin-1", DisplayName = "Admin" };
        _reportedUser = new User { Id = Guid.NewGuid(), GoogleSubject = "user-1", DisplayName = "Reported User" };

        _db.Users.Add(_adminUser);
        _db.Users.Add(_reportedUser);

        _testReport = new Report
        {
            Id = Guid.NewGuid(),
            ReporterId = _adminUser.Id,
            ReportType = ReportType.User,
            TargetId = _reportedUser.Id.ToString(),
            Reason = "Test report",
            Status = ReportStatus.Open
        };
        _db.Reports.Add(_testReport);
        _db.SaveChanges();

        _adminActions = new AdminActionService(_db);

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_adminUser, false));

        _controller = new AdminReportsController(_db, _userService.Object, _adminActions);
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
    public async Task GetReports_ReturnsOkWithReports()
    {
        var result = await _controller.GetReports(new PaginationParams());

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetReports_FilterByStatus_ReturnsFilteredResults()
    {
        var result = await _controller.GetReports(new PaginationParams(), status: ReportStatus.Open);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetReports_FilterByType_ReturnsFilteredResults()
    {
        var result = await _controller.GetReports(new PaginationParams(), type: ReportType.User);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetReports_FilterByNonMatchingStatus_ReturnsEmptyResults()
    {
        var result = await _controller.GetReports(new PaginationParams(), status: ReportStatus.Resolved);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetReports_FilterByBothStatusAndType_ReturnsOk()
    {
        var result = await _controller.GetReports(new PaginationParams(), status: ReportStatus.Open, type: ReportType.User);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetReport_ExistingId_ReturnsOk()
    {
        var result = await _controller.GetReport(_testReport.Id);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetReport_NonExistentId_ReturnsNotFound()
    {
        var result = await _controller.GetReport(Guid.NewGuid());

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task UpdateReport_ExistingReport_ReturnsOk()
    {
        var request = new AdminReportsController.UpdateReportRequest
        {
            Status = ReportStatus.Resolved,
            Resolution = "Resolved by admin"
        };

        var result = await _controller.UpdateReport(_testReport.Id, request);

        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task UpdateReport_Resolved_SetsResolvedFields()
    {
        var request = new AdminReportsController.UpdateReportRequest
        {
            Status = ReportStatus.Resolved,
            Resolution = "Issue addressed"
        };

        await _controller.UpdateReport(_testReport.Id, request);

        var report = await _db.Reports.FindAsync(_testReport.Id);
        report!.Status.Should().Be(ReportStatus.Resolved);
        report.Resolution.Should().Be("Issue addressed");
        report.ResolvedAt.Should().NotBeNull();
        report.ResolvedByUserId.Should().Be(_adminUser.Id);
    }

    [Fact]
    public async Task UpdateReport_Dismissed_SetsResolvedFields()
    {
        var request = new AdminReportsController.UpdateReportRequest
        {
            Status = ReportStatus.Dismissed,
            Resolution = "Not a valid report"
        };

        await _controller.UpdateReport(_testReport.Id, request);

        var report = await _db.Reports.FindAsync(_testReport.Id);
        report!.Status.Should().Be(ReportStatus.Dismissed);
        report.ResolvedAt.Should().NotBeNull();
        report.ResolvedByUserId.Should().Be(_adminUser.Id);
    }

    [Fact]
    public async Task UpdateReport_Dismissed_LogsAdminAction()
    {
        var request = new AdminReportsController.UpdateReportRequest
        {
            Status = ReportStatus.Dismissed,
            Resolution = "Spam"
        };

        await _controller.UpdateReport(_testReport.Id, request);

        var action = await _db.AdminActions.FirstOrDefaultAsync(a => a.ActionType == AdminActionType.ReportDismissed);
        action.Should().NotBeNull();
        action!.ActorUserId.Should().Be(_adminUser.Id);
    }

    [Fact]
    public async Task UpdateReport_Resolved_LogsAdminAction()
    {
        var request = new AdminReportsController.UpdateReportRequest
        {
            Status = ReportStatus.Resolved,
            Resolution = "Handled"
        };

        await _controller.UpdateReport(_testReport.Id, request);

        var action = await _db.AdminActions.FirstOrDefaultAsync(a => a.ActionType == AdminActionType.ReportResolved);
        action.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateReport_ReopenToOpen_ClearsResolvedFields()
    {
        // First resolve the report
        _testReport.Status = ReportStatus.Resolved;
        _testReport.ResolvedAt = DateTimeOffset.UtcNow;
        _testReport.ResolvedByUserId = _adminUser.Id;
        _testReport.Resolution = "Resolved";
        await _db.SaveChangesAsync();

        // Then reopen it
        var request = new AdminReportsController.UpdateReportRequest
        {
            Status = ReportStatus.Open
        };

        await _controller.UpdateReport(_testReport.Id, request);

        var report = await _db.Reports.FindAsync(_testReport.Id);
        report!.Status.Should().Be(ReportStatus.Open);
        report.ResolvedAt.Should().BeNull();
        report.ResolvedByUserId.Should().BeNull();
        report.Resolution.Should().BeNull();
    }

    [Fact]
    public async Task UpdateReport_SetToReviewing_ClearsResolvedFields()
    {
        _testReport.Status = ReportStatus.Resolved;
        _testReport.ResolvedAt = DateTimeOffset.UtcNow;
        _testReport.ResolvedByUserId = _adminUser.Id;
        _testReport.Resolution = "Resolved";
        await _db.SaveChangesAsync();

        var request = new AdminReportsController.UpdateReportRequest
        {
            Status = ReportStatus.Reviewing
        };

        await _controller.UpdateReport(_testReport.Id, request);

        var report = await _db.Reports.FindAsync(_testReport.Id);
        report!.Status.Should().Be(ReportStatus.Reviewing);
        report.ResolvedAt.Should().BeNull();
        report.ResolvedByUserId.Should().BeNull();
        report.Resolution.Should().BeNull();
    }

    [Fact]
    public async Task UpdateReport_AssignToUser_SetsAssignedUserId()
    {
        var assigneeId = Guid.NewGuid();
        var request = new AdminReportsController.UpdateReportRequest
        {
            AssignedToUserId = assigneeId
        };

        await _controller.UpdateReport(_testReport.Id, request);

        var report = await _db.Reports.FindAsync(_testReport.Id);
        report!.AssignedToUserId.Should().Be(assigneeId);
    }

    [Fact]
    public async Task UpdateReport_NonExistentReport_ReturnsNotFound()
    {
        var request = new AdminReportsController.UpdateReportRequest
        {
            Status = ReportStatus.Resolved
        };

        var result = await _controller.UpdateReport(Guid.NewGuid(), request);

        result.Should().BeOfType<NotFoundResult>();
    }

    public void Dispose() => _db.Dispose();
}
