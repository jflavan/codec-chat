using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Controllers.Admin;

[ApiController]
[Authorize(Policy = "GlobalAdmin")]
[Route("admin/reports")]
public class AdminReportsController(CodecDbContext db, IUserService userService, AdminActionService adminActions) : ControllerBase
{
    public record UpdateReportRequest
    {
        public ReportStatus? Status { get; init; }
        public Guid? AssignedToUserId { get; init; }
        public string? Resolution { get; init; }
    }

    [HttpGet]
    public async Task<IActionResult> GetReports([FromQuery] PaginationParams p, [FromQuery] ReportStatus? status = null, [FromQuery] ReportType? type = null)
    {
        var query = db.Reports.AsNoTracking().AsQueryable();
        if (status.HasValue) query = query.Where(r => r.Status == status.Value);
        if (type.HasValue) query = query.Where(r => r.ReportType == type.Value);

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((p.Page - 1) * p.PageSize)
            .Take(p.PageSize)
            .Include(r => r.Reporter)
            .Select(r => new
            {
                r.Id, r.ReportType, r.TargetId, r.Reason, r.Status, r.CreatedAt,
                ReporterName = r.Reporter != null ? r.Reporter.DisplayName : null,
                r.AssignedToUserId,
                RelatedCount = db.Reports.Count(r2 => r2.ReportType == r.ReportType && r2.TargetId == r.TargetId && r2.Id != r.Id)
            })
            .ToListAsync();

        return Ok(PaginatedResponse<object>.Create(items.Cast<object>().ToList(), totalCount, p.Page, p.PageSize));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetReport(Guid id)
    {
        var report = await db.Reports.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new
            {
                r.Id, r.ReportType, r.TargetId, r.TargetSnapshot,
                r.Reason, r.Status, r.CreatedAt,
                r.AssignedToUserId, r.Resolution, r.ResolvedAt,
                ReporterName = r.Reporter != null ? r.Reporter.DisplayName : null,
                AssignedToName = r.AssignedToUser != null ? r.AssignedToUser.DisplayName : null,
                ResolvedByName = r.ResolvedByUser != null ? r.ResolvedByUser.DisplayName : null,
                RelatedCount = db.Reports.Count(r2 => r2.ReportType == r.ReportType && r2.TargetId == r.TargetId && r2.Id != r.Id)
            })
            .FirstOrDefaultAsync();
        if (report is null) return NotFound();

        return Ok(report);
    }

    [HttpPut("{id:guid}")]
    [EnableRateLimiting("admin-writes")]
    public async Task<IActionResult> UpdateReport(Guid id, [FromBody] UpdateReportRequest request)
    {
        var report = await db.Reports.FindAsync(id);
        if (report is null) return NotFound();

        var (admin, _) = await userService.GetOrCreateUserAsync(User);

        if (request.Status.HasValue) report.Status = request.Status.Value;
        if (request.AssignedToUserId.HasValue) report.AssignedToUserId = request.AssignedToUserId.Value;
        if (request.Resolution is not null) report.Resolution = request.Resolution;

        if (request.Status is ReportStatus.Resolved or ReportStatus.Dismissed)
        {
            report.ResolvedAt = DateTimeOffset.UtcNow;
            report.ResolvedByUserId = admin.Id;

            var actionType = request.Status == ReportStatus.Resolved ? AdminActionType.ReportResolved : AdminActionType.ReportDismissed;
            await adminActions.LogAsync(admin.Id, actionType, "Report", id.ToString(), request.Resolution);
        }
        else if (request.Status is ReportStatus.Open or ReportStatus.Reviewing)
        {
            report.ResolvedAt = null;
            report.ResolvedByUserId = null;
            report.Resolution = null;
        }

        await db.SaveChangesAsync();
        return Ok();
    }

}
