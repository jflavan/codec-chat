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

        if (!Guid.TryParse(request.TargetId, out var targetGuid))
            return BadRequest(new { error = "Invalid target ID." });

        string? snapshot = null;
        if (request.ReportType == ReportType.Message)
        {
            var msg = await db.Messages.AsNoTracking()
                .Where(m => m.Id == targetGuid)
                .Select(m => new { m.Body, Author = m.AuthorUser!.DisplayName })
                .FirstOrDefaultAsync();
            if (msg is null) return NotFound(new { error = "Target message not found." });
            snapshot = JsonSerializer.Serialize(msg);
        }
        else if (request.ReportType == ReportType.User)
        {
            var u = await db.Users.AsNoTracking()
                .Where(u => u.Id == targetGuid)
                .Select(u => new { u.DisplayName, u.Email })
                .FirstOrDefaultAsync();
            if (u is null) return NotFound(new { error = "Target user not found." });
            snapshot = JsonSerializer.Serialize(u);
        }
        else if (request.ReportType == ReportType.Server)
        {
            var s = await db.Servers.AsNoTracking()
                .Where(s => s.Id == targetGuid)
                .Select(s => new { s.Name, s.Description })
                .FirstOrDefaultAsync();
            if (s is null) return NotFound(new { error = "Target server not found." });
            snapshot = JsonSerializer.Serialize(s);
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
