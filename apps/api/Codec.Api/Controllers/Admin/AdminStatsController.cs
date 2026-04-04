using Codec.Api.Data;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Controllers.Admin;

[ApiController]
[Authorize(Policy = "GlobalAdmin")]
[Route("admin/stats")]
public class AdminStatsController(CodecDbContext db, MetricsCounterService metrics, PresenceTracker presence) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetStats()
    {
        var now = DateTimeOffset.UtcNow;
        var day = now.AddDays(-1);
        var week = now.AddDays(-7);
        var month = now.AddDays(-30);

        var totalUsers = await db.Users.CountAsync();
        var newUsers24h = await db.Users.CountAsync(u => u.CreatedAt >= day);
        var newUsers7d = await db.Users.CountAsync(u => u.CreatedAt >= week);
        var newUsers30d = await db.Users.CountAsync(u => u.CreatedAt >= month);

        var totalServers = await db.Servers.CountAsync();
        var newServers24h = await db.Servers.CountAsync(s => s.CreatedAt >= day);
        var newServers7d = await db.Servers.CountAsync(s => s.CreatedAt >= week);
        var newServers30d = await db.Servers.CountAsync(s => s.CreatedAt >= month);

        var totalMessages24h = await db.Messages.CountAsync(m => m.CreatedAt >= day);
        var totalMessages7d = await db.Messages.CountAsync(m => m.CreatedAt >= week);
        var totalMessages30d = await db.Messages.CountAsync(m => m.CreatedAt >= month);

        var openReports = await db.Reports.CountAsync(r => r.Status == Models.ReportStatus.Open);

        var activeConnections = presence.GetConnectionCount();
        var messagesPerMinute = metrics.GetMessagesPerMinute();

        return Ok(new
        {
            users = new { total = totalUsers, new24h = newUsers24h, new7d = newUsers7d, new30d = newUsers30d },
            servers = new { total = totalServers, new24h = newServers24h, new7d = newServers7d, new30d = newServers30d },
            messages = new { last24h = totalMessages24h, last7d = totalMessages7d, last30d = totalMessages30d },
            openReports,
            live = new { activeConnections, messagesPerMinute }
        });
    }
}
