using System.Text.Json;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Controllers;

[ApiController]
[Authorize]
[Route("reports")]
public class ReportsController(CodecDbContext db, IUserService userService) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("reports")]
    public async Task<IActionResult> CreateReport([FromBody] CreateReportRequest request)
    {
        var (user, _) = await userService.GetOrCreateUserAsync(User);

        string? snapshot = null;
        if (request.ReportType == ReportType.Message)
        {
            var msg = await db.Messages.AsNoTracking()
                .Where(m => m.Id.ToString() == request.TargetId)
                .Select(m => new { m.Body, Author = m.AuthorUser!.DisplayName })
                .FirstOrDefaultAsync();
            if (msg is not null) snapshot = JsonSerializer.Serialize(msg);
        }
        else if (request.ReportType == ReportType.User)
        {
            var u = await db.Users.AsNoTracking()
                .Where(u => u.Id.ToString() == request.TargetId)
                .Select(u => new { u.DisplayName, u.Email })
                .FirstOrDefaultAsync();
            if (u is not null) snapshot = JsonSerializer.Serialize(u);
        }
        else if (request.ReportType == ReportType.Server)
        {
            var s = await db.Servers.AsNoTracking()
                .Where(s => s.Id.ToString() == request.TargetId)
                .Select(s => new { s.Name, s.Description })
                .FirstOrDefaultAsync();
            if (s is not null) snapshot = JsonSerializer.Serialize(s);
        }

        var report = new Report
        {
            Id = Guid.NewGuid(),
            ReporterId = user.Id,
            ReportType = request.ReportType,
            TargetId = request.TargetId,
            TargetSnapshot = snapshot,
            Reason = request.Reason,
            Status = ReportStatus.Open,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Reports.Add(report);
        await db.SaveChangesAsync();

        return Ok(new { report.Id });
    }
}
