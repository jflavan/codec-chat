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
                ReporterName = r.Reporter!.DisplayName,
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
            .Include(r => r.Reporter)
            .Include(r => r.AssignedToUser)
            .Include(r => r.ResolvedByUser)
            .FirstOrDefaultAsync(r => r.Id == id);
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

        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("/admin/messages/search")]
    public async Task<IActionResult> SearchMessages([FromQuery] string search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        if (string.IsNullOrWhiteSpace(search) || search.Length < 2)
            return BadRequest(new { error = "Search term must be at least 2 characters." });

        pageSize = Math.Min(pageSize, 100);
        var term = $"%{search}%";

        var query = db.Messages.AsNoTracking()
            .Where(m => EF.Functions.ILike(m.Body, term));

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new
            {
                m.Id, Content = m.Body, m.CreatedAt, m.ChannelId,
                AuthorName = m.AuthorUser!.DisplayName,
                ChannelName = m.Channel!.Name,
                ServerName = m.Channel.Server!.Name,
                ServerId = m.Channel.ServerId
            })
            .ToListAsync();

        return Ok(PaginatedResponse<object>.Create(items.Cast<object>().ToList(), totalCount, page, pageSize));
    }
}
